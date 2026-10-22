namespace InvoiceExtractor.Integration.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Features.Documents;
using InvoiceExtractor.Application.Features.Review;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;
using Microsoft.Extensions.DependencyInjection;

[Collection(PostgresCollection.Name)]
public sealed class ReviewApiTests(PostgresFixture fixture) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

	private readonly HttpClient _client = fixture.Api.CreateClient();

	// The total was misread: 1250 instead of 1150.
	private static readonly ExtractedInvoice Misread = new(
		"Distribuidora El Mirador, S.A.",
		"J0310000252297",
		"Café Mirlo",
		"F-000123",
		new DateOnly(2026, 10, 2),
		"NIO",
		[new("Café en grano 1 kg", 2, 250m, 500m), new("Filtros de papel", 1, 500m, 500m)],
		1000m,
		150m,
		1250m);

	public Task InitializeAsync() => fixture.ResetAsync();

	public Task DisposeAsync()
	{
		_client.Dispose();
		return Task.CompletedTask;
	}

	[Fact]
	public async Task A_document_in_review_shows_its_reading_issues_and_file_and_leaves_the_queue_once_approved()
	{
		var id = await SeedInReviewAsync();

		var queue = await _client.GetFromJsonAsync<List<DocumentResponse>>("/v1/review-queue", Json);
		Assert.Equal([id], queue!.Select(static d => d.Id));

		var review = await _client.GetFromJsonAsync<ReviewResponse>($"/v1/documents/{id}/review", Json);
		Assert.Equal(("NI", 1250m, 1), (review!.Country, review.Invoice!.Total, review.Calls));
		Assert.Contains(review.Issues, static i => i.Code == IssueCode.TotalMismatch && i.Field == InvoiceFields.Total);
		Assert.Contains("RUC", review.TaxIdFormat, StringComparison.Ordinal);

		using var file = await _client.GetAsync(new Uri($"/v1/documents/{id}/file", UriKind.Relative));
		Assert.Equal("image/png", file.Content.Headers.ContentType?.MediaType);
		Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single());
		Assert.Equal(Samples.Scan, await file.Content.ReadAsByteArrayAsync());

		using var approve = await _client.PostAsJsonAsync($"/v1/documents/{id}/approve", new ApproveRequest(Misread with { Total = 1150m }), Json);
		Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
		var approved = (await approve.Content.ReadFromJsonAsync<ReviewResponse>(Json))!;
		Assert.Equal(DocumentStatus.Approved, approved.Document.Status);
		Assert.Equal(1150m, approved.Invoice!.Total);
		Assert.Empty(approved.Issues);

		Assert.Empty((await _client.GetFromJsonAsync<List<DocumentResponse>>("/v1/review-queue", Json))!);
	}

	[Fact]
	public async Task The_reviewer_has_the_last_word_and_approves_data_that_still_breaks_a_rule()
	{
		var id = await SeedInReviewAsync();

		using var approve = await _client.PostAsJsonAsync($"/v1/documents/{id}/approve", new ApproveRequest(Misread), Json);

		var approved = (await approve.Content.ReadFromJsonAsync<ReviewResponse>(Json))!;
		Assert.Equal(DocumentStatus.Approved, approved.Document.Status);
		Assert.Contains(approved.Issues, static i => i.Code == IssueCode.TotalMismatch);
	}

	[Fact]
	public async Task Only_a_document_in_review_can_be_approved_and_only_with_data()
	{
		var id = await SeedInReviewAsync();
		using (await _client.PostAsJsonAsync($"/v1/documents/{id}/approve", new ApproveRequest(Misread), Json))
		{
		}

		using var again = await _client.PostAsJsonAsync($"/v1/documents/{id}/approve", new ApproveRequest(Misread), Json);
		using var empty = await _client.PostAsJsonAsync($"/v1/documents/{id}/approve", new { }, Json);
		using var unknown = await _client.PostAsJsonAsync($"/v1/documents/{Guid.NewGuid()}/approve", new ApproveRequest(Misread), Json);

		Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
		Assert.Equal(ReviewErrors.NotInReview, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
		Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
	}

	[Fact]
	public async Task Recent_batches_are_listed_with_their_documents()
	{
		var id = await SeedInReviewAsync();

		var batches = await _client.GetFromJsonAsync<List<BatchResponse>>("/v1/batches", Json);

		var batch = Assert.Single(batches!);
		Assert.Equal("NI", batch.Country);
		Assert.Equal(id, Assert.Single(batch.Documents).Id);
	}

	[Fact]
	public async Task Approving_an_invoice_another_document_already_settled_is_refused_with_the_original()
	{
		var original = await SeedSettledAsync(Misread with { Total = 1150m });
		var id = await SeedInReviewAsync();

		using var approve = await _client.PostAsJsonAsync($"/v1/documents/{id}/approve", new ApproveRequest(Misread with { Total = 1150m }), Json);

		Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
		var problem = await approve.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(ReviewErrors.DuplicateInvoice, problem.GetProperty("code").GetString());
		Assert.Equal(original.ToString(), problem.GetProperty(ReviewErrors.OriginalDetail).GetString());

		var review = await _client.GetFromJsonAsync<ReviewResponse>($"/v1/documents/{id}/review", Json);
		Assert.Equal(DocumentStatus.NeedsReview, review!.Document.Status);
	}

	// A document approved by the rules, the invoice settled.
	private async Task<Guid> SeedSettledAsync(ExtractedInvoice invoice)
	{
		var now = DateTimeOffset.UtcNow;
		var batch = Batch.Start(1, "NI", now);
		var document = Document.Queue(batch.Id, "factura.pdf", ContentHash.Compute(Guid.NewGuid().ToByteArray()), DocumentFormat.Pdf, 1, true, 100, now);
		document.Claim(now, now.AddMinutes(5));
		document.Complete(DocumentStatus.AutoApproved, now, InvoiceKey.From(invoice));

		await using var db = fixture.CreateContext();
		db.Batches.Add(batch);
		db.Documents.Add(document);
		await db.SaveChangesAsync();
		return document.Id.Value;
	}

	// A document the worker read, broke a rule on, and sent to review: what the review screen starts from.
	private async Task<Guid> SeedInReviewAsync()
	{
		var now = DateTimeOffset.UtcNow;
		var content = Samples.Scan;
		var batch = Batch.Start(1, "NI", now);
		var document = Document.Queue(batch.Id, "escaneo.png", ContentHash.Compute(content), DocumentFormat.Png, 1, false, content.Length, now);
		document.Claim(now, now.AddMinutes(5));
		var issues = new[] { new ValidationIssue(InvoiceFields.Total, IssueCode.TotalMismatch, "Subtotal plus tax is 1150.00, the total reads 1250.00.") };
		var extraction = Extraction.Record(
			document,
			"claude-haiku-5-5",
			Misread,
			new Dictionary<string, FieldConfidence> { [InvoiceFields.Total] = FieldConfidence.Medium },
			issues,
			null,
			false,
			2000,
			300,
			0.00035m,
			now);
		document.Complete(DocumentStatus.NeedsReview, now);

		await fixture.Api.Services.GetRequiredService<IFileStore>().SaveAsync(document.ContentHash, document.Format, content, CancellationToken.None);
		await using var db = fixture.CreateContext();
		db.Batches.Add(batch);
		db.Documents.Add(document);
		db.Extractions.Add(extraction);
		await db.SaveChangesAsync();
		return document.Id.Value;
	}
}
