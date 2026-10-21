namespace InvoiceExtractor.Application.Tests;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Features.Processing;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;
using Microsoft.Extensions.Options;

public sealed class ProcessDocumentTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

	private readonly FakeQueue _queue = new();
	private readonly FakeFileStore _files = new();
	private readonly FakeExtractor _extractor = new();
	private readonly ProcessingOptions _options = new();

	// 2 × 250 + 1 × 500 = 1000, IVA 15% = 150, total 1150, with a valid Nicaraguan RUC.
	private static ExtractedInvoice Consistent() => new(
		"Distribuidora El Mirador, S.A.",
		"J0310000252297",
		"Café Mirlo",
		"F-000123",
		new DateOnly(2026, 10, 2),
		"NIO",
		[new("Café en grano 1 kg", 2, 250m, 500m), new("Filtros de papel", 1, 500m, 500m)],
		1000m,
		150m,
		1150m);

	private static Dictionary<string, FieldConfidence> Sure() => new()
	{
		[InvoiceFields.IssuerTaxId] = FieldConfidence.High,
		[InvoiceFields.Number] = FieldConfidence.High,
		[InvoiceFields.IssueDate] = FieldConfidence.High,
		[InvoiceFields.Lines] = FieldConfidence.High,
		[InvoiceFields.Subtotal] = FieldConfidence.High,
		[InvoiceFields.TaxTotal] = FieldConfidence.High,
		[InvoiceFields.Total] = FieldConfidence.High,
	};

	private static InvoiceExtractionResult Read(ExtractedInvoice? invoice, Dictionary<string, FieldConfidence>? confidence = null, string? failure = null) =>
		new(invoice, confidence ?? Sure(), FakeExtractor.Usage, failure);

	private async Task<(DocumentStatus Outcome, Document Document)> ProcessAsync(string country = "NI")
	{
		var batch = Batch.Start(1, country, Now);
		var content = Files.Png("invoice");
		var document = Document.Queue(batch.Id, "factura.png", ContentHash.Compute(content), DocumentFormat.Png, 1, false, content.Length, Now);
		await _files.SaveAsync(document.ContentHash, document.Format, content, CancellationToken.None);
		_queue.Countries[batch.Id] = country;
		document.Claim(Now, Now.AddMinutes(5));

		var useCase = new ProcessDocument(
			_queue,
			_files,
			new FakePreprocessor(),
			new ExtractionPipeline(_extractor, Options.Create(_options)),
			new TaxProfileCatalog(Options.Create(new TaxOptions())),
			new FixedClock(Now));
		return (await useCase.HandleAsync(document, CancellationToken.None), document);
	}

	[Fact]
	public async Task A_consistent_reading_the_model_is_sure_of_is_approved_in_one_call_with_its_cost_recorded()
	{
		_extractor.Responses.Add(Read(Consistent()));

		var (outcome, document) = await ProcessAsync();

		Assert.Equal(DocumentStatus.AutoApproved, outcome);
		Assert.Equal(DocumentStatus.AutoApproved, document.Status);
		var extraction = Assert.Single(_queue.Saved);
		Assert.Empty(extraction.Issues);
		Assert.False(extraction.IsRetry);
		Assert.Equal(FakeExtractor.Usage.CostUsd, extraction.CostUsd);
		Assert.Equal(1, extraction.Attempt);
	}

	[Fact]
	public async Task A_misread_the_retry_corrects_is_approved_and_both_calls_are_recorded()
	{
		_extractor.Responses.Add(Read(Consistent() with { Total = 1250m }));
		_extractor.Responses.Add(Read(Consistent()));

		var (outcome, _) = await ProcessAsync();

		Assert.Equal(DocumentStatus.AutoApproved, outcome);
		Assert.Equal([false, true], _queue.Saved.Select(static e => e.IsRetry));
		Assert.Contains(_queue.Saved[0].Issues, static i => i.Code == IssueCode.TotalMismatch);
		Assert.Empty(_queue.Saved[1].Issues);

		// The retry was told what failed, with the numbers.
		Assert.Empty(_extractor.Feedback[0]);
		Assert.Contains(_extractor.Feedback[1], static i => i.Code == IssueCode.TotalMismatch);
	}

	[Fact]
	public async Task A_rule_still_broken_after_the_retry_goes_to_review()
	{
		_extractor.Responses.Add(Read(Consistent() with { Total = 1250m }));

		var (outcome, _) = await ProcessAsync();

		Assert.Equal(DocumentStatus.NeedsReview, outcome);
		Assert.Equal(2, _queue.Saved.Count);
		Assert.Contains(_queue.Saved[1].Issues, static i => i.Code == IssueCode.TotalMismatch);
	}

	[Fact]
	public async Task With_the_retry_off_a_broken_rule_goes_straight_to_review()
	{
		_options.RetryOnFailedRules = false;
		_extractor.Responses.Add(Read(Consistent() with { Total = 1250m }));
		_extractor.Responses.Add(Read(Consistent()));

		var (outcome, _) = await ProcessAsync();

		Assert.Equal(DocumentStatus.NeedsReview, outcome);
		Assert.Single(_queue.Saved);
	}

	[Fact]
	public async Task A_retry_that_returns_nothing_leaves_the_first_reading_in_review()
	{
		_extractor.Responses.Add(Read(Consistent() with { Total = 1250m }));
		_extractor.Responses.Add(Read(null, [], "The answer was cut off."));

		var (outcome, _) = await ProcessAsync();

		Assert.Equal(DocumentStatus.NeedsReview, outcome);
		Assert.Equal(2, _queue.Saved.Count);
	}

	[Fact]
	public async Task A_key_field_the_model_marks_low_goes_to_review_without_a_retry()
	{
		var confidence = Sure();
		confidence[InvoiceFields.Total] = FieldConfidence.Low;
		_extractor.Responses.Add(Read(Consistent(), confidence));

		var (outcome, _) = await ProcessAsync();

		Assert.Equal(DocumentStatus.NeedsReview, outcome);
		Assert.Empty(Assert.Single(_queue.Saved).Issues);
	}

	[Fact]
	public async Task The_batch_country_picks_the_rules()
	{
		// A Nicaraguan RUC is not an RFC: the same reading fails when the batch is Mexican.
		_extractor.Responses.Add(Read(Consistent() with { TaxTotal = 160m, Total = 1160m }));

		var (outcome, _) = await ProcessAsync(country: "MX");

		Assert.Equal(DocumentStatus.NeedsReview, outcome);
		Assert.Contains(_queue.Saved[0].Issues, static i => i.Code == IssueCode.InvalidTaxId);
	}

	[Fact]
	public async Task A_reading_that_came_back_empty_goes_to_review_with_the_reason_and_is_not_retried()
	{
		_extractor.Responses.Add(Read(null, [], "The model declined to read the document."));

		var (outcome, _) = await ProcessAsync();

		Assert.Equal(DocumentStatus.NeedsReview, outcome);
		var extraction = Assert.Single(_queue.Saved);
		Assert.Null(extraction.Invoice);
		Assert.Equal("The model declined to read the document.", extraction.Failure);
		Assert.Equal(FakeExtractor.Usage.CostUsd, extraction.CostUsd);
	}

	[Fact]
	public async Task A_transient_failure_is_thrown_and_nothing_is_saved()
	{
		_extractor.Throw = new HttpRequestException("503");

		await Assert.ThrowsAsync<HttpRequestException>(() => ProcessAsync());

		Assert.Empty(_queue.Saved);
		Assert.Equal(0, _queue.Saves);
	}

	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}
}
