namespace InvoiceExtractor.Infrastructure.Persistence;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;
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

	public async Task<IReadOnlyList<Document>> ListByStatusAsync(DocumentStatus status, int limit, CancellationToken cancellationToken) =>
		await db.Documents.AsNoTracking()
			.Where(d => d.Status == status)
			.OrderBy(d => d.CreatedAt)
			.ThenBy(d => d.Id)
			.Take(limit)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<Batch>> ListRecentBatchesAsync(int limit, CancellationToken cancellationToken) =>
		await db.Batches.AsNoTracking()
			.OrderByDescending(b => b.CreatedAt)
			.Take(limit)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<Document>> ListByBatchesAsync(IReadOnlyCollection<BatchId> ids, CancellationToken cancellationToken) =>
		await db.Documents.AsNoTracking()
			.Where(d => ids.Contains(d.BatchId))
			.OrderBy(d => d.CreatedAt)
			.ThenBy(d => d.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<Extraction>> ListExtractionsAsync(DocumentId id, CancellationToken cancellationToken) =>
		await db.Extractions.AsNoTracking()
			.Where(e => e.DocumentId == id)
			.OrderBy(e => e.CreatedAt)
			.ThenBy(e => e.IsRetry)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<Document>> ListApprovedAsync(BatchId? batch, CancellationToken cancellationToken) =>
		await db.Documents.AsNoTracking()
			.Where(d => d.Status == DocumentStatus.AutoApproved || d.Status == DocumentStatus.Approved)
			.Where(d => batch == null || d.BatchId == batch)
			.OrderBy(d => d.CreatedAt)
			.ThenBy(d => d.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<IReadOnlyDictionary<BatchId, string>> CountriesOfAsync(IReadOnlyCollection<BatchId> ids, CancellationToken cancellationToken) =>
		await db.Batches.AsNoTracking()
			.Where(b => ids.Contains(b.Id))
			.ToDictionaryAsync(static b => b.Id, static b => b.Country, cancellationToken)
			.ConfigureAwait(false);

	// Read whole and picked in memory: a batch export is tens of documents with one or two readings each.
	public async Task<IReadOnlyDictionary<DocumentId, ExtractedInvoice>> FinalReadingsAsync(IReadOnlyCollection<DocumentId> ids, CancellationToken cancellationToken)
	{
		var readings = await db.Extractions.AsNoTracking()
			.Where(e => ids.Contains(e.DocumentId) && e.Invoice != null)
			.OrderBy(e => e.CreatedAt)
			.ThenBy(e => e.IsRetry)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return readings
			.GroupBy(static e => e.DocumentId)
			.ToDictionary(static g => g.Key, static g => g.Last().Invoice!);
	}

	public async Task<IReadOnlyDictionary<DocumentStatus, int>> CountByStatusAsync(CancellationToken cancellationToken) =>
		await db.Documents.AsNoTracking()
			.GroupBy(static d => d.Status)
			.Select(static g => new { Status = g.Key, Count = g.Count() })
			.ToDictionaryAsync(static g => g.Status, static g => g.Count, cancellationToken)
			.ConfigureAwait(false);

	public async Task<ExtractionTotals> ExtractionTotalsAsync(DateTimeOffset? since, CancellationToken cancellationToken)
	{
		var extractions = db.Extractions.AsNoTracking().Where(e => since == null || e.CreatedAt >= since);
		var calls = await extractions.CountAsync(cancellationToken).ConfigureAwait(false);
		var retries = await extractions.CountAsync(static e => e.IsRetry, cancellationToken).ConfigureAwait(false);
		var documents = await extractions.Select(static e => e.DocumentId).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);
		var cost = await extractions.SumAsync(static e => e.CostUsd, cancellationToken).ConfigureAwait(false);
		return new ExtractionTotals(calls, retries, documents, cost);
	}

	public Task<Document?> FindTrackedAsync(DocumentId id, CancellationToken cancellationToken) =>
		db.Documents.AsTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

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
