namespace InvoiceExtractor.Application.Features.Upload;

using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Domain.Documents;

/// <summary>
/// Decides the format from the file's first bytes (its magic number), never from the name or the media type
/// the browser sent: a file called <c>factura.pdf</c> that is really an executable is rejected here.
/// </summary>
public static class FormatDetector
{
	private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

	// JPEG: start-of-image marker followed by the first marker's prefix.
	private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

	// PNG: the fixed eight-byte signature from the PNG specification.
	private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	/// <summary>Detects the format.</summary>
	/// <param name="content">The file's bytes.</param>
	/// <returns>The format, or <c>document.unsupported_format</c>.</returns>
	public static Result<DocumentFormat> Detect(ReadOnlySpan<byte> content)
	{
		if (content.StartsWith(PdfMagic))
		{
			return DocumentFormat.Pdf;
		}

		if (content.StartsWith(JpegMagic))
		{
			return DocumentFormat.Jpeg;
		}

		if (content.StartsWith(PngMagic))
		{
			return DocumentFormat.Png;
		}

		return Error.Validation(UploadErrors.UnsupportedFormat, "Supported formats: PDF, JPG and PNG.");
	}
}

/// <summary>The upload's error codes, documented in <c>docs/errors.md</c>.</summary>
public static class UploadErrors
{
	/// <summary>The request carried no files.</summary>
	public const string EmptyBatch = "batch.empty";

	/// <summary>More files than one batch takes.</summary>
	public const string TooManyFiles = "batch.too_many_files";

	/// <summary>A zero-byte file.</summary>
	public const string EmptyFile = "document.empty";

	/// <summary>A file over the size limit.</summary>
	public const string TooLarge = "document.too_large";

	/// <summary>Not a PDF, JPG or PNG.</summary>
	public const string UnsupportedFormat = "document.unsupported_format";

	/// <summary>A PDF with more pages than an invoice plausibly has.</summary>
	public const string TooManyPages = "document.too_many_pages";

	/// <summary>The file claims a format but cannot be opened as one.</summary>
	public const string Unreadable = "document.unreadable";

	/// <summary>A password-protected PDF.</summary>
	public const string Encrypted = "document.encrypted";

	/// <summary>An image with more pixels than it is safe to decode.</summary>
	public const string ImageTooLarge = "document.image_too_large";
}
