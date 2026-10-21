namespace InvoiceExtractor.Application.Tests;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Domain.Documents;

internal sealed class FakeDocumentRepository : IDocumentRepository
{
	private readonly List<(Batch Batch, IReadOnlyCollection<Document> Documents)> _staged = [];

	public List<Batch> Batches { get; } = [];

	public List<Document> Documents { get; } = [];

	/// <summary>Documents another request "commits" just before this one saves, to provoke a conflict.</summary>
	public List<Document> CommittedByOthersOnNextSave { get; } = [];

	public int Saves { get; private set; }

	public Task<IReadOnlyDictionary<ContentHash, Document>> FindByHashesAsync(IReadOnlyCollection<ContentHash> hashes, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyDictionary<ContentHash, Document>>(
			Documents.Where(d => hashes.Contains(d.ContentHash)).ToDictionary(static d => d.ContentHash));

	public Task<Document?> GetAsync(DocumentId id, CancellationToken cancellationToken) =>
		Task.FromResult(Documents.FirstOrDefault(d => d.Id == id));

	public Task<Batch?> GetBatchAsync(BatchId id, CancellationToken cancellationToken) =>
		Task.FromResult(Batches.FirstOrDefault(b => b.Id == id));

	public Task<IReadOnlyList<Document>> ListByBatchAsync(BatchId id, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<Document>>([.. Documents.Where(d => d.BatchId == id)]);

	public Task<IReadOnlyList<Document>> ListByStatusAsync(DocumentStatus status, int limit, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<Document>>([.. Documents.Where(d => d.Status == status).Take(limit)]);

	public Task<IReadOnlyList<Batch>> ListRecentBatchesAsync(int limit, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<Batch>>([.. Batches.OrderByDescending(static b => b.CreatedAt).Take(limit)]);

	public Task<IReadOnlyList<Document>> ListByBatchesAsync(IReadOnlyCollection<BatchId> ids, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<Document>>([.. Documents.Where(d => ids.Contains(d.BatchId))]);

	public Task<IReadOnlyList<Extraction>> ListExtractionsAsync(DocumentId id, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<Extraction>>([]);

	public Task<Document?> FindTrackedAsync(DocumentId id, CancellationToken cancellationToken) => GetAsync(id, cancellationToken);

	public void Add(Batch batch, IReadOnlyCollection<Document> documents) => _staged.Add((batch, documents));

	public Task SaveChangesAsync(CancellationToken cancellationToken)
	{
		Saves++;
		if (CommittedByOthersOnNextSave.Count > 0)
		{
			Documents.AddRange(CommittedByOthersOnNextSave);
			CommittedByOthersOnNextSave.Clear();
			var conflict = _staged.SelectMany(static s => s.Documents).Any(s => Documents.Any(d => d.ContentHash == s.ContentHash));
			if (conflict)
			{
				_staged.Clear();
				throw new DuplicateContentException();
			}
		}

		foreach (var (batch, documents) in _staged)
		{
			Batches.Add(batch);
			Documents.AddRange(documents);
		}

		_staged.Clear();
		return Task.CompletedTask;
	}
}

internal sealed class FakeFileStore : IFileStore
{
	public Dictionary<ContentHash, byte[]> Files { get; } = [];

	public Task SaveAsync(ContentHash hash, DocumentFormat format, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
	{
		Files[hash] = content.ToArray();
		return Task.CompletedTask;
	}

	public Task<byte[]> ReadAsync(ContentHash hash, DocumentFormat format, CancellationToken cancellationToken) =>
		Files.TryGetValue(hash, out var content) ? Task.FromResult(content) : throw new FileNotFoundException(hash.Value);
}

/// <summary>Every file is a readable one-page document unless told otherwise.</summary>
internal sealed class FakeInspector : IDocumentInspector
{
	public Dictionary<string, Result<DocumentInspection>> ByContent { get; } = [];

	public Result<DocumentInspection> Inspect(ReadOnlyMemory<byte> content, DocumentFormat format) =>
		ByContent.TryGetValue(Convert.ToBase64String(content.Span), out var result)
			? result
			: new DocumentInspection(1, format == DocumentFormat.Pdf);
}

internal static class Files
{
	public static byte[] Pdf(string body) => [.. "%PDF-1.7\n"u8, .. System.Text.Encoding.ASCII.GetBytes(body)];

	public static byte[] Jpeg(string body) => [0xFF, 0xD8, 0xFF, 0xE0, .. System.Text.Encoding.ASCII.GetBytes(body)];

	public static byte[] Png(string body) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. System.Text.Encoding.ASCII.GetBytes(body)];
}

internal sealed class FakeQueue : IDocumentQueue
{
	private readonly List<Extraction> _staged = [];

	public Dictionary<BatchId, string> Countries { get; } = [];

	public List<Extraction> Saved { get; } = [];

	public int Saves { get; private set; }

	public Task<Document?> ClaimNextAsync(DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken) =>
		Task.FromResult<Document?>(null);

	public Task<Document?> FindAsync(DocumentId id, CancellationToken cancellationToken) => Task.FromResult<Document?>(null);

	public Task<string> CountryOfAsync(BatchId id, CancellationToken cancellationToken) => Task.FromResult(Countries[id]);

	public Task<decimal> SpentSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(Saved.Sum(static e => e.CostUsd));

	public void Add(Extraction extraction) => _staged.Add(extraction);

	public Task SaveChangesAsync(CancellationToken cancellationToken)
	{
		Saves++;
		Saved.AddRange(_staged);
		_staged.Clear();
		return Task.CompletedTask;
	}
}

internal sealed class FakePreprocessor : IDocumentPreprocessor
{
	public PreparedDocument Prepare(ReadOnlyMemory<byte> content, DocumentFormat format, bool hasTextLayer) =>
		new PreparedImage(content, 10, 10);
}

/// <summary>Answers each call with the next response in line (the last one repeats), recording the feedback it got.</summary>
internal sealed class FakeExtractor : IInvoiceExtractor
{
	public static ExtractionUsage Usage { get; } = new("claude-haiku-5-5", 2000, 300, 0.00035m);

	public List<InvoiceExtractionResult> Responses { get; } = [];

	public List<IReadOnlyList<Domain.Validation.ValidationIssue>> Feedback { get; } = [];

	public Exception? Throw { get; set; }

	public bool IsConfigured => true;

	public Task<InvoiceExtractionResult> ExtractAsync(PreparedDocument document, IReadOnlyList<Domain.Validation.ValidationIssue> feedback, CancellationToken cancellationToken)
	{
		if (Throw is not null)
		{
			return Task.FromException<InvoiceExtractionResult>(Throw);
		}

		Feedback.Add(feedback);
		return Task.FromResult(Responses[Math.Min(Feedback.Count, Responses.Count) - 1]);
	}
}
