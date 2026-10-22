namespace InvoiceExtractor.Integration.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;
using InvoiceExtractor.Application.Features.Dashboard;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;

[Collection(PostgresCollection.Name)]
public sealed class ExportApiTests(PostgresFixture fixture) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

	private readonly HttpClient _client = fixture.Api.CreateClient();

	// Each seeding is different invoices: the same invoice can only be settled once (ADR 0004).
	private int _seeds;

	// An issuer name a hostile document could carry, copied faithfully by the model.
	private const string FormulaName = "=HYPERLINK(\"http://evil.example\",\"Click\")";

	private static ExtractedInvoice Invoice(string issuer, string number, decimal total) => new(
		issuer,
		"J0310000252297",
		"Café Mirlo",
		number,
		new DateOnly(2026, 10, 2),
		"NIO",
		[new("Café en grano 1 kg", 2, 250m, 500m), new("Filtros de papel", 1.5m, 500m, 750m)],
		1250m,
		187.50m,
		total);

	public Task InitializeAsync() => fixture.ResetAsync();

	public Task DisposeAsync()
	{
		_client.Dispose();
		return Task.CompletedTask;
	}

	[Fact]
	public async Task The_workbook_holds_only_settled_invoices_with_numbers_as_numbers_and_dates_as_dates()
	{
		await SeedAsync();

		using var response = await _client.GetAsync(new Uri("/v1/export?format=xlsx", UriKind.Relative));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
		Assert.Matches(@"^facturas-\d{4}-\d{2}-\d{2}\.xlsx$", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);

		using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
		var invoices = workbook.Worksheet("Facturas");
		Assert.Equal(3, invoices.LastRowUsed()!.RowNumber()); // header + the auto-approved and the approved one

		Assert.Equal(["auto.png", "reviewed.png"], [invoices.Cell(2, 2).GetString(), invoices.Cell(3, 2).GetString()]);
		Assert.Equal("Reglas (automático)", invoices.Cell(2, 3).GetString());
		Assert.Equal("Revisión humana", invoices.Cell(3, 3).GetString());

		var date = invoices.Cell(2, 9);
		Assert.Equal(XLDataType.DateTime, date.DataType);
		Assert.Equal(new DateTime(2026, 10, 2), date.GetDateTime());

		var total = invoices.Cell(3, 13);
		Assert.Equal(XLDataType.Number, total.DataType);
		Assert.Equal(1437.50, total.GetDouble()); // what the reviewer approved, not the reading

		// Text from the document stays text: a formula-looking name is not run.
		Assert.Equal(FormulaName, invoices.Cell(2, 5).GetString());
		Assert.False(invoices.Cell(2, 5).HasFormula);

		var lines = workbook.Worksheet("Líneas");
		Assert.Equal(5, lines.LastRowUsed()!.RowNumber()); // header + two lines per invoice
		Assert.Equal(1.5, lines.Cell(3, 5).GetDouble());
	}

	[Fact]
	public async Task The_csv_is_invariant_utf8_with_english_headers_and_formula_text_neutralised()
	{
		await SeedAsync();

		var bytes = await _client.GetByteArrayAsync(new Uri("/v1/export?format=csv&lang=en", UriKind.Relative));

		Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
		var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
		Assert.StartsWith("Document,File,Approved by,Country,Issuer,Issuer tax id,Customer,Number,Date,Currency,Subtotal,Tax,Total", lines[0], StringComparison.Ordinal);
		Assert.Equal(3, lines.Length);

		// Quoted (it holds commas and quotes), prefixed with an apostrophe so Excel keeps it as text.
		Assert.Contains("\"'=HYPERLINK(\"\"http://evil.example\"\",\"\"Click\"\")\"", lines[1], StringComparison.Ordinal);
		Assert.EndsWith(",2026-10-02,NIO,1250.00,187.50,1437.50", lines[2], StringComparison.Ordinal);
	}

	[Fact]
	public async Task An_export_can_be_limited_to_one_batch()
	{
		var batch = await SeedAsync();
		await SeedAsync();

		var bytes = await _client.GetByteArrayAsync(new Uri($"/v1/export?format=csv&batchId={batch}", UriKind.Relative));

		Assert.Equal(3, Encoding.UTF8.GetString(bytes).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
	}

	[Fact]
	public async Task An_unknown_format_is_a_problem()
	{
		using var response = await _client.GetAsync(new Uri("/v1/export?format=pdf", UriKind.Relative));

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal("export.unknown_option", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
	}

	[Fact]
	public async Task The_dashboard_adds_up_statuses_rates_and_cost()
	{
		await SeedAsync();

		var dashboard = await _client.GetFromJsonAsync<DashboardResponse>("/v1/dashboard", Json);

		Assert.Equal(4, dashboard!.Documents);
		Assert.Equal(1, dashboard.ByStatus[DocumentStatus.AutoApproved]);
		Assert.Equal(1, dashboard.ByStatus[DocumentStatus.Approved]);
		Assert.Equal(1, dashboard.ByStatus[DocumentStatus.NeedsReview]);
		Assert.Equal(1, dashboard.ByStatus[DocumentStatus.Failed]);
		Assert.Equal(0, dashboard.ByStatus[DocumentStatus.Queued]);
		Assert.Equal(3, dashboard.Decided);
		Assert.Equal(1.0 / 3, dashboard.AutoApprovedRate, 6);
		Assert.Equal(2.0 / 3, dashboard.ReviewRate, 6);

		// Four calls (one a retry) on three documents; the failed one never reached the model.
		Assert.Equal((4, 1), (dashboard.ModelCalls, dashboard.Retries));
		Assert.Equal(0.0020m, dashboard.CostUsd);
		Assert.Equal(0.000667m, dashboard.CostPerDocumentUsd);
		Assert.Equal(0.0020m, dashboard.SpentTodayUsd);
	}

	// One batch: auto-approved, approved by a person (with a correction), in review, failed without a call.
	private async Task<Guid> SeedAsync()
	{
		var now = DateTimeOffset.UtcNow;
		var seed = ++_seeds;
		var batch = Batch.Start(4, "NI", now);
		Document Queued(string name, int order) =>
			Document.Queue(batch.Id, name, ContentHash.Compute(Guid.NewGuid().ToByteArray()), DocumentFormat.Png, 1, false, 100, now.AddMilliseconds(order));

		var auto = Queued("auto.png", 0);
		var reviewed = Queued("reviewed.png", 1);
		var waiting = Queued("waiting.png", 2);
		var failed = Queued("failed.png", 3);
		var extractions = new List<Extraction>();
		void Read(Document document, ExtractedInvoice invoice, bool retry = false) =>
			extractions.Add(Extraction.Record(document, "claude-haiku-5-5", invoice, new Dictionary<string, FieldConfidence>(), [], null, retry, 2000, 300, 0.0005m, now));

		foreach (var document in new[] { auto, reviewed, waiting, failed })
		{
			document.Claim(now, now.AddMinutes(5));
		}

		Read(auto, Invoice(FormulaName, $"F-{seed}1", 1437.50m));
		auto.Complete(DocumentStatus.AutoApproved, now);

		Read(reviewed, Invoice("Ferretería La Esperanza", $"F-{seed}2", 1537.50m));
		Read(reviewed, Invoice("Ferretería La Esperanza", $"F-{seed}2", 1537.50m), retry: true);
		reviewed.Complete(DocumentStatus.NeedsReview, now);
		reviewed.Approve(Invoice("Ferretería La Esperanza", $"F-{seed}2", 1437.50m), now);

		Read(waiting, Invoice("Imprenta Los Robles", $"F-{seed}3", 999m));
		waiting.Complete(DocumentStatus.NeedsReview, now);

		failed.Release(maxAttempts: 1, now);

		await using var db = fixture.CreateContext();
		db.Batches.Add(batch);
		db.Documents.AddRange(auto, reviewed, waiting, failed);
		db.Extractions.AddRange(extractions);
		await db.SaveChangesAsync();
		return batch.Id.Value;
	}
}
