namespace InvoiceExtractor.Application.Features.Documents;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Domain.Documents;

/// <summary>A document as the API returns it.</summary>
/// <param name="Id">The id.</param>
/// <param name="BatchId">The upload it came in.</param>
/// <param name="FileName">The user's file name.</param>
/// <param name="Format">The detected type.</param>
/// <param name="Pages">Page count.</param>
/// <param name="HasTextLayer">Whether the PDF's text is readable without vision.</param>
/// <param name="SizeBytes">The file's size.</param>
/// <param name="Status">Where it is.</param>
/// <param name="Attempts">Extraction attempts so far.</param>
/// <param name="CreatedAt">When it was uploaded.</param>
/// <param name="ProcessedAt">When processing last finished.</param>
public sealed record DocumentResponse(
	Guid Id,
	Guid BatchId,
	string FileName,
	DocumentFormat Format,
	int Pages,
	bool HasTextLayer,
	long SizeBytes,
	DocumentStatus Status,
	int Attempts,
	DateTimeOffset CreatedAt,
	DateTimeOffset? ProcessedAt)
{
	/// <summary>Maps a document.</summary>
	/// <param name="document">The document.</param>
	/// <returns>The response.</returns>
	public static DocumentResponse From(Document document)
	{
		ArgumentNullException.ThrowIfNull(document);
		return new(
			document.Id.Value,
			document.BatchId.Value,
			document.FileName,
			document.Format,
			document.Pages,
			document.HasTextLayer,
			document.SizeBytes,
			document.Status,
			document.Attempts,
			document.CreatedAt,
			document.ProcessedAt);
	}
}

/// <summary>A batch and its documents.</summary>
/// <param name="Id">The id.</param>
/// <param name="Country">The tax profile its documents are validated with.</param>
/// <param name="CreatedAt">When it was uploaded.</param>
/// <param name="Documents">Its documents, oldest first.</param>
public sealed record BatchResponse(Guid Id, string Country, DateTimeOffset CreatedAt, IReadOnlyList<DocumentResponse> Documents);

/// <summary>Reads one document.</summary>
/// <param name="documents">The repository.</param>
public sealed class GetDocument(IDocumentRepository documents)
{
	/// <summary>Reads it.</summary>
	/// <param name="id">The document's id.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The document, or <c>document.not_found</c>.</returns>
	public async Task<Result<DocumentResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
	{
		var document = await documents.GetAsync(new DocumentId(id), cancellationToken).ConfigureAwait(false);
		return document is null
			? Error.NotFound(DocumentErrors.NotFound, $"Document {id} does not exist.")
			: DocumentResponse.From(document);
	}
}

/// <summary>Reads a batch with its documents: what the UI polls while a batch is processed.</summary>
/// <param name="documents">The repository.</param>
public sealed class GetBatch(IDocumentRepository documents)
{
	/// <summary>Reads it.</summary>
	/// <param name="id">The batch's id.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The batch, or <c>batch.not_found</c>.</returns>
	public async Task<Result<BatchResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
	{
		var batchId = new BatchId(id);
		var batch = await documents.GetBatchAsync(batchId, cancellationToken).ConfigureAwait(false);
		if (batch is null)
		{
			return Error.NotFound(DocumentErrors.BatchNotFound, $"Batch {id} does not exist.");
		}

		var items = await documents.ListByBatchAsync(batchId, cancellationToken).ConfigureAwait(false);
		return new BatchResponse(batch.Id.Value, batch.Country, batch.CreatedAt, [.. items.Select(DocumentResponse.From)]);
	}
}

/// <summary>The read side's error codes.</summary>
public static class DocumentErrors
{
	/// <summary>No document with that id.</summary>
	public const string NotFound = "document.not_found";

	/// <summary>No batch with that id.</summary>
	public const string BatchNotFound = "batch.not_found";
}
