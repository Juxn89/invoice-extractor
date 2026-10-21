namespace InvoiceExtractor.Application.Abstractions;

using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Domain.Documents;

/// <summary>Batches and documents in the database.</summary>
public interface IDocumentRepository
{
	/// <summary>The documents already stored with any of these hashes.</summary>
	/// <param name="hashes">The hashes to look for.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The matches, keyed by hash.</returns>
	Task<IReadOnlyDictionary<ContentHash, Document>> FindByHashesAsync(IReadOnlyCollection<ContentHash> hashes, CancellationToken cancellationToken);

	/// <summary>One document.</summary>
	/// <param name="id">Its id.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The document, or <see langword="null"/>.</returns>
	Task<Document?> GetAsync(DocumentId id, CancellationToken cancellationToken);

	/// <summary>One batch.</summary>
	/// <param name="id">Its id.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The batch, or <see langword="null"/>.</returns>
	Task<Batch?> GetBatchAsync(BatchId id, CancellationToken cancellationToken);

	/// <summary>A batch's documents, oldest first.</summary>
	/// <param name="id">The batch.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The documents.</returns>
	Task<IReadOnlyList<Document>> ListByBatchAsync(BatchId id, CancellationToken cancellationToken);

	/// <summary>Stages a new batch and its documents; nothing is written until <see cref="SaveChangesAsync"/>.</summary>
	/// <param name="batch">The batch.</param>
	/// <param name="documents">Its documents.</param>
	void Add(Batch batch, IReadOnlyCollection<Document> documents);

	/// <summary>Writes what was staged, in one transaction.</summary>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>A task.</returns>
	/// <exception cref="DuplicateContentException">Another request stored one of the same files first.</exception>
	Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Where uploaded files live. Content-addressed: saving the same bytes twice is harmless.</summary>
public interface IFileStore
{
	/// <summary>Stores a file under its hash.</summary>
	/// <param name="hash">The bytes' hash.</param>
	/// <param name="format">The file type, which picks the extension.</param>
	/// <param name="content">The bytes.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>A task.</returns>
	Task SaveAsync(ContentHash hash, DocumentFormat format, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

	/// <summary>Reads a stored file.</summary>
	/// <param name="hash">The bytes' hash.</param>
	/// <param name="format">The file type.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The bytes.</returns>
	/// <exception cref="FileNotFoundException">Nothing is stored under that hash.</exception>
	Task<byte[]> ReadAsync(ContentHash hash, DocumentFormat format, CancellationToken cancellationToken);
}

/// <summary>What an upload needs to know about a file before accepting it.</summary>
/// <param name="Pages">A PDF's page count; 1 for an image.</param>
/// <param name="HasTextLayer">A PDF whose text is readable without vision.</param>
public sealed record DocumentInspection(int Pages, bool HasTextLayer);

/// <summary>Opens a file far enough to know it is readable, without preparing it for the model.</summary>
public interface IDocumentInspector
{
	/// <summary>Inspects a file whose format was already detected from its bytes.</summary>
	/// <param name="content">The bytes.</param>
	/// <param name="format">The detected format.</param>
	/// <returns>What it found, or why the file cannot be used (corrupt, encrypted, too large to decode).</returns>
	Result<DocumentInspection> Inspect(ReadOnlyMemory<byte> content, DocumentFormat format);
}

/// <summary>What the model is given for one document.</summary>
public abstract record PreparedDocument;

/// <summary>A digital PDF's text, in reading order: no vision needed, so cheaper and exact.</summary>
/// <param name="Text">The text, pages separated by a marker.</param>
/// <param name="Pages">How many pages it came from.</param>
public sealed record PreparedText(string Text, int Pages) : PreparedDocument;

/// <summary>A scanned PDF, passed as is: the model reads its pages as images.</summary>
/// <param name="Content">The PDF.</param>
/// <param name="Pages">Its page count.</param>
public sealed record PreparedPdf(ReadOnlyMemory<byte> Content, int Pages) : PreparedDocument;

/// <summary>A photo or scan, upright and scaled down to what the model can use.</summary>
/// <param name="Jpeg">The re-encoded image.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
public sealed record PreparedImage(ReadOnlyMemory<byte> Jpeg, int Width, int Height) : PreparedDocument;

/// <summary>Turns a stored file into what the extraction sends to the model.</summary>
public interface IDocumentPreprocessor
{
	/// <summary>Prepares a file that already passed <see cref="IDocumentInspector"/>.</summary>
	/// <param name="content">The bytes.</param>
	/// <param name="format">The format.</param>
	/// <param name="hasTextLayer">Whether the PDF has a text layer.</param>
	/// <returns>The model's input.</returns>
	PreparedDocument Prepare(ReadOnlyMemory<byte> content, DocumentFormat format, bool hasTextLayer);
}
