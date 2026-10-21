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
