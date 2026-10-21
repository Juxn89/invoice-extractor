namespace InvoiceExtractor.Infrastructure.Persistence;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal sealed class DocumentRepository(InvoiceExtractorDbContext db) : IDocumentRepository
{
	public async Task<IReadOnlyDictionary<ContentHash, Document>> FindByHashesAsync(
		IReadOnlyCollection<ContentHash> hashes,
		CancellationToken cancellationToken)
	{
		var found = await db.Documents.AsNoTracking()
			.Where(d => hashes.Contains(d.ContentHash))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return found.ToDictionary(static d => d.ContentHash);
	}

	public Task<Document?> GetAsync(DocumentId id, CancellationToken cancellationToken) =>
		db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

	public Task<Batch?> GetBatchAsync(BatchId id, CancellationToken cancellationToken) =>
		db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

	public async Task<IReadOnlyList<Document>> ListByBatchAsync(BatchId id, CancellationToken cancellationToken) =>
		await db.Documents.AsNoTracking()
			.Where(d => d.BatchId == id)
			.OrderBy(d => d.CreatedAt)
			.ThenBy(d => d.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public void Add(Batch batch, IReadOnlyCollection<Document> documents)
	{
		db.Batches.Add(batch);
		db.Documents.AddRange(documents);
	}

	public async Task SaveChangesAsync(CancellationToken cancellationToken)
	{
		try
		{
			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
		{
			// The failed rows stay tracked; a retry in this scope must start from a clean context.
			db.ChangeTracker.Clear();
			throw new DuplicateContentException("A file in the batch was stored by another request first.", exception);
		}
	}
}
