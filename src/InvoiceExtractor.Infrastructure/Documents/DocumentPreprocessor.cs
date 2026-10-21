namespace InvoiceExtractor.Infrastructure.Documents;

using System.Globalization;
using System.Text;
using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Documents;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

/// <summary>
/// Turns a stored file into the model's input, choosing the cheapest form that keeps the information: text for
/// a digital PDF, the PDF itself for a scanned one, and for a photo an upright JPEG no larger than the model
/// uses.
/// </summary>
internal sealed class DocumentPreprocessor : IDocumentPreprocessor
{
	/// <summary>
	/// Anthropic's vision guidance: an image whose long edge exceeds 1568 px is scaled down by the API anyway,
	/// adding latency and no accuracy. Scaling it here also shrinks the upload.
	/// </summary>
	internal const int MaxLongEdgePixels = 1568;

	/// <summary>High enough that small print stays legible after re-encoding; the evals measure whether it is.</summary>
	internal const int JpegQuality = 85;

	private const string PageMarkerFormat = "--- page {0} ---";

	private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

	public PreparedDocument Prepare(ReadOnlyMemory<byte> content, DocumentFormat format, bool hasTextLayer) => format switch
	{
		DocumentFormat.Pdf when hasTextLayer => PdfText(content),
		DocumentFormat.Pdf => new PreparedPdf(content, PageCount(content)),
		DocumentFormat.Jpeg or DocumentFormat.Png => Image(content),
		_ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format."),
	};

	private static PreparedText PdfText(ReadOnlyMemory<byte> content)
	{
		using var pdf = PdfDocument.Open(content.ToArray());
		var text = new StringBuilder();
		foreach (var page in pdf.GetPages())
		{
			// The marker lets the model and a reviewer tell which page a value came from.
			text.AppendLine(string.Format(CultureInfo.InvariantCulture, PageMarkerFormat, page.Number));
			text.AppendLine(ContentOrderTextExtractor.GetText(page).Trim());
		}

		return new PreparedText(text.ToString().TrimEnd(), pdf.NumberOfPages);
	}

	private static int PageCount(ReadOnlyMemory<byte> content)
	{
		using var pdf = PdfDocument.Open(content.ToArray());
		return pdf.NumberOfPages;
	}

	private static PreparedImage Image(ReadOnlyMemory<byte> content)
	{
		using var data = SKData.CreateCopy(content.Span);
		using var codec = SKCodec.Create(data)
			?? throw new InvalidDataException("The image cannot be decoded; it should not have passed inspection.");

		using var decoded = Decode(codec);
		using var upright = Orient(decoded, codec.EncodedOrigin);
		using var scaled = ScaleDown(upright);

		// Onto white: a transparent PNG encoded straight to JPEG would come out with a black background.
		using var flattened = new SKBitmap(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
		using (var canvas = new SKCanvas(flattened))
		{
			canvas.Clear(SKColors.White);
			using var image = SKImage.FromBitmap(scaled);
			canvas.DrawImage(image, 0, 0, Sampling);
		}

		using var jpeg = flattened.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
		return new PreparedImage(jpeg.ToArray(), flattened.Width, flattened.Height);
	}

	// A JPEG can be decoded at 1/2, 1/4 or 1/8 scale directly, which keeps a 48 MP photo from ever being
	// held in memory at full size. Other formats decode at full size and are scaled afterwards.
	private static SKBitmap Decode(SKCodec codec)
	{
		var longEdge = Math.Max(codec.Info.Width, codec.Info.Height);
		var size = longEdge > MaxLongEdgePixels
			? codec.GetScaledDimensions((float)MaxLongEdgePixels / longEdge)
			: codec.Info.Size;

		// GetScaledDimensions rounds to the nearest supported scale, which may be below the target; then the
		// full size is decoded instead, so the later scale-down never enlarges.
		if (Math.Max(size.Width, size.Height) < Math.Min(longEdge, MaxLongEdgePixels))
		{
			size = codec.Info.Size;
		}

		var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
		var bitmap = new SKBitmap(info);
		var result = codec.GetPixels(info, bitmap.GetPixels());
		if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
		{
			bitmap.Dispose();
			throw new InvalidDataException($"The image could not be decoded: {result}.");
		}

		return bitmap;
	}

	// EXIF orientation: phones store the sensor's pixels and a tag saying how to turn them. Applying it here
	// means the model always sees the page the way the user did.
	private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
	{
		if (origin == SKEncodedOrigin.TopLeft)
		{
			return source.Copy();
		}

		float w = source.Width;
		float h = source.Height;
		var transposed = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
			or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

		// x' = scaleX·x + skewX·y + transX ; y' = skewY·x + scaleY·y + transY
		var matrix = origin switch
		{
			SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
			SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
			SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
			SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
			SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
			SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
			SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
			_ => SKMatrix.Identity,
		};

		var oriented = new SKBitmap(transposed ? source.Height : source.Width, transposed ? source.Width : source.Height);
		using var canvas = new SKCanvas(oriented);
		canvas.SetMatrix(matrix);
		using var image = SKImage.FromBitmap(source);
		canvas.DrawImage(image, 0, 0, Sampling);
		return oriented;
	}

	private static SKBitmap ScaleDown(SKBitmap source)
	{
		var longEdge = Math.Max(source.Width, source.Height);
		if (longEdge <= MaxLongEdgePixels)
		{
			return source.Copy();
		}

		var scale = (double)MaxLongEdgePixels / longEdge;
		var info = new SKImageInfo(
			Math.Max(1, (int)Math.Round(source.Width * scale)),
			Math.Max(1, (int)Math.Round(source.Height * scale)));
		return source.Resize(info, Sampling);
	}
}
