namespace InvoiceExtractor.Application.Features.Review;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Application.Features.Documents;
using InvoiceExtractor.Application.Features.Processing;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;

/// <summary>Everything the review screen shows for one document.</summary>
/// <param name="Document">The document.</param>
/// <param name="Country">The batch's tax profile.</param>
/// <param name="TaxIdFormat">What a valid tax id looks like there, shown next to the field.</param>
/// <param name="Invoice">The reading to review (or the approved data, once approved).</param>
/// <param name="Issues">The rules it breaks.</param>
/// <param name="Confidence">The model's confidence per key field.</param>
/// <param name="Failure">Why nothing was read, when that is the case.</param>
/// <param name="Calls">Model calls made on it.</param>
/// <param name="CostUsd">What they cost.</param>
public sealed record ReviewResponse(
	DocumentResponse Document,
	string Country,
	string TaxIdFormat,
	ExtractedInvoice? Invoice,
	IReadOnlyList<ValidationIssue> Issues,
	IReadOnlyDictionary<string, FieldConfidence> Confidence,
	string? Failure,
	int Calls,
	decimal CostUsd);

/// <summary>The data a reviewer approves, corrections included.</summary>
/// <param name="Invoice">The invoice as approved.</param>
public sealed record ApproveRequest(ExtractedInvoice Invoice);

/// <summary>The review side's error codes.</summary>
public static class ReviewErrors
{
	/// <summary>Only a document waiting for review can be approved.</summary>
	public const string NotInReview = "document.not_in_review";

	/// <summary>The approval carried no invoice.</summary>
	public const string MissingInvoice = "review.missing_invoice";
}

/// <summary>The documents waiting for a person, oldest first.</summary>
/// <param name="documents">The repository.</param>
public sealed class GetReviewQueue(IDocumentRepository documents)
{
	// More than a day's work for one reviewer; the screen pages through it with previous/next.
	private const int QueueLimit = 500;

	/// <summary>Lists them.</summary>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The queue.</returns>
	public async Task<IReadOnlyList<DocumentResponse>> HandleAsync(CancellationToken cancellationToken) =>
		[.. (await documents.ListByStatusAsync(DocumentStatus.NeedsReview, QueueLimit, cancellationToken).ConfigureAwait(false))
			.Select(DocumentResponse.From)];
}

/// <summary>The most recent batches with their documents, for the upload screen.</summary>
/// <param name="documents">The repository.</param>
public sealed class ListBatches(IDocumentRepository documents)
{
	private const int RecentBatches = 20;

	/// <summary>Lists them.</summary>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>Newest first.</returns>
	public async Task<IReadOnlyList<BatchResponse>> HandleAsync(CancellationToken cancellationToken)
	{
		var batches = await documents.ListRecentBatchesAsync(RecentBatches, cancellationToken).ConfigureAwait(false);
		var items = (await documents.ListByBatchesAsync([.. batches.Select(static b => b.Id)], cancellationToken).ConfigureAwait(false))
			.ToLookup(static d => d.BatchId);
		return [.. batches.Select(b => new BatchResponse(b.Id.Value, b.Country, b.CreatedAt, [.. items[b.Id].Select(DocumentResponse.From)]))];
	}
}

/// <summary>Reads one document for the review screen.</summary>
/// <param name="documents">The repository.</param>
/// <param name="profiles">The country rules.</param>
/// <param name="clock">The clock.</param>
public sealed class GetDocumentReview(IDocumentRepository documents, TaxProfileCatalog profiles, TimeProvider clock)
{
	/// <summary>Reads it.</summary>
	/// <param name="id">The document.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The review, or <c>document.not_found</c>.</returns>
	public async Task<Result<ReviewResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
	{
		var document = await documents.GetAsync(new DocumentId(id), cancellationToken).ConfigureAwait(false);
		if (document is null)
		{
			return Error.NotFound(DocumentErrors.NotFound, $"Document {id} does not exist.");
		}

		return await ReviewBuilder.BuildAsync(document, documents, profiles, clock, cancellationToken).ConfigureAwait(false);
	}
}

/// <summary>A person approves a document in review, with their corrections.</summary>
/// <param name="documents">The repository.</param>
/// <param name="profiles">The country rules.</param>
/// <param name="clock">The clock.</param>
public sealed class ApproveDocument(IDocumentRepository documents, TaxProfileCatalog profiles, TimeProvider clock)
{
	/// <summary>Approves it.</summary>
	/// <param name="id">The document.</param>
	/// <param name="request">The approved data.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The review as approved, with any rule the approved data still breaks.</returns>
	public async Task<Result<ReviewResponse>> HandleAsync(Guid id, ApproveRequest? request, CancellationToken cancellationToken)
	{
		if (request?.Invoice is null)
		{
			return Error.Validation(ReviewErrors.MissingInvoice, "Send the invoice as approved.");
		}

		var document = await documents.FindTrackedAsync(new DocumentId(id), cancellationToken).ConfigureAwait(false);
		if (document is null)
		{
			return Error.NotFound(DocumentErrors.NotFound, $"Document {id} does not exist.");
		}

		if (document.Status != DocumentStatus.NeedsReview)
		{
			return Error.Conflict(ReviewErrors.NotInReview, $"Document {id} is {document.Status}, not waiting for review.");
		}

		document.Approve(request.Invoice with { Lines = request.Invoice.Lines ?? [] }, clock.GetUtcNow());
		await documents.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return await ReviewBuilder.BuildAsync(document, documents, profiles, clock, cancellationToken).ConfigureAwait(false);
	}
}

/// <summary>The original file, for the viewer.</summary>
/// <param name="Content">The bytes.</param>
/// <param name="ContentType">Its media type.</param>
/// <param name="FileName">The user's file name.</param>
public sealed record DocumentFile(byte[] Content, string ContentType, string FileName);

/// <summary>Reads a document's original file.</summary>
/// <param name="documents">The repository.</param>
/// <param name="files">The file store.</param>
public sealed class GetDocumentFile(IDocumentRepository documents, IFileStore files)
{
	/// <summary>Reads it.</summary>
	/// <param name="id">The document.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The file, or <c>document.not_found</c>.</returns>
	public async Task<Result<DocumentFile>> HandleAsync(Guid id, CancellationToken cancellationToken)
	{
		var document = await documents.GetAsync(new DocumentId(id), cancellationToken).ConfigureAwait(false);
		if (document is null)
		{
			return Error.NotFound(DocumentErrors.NotFound, $"Document {id} does not exist.");
		}

		var content = await files.ReadAsync(document.ContentHash, document.Format, cancellationToken).ConfigureAwait(false);
		var contentType = document.Format switch
		{
			DocumentFormat.Pdf => "application/pdf",
			DocumentFormat.Jpeg => "image/jpeg",
			DocumentFormat.Png => "image/png",
			_ => throw new InvalidOperationException($"Unknown format {document.Format}."),
		};
		return new DocumentFile(content, contentType, document.FileName);
	}
}

internal static class ReviewBuilder
{
	// What the screen shows: the approved data once a person approved it; before that, the last reading the
	// model returned (the retry's, when it made one), with the rules it broke.
	public static async Task<ReviewResponse> BuildAsync(
		Document document,
		IDocumentRepository documents,
		TaxProfileCatalog profiles,
		TimeProvider clock,
		CancellationToken cancellationToken)
	{
		var batch = await documents.GetBatchAsync(document.BatchId, cancellationToken).ConfigureAwait(false)
			?? throw new InvalidOperationException($"Batch {document.BatchId} of document {document.Id} is missing.");
		var profile = profiles.For(batch.Country);
		var extractions = await documents.ListExtractionsAsync(document.Id, cancellationToken).ConfigureAwait(false);
		var reading = extractions.LastOrDefault(static e => e.Invoice is not null);

		var invoice = document.ReviewedInvoice ?? reading?.Invoice;
		var issues = document.ReviewedInvoice is { } reviewed
			? InvoiceValidator.Validate(reviewed, profile, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)).Issues
			: reading?.Issues ?? [];

		return new ReviewResponse(
			DocumentResponse.From(document),
			batch.Country,
			profile.TaxIdFormat,
			invoice,
			issues,
			reading?.Confidence ?? new Dictionary<string, FieldConfidence>(),
			reading is null ? extractions.LastOrDefault()?.Failure : null,
			extractions.Count,
			extractions.Sum(static e => e.CostUsd));
	}
}
