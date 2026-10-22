namespace InvoiceExtractor.Integration.Tests;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Infrastructure.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

// The stand-in the end-to-end tests run on: it must read a recording the way a real answer is read, and it must
// never be reachable in a deployed API.
public sealed class ReplayExtractorTests : IDisposable
{
	private const string Recording = """
		{
		  "answers": [
		    {
		      "match": "INV-000042",
		      "answer": {
		        "issuerName": "Northwind Supplies",
		        "issuerTaxId": "NW-123456",
		        "receiverName": "Contoso",
		        "number": "INV-000042",
		        "issueDate": "2026-09-30",
		        "currency": "USD",
		        "lines": [{ "description": "Paper", "quantity": "2", "unitPrice": "10.50", "lineTotal": "21.00" }],
		        "subtotal": "21.00",
		        "taxTotal": "2.10",
		        "total": "23.10",
		        "confidence": { "issuerTaxId": "high", "number": "high", "issueDate": "high", "lines": "high", "subtotal": "high", "taxTotal": "high", "total": "low" }
		      }
		    }
		  ]
		}
		""";

	private readonly string _file = Path.Combine(Path.GetTempPath(), $"replay-{Guid.NewGuid():N}.json");

	public ReplayExtractorTests() => File.WriteAllText(_file, Recording);

	public void Dispose() => File.Delete(_file);

	[Fact]
	public async Task A_recorded_answer_is_read_like_a_real_one_and_costs_nothing()
	{
		var extractor = new ReplayInvoiceExtractor(Options.Create(new ReplayOptions { File = _file }));

		var result = await extractor.ExtractAsync(new PreparedText("Invoice INV-000042\nNorthwind Supplies", 1), [], CancellationToken.None);

		Assert.Null(result.Failure);
		Assert.Equal(("INV-000042", 23.10m, new DateOnly(2026, 9, 30)), (result.Invoice!.Number, result.Invoice.Total, result.Invoice.IssueDate));
		Assert.Equal(21.00m, Assert.Single(result.Invoice.Lines).LineTotal);
		Assert.Equal(FieldConfidence.Low, result.Confidence["total"]);
		Assert.Equal(0m, result.Usage.CostUsd);
		Assert.True(extractor.IsConfigured);
	}

	[Fact]
	public async Task A_document_without_a_recording_fails_instead_of_getting_someone_elses_answer()
	{
		var extractor = new ReplayInvoiceExtractor(Options.Create(new ReplayOptions { File = _file }));

		var text = await extractor.ExtractAsync(new PreparedText("Invoice INV-000043", 1), [], CancellationToken.None);
		var image = await extractor.ExtractAsync(new PreparedImage(new byte[] { 0xFF, 0xD8 }, 1, 1), [], CancellationToken.None);

		Assert.All([text, image], static result => Assert.Equal((null, "No recorded answer for this document."), (result.Invoice, result.Failure)));
	}

	[Fact]
	public void The_api_refuses_to_start_with_recorded_answers_outside_development()
	{
		using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
			.UseEnvironment("Production")
			.UseSetting("ConnectionStrings:invoicesdb", "Host=localhost;Database=unused")
			.UseSetting("Replay:File", _file));

		var refusal = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
		Assert.Contains("Replay:File", refusal.Message, StringComparison.Ordinal);
	}
}
