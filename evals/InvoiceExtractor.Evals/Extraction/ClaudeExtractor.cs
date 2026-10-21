namespace InvoiceExtractor.Evals.Extraction;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Features.Processing;
using InvoiceExtractor.Application.Features.Upload;
using InvoiceExtractor.Domain.Validation;
using InvoiceExtractor.Evals.Dataset;
using InvoiceExtractor.Evals.Generation;
using InvoiceExtractor.Evals.Paid;
using InvoiceExtractor.Evals.Scoring;
using InvoiceExtractor.Infrastructure.Ai;
using InvoiceExtractor.Infrastructure.Documents;
using Microsoft.Extensions.Options;

/// <summary>
/// The app's pipeline on a dataset file: format detection, inspection, preprocessing, then
/// <see cref="ExtractionPipeline"/> with Claude (read, validate, retry with feedback, decide). The same classes
/// the API uses; only the spending guard is added, in front of every call.
/// </summary>
internal sealed class ClaudeExtractor : IDocumentExtractor, IDisposable
{
	private readonly DocumentInspector _inspector = new(Options.Create(new UploadOptions()));
	private readonly DocumentPreprocessor _preprocessor = new();
	private readonly ClaudeInvoiceExtractor _claude;
	private readonly ExtractionPipeline _pipeline;

	/// <summary>Creates it.</summary>
	/// <param name="options">The model settings, API key included.</param>
	/// <param name="guard">The run's budget.</param>
	public ClaudeExtractor(AnthropicOptions options, SpendingGuard guard)
	{
		ArgumentNullException.ThrowIfNull(options);
		Name = options.Model;
		_claude = new ClaudeInvoiceExtractor(Options.Create(options));
		_pipeline = new ExtractionPipeline(
			new GuardedExtractor(_claude, guard, options),
			Options.Create(new ProcessingOptions { RetryOnFailedRules = true }));
	}

	public string Name { get; }

	public bool Retries => true;

	public async Task<ExtractionRun> ExtractAsync(DatasetEntry entry, string path, CancellationToken cancellationToken)
	{
		var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
		var format = FormatDetector.Detect(content).Value;
		var inspection = _inspector.Inspect(content, format).Value;
		var prepared = _preprocessor.Prepare(content, format, inspection.HasTextLayer);

		var outcome = await _pipeline.RunAsync(prepared, TaxProfiles.For(entry.Country), DatasetPlan.AsOf, cancellationToken).ConfigureAwait(false);

		static ExtractionAttempt Attempt(PipelineAttempt attempt, decimal cost) =>
			new(attempt.Result.Invoice, cost, attempt.Result.Failure, attempt.Result.Confidence);

		var first = outcome.Attempts[0];
		return new ExtractionRun(
			Attempt(first, first.Result.Usage.CostUsd),
			Attempt(outcome.Final, outcome.CostUsd),
			Retried: outcome.Attempts.Count > 1);
	}

	public void Dispose() => _claude.Dispose();

	/// <summary>Checks the budget before each call, with the worst case the call could cost, and records it after.</summary>
	private sealed class GuardedExtractor(IInvoiceExtractor inner, SpendingGuard guard, AnthropicOptions options) : IInvoiceExtractor
	{
		// Anthropic's vision guide: an image costs about width × height / 750 tokens.
		private const decimal PixelsPerImageToken = 750m;

		// A scanned PDF page is read as an image; at the API's 1568 px long edge an A4 page is about 1568 × 1109
		// pixels, ≈ 2,300 tokens, plus any text it extracts. Rounded up for the worst-case estimate.
		private const decimal TokensPerPdfPage = 3_300m;

		// Characters per token for the prompt and text, with Haiku 5.5's tokenizer margin (≈ 30% more tokens).
		private const decimal CharactersPerToken = 4m / 1.3m;

		private static readonly decimal PromptTokens =
			(PromptLibrary.Read("extract-invoice.md").Length
				+ PromptLibrary.Read("retry-feedback.md").Length
				+ InvoiceExtractionSchema.Schema.ToJsonString().Length) / CharactersPerToken;

		public bool IsConfigured => inner.IsConfigured;

		public async Task<InvoiceExtractionResult> ExtractAsync(PreparedDocument document, IReadOnlyList<ValidationIssue> feedback, CancellationToken cancellationToken)
		{
			guard.EnsureCanCall(WorstCaseUsd(document, feedback));
			var result = await inner.ExtractAsync(document, feedback, cancellationToken).ConfigureAwait(false);
			guard.Record(result.Usage.InputTokens, result.Usage.OutputTokens, result.Usage.CostUsd);
			return result;
		}

		private decimal WorstCaseUsd(PreparedDocument prepared, IReadOnlyList<ValidationIssue> feedback)
		{
			var documentTokens = prepared switch
			{
				PreparedText text => text.Text.Length / CharactersPerToken,
				PreparedPdf pdf => pdf.Pages * TokensPerPdfPage,
				PreparedImage image => image.Width * (decimal)image.Height / PixelsPerImageToken,
				_ => throw new ArgumentOutOfRangeException(nameof(prepared)),
			};
			var feedbackTokens = feedback.Sum(static issue => issue.Detail.Length) / CharactersPerToken;

			return (((PromptTokens + documentTokens + feedbackTokens) * options.InputUsdPerMillionTokens)
				+ (options.MaxOutputTokens * options.OutputUsdPerMillionTokens)) / 1_000_000m;
		}
	}
}
