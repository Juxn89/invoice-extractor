namespace InvoiceExtractor.Infrastructure.Persistence;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Domain.Documents;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The queue is the documents table itself. A claim is one statement: pick the oldest claimable row, lock it,
/// and mark it taken. <c>FOR UPDATE SKIP LOCKED</c> makes concurrent workers step over rows another worker
/// is locking instead of waiting for them or taking them twice. No broker, and nothing to keep in sync with
/// the database. Once taken, the lease (not the lock) is what holds the row: the lock lasts one statement.
/// </summary>
/// <param name="db">The unit of work.</param>
internal sealed class PgDocumentQueue(InvoiceExtractorDbContext db) : IDocumentQueue
{
	// The statuses are stored by name (see DocumentConfiguration). The row's xmin is returned explicitly:
	// RETURNING * leaves out system columns, and EF needs it as the concurrency token of the tracked row.
	private static readonly string ClaimSql = $"""
		UPDATE documents AS d
		SET status = '{nameof(DocumentStatus.Processing)}', attempts = d.attempts + 1, lease_until = @lease_until
		WHERE d.id = (
			SELECT id FROM documents
			WHERE status = '{nameof(DocumentStatus.Queued)}'
				OR (status = '{nameof(DocumentStatus.Processing)}' AND lease_until < @now)
			ORDER BY created_at, id
			LIMIT 1
			FOR UPDATE SKIP LOCKED)
		RETURNING d.*, d.xmin
		""";

	public async Task<Document?> ClaimNextAsync(DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
	{
		var claimed = await db.Documents
			.FromSqlRaw(ClaimSql, new Npgsql.NpgsqlParameter("now", now), new Npgsql.NpgsqlParameter("lease_until", leaseUntil))
			.AsTracking()
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return claimed.SingleOrDefault();
	}

	public Task<Document?> FindAsync(DocumentId id, CancellationToken cancellationToken) =>
		db.Documents.AsTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

	public Task<string> CountryOfAsync(BatchId id, CancellationToken cancellationToken) =>
		db.Batches.Where(b => b.Id == id).Select(static b => b.Country).SingleAsync(cancellationToken);

	public Task<decimal> SpentSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
		db.Extractions.Where(e => e.CreatedAt >= since).SumAsync(static e => e.CostUsd, cancellationToken);

	public void Add(Extraction extraction) => db.Extractions.Add(extraction);

	public async Task SaveChangesAsync(CancellationToken cancellationToken)
	{
		try
		{
			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (DbUpdateConcurrencyException exception)
		{
			db.ChangeTracker.Clear();
			throw new LeaseLostException("The document changed since it was claimed: another worker took it over.", exception);
		}
	}
}
