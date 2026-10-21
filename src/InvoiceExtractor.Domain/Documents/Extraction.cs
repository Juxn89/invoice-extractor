namespace InvoiceExtractor.Domain.Documents;

using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;

/// <summary>
/// One model call on one document and everything that came of it: what was read, how sure the model said it
/// was, which rules failed and what it cost. Kept for every attempt, so the cost per document and the review
/// screen's highlighted fields come from the same record.
/// </summary>
public sealed class Extraction
{
	/// <summary>Longest model id kept.</summary>
	public const int ModelMaxLength = 100;

	/// <summary>Longest failure reason kept.</summary>
	public const int FailureMaxLength = 2000;

	private Extraction()
	{
		Model = string.Empty;
		Confidence = new Dictionary<string, FieldConfidence>();
		Issues = [];
	}

	/// <summary>The id.</summary>
	public ExtractionId Id { get; private set; }

	/// <summary>The document read.</summary>
	public DocumentId DocumentId { get; private set; }

	/// <summary>Which attempt on the document this was, from 1.</summary>
	public int Attempt { get; private set; }

	/// <summary>The model id.</summary>
	public string Model { get; private set; }

	/// <summary>What was read, or <see langword="null"/> when the call returned nothing usable.</summary>
	public ExtractedInvoice? Invoice { get; private set; }

	/// <summary>The model's confidence per key field.</summary>
	public IReadOnlyDictionary<string, FieldConfidence> Confidence { get; private set; }

	/// <summary>The rules the reading broke.</summary>
	public IReadOnlyList<ValidationIssue> Issues { get; private set; }

	/// <summary>Why nothing usable came back, when it did not.</summary>
	public string? Failure { get; private set; }

	/// <summary>Whether this was the second reading, made with the first one's failed checks as feedback.</summary>
	public bool IsRetry { get; private set; }

	/// <summary>Billed input tokens.</summary>
	public long InputTokens { get; private set; }

	/// <summary>Billed output tokens, thinking included.</summary>
	public long OutputTokens { get; private set; }

	/// <summary>What the call cost.</summary>
	public decimal CostUsd { get; private set; }

	/// <summary>When it was recorded.</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Records one call.</summary>
	/// <param name="document">The document, while it is being processed.</param>
	/// <param name="model">The model id.</param>
	/// <param name="invoice">What was read, if anything.</param>
	/// <param name="confidence">The model's confidence per key field.</param>
	/// <param name="issues">The rules the reading broke.</param>
	/// <param name="failure">Why nothing usable came back.</param>
	/// <param name="isRetry">Whether it was the retry with feedback.</param>
	/// <param name="inputTokens">Billed input tokens.</param>
	/// <param name="outputTokens">Billed output tokens.</param>
	/// <param name="costUsd">The call's cost.</param>
	/// <param name="now">The current time.</param>
	/// <returns>The extraction.</returns>
	public static Extraction Record(
		Document document,
		string model,
		ExtractedInvoice? invoice,
		IReadOnlyDictionary<string, FieldConfidence> confidence,
		IReadOnlyList<ValidationIssue> issues,
		string? failure,
		bool isRetry,
		long inputTokens,
		long outputTokens,
		decimal costUsd,
		DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentException.ThrowIfNullOrWhiteSpace(model);
		ArgumentOutOfRangeException.ThrowIfNegative(costUsd);
		if (invoice is null && string.IsNullOrWhiteSpace(failure))
		{
			throw new ArgumentException("An extraction without an invoice must say why.", nameof(failure));
		}

		return new Extraction
		{
			Id = ExtractionId.New(),
			DocumentId = document.Id,
			Attempt = document.Attempts,
			Model = model,
			Invoice = invoice,
			Confidence = confidence,
			Issues = issues,
			Failure = failure is { Length: > FailureMaxLength } ? failure[..FailureMaxLength] : failure,
			IsRetry = isRetry,
			InputTokens = inputTokens,
			OutputTokens = outputTokens,
			CostUsd = costUsd,
			CreatedAt = now,
		};
	}
}
