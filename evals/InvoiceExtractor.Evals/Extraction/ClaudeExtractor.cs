namespace InvoiceExtractor.Evals.Extraction;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Features.Upload;
using InvoiceExtractor.Evals.Dataset;
using InvoiceExtractor.Evals.Paid;
using InvoiceExtractor.Infrastructure.Ai;
using InvoiceExtractor.Infrastructure.Documents;
using Microsoft.Extensions.Options;

/// <summary>
/// The real pipeline on a dataset file: format detection, inspection, preprocessing and the Claude extractor,
/// the same classes the API uses. Every call passes the spending guard first.
/// </summary>
/// <param name="options">The model settings, API key included.</param>
/// <param name="guard">The run's budget.</param>
internal sealed class ClaudeExtractor(AnthropicOptions options, SpendingGuard guard) : IDocumentExtractor, IDisposable
{
	// Anthropic's vision guide: an image costs about width × height / 750 tokens.
	private const decimal PixelsPerImageToken = 750m;

	// A scanned PDF page is read as an image; at the API's 1568 px long edge an A4 page is about 1568 × 1109
	// pixels, ≈ 2,300 tokens, plus any text it extracts. Rounded up for the worst-case estimate.
	private const decimal TokensPerPdfPage = 3_300m;

	// Characters per token for the prompt and text, with Haiku 5.5's tokenizer margin (≈ 30% more tokens).
	private const decimal CharactersPerToken = 4m / 1.3m;

	private static readonly decimal PromptTokens =
		(PromptLibrary.Read("extract-invoice.md").Length + InvoiceExtractionSchema.Schema.ToJsonString().Length) / CharactersPerToken;

	private readonly DocumentInspector _inspector = new(Options.Create(new UploadOptions()));
	private readonly DocumentPreprocessor _preprocessor = new();
	private readonly ClaudeInvoiceExtractor _extractor = new(Options.Create(options));

	public string Name => options.Model;

	public async Task<ExtractionAttempt> ExtractAsync(DatasetEntry entry, string path, CancellationToken cancellationToken)
	{
		var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
		var format = FormatDetector.Detect(content).Value;
		var inspection = _inspector.Inspect(content, format).Value;
		var prepared = _preprocessor.Prepare(content, format, inspection.HasTextLayer);

		guard.EnsureCanCall(WorstCaseUsd(prepared));
		var result = await _extractor.ExtractAsync(prepared, cancellationToken).ConfigureAwait(false);
		guard.Record(result.Usage.InputTokens, result.Usage.OutputTokens, result.Usage.CostUsd);

		return new ExtractionAttempt(result.Invoice, result.Usage.CostUsd, result.Failure);
	}

	public void Dispose() => _extractor.Dispose();

	private decimal WorstCaseUsd(PreparedDocument prepared)
	{
		var documentTokens = prepared switch
		{
			PreparedText text => text.Text.Length / CharactersPerToken,
			PreparedPdf pdf => pdf.Pages * TokensPerPdfPage,
			PreparedImage image => image.Width * (decimal)image.Height / PixelsPerImageToken,
			_ => throw new ArgumentOutOfRangeException(nameof(prepared)),
		};

		return (((PromptTokens + documentTokens) * options.InputUsdPerMillionTokens)
			+ (options.MaxOutputTokens * options.OutputUsdPerMillionTokens)) / 1_000_000m;
	}
}
