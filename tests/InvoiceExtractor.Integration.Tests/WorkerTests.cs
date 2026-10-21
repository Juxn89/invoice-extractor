namespace InvoiceExtractor.Integration.Tests;

using System.Collections.Concurrent;
using System.Text;
using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;
using InvoiceExtractor.Infrastructure;
using InvoiceExtractor.Infrastructure.Persistence;
using InvoiceExtractor.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// The real queue, worker and database; only the model, the file store and the preprocessor are fakes, so no
// test here costs anything.
[Collection(PostgresCollection.Name)]
public sealed class WorkerTests(PostgresFixture fixture) : IAsyncLifetime
{
	private const int MaxAttempts = 3;

	private readonly FakeModel _model = new();

	public Task InitializeAsync() => fixture.ResetAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task Four_workers_process_a_hundred_documents_each_exactly_once()
	{
		await SeedAsync(100);
		await using var services = Services();
		var worker = services.GetRequiredService<ExtractionWorker>();

		async Task DrainAsync()
		{
			while (await worker.ProcessNextAsync(CancellationToken.None))
			{
			}
		}

		await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(DrainAsync)));

		await using var db = fixture.CreateContext();
		Assert.Equal(100, await db.Documents.CountAsync(static d => d.Status == DocumentStatus.AutoApproved && d.Attempts == 1));
		Assert.Equal(100, await db.Extractions.CountAsync());
		Assert.Equal(100, await db.Extractions.Select(static e => e.DocumentId).Distinct().CountAsync());
		Assert.All(_model.CallsPerDocument.Values, static calls => Assert.Equal(1, calls));
		Assert.Equal(100, _model.CallsPerDocument.Count);

		// Without this the test would also pass if the workers had taken turns.
		Assert.True(_model.MaxConcurrentCalls > 1, $"the workers never overlapped (max {_model.MaxConcurrentCalls} at once)");
	}

	[Fact]
	public async Task A_crashed_workers_document_is_taken_again_once_its_lease_runs_out_but_not_before()
	{
		var now = DateTimeOffset.UtcNow;
		var (expired, held) = (await SeedAsync(2)) switch { var d => (d[0], d[1]) };
		await UpdateAsync(expired.Id, d => d.Claim(now.AddHours(-1), now.AddMinutes(-1)));
		await UpdateAsync(held.Id, d => d.Claim(now, now.AddHours(1)));
		await using var services = Services();
		var worker = services.GetRequiredService<ExtractionWorker>();

		Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
		Assert.False(await worker.ProcessNextAsync(CancellationToken.None));

		await using var db = fixture.CreateContext();
		var recovered = await db.Documents.SingleAsync(d => d.Id == expired.Id);
		Assert.Equal((DocumentStatus.AutoApproved, 2), (recovered.Status, recovered.Attempts));
		Assert.Equal(DocumentStatus.Processing, (await db.Documents.SingleAsync(d => d.Id == held.Id)).Status);
	}

	[Fact]
	public async Task A_transient_failure_puts_the_document_back_until_it_runs_out_of_attempts()
	{
		var document = (await SeedAsync(1))[0];
		_model.FailFor.Add(document.ContentHash.Value);
		await using var services = Services();
		var worker = services.GetRequiredService<ExtractionWorker>();

		for (var attempt = 1; attempt <= MaxAttempts; attempt++)
		{
			Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
			await using var db = fixture.CreateContext();
			var stored = await db.Documents.SingleAsync(d => d.Id == document.Id);
			Assert.Equal(attempt < MaxAttempts ? DocumentStatus.Queued : DocumentStatus.Failed, stored.Status);
			Assert.Equal(attempt, stored.Attempts);
		}

		Assert.False(await worker.ProcessNextAsync(CancellationToken.None));
		Assert.Equal(MaxAttempts, _model.CallsPerDocument[document.ContentHash.Value]);
	}

	[Fact]
	public async Task A_document_whose_workers_keep_dying_is_failed_without_another_model_call()
	{
		var now = DateTimeOffset.UtcNow;
		var document = (await SeedAsync(1))[0];

		// Three workers took it and never came back: three attempts used, the last lease long gone.
		await UpdateAsync(document.Id, d =>
		{
			for (var i = 0; i < MaxAttempts; i++)
			{
				d.Claim(now.AddHours(-1).AddMinutes(i * 2), now.AddHours(-1).AddMinutes((i * 2) + 1));
			}
		});
		await using var services = Services();

		Assert.True(await services.GetRequiredService<ExtractionWorker>().ProcessNextAsync(CancellationToken.None));

		await using var db = fixture.CreateContext();
		Assert.Equal(DocumentStatus.Failed, (await db.Documents.SingleAsync(d => d.Id == document.Id)).Status);
		Assert.Empty(_model.CallsPerDocument);
	}

	[Fact]
	public async Task Workers_stop_claiming_once_the_daily_budget_is_spent()
	{
		await SeedAsync(2);
		_model.CostUsd = 0.002m;
		await using var services = Services(("Processing:DailyBudgetUsd", "0.001"));
		var worker = services.GetRequiredService<ExtractionWorker>();

		Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
		Assert.False(await worker.ProcessNextAsync(CancellationToken.None));

		await using var db = fixture.CreateContext();
		Assert.Equal(1, await db.Documents.CountAsync(static d => d.Status == DocumentStatus.Queued));
		Assert.Single(_model.CallsPerDocument);
	}

	[Fact]
	public async Task A_worker_whose_lease_ran_out_cannot_overwrite_the_outcome_of_the_one_that_took_over()
	{
		var now = DateTimeOffset.UtcNow;
		await SeedAsync(1);

		await using var slowDb = fixture.CreateContext();
		await using var fastDb = fixture.CreateContext();
		var slow = new PgDocumentQueue(slowDb);
		var fast = new PgDocumentQueue(fastDb);

		var slowCopy = (await slow.ClaimNextAsync(now, now.AddSeconds(1), CancellationToken.None))!;
		var fastCopy = (await fast.ClaimNextAsync(now.AddSeconds(2), now.AddMinutes(5), CancellationToken.None))!;
		fastCopy.Complete(DocumentStatus.NeedsReview, now.AddSeconds(3));
		await fast.SaveChangesAsync(CancellationToken.None);

		slowCopy.Complete(DocumentStatus.AutoApproved, now.AddSeconds(4));
		await Assert.ThrowsAsync<LeaseLostException>(() => slow.SaveChangesAsync(CancellationToken.None));

		await using var db = fixture.CreateContext();
		Assert.Equal(DocumentStatus.NeedsReview, (await db.Documents.SingleAsync()).Status);
	}

	private ServiceProvider Services(params (string Key, string Value)[] settings)
	{
		var values = new Dictionary<string, string?>
		{
			["ConnectionStrings:invoicesdb"] = fixture.ConnectionString,
			["FileStore:RootPath"] = fixture.StorageRoot,
			["Processing:MaxAttempts"] = MaxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
		};
		foreach (var (key, value) in settings)
		{
			values[key] = value;
		}

		var services = new ServiceCollection()
			.AddLogging()
			.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

		// Registered last, so they win over the real ones.
		services.AddSingleton<IInvoiceExtractor>(_model);
		services.AddSingleton<IFileStore, HashFileStore>();
		services.AddSingleton<IDocumentPreprocessor, PassThroughPreprocessor>();
		return services.BuildServiceProvider();
	}

	private async Task<List<Document>> SeedAsync(int count)
	{
		var now = DateTimeOffset.UtcNow;
		var batch = Batch.Start(count, "NI", now);
		var documents = Enumerable.Range(0, count)
			.Select(i => Document.Queue(batch.Id, $"{i}.png", ContentHash.Compute(Guid.NewGuid().ToByteArray()), DocumentFormat.Png, 1, false, 100, now.AddMilliseconds(i)))
			.ToList();

		await using var db = fixture.CreateContext();
		var repository = new DocumentRepository(db);
		repository.Add(batch, documents);
		await repository.SaveChangesAsync(CancellationToken.None);
		return documents;
	}

	private async Task UpdateAsync(DocumentId id, Action<Document> change)
	{
		await using var db = fixture.CreateContext();
		var document = await db.Documents.AsTracking().SingleAsync(d => d.Id == id);
		change(document);
		await db.SaveChangesAsync();
	}

	/// <summary>The "file" is the document's hash, so the model knows which document it was given.</summary>
	private sealed class HashFileStore : IFileStore
	{
		public Task SaveAsync(ContentHash hash, DocumentFormat format, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
			Task.CompletedTask;

		public Task<byte[]> ReadAsync(ContentHash hash, DocumentFormat format, CancellationToken cancellationToken) =>
			Task.FromResult(Encoding.ASCII.GetBytes(hash.Value));
	}

	private sealed class PassThroughPreprocessor : IDocumentPreprocessor
	{
		public PreparedDocument Prepare(ReadOnlyMemory<byte> content, DocumentFormat format, bool hasTextLayer) =>
			new PreparedImage(content, 1, 1);
	}

	/// <summary>Answers with a consistent invoice after a short pause, counting calls per document and overlap.</summary>
	private sealed class FakeModel : IInvoiceExtractor
	{
		private int _inFlight;
		private int _maxInFlight;

		public ConcurrentDictionary<string, int> CallsPerDocument { get; } = new();

		public ConcurrentBag<string> FailFor { get; } = [];

		public decimal CostUsd { get; set; } = 0.0005m;

		public int MaxConcurrentCalls => Volatile.Read(ref _maxInFlight);

		public bool IsConfigured => true;

		public async Task<InvoiceExtractionResult> ExtractAsync(PreparedDocument document, IReadOnlyList<ValidationIssue> feedback, CancellationToken cancellationToken)
		{
			var key = Encoding.ASCII.GetString(((PreparedImage)document).Jpeg.Span);
			CallsPerDocument.AddOrUpdate(key, 1, static (_, calls) => calls + 1);

			var inFlight = Interlocked.Increment(ref _inFlight);
			InterlockedMax(ref _maxInFlight, inFlight);
			try
			{
				// Long enough for the other workers to claim while this one is busy.
				await Task.Delay(TimeSpan.FromMilliseconds(15), cancellationToken);
			}
			finally
			{
				Interlocked.Decrement(ref _inFlight);
			}

			if (FailFor.Contains(key))
			{
				throw new HttpRequestException("503 from the fake model");
			}

			return new InvoiceExtractionResult(
				new ExtractedInvoice("Distribuidora El Mirador, S.A.", "J0310000252297", null, "F-000001", new DateOnly(2026, 10, 2), "NIO", [new("Café", 1, 100m, 100m)], 100m, 15m, 115m),
				new Dictionary<string, FieldConfidence> { [InvoiceFields.Total] = FieldConfidence.High },
				new ExtractionUsage("fake", 100, 10, CostUsd),
				null);
		}

		private static void InterlockedMax(ref int target, int value)
		{
			int current;
			while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
			{
			}
		}
	}
}
