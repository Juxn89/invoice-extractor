namespace InvoiceExtractor.Infrastructure.Documents;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Application.Features.Upload;
using InvoiceExtractor.Domain.Documents;
using Microsoft.Extensions.Options;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

/// <summary>
/// Opens each upload far enough to know it can be processed: a PDF's page count and whether it has a text
/// layer, an image's header. Nothing is decoded in full here; that is the preprocessor's job, later.
/// </summary>
/// <param name="options">The upload limits, so pages beyond the cap are not scanned for text.</param>
internal sealed class DocumentInspector(IOptions<UploadOptions> options) : IDocumentInspector
{
	/// <summary>
	/// A page counts as text when it carries at least this many letters or digits. Even the shortest receipt
	/// has more; a scan with an invisible header or a "scanned with" watermark has fewer.
	/// </summary>
	internal const int MinTextCharactersPerPage = 40;

	/// <summary>
	/// 50 megapixels: a 48 MP phone photo fits. A PNG cannot be decoded at a reduced scale, so the cap bounds
	/// the memory a single upload can make the server allocate (4 bytes per pixel, about 200 MB).
	/// </summary>
	internal const long MaxImagePixels = 50_000_000;

	public Result<DocumentInspection> Inspect(ReadOnlyMemory<byte> content, DocumentFormat format) => format switch
	{
		DocumentFormat.Pdf => InspectPdf(content),
		DocumentFormat.Jpeg or DocumentFormat.Png => InspectImage(content),
		_ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format."),
	};

	private Result<DocumentInspection> InspectPdf(ReadOnlyMemory<byte> content)
	{
		try
		{
			using var pdf = PdfDocument.Open(content.ToArray());
			var pages = pdf.NumberOfPages;
			if (pages < 1)
			{
				return Unreadable("The PDF has no pages.");
			}

			// A PDF over the page cap is rejected anyway; reading its text would be wasted work.
			var hasTextLayer = pages <= options.Value.MaxPdfPages
				&& pdf.GetPages().All(static page => page.Letters.Count(static l => l.Value.Any(char.IsLetterOrDigit)) >= MinTextCharactersPerPage);
			return new DocumentInspection(pages, hasTextLayer);
		}
		catch (PdfDocumentEncryptedException)
		{
			return Error.Validation(UploadErrors.Encrypted, "The PDF is password-protected; upload an unprotected copy.");
		}
#pragma warning disable CA1031 // A malformed PDF can fail anywhere in the parser, with any exception type.
		catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
		{
			return Unreadable("The file says it is a PDF but cannot be opened as one.");
		}
	}

	private static Result<DocumentInspection> InspectImage(ReadOnlyMemory<byte> content)
	{
		using var data = SKData.CreateCopy(content.Span);
		using var codec = SKCodec.Create(data);
		if (codec is null || codec.Info.Width < 1 || codec.Info.Height < 1)
		{
			return Unreadable("The image cannot be decoded.");
		}

		if ((long)codec.Info.Width * codec.Info.Height > MaxImagePixels)
		{
			return Error.Validation(
				UploadErrors.ImageTooLarge,
				$"The image is {codec.Info.Width}×{codec.Info.Height}; the limit is {MaxImagePixels / 1_000_000} megapixels.");
		}

		return new DocumentInspection(Pages: 1, HasTextLayer: false);
	}

	private static Error Unreadable(string message) => Error.Validation(UploadErrors.Unreadable, message);
}
