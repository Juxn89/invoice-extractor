namespace InvoiceExtractor.Application.Features.Processing;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Tax;
using InvoiceExtractor.Domain.Validation;
using Microsoft.Extensions.Options;

/// <summary>One model call and the rules its reading broke.</summary>
/// <param name="Result">What the model returned, with its cost.</param>
/// <param name="Issues">The rules broken; empty when nothing was read.</param>
/// <param name="IsRetry">Whether this was the second reading, made with the first one's issues as feedback.</param>
public sealed record PipelineAttempt(InvoiceExtractionResult Result, IReadOnlyList<ValidationIssue> Issues, bool IsRetry);

/// <summary>Everything that happened to one document, and the verdict.</summary>
/// <param name="Attempts">One or two calls, in order.</param>
/// <param name="Final">The attempt the verdict stands on.</param>
/// <param name="Status">Auto-approved, or needs review.</param>
public sealed record PipelineOutcome(IReadOnlyList<PipelineAttempt> Attempts, PipelineAttempt Final, DocumentStatus Status)
{
	/// <summary>What all the calls cost together.</summary>
	public decimal CostUsd => Attempts.Sum(static a => a.Result.Usage.CostUsd);
}

/// <summary>
/// Read, validate, and when a rule fails read once more with the failures as feedback, then decide. The app
/// and the evals both run this, so the evals measure exactly what the app does.
/// </summary>
/// <param name="extractor">The model.</param>
/// <param name="options">Whether to retry.</param>
public sealed class ExtractionPipeline(IInvoiceExtractor extractor, IOptions<ProcessingOptions> options)
{
	/// <summary>Runs it.</summary>
	/// <param name="document">The prepared document.</param>
	/// <param name="profile">The batch country's rules.</param>
	/// <param name="today">The date the date rules compare with.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The attempts and the verdict.</returns>
	public async Task<PipelineOutcome> RunAsync(PreparedDocument document, ITaxProfile profile, DateOnly today, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(options);
		var first = await AttemptAsync(document, profile, today, feedback: [], cancellationToken).ConfigureAwait(false);
		List<PipelineAttempt> attempts = [first];
		var final = first;

		// A broken rule is either a misread, which a second look can fix, or a document wrong on paper, which
		// it must not "fix". The prompt draws that line; the evals' planted documents check it holds. Nothing
		// read at all (a refusal, a truncated answer) is not retried: there is nothing to point the model at.
		if (options.Value.RetryOnFailedRules && first.Result.Invoice is not null && first.Issues.Count > 0)
		{
			var retry = await AttemptAsync(document, profile, today, first.Issues, cancellationToken).ConfigureAwait(false);
			attempts.Add(retry);
			if (retry.Result.Invoice is not null)
			{
				final = retry;
			}
		}

		var status = ReviewPolicy.Decide(final.Result.Invoice, final.Issues, final.Result.Confidence);
		return new PipelineOutcome(attempts, final, status);
	}

	private async Task<PipelineAttempt> AttemptAsync(
		PreparedDocument document,
		ITaxProfile profile,
		DateOnly today,
		IReadOnlyList<ValidationIssue> feedback,
		CancellationToken cancellationToken)
	{
		var result = await extractor.ExtractAsync(document, feedback, cancellationToken).ConfigureAwait(false);
		IReadOnlyList<ValidationIssue> issues = result.Invoice is { } invoice
			? InvoiceValidator.Validate(invoice, profile, today).Issues
			: [];
		return new PipelineAttempt(result, issues, IsRetry: feedback.Count > 0);
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
