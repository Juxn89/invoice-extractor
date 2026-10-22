namespace InvoiceExtractor.Domain.Documents;

using InvoiceExtractor.Domain.Invoices;

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

	/// <summary>What the reviewer approved, corrections included; null until a person approves it.</summary>
	public ExtractedInvoice? ReviewedInvoice { get; private set; }

	/// <summary>When a person approved it.</summary>
	public DateTimeOffset? ReviewedAt { get; private set; }

	/// <summary>
	/// While settled (auto-approved or approved), which invoice it is for the books. Unique across documents, so
	/// one invoice is settled once however many files of it arrive; null otherwise, or when a part was not read.
	/// </summary>
	public InvoiceKey? InvoiceKey { get; private set; }

	/// <summary>When <see cref="DocumentStatus.Duplicate"/>, the settled document it repeats.</summary>
	public DocumentId? DuplicateOf { get; private set; }

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

	/// <summary>
	/// Takes the document for processing until <paramref name="leaseUntil"/>. The queue does this in one SQL
	/// statement (so two workers cannot both take it); this method states the same rule for code and tests.
	/// </summary>
	/// <param name="now">The current time.</param>
	/// <param name="leaseUntil">When the claim lapses if the worker never reports back.</param>
	/// <exception cref="InvalidOperationException">It is neither queued nor held under an expired lease.</exception>
	public void Claim(DateTimeOffset now, DateTimeOffset leaseUntil)
	{
		var claimable = Status == DocumentStatus.Queued || (Status == DocumentStatus.Processing && LeaseUntil < now);
		if (!claimable || leaseUntil <= now)
		{
			throw new InvalidOperationException($"Document {Id} is {Status} and cannot be claimed.");
		}

		Status = DocumentStatus.Processing;
		Attempts++;
		LeaseUntil = leaseUntil;
	}

	/// <summary>Records a finished extraction: approved without review, or waiting for a person.</summary>
	/// <param name="outcome"><see cref="DocumentStatus.AutoApproved"/> or <see cref="DocumentStatus.NeedsReview"/>.</param>
	/// <param name="now">The current time.</param>
	/// <param name="key">The reading's invoice key; kept only when it is approved, since only then is it settled.</param>
	/// <exception cref="InvalidOperationException">It is not being processed.</exception>
	public void Complete(DocumentStatus outcome, DateTimeOffset now, InvoiceKey? key = null)
	{
		EnsureProcessing();
		if (outcome is not (DocumentStatus.AutoApproved or DocumentStatus.NeedsReview))
		{
			throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "An extraction ends approved or in review.");
		}

		Status = outcome;
		InvoiceKey = outcome == DocumentStatus.AutoApproved ? key : null;
		LeaseUntil = null;
		ProcessedAt = now;
	}

	/// <summary>
	/// Records that the reading is an invoice already settled under another document: it is not exported and no
	/// one needs to review it. Its extractions are kept, so what was read and what it cost stay visible.
	/// </summary>
	/// <param name="original">The settled document with the same <see cref="InvoiceKey"/>.</param>
	/// <param name="now">The current time.</param>
	/// <exception cref="InvalidOperationException">It is not being processed.</exception>
	/// <exception cref="ArgumentException">It would be a duplicate of itself.</exception>
	public void MarkDuplicate(DocumentId original, DateTimeOffset now)
	{
		EnsureProcessing();
		if (original == Id)
		{
			throw new ArgumentException("A document cannot repeat itself.", nameof(original));
		}

		Status = DocumentStatus.Duplicate;
		DuplicateOf = original;
		LeaseUntil = null;
		ProcessedAt = now;
	}

	/// <summary>
	/// Gives the document back after an attempt that failed for a transient reason: queued again, or failed for
	/// good once it has used <paramref name="maxAttempts"/>.
	/// </summary>
	/// <param name="maxAttempts">Attempts allowed in total.</param>
	/// <param name="now">The current time.</param>
	/// <exception cref="InvalidOperationException">It is not being processed.</exception>
	public void Release(int maxAttempts, DateTimeOffset now)
	{
		EnsureProcessing();
		LeaseUntil = null;
		if (Attempts >= maxAttempts)
		{
			Status = DocumentStatus.Failed;
			ProcessedAt = now;
			return;
		}

		Status = DocumentStatus.Queued;
	}

	/// <summary>
	/// A person checked the reading against the document and approves it, with whatever they corrected. The
	/// reviewer has the last word: rules still failing are shown to them, not enforced, because some documents
	/// are wrong on paper and still have to be booked.
	/// </summary>
	/// <param name="invoice">The data as the reviewer approved it.</param>
	/// <param name="now">The current time.</param>
	/// <exception cref="InvalidOperationException">It is not waiting for review.</exception>
	public void Approve(ExtractedInvoice invoice, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(invoice);
		if (Status != DocumentStatus.NeedsReview)
		{
			throw new InvalidOperationException($"Document {Id} is {Status}; only a document in review can be approved.");
		}

		Status = DocumentStatus.Approved;
		ReviewedInvoice = invoice;
		ReviewedAt = now;
		InvoiceKey = InvoiceKey.From(invoice);
	}

	private void EnsureProcessing()
	{
		if (Status != DocumentStatus.Processing)
		{
			throw new InvalidOperationException($"Document {Id} is {Status}, not {DocumentStatus.Processing}.");
		}
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
	/// <summary>Country codes are ISO 3166-1 alpha-2, or <c>XX</c> for the generic profile.</summary>
	public const int CountryLength = 2;

	private Batch(BatchId id, int fileCount, string country, DateTimeOffset createdAt)
	{
		Id = id;
		FileCount = fileCount;
		Country = country;
		CreatedAt = createdAt;
	}

	/// <summary>The id.</summary>
	public BatchId Id { get; private set; }

	/// <summary>New documents the upload created (duplicates and rejected files are not counted).</summary>
	public int FileCount { get; private set; }

	/// <summary>
	/// The country whose tax rules validate the batch's documents. Chosen at upload, never inferred from what
	/// the model read: a misread tax id must fail the issuer country's format, not fall back to a looser one.
	/// </summary>
	public string Country { get; private set; }

	/// <summary>When it was uploaded.</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Starts a batch.</summary>
	/// <param name="fileCount">New documents in it, at least 1.</param>
	/// <param name="country">The tax profile's country code.</param>
	/// <param name="now">The upload time.</param>
	/// <returns>The batch.</returns>
	public static Batch Start(int fileCount, string country, DateTimeOffset now)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(fileCount, 1);
		ArgumentException.ThrowIfNullOrWhiteSpace(country);
		if (country.Length != CountryLength)
		{
			throw new ArgumentException("A country code has two letters.", nameof(country));
		}

		return new Batch(BatchId.New(), fileCount, country.ToUpperInvariant(), now);
	}
}
