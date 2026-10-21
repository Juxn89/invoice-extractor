namespace InvoiceExtractor.Integration.Tests;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Features.Upload;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Infrastructure.Documents;
using Microsoft.Extensions.Options;
using SkiaSharp;

// No database: these exercise PdfPig and SkiaSharp on real files, which is why they are not unit tests.
public sealed class DocumentPreparationTests
{
	private readonly DocumentInspector _inspector = new(Options.Create(new UploadOptions()));
	private readonly DocumentPreprocessor _preprocessor = new();

	[Fact]
	public void A_digital_pdf_has_a_text_layer_and_is_sent_as_text()
	{
		var inspection = _inspector.Inspect(Samples.DigitalPdf, DocumentFormat.Pdf).Value;
		var prepared = Assert.IsType<PreparedText>(_preprocessor.Prepare(Samples.DigitalPdf, DocumentFormat.Pdf, inspection.HasTextLayer));

		Assert.Equal(new DocumentInspection(1, true), inspection);
		Assert.Contains("--- page 1 ---", prepared.Text, StringComparison.Ordinal);
		Assert.Contains("J0310000252297", prepared.Text, StringComparison.Ordinal);
		Assert.Contains("F-001037", prepared.Text, StringComparison.Ordinal);
		Assert.Contains("7,517.61", prepared.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void A_scanned_pdf_has_no_text_layer_and_is_sent_as_the_pdf_itself()
	{
		var scanned = ScannedPdf();

		var inspection = _inspector.Inspect(scanned, DocumentFormat.Pdf).Value;
		var prepared = Assert.IsType<PreparedPdf>(_preprocessor.Prepare(scanned, DocumentFormat.Pdf, inspection.HasTextLayer));

		Assert.False(inspection.HasTextLayer);
		Assert.Equal(1, prepared.Pages);
		Assert.Equal(scanned, prepared.Content.ToArray());
	}

	[Fact]
	public void A_pdf_that_cannot_be_parsed_is_unreadable() =>
		Assert.Equal(
			UploadErrors.Unreadable,
			_inspector.Inspect("%PDF-1.7\nthis is not a pdf body"u8.ToArray(), DocumentFormat.Pdf).Error?.Code);

	[Fact]
	public void An_image_that_cannot_be_decoded_is_unreadable() =>
		Assert.Equal(
			UploadErrors.Unreadable,
			_inspector.Inspect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 }, DocumentFormat.Jpeg).Error?.Code);

	[Fact]
	public void A_photo_is_scaled_so_its_long_edge_fits_what_the_model_uses()
	{
		var prepared = Assert.IsType<PreparedImage>(_preprocessor.Prepare(Samples.Photo, DocumentFormat.Jpeg, false));

		// The sample is 1210×1600.
		Assert.Equal(DocumentPreprocessor.MaxLongEdgePixels, prepared.Height);
		Assert.Equal(1186, prepared.Width);
		using var decoded = SKBitmap.Decode(prepared.Jpeg.ToArray());
		Assert.Equal((prepared.Width, prepared.Height), (decoded.Width, decoded.Height));
	}

	[Fact]
	public void A_png_scan_is_scaled_keeping_its_aspect_ratio()
	{
		var prepared = Assert.IsType<PreparedImage>(_preprocessor.Prepare(Samples.Scan, DocumentFormat.Png, false));

		// The sample is 1240×1754 (A4 at 150 dpi): 1240 × 1568 / 1754 = 1108.5.
		Assert.Equal((1109, DocumentPreprocessor.MaxLongEdgePixels), (prepared.Width, prepared.Height));
	}

	[Fact]
	public void An_image_within_the_limit_keeps_its_size()
	{
		var prepared = Assert.IsType<PreparedImage>(_preprocessor.Prepare(RedCornerJpeg(300, 200), DocumentFormat.Jpeg, false));

		Assert.Equal((300, 200), (prepared.Width, prepared.Height));
	}

	[Fact]
	public void A_phone_photo_tagged_as_rotated_is_turned_upright()
	{
		// 40×20 with a red top-left corner, tagged "rotate 90° clockwise to display" (EXIF orientation 6).
		var jpeg = WithExifOrientation(RedCornerJpeg(40, 20), orientation: 6);

		var prepared = Assert.IsType<PreparedImage>(_preprocessor.Prepare(jpeg, DocumentFormat.Jpeg, false));
		using var upright = SKBitmap.Decode(prepared.Jpeg.ToArray());

		Assert.Equal((20, 40), (upright.Width, upright.Height));
		Assert.True(IsRed(upright.GetPixel(15, 5)), "after a clockwise turn the red corner is top-right");
		Assert.False(IsRed(upright.GetPixel(5, 5)));
	}

	[Fact]
	public void A_transparent_png_is_flattened_onto_white_not_black()
	{
		using var transparent = new SKBitmap(10, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
		transparent.Erase(SKColors.Transparent);
		using var png = transparent.Encode(SKEncodedImageFormat.Png, 100);

		var prepared = Assert.IsType<PreparedImage>(_preprocessor.Prepare(png.ToArray(), DocumentFormat.Png, false));
		using var flattened = SKBitmap.Decode(prepared.Jpeg.ToArray());

		Assert.True(flattened.GetPixel(5, 5).Red > 245, "white, not black");
	}

	private static bool IsRed(SKColor color) => color.Red > 200 && color.Green < 80 && color.Blue < 80;

	private static byte[] RedCornerJpeg(int width, int height)
	{
		using var bitmap = new SKBitmap(width, height);
		using (var canvas = new SKCanvas(bitmap))
		{
			canvas.Clear(SKColors.White);
			using var red = new SKPaint { Color = SKColors.Red };
			canvas.DrawRect(0, 0, 10, 10, red);
		}

		using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 95);
		return data.ToArray();
	}

	// A minimal APP1 segment holding one TIFF tag, Orientation (0x0112), inserted right after the JPEG's SOI.
	private static byte[] WithExifOrientation(byte[] jpeg, byte orientation)
	{
		byte[] payload =
		[
			.. "Exif\0\0"u8,
			.. "MM\0*"u8, 0, 0, 0, 8,          // big-endian TIFF header, first IFD at offset 8
			0, 1,                              // one entry
			0x01, 0x12, 0, 3, 0, 0, 0, 1,      // Orientation, SHORT, count 1
			0, orientation, 0, 0,              // value
			0, 0, 0, 0,                        // no next IFD
		];
		var length = payload.Length + 2;
		return [jpeg[0], jpeg[1], 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload, .. jpeg[2..]];
	}

	// A PDF whose only content is a picture of a page: what a scanner that does no OCR produces.
	private static byte[] ScannedPdf()
	{
		using var page = SKBitmap.Decode(Samples.Scan);
		using var image = SKImage.FromBitmap(page);
		using var stream = new SKDynamicMemoryWStream();
		using (var document = SKDocument.CreatePdf(stream))
		{
			var canvas = document.BeginPage(page.Width / 2f, page.Height / 2f);
			canvas.Scale(0.5f);
			canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
			document.EndPage();
			document.Close();
		}

		using var data = stream.DetachAsData();
		return data.ToArray();
	}
}
