namespace InvoiceExtractor.Application.Abstractions;

using InvoiceExtractor.Domain.Invoices;

/// <summary>How sure the model says it is of a field. A hint for the reviewer, never a reason to approve.</summary>
public enum FieldConfidence
{
	/// <summary>Hard to read, inferred or guessed.</summary>
	Low = 1,

	/// <summary>Read, with some doubt.</summary>
	Medium = 2,

	/// <summary>Printed clearly.</summary>
	High = 3,
}

/// <summary>What one model call cost.</summary>
/// <param name="Model">The model id.</param>
/// <param name="InputTokens">Billed input tokens.</param>
/// <param name="OutputTokens">Billed output tokens, thinking included.</param>
/// <param name="CostUsd">The price of both, from the configured rates.</param>
public sealed record ExtractionUsage(string Model, long InputTokens, long OutputTokens, decimal CostUsd);

/// <summary>One extraction.</summary>
/// <param name="Invoice">What was read, or <see langword="null"/> when nothing usable came back.</param>
/// <param name="Confidence">
/// The model's confidence per key field, keyed by the validator's field names (<c>InvoiceFields</c>), so the
/// review screen can show both on the same field.
/// </param>
/// <param name="Usage">What the call cost; present even when it failed, since a failed call can be billed.</param>
/// <param name="Failure">Why <paramref name="Invoice"/> is missing: a refusal, a truncated or malformed answer.</param>
public sealed record InvoiceExtractionResult(
	ExtractedInvoice? Invoice,
	IReadOnlyDictionary<string, FieldConfidence> Confidence,
	ExtractionUsage Usage,
	string? Failure);

/// <summary>Reads an invoice with a language model.</summary>
public interface IInvoiceExtractor
{
	/// <summary>Reads one prepared document.</summary>
	/// <param name="document">Text, a PDF or an image (see <see cref="IDocumentPreprocessor"/>).</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The extraction, with its cost.</returns>
	Task<InvoiceExtractionResult> ExtractAsync(PreparedDocument document, CancellationToken cancellationToken);
}
