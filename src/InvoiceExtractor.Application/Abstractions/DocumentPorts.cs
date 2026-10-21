namespace InvoiceExtractor.Application.Abstractions;

using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;

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

	/// <summary>Documents in one status, oldest first.</summary>
	/// <param name="status">The status.</param>
	/// <param name="limit">At most this many.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The documents.</returns>
	Task<IReadOnlyList<Document>> ListByStatusAsync(DocumentStatus status, int limit, CancellationToken cancellationToken);

	/// <summary>The most recent batches, newest first.</summary>
	/// <param name="limit">At most this many.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The batches.</returns>
	Task<IReadOnlyList<Batch>> ListRecentBatchesAsync(int limit, CancellationToken cancellationToken);

	/// <summary>The documents of several batches, oldest first.</summary>
	/// <param name="ids">The batches.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The documents.</returns>
	Task<IReadOnlyList<Document>> ListByBatchesAsync(IReadOnlyCollection<BatchId> ids, CancellationToken cancellationToken);

	/// <summary>Every model call made on a document, in order.</summary>
	/// <param name="id">The document.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The extractions.</returns>
	Task<IReadOnlyList<Extraction>> ListExtractionsAsync(DocumentId id, CancellationToken cancellationToken);

	/// <summary>
	/// The documents whose data is settled, approved by the rules or by a person, oldest first; of one batch
	/// when <paramref name="batch"/> is given.
	/// </summary>
	/// <param name="batch">The batch, or <see langword="null"/> for all.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The documents.</returns>
	Task<IReadOnlyList<Document>> ListApprovedAsync(BatchId? batch, CancellationToken cancellationToken);

	/// <summary>The country each batch was uploaded with.</summary>
	/// <param name="ids">The batches.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>Country code by batch.</returns>
	Task<IReadOnlyDictionary<BatchId, string>> CountriesOfAsync(IReadOnlyCollection<BatchId> ids, CancellationToken cancellationToken);

	/// <summary>Each document's last reading that returned an invoice.</summary>
	/// <param name="ids">The documents.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The reading by document; documents with none are absent.</returns>
	Task<IReadOnlyDictionary<DocumentId, ExtractedInvoice>> FinalReadingsAsync(IReadOnlyCollection<DocumentId> ids, CancellationToken cancellationToken);

	/// <summary>How many documents are in each status.</summary>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The counts; statuses with none are absent.</returns>
	Task<IReadOnlyDictionary<DocumentStatus, int>> CountByStatusAsync(CancellationToken cancellationToken);

	/// <summary>Model calls since a moment (or ever), with how many were retries and what they cost.</summary>
	/// <param name="since">The start, or <see langword="null"/> for all time.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The totals.</returns>
	Task<ExtractionTotals> ExtractionTotalsAsync(DateTimeOffset? since, CancellationToken cancellationToken);

	/// <summary>One document, tracked, to change it.</summary>
	/// <param name="id">Its id.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The document, or <see langword="null"/>.</returns>
	Task<Document?> FindTrackedAsync(DocumentId id, CancellationToken cancellationToken);

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

/// <summary>Model calls added up.</summary>
/// <param name="Calls">Calls made.</param>
/// <param name="Retries">How many of them were retries with feedback.</param>
/// <param name="Documents">Distinct documents they were made on.</param>
/// <param name="CostUsd">What they cost.</param>
public sealed record ExtractionTotals(int Calls, int Retries, int Documents, decimal CostUsd);

/// <summary>The language the export's headers are written in.</summary>
public enum ExportLanguage
{
	/// <summary>Spanish.</summary>
	Es = 1,

	/// <summary>English.</summary>
	En = 2,
}

/// <summary>A file format the accounting export writes.</summary>
public enum ExportFormat
{
	/// <summary>An Excel workbook.</summary>
	Xlsx = 1,

	/// <summary>Comma-separated values, UTF-8.</summary>
	Csv = 2,
}

/// <summary>One invoice ready to book: the settled data and where it came from.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="FileName">The user's file name.</param>
/// <param name="Country">The batch's tax profile.</param>
/// <param name="ApprovedBy">Who settled it: the rules alone, or a person.</param>
/// <param name="Invoice">The data.</param>
public sealed record ExportRow(Guid DocumentId, string FileName, string Country, DocumentStatus ApprovedBy, ExtractedInvoice Invoice);

/// <summary>
/// Writes settled invoices in a format an accounting system or a person imports. Excel and CSV here; an
/// adapter for a specific ERP would be one more implementation.
/// </summary>
public interface IAccountingExport
{
	/// <summary>The format written.</summary>
	ExportFormat Format { get; }

	/// <summary>The media type.</summary>
	string ContentType { get; }

	/// <summary>The file extension, with its dot.</summary>
	string Extension { get; }

	/// <summary>Writes the file.</summary>
	/// <param name="rows">The invoices, in order.</param>
	/// <param name="language">The headers' language.</param>
	/// <returns>The file's bytes.</returns>
	byte[] Write(IReadOnlyList<ExportRow> rows, ExportLanguage language);
}
