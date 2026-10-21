namespace InvoiceExtractor.Domain.Documents;

/// <summary>The file types accepted, decided from the bytes, never from the extension.</summary>
public enum DocumentFormat
{
	/// <summary>A PDF, digital (with a text layer) or scanned.</summary>
	Pdf = 1,

	/// <summary>A JPEG, typically a phone photo.</summary>
	Jpeg = 2,

	/// <summary>A PNG, typically a scan or a screenshot.</summary>
	Png = 3,
}

/// <summary>Where a document is in its life.</summary>
public enum DocumentStatus
{
	/// <summary>Stored, waiting for a worker.</summary>
	Queued = 1,

	/// <summary>A worker holds it until <see cref="Document.LeaseUntil"/>.</summary>
	Processing = 2,

	/// <summary>Every rule passed: no one needs to look at it.</summary>
	AutoApproved = 3,

	/// <summary>A rule failed or a field is uncertain: waiting for a person.</summary>
	NeedsReview = 4,

	/// <summary>A person reviewed and approved it.</summary>
	Approved = 5,

	/// <summary>Extraction failed on every attempt.</summary>
	Failed = 6,

	/// <summary>
	/// A different file of an invoice already read (same issuer, number and total), detected after extraction.
	/// The same file uploaded twice never gets this far: it resolves to the existing document by content hash.
	/// </summary>
	Duplicate = 7,
}

/// <summary>An uploaded file and what has happened to it.</summary>
public sealed class Document
{
	/// <summary>Longest file name kept; a longer one is cut, keeping the extension.</summary>
	public const int FileNameMaxLength = 255;

	/// <summary>What a file uploaded without a name is called.</summary>
	public const string UnnamedFileName = "document";

	private Document(
		DocumentId id,
		BatchId batchId,
		string fileName,
		ContentHash contentHash,
		DocumentFormat format,
		int pages,
		bool hasTextLayer,
		long sizeBytes,
		DateTimeOffset createdAt)
	{
		Id = id;
		BatchId = batchId;
		FileName = fileName;
		ContentHash = contentHash;
		Format = format;
		Pages = pages;
		HasTextLayer = hasTextLayer;
		SizeBytes = sizeBytes;
		CreatedAt = createdAt;
		Status = DocumentStatus.Queued;
	}

	/// <summary>The id.</summary>
	public DocumentId Id { get; private set; }

	/// <summary>The upload it came in.</summary>
	public BatchId BatchId { get; private set; }

	/// <summary>The name the user's file had, for display and the export only.</summary>
	public string FileName { get; private set; }

	/// <summary>The bytes' SHA-256, unique across documents.</summary>
	public ContentHash ContentHash { get; private set; }

	/// <summary>The file type.</summary>
	public DocumentFormat Format { get; private set; }

	/// <summary>Pages: a PDF's page count, 1 for an image.</summary>
	public int Pages { get; private set; }

	/// <summary>A PDF whose text can be read without vision: cheaper and more exact to extract.</summary>
	public bool HasTextLayer { get; private set; }

	/// <summary>The file's size.</summary>
	public long SizeBytes { get; private set; }

	/// <summary>Where it is.</summary>
	public DocumentStatus Status { get; private set; }

	/// <summary>Extraction attempts made so far.</summary>
	public int Attempts { get; private set; }

	/// <summary>While <see cref="DocumentStatus.Processing"/>, when the worker's claim expires.</summary>
	public DateTimeOffset? LeaseUntil { get; private set; }

	/// <summary>When it was uploaded.</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>When processing last finished.</summary>
	public DateTimeOffset? ProcessedAt { get; private set; }

	/// <summary>Registers an uploaded file, queued for extraction.</summary>
	/// <param name="batchId">The upload it came in.</param>
	/// <param name="fileName">The user's file name.</param>
	/// <param name="contentHash">The bytes' hash.</param>
	/// <param name="format">The detected type.</param>
	/// <param name="pages">Page count, at least 1.</param>
	/// <param name="hasTextLayer">Whether a PDF carries readable text; always false for an image.</param>
	/// <param name="sizeBytes">The file's size, at least 1.</param>
	/// <param name="now">The upload time.</param>
	/// <returns>The document.</returns>
	public static Document Queue(
		BatchId batchId,
		string fileName,
		ContentHash contentHash,
		DocumentFormat format,
		int pages,
		bool hasTextLayer,
		long sizeBytes,
		DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(contentHash);
		ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(sizeBytes, 1);
		if (hasTextLayer && format != DocumentFormat.Pdf)
		{
			throw new ArgumentException("Only a PDF has a text layer.", nameof(hasTextLayer));
		}

		return new Document(DocumentId.New(), batchId, Shorten(fileName), contentHash, format, pages, hasTextLayer, sizeBytes, now);
	}

	// Keeps only the name (a browser on Windows may send the full path, with backslashes that Linux does not
	// treat as separators), and keeps the extension when cutting, since it tells the user which file it was.
	private static string Shorten(string fileName)
	{
		var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
		if (name.Length == 0)
		{
			return UnnamedFileName;
		}

		if (name.Length <= FileNameMaxLength)
		{
			return name;
		}

		var extension = Path.GetExtension(name);
		return name[..(FileNameMaxLength - extension.Length)] + extension;
	}
}

/// <summary>One upload of one or more files.</summary>
public sealed class Batch
{
	private Batch(BatchId id, int fileCount, DateTimeOffset createdAt)
	{
		Id = id;
		FileCount = fileCount;
		CreatedAt = createdAt;
	}

	/// <summary>The id.</summary>
	public BatchId Id { get; private set; }

	/// <summary>New documents the upload created (duplicates and rejected files are not counted).</summary>
	public int FileCount { get; private set; }

	/// <summary>When it was uploaded.</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Starts a batch.</summary>
	/// <param name="fileCount">New documents in it, at least 1.</param>
	/// <param name="now">The upload time.</param>
	/// <returns>The batch.</returns>
	public static Batch Start(int fileCount, DateTimeOffset now)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(fileCount, 1);
		return new Batch(BatchId.New(), fileCount, now);
	}
}
