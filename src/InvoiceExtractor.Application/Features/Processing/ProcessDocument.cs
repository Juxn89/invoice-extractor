namespace InvoiceExtractor.Application.Features.Processing;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;

/// <summary>
/// Reads one claimed document and decides what happens to it. The model proposes, the domain decides: the
/// reading is approved only when every rule of the batch's country passes and the model was not unsure of a
/// key field; anything else goes to a person. A transient failure (the API, the disk) is thrown, so the worker
/// gives the document back for another attempt.
/// </summary>
/// <param name="queue">The queue that claimed it, which also stores the result.</param>
/// <param name="files">Where the upload is.</param>
/// <param name="preprocessor">Turns the file into the model's input.</param>
/// <param name="extractor">The model.</param>
/// <param name="profiles">The country rules.</param>
/// <param name="clock">The clock.</param>
public sealed class ProcessDocument(
	IDocumentQueue queue,
	IFileStore files,
	IDocumentPreprocessor preprocessor,
	IInvoiceExtractor extractor,
	TaxProfileCatalog profiles,
	TimeProvider clock)
{
	/// <summary>Processes the document and stores the outcome with its extraction, in one transaction.</summary>
	/// <param name="document">A document this worker holds a lease on.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>Where the document ended.</returns>
	/// <exception cref="LeaseLostException">The lease ran out and another worker took the document.</exception>
	public async Task<DocumentStatus> HandleAsync(Document document, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(document);

		var content = await files.ReadAsync(document.ContentHash, document.Format, cancellationToken).ConfigureAwait(false);
		var prepared = preprocessor.Prepare(content, document.Format, document.HasTextLayer);
		var result = await extractor.ExtractAsync(prepared, cancellationToken).ConfigureAwait(false);

		var now = clock.GetUtcNow();
		IReadOnlyList<ValidationIssue> issues = [];
		if (result.Invoice is { } invoice)
		{
			var country = await queue.CountryOfAsync(document.BatchId, cancellationToken).ConfigureAwait(false);
			issues = InvoiceValidator.Validate(invoice, profiles.For(country), DateOnly.FromDateTime(now.UtcDateTime)).Issues;
		}

		var outcome = ReviewPolicy.Decide(result.Invoice, issues, result.Confidence);
		queue.Add(Extraction.Record(
			document,
			result.Usage.Model,
			result.Invoice,
			result.Confidence,
			issues,
			result.Failure,
			result.Usage.InputTokens,
			result.Usage.OutputTokens,
			result.Usage.CostUsd,
			now));
		document.Complete(outcome, now);
		await queue.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return outcome;
	}
}

/// <summary>When a reading may skip the human.</summary>
public static class ReviewPolicy
{
	// The fields a wrong value in would make the exported row wrong. A model that marks one of them "low"
	// is saying it guessed, and a guess is never approved, even one that happens to pass every rule.
	private static readonly string[] KeyFields =
	[
		InvoiceFields.IssuerTaxId,
		InvoiceFields.Number,
		InvoiceFields.IssueDate,
		InvoiceFields.Subtotal,
		InvoiceFields.TaxTotal,
		InvoiceFields.Total,
	];

	/// <summary>Decides.</summary>
	/// <param name="invoice">What was read, if anything.</param>
	/// <param name="issues">The rules it broke.</param>
	/// <param name="confidence">The model's confidence per field.</param>
	/// <returns>Auto-approved, or needs review.</returns>
	public static DocumentStatus Decide(
		ExtractedInvoice? invoice,
		IReadOnlyList<ValidationIssue> issues,
		IReadOnlyDictionary<string, FieldConfidence> confidence)
	{
		ArgumentNullException.ThrowIfNull(issues);
		ArgumentNullException.ThrowIfNull(confidence);

		var unsure = KeyFields.Any(field => confidence.TryGetValue(field, out var level) && level == FieldConfidence.Low);
		return invoice is not null && issues.Count == 0 && !unsure
			? DocumentStatus.AutoApproved
			: DocumentStatus.NeedsReview;
	}
}
