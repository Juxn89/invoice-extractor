namespace InvoiceExtractor.Evals.Extraction;

using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Evals.Dataset;

/// <summary>One reading and what it cost.</summary>
/// <param name="Invoice">What was read, or <see langword="null"/> when extraction failed outright.</param>
/// <param name="CostUsd">What it cost; 0 for anything that does not call a model.</param>
/// <param name="Error">Why extraction failed, when it did.</param>
/// <param name="Confidence">The model's confidence per key field; none for the oracle.</param>
internal sealed record ExtractionAttempt(
	ExtractedInvoice? Invoice,
	decimal CostUsd,
	string? Error = null,
	IReadOnlyDictionary<string, FieldConfidence>? Confidence = null);

/// <summary>
/// What happened to one document. <see cref="First"/> is the first reading alone: what the app would decide
/// with the retry turned off. <see cref="Final"/> is what it decides with the retry on, at the cost of every
/// call made. One run scores both configurations, because the first is a prefix of the second.
/// </summary>
/// <param name="First">The first reading.</param>
/// <param name="Final">The reading the verdict stands on, with the total cost.</param>
/// <param name="Retried">Whether a second call was made.</param>
internal sealed record ExtractionRun(ExtractionAttempt First, ExtractionAttempt Final, bool Retried);

/// <summary>What the evals score: the oracle, or the app's pipeline with Claude.</summary>
internal interface IDocumentExtractor
{
	/// <summary>The name written into results, e.g. <c>oracle</c>.</summary>
	string Name { get; }

	/// <summary>Whether it can retry, so the run reports the configuration with retry separately.</summary>
	bool Retries { get; }

	/// <summary>Reads one dataset document.</summary>
	/// <param name="entry">The manifest entry.</param>
	/// <param name="path">The document's file.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The run.</returns>
	Task<ExtractionRun> ExtractAsync(DatasetEntry entry, string path, CancellationToken cancellationToken);
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
	public bool Retries => false;

	/// <inheritdoc />
	public Task<ExtractionRun> ExtractAsync(DatasetEntry entry, string path, CancellationToken cancellationToken)
	{
		var attempt = new ExtractionAttempt(entry.Expected, CostUsd: 0m);
		return Task.FromResult(new ExtractionRun(attempt, attempt, Retried: false));
	}
}
