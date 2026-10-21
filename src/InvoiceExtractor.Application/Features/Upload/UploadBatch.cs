namespace InvoiceExtractor.Application.Features.Upload;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Application.Features.Documents;
using InvoiceExtractor.Domain.Documents;
using Microsoft.Extensions.Options;

/// <summary>One file of an upload.</summary>
/// <param name="FileName">The user's file name.</param>
/// <param name="SizeBytes">The file's size as received.</param>
/// <param name="Content">
/// The bytes; empty when the caller did not read a file the limits already refuse (over the size limit).
/// </param>
public sealed record UploadedFile(string FileName, long SizeBytes, ReadOnlyMemory<byte> Content);

/// <summary>What happened to one file.</summary>
public enum UploadOutcome
{
	/// <summary>New: stored and queued for extraction.</summary>
	Queued = 1,

	/// <summary>Already uploaded, here or earlier in the same batch: nothing new was stored or will be paid for.</summary>
	Duplicate = 2,

	/// <summary>Not accepted; <see cref="FileResult.Error"/> says why.</summary>
	Rejected = 3,
}

/// <summary>One file's result.</summary>
/// <param name="FileName">The user's file name.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Document">The document it is now (new or existing), unless rejected.</param>
/// <param name="Error">Why it was rejected.</param>
public sealed record FileResult(string FileName, UploadOutcome Outcome, DocumentResponse? Document, Error? Error);

/// <summary>An upload's results, one per file in the order sent.</summary>
/// <param name="BatchId">The new batch, or <see langword="null"/> when no file was new.</param>
/// <param name="Files">The results.</param>
public sealed record UploadResponse(Guid? BatchId, IReadOnlyList<FileResult> Files);

/// <summary>
/// Accepts a batch of files: each one is checked on its own (size, real format, readable, page count), so one
/// bad file does not sink the rest. Files already known by their hash are reported as duplicates and cost
/// nothing; the new ones are stored and queued in one transaction.
/// </summary>
/// <param name="documents">The repository.</param>
/// <param name="files">The file store.</param>
/// <param name="inspector">Opens each file far enough to know it is usable.</param>
/// <param name="options">The limits.</param>
/// <param name="clock">The clock.</param>
public sealed class UploadBatch(
	IDocumentRepository documents,
	IFileStore files,
	IDocumentInspector inspector,
	IOptions<UploadOptions> options,
	TimeProvider clock)
{
	// One retry is enough: after a conflict the competing request has committed, so the second lookup sees
	// its documents and reports them as duplicates.
	private const int SaveAttempts = 2;

	/// <summary>Uploads the batch.</summary>
	/// <param name="uploads">The files, in the order the user sent them.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>One result per file, or an error for the request as a whole (empty, too many files).</returns>
	public async Task<Result<UploadResponse>> HandleAsync(IReadOnlyList<UploadedFile> uploads, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(uploads);
		ArgumentNullException.ThrowIfNull(options);
		var limits = options.Value;

		if (uploads.Count == 0)
		{
			return Error.Validation(UploadErrors.EmptyBatch, "Send at least one file.");
		}

		if (uploads.Count > limits.MaxFilesPerBatch)
		{
			return Error.Validation(
				UploadErrors.TooManyFiles,
				$"{uploads.Count} files sent; a batch takes up to {limits.MaxFilesPerBatch}.");
		}

		var checkedFiles = uploads.Select(upload => Check(upload, limits)).ToList();

		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return await StoreAsync(checkedFiles, cancellationToken).ConfigureAwait(false);
			}
			catch (DuplicateContentException) when (attempt < SaveAttempts)
			{
				// Another request stored one of these files first; look again and report it as a duplicate.
			}
		}
	}

	private CheckedFile Check(UploadedFile upload, UploadOptions limits)
	{
		var content = upload.Content;
		if (upload.SizeBytes > limits.MaxFileBytes || content.Length > limits.MaxFileBytes)
		{
			return CheckedFile.Reject(upload, Error.Validation(
				UploadErrors.TooLarge,
				$"The file is {Math.Max(upload.SizeBytes, content.Length):N0} bytes; the limit is {limits.MaxFileBytes:N0}."));
		}

		if (content.IsEmpty)
		{
			return CheckedFile.Reject(upload, Error.Validation(UploadErrors.EmptyFile, "The file is empty."));
		}

		var format = FormatDetector.Detect(content.Span);
		if (!format.IsSuccess)
		{
			return CheckedFile.Reject(upload, format.Error!);
		}

		var inspection = inspector.Inspect(content, format.Value);
		if (!inspection.IsSuccess)
		{
			return CheckedFile.Reject(upload, inspection.Error!);
		}

		if (inspection.Value.Pages > limits.MaxPdfPages)
		{
			return CheckedFile.Reject(upload, Error.Validation(
				UploadErrors.TooManyPages,
				$"The PDF has {inspection.Value.Pages} pages; the limit is {limits.MaxPdfPages}."));
		}

		return new CheckedFile(upload, ContentHash.Compute(content.Span), format.Value, inspection.Value, null);
	}

	private async Task<UploadResponse> StoreAsync(IReadOnlyList<CheckedFile> checkedFiles, CancellationToken cancellationToken)
	{
		var hashes = checkedFiles.Where(static f => f.Hash is not null).Select(static f => f.Hash!).Distinct().ToList();
		IReadOnlyDictionary<ContentHash, Document> existing = hashes.Count == 0
			? new Dictionary<ContentHash, Document>()
			: await documents.FindByHashesAsync(hashes, cancellationToken).ConfigureAwait(false);

		var now = clock.GetUtcNow();
		var newFiles = checkedFiles
			.Where(f => f.Error is null && !existing.ContainsKey(f.Hash!))
			.DistinctBy(static f => f.Hash)
			.ToList();

		Batch? batch = null;
		var created = new Dictionary<ContentHash, Document>();
		if (newFiles.Count > 0)
		{
			batch = Batch.Start(newFiles.Count, now);
			foreach (var file in newFiles)
			{
				created[file.Hash!] = Document.Queue(
					batch.Id,
					file.Upload.FileName,
					file.Hash!,
					file.Format,
					file.Inspection!.Pages,
					file.Inspection.HasTextLayer,
					file.Upload.Content.Length,
					now);

				// Files first, rows second: a crash in between leaves an orphan file (harmless, content-addressed)
				// rather than a queued document whose file is missing.
				await files.SaveAsync(file.Hash!, file.Format, file.Upload.Content, cancellationToken).ConfigureAwait(false);
			}

			documents.Add(batch, created.Values);
			await documents.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		var claimed = new HashSet<ContentHash>();
		var results = checkedFiles.Select(file =>
		{
			if (file.Error is not null)
			{
				return new FileResult(file.Upload.FileName, UploadOutcome.Rejected, null, file.Error);
			}

			if (existing.TryGetValue(file.Hash!, out var known))
			{
				return new FileResult(file.Upload.FileName, UploadOutcome.Duplicate, DocumentResponse.From(known), null);
			}

			// The first copy in the batch is the new document; any later copy of the same bytes is its duplicate.
			var outcome = claimed.Add(file.Hash!) ? UploadOutcome.Queued : UploadOutcome.Duplicate;
			return new FileResult(file.Upload.FileName, outcome, DocumentResponse.From(created[file.Hash!]), null);
		}).ToList();

		return new UploadResponse(batch?.Id.Value, results);
	}

	private sealed record CheckedFile(
		UploadedFile Upload,
		ContentHash? Hash,
		DocumentFormat Format,
		DocumentInspection? Inspection,
		Error? Error)
	{
		public static CheckedFile Reject(UploadedFile upload, Error error) => new(upload, null, default, null, error);
	}
}
