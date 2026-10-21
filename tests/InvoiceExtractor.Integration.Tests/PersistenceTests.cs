namespace InvoiceExtractor.Integration.Tests;

using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class PersistenceTests(PostgresFixture fixture) : IAsyncLifetime
{
	private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

	public Task InitializeAsync() => fixture.ResetAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private static (Batch Batch, Document Document) NewDocument(ContentHash hash)
	{
		var batch = Batch.Start(1, Now);
		return (batch, Document.Queue(batch.Id, "factura.pdf", hash, DocumentFormat.Pdf, 1, true, 100, Now));
	}

	[Fact]
	public async Task A_document_round_trips_with_its_typed_ids_hash_and_enums()
	{
		var (batch, document) = NewDocument(ContentHash.Compute("round trip"u8));
		await using (var db = fixture.CreateContext())
		{
			var repository = new DocumentRepository(db);
			repository.Add(batch, [document]);
			await repository.SaveChangesAsync(CancellationToken.None);
		}

		await using var read = fixture.CreateContext();
		var stored = await new DocumentRepository(read).GetAsync(document.Id, CancellationToken.None);

		Assert.NotNull(stored);
		Assert.Equal(document.ContentHash, stored.ContentHash);
		Assert.Equal(DocumentStatus.Queued, stored.Status);
		Assert.Equal(batch.Id, stored.BatchId);
	}

	[Fact]
	public async Task The_unique_hash_index_turns_a_concurrent_duplicate_into_a_typed_exception()
	{
		var hash = ContentHash.Compute("same bytes"u8);
		var (firstBatch, first) = NewDocument(hash);
		var (secondBatch, second) = NewDocument(hash);

		// Two requests that both looked and saw nothing; the first commits.
		await using var winner = fixture.CreateContext();
		await using var loser = fixture.CreateContext();
		var winnerRepository = new DocumentRepository(winner);
		var loserRepository = new DocumentRepository(loser);
		winnerRepository.Add(firstBatch, [first]);
		loserRepository.Add(secondBatch, [second]);
		await winnerRepository.SaveChangesAsync(CancellationToken.None);

		await Assert.ThrowsAsync<DuplicateContentException>(() => loserRepository.SaveChangesAsync(CancellationToken.None));
		Assert.Empty(loser.ChangeTracker.Entries());
	}
}
