namespace InvoiceExtractor.Evals.Extraction;

using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Evals.Dataset;

/// <summary>One extraction and what it cost.</summary>
/// <param name="Invoice">What was read, or <see langword="null"/> when extraction failed outright.</param>
/// <param name="CostUsd">What the call cost; 0 for anything that does not call a model.</param>
/// <param name="Error">Why extraction failed, when it did.</param>
internal sealed record ExtractionAttempt(ExtractedInvoice? Invoice, decimal CostUsd, string? Error = null);

/// <summary>What the evals score. The oracle today; the Haiku extractor from day 4 of the plan.</summary>
internal interface IDocumentExtractor
{
	/// <summary>The name written into results, e.g. <c>oracle</c>.</summary>
	string Name { get; }

	/// <summary>Reads one dataset document.</summary>
	/// <param name="entry">The manifest entry.</param>
	/// <param name="path">The document's file.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The attempt.</returns>
	Task<ExtractionAttempt> ExtractAsync(DatasetEntry entry, string path, CancellationToken cancellationToken);
}

/// <summary>
/// Returns exactly what the document prints. It scores the harness rather than a model: with it every field
/// and line matches, every consistent document is approved and every planted one goes to review. Any other
/// result is a bug in the dataset, the validator or the scorer.
/// </summary>
internal sealed class OracleExtractor : IDocumentExtractor
{
	/// <inheritdoc />
	public string Name => "oracle";

	/// <inheritdoc />
	public Task<ExtractionAttempt> ExtractAsync(DatasetEntry entry, string path, CancellationToken cancellationToken) =>
		Task.FromResult(new ExtractionAttempt(entry.Expected, CostUsd: 0m));
}
