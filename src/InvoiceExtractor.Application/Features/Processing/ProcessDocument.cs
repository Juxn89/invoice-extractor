namespace InvoiceExtractor.Application.Features.Processing;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Documents;

/// <summary>
/// Reads one claimed document and records what happens to it. The model proposes, the domain decides: see
/// <see cref="ExtractionPipeline"/> and <see cref="ReviewPolicy"/>. A transient failure (the API, the disk) is
/// thrown, so the worker gives the document back for another attempt.
/// </summary>
/// <param name="queue">The queue that claimed it, which also stores the result.</param>
/// <param name="files">Where the upload is.</param>
/// <param name="preprocessor">Turns the file into the model's input.</param>
/// <param name="pipeline">Read, validate, maybe retry, decide.</param>
/// <param name="profiles">The country rules.</param>
/// <param name="clock">The clock.</param>
public sealed class ProcessDocument(
	IDocumentQueue queue,
	IFileStore files,
	IDocumentPreprocessor preprocessor,
	ExtractionPipeline pipeline,
	TaxProfileCatalog profiles,
	TimeProvider clock)
{
	/// <summary>Processes the document and stores the outcome with every call made, in one transaction.</summary>
	/// <param name="document">A document this worker holds a lease on.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>Where the document ended.</returns>
	/// <exception cref="Common.LeaseLostException">The lease ran out and another worker took the document.</exception>
	public async Task<DocumentStatus> HandleAsync(Document document, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(document);

		var content = await files.ReadAsync(document.ContentHash, document.Format, cancellationToken).ConfigureAwait(false);
		var prepared = preprocessor.Prepare(content, document.Format, document.HasTextLayer);
		var country = await queue.CountryOfAsync(document.BatchId, cancellationToken).ConfigureAwait(false);
		var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

		var outcome = await pipeline.RunAsync(prepared, profiles.For(country), today, cancellationToken).ConfigureAwait(false);

		var now = clock.GetUtcNow();
		foreach (var attempt in outcome.Attempts)
		{
			var result = attempt.Result;
			queue.Add(Extraction.Record(
				document,
				result.Usage.Model,
				result.Invoice,
				result.Confidence,
				attempt.Issues,
				result.Failure,
				attempt.IsRetry,
				result.Usage.InputTokens,
				result.Usage.OutputTokens,
				result.Usage.CostUsd,
				now));
		}

		document.Complete(outcome.Status, now);
		await queue.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return outcome.Status;
	}
}
