namespace InvoiceExtractor.Evals.Generation;

using System.Globalization;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Evals.Dataset;
using SkiaSharp;

/// <summary>
/// Draws a document once and turns it into the four kinds the dataset needs: a PDF with a real text layer,
/// a clean scan, a phone photo and a thermal receipt, with the fonts bundled in <c>evals/fonts</c>.
/// </summary>
internal static class DocumentRenderer
{
	// An A4 page at 150 dpi, the resolution an office scanner defaults to.
	private const int PageWidth = 1240;
	private const int PageHeight = 1754;

	// A 72 mm till roll at 203 dpi, the common thermal printer resolution.
	private const int ReceiptWidth = 576;

	// PDF points per A4 page, so the drawing at 150 dpi scales onto the page.
	private const float PdfPageWidth = 595f;
	private const float PdfPageHeight = 842f;

	// Photos are capped at this long edge, as a phone app would upload them.
	private const int PhotoMaxEdge = 1600;
	private const int PhotoJpegQuality = 72;

	// The hard set: a cheap phone in poor light, an office scanner at 100 dpi, a 58 mm till roll.
	private const int HardPhotoMaxEdge = 1100;
	private const int HardPhotoJpegQuality = 50;
	private const float HardScanScale = 100f / 150f;
	private const int NarrowReceiptWidth = 420;
	private const int ScannedPdfJpegQuality = 60;

	// Thirty days, the most common payment term on a B2B invoice.
	private const int DueInDays = 30;

	private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);

	// Bundled, not looked up by family name: system fonts differ between Windows, Linux and Docker, and the
	// dataset must render identically everywhere. Noto Sans, subset to Latin-1 (see evals/fonts/README.md),
	// because Skia's PDF backend embeds the whole font file in every PDF.
	private static readonly SKTypeface Regular = Bundled("NotoSans-Regular.ttf");
	private static readonly SKTypeface Bold = Bundled("NotoSans-Bold.ttf");
	private static readonly SKTypeface Mono = Bundled("NotoSansMono-Regular.ttf");

	/// <summary>Renders one document into <paramref name="folder"/>.</summary>
	/// <param name="spec">The document.</param>
	/// <param name="folder">The output folder.</param>
	/// <param name="random">Seeded source for tilt, light and noise.</param>
	public static void Render(DocumentSpec spec, string folder, Random random)
	{
		var path = Path.Combine(folder, spec.FileName);
		switch (spec.Kind)
		{
			case DocumentKind.DigitalPdf:
				WritePdf(spec, path);
				break;
			case DocumentKind.Scan:
				using (var page = DrawPage(spec))
				{
					WritePng(spec.Hard ? PoorScan(page, random) : Scanned(page, random), path);
				}

				break;
			case DocumentKind.Photo:
				using (var page = DrawPage(spec))
				{
					WriteJpeg(spec.Hard ? PoorPhoto(page, random) : Photographed(page, random), path, spec.Hard ? HardPhotoJpegQuality : PhotoJpegQuality);
				}

				break;
			case DocumentKind.ThermalReceipt:
				using (var receipt = DrawReceipt(spec))
				{
					WritePng(spec.Hard ? Faded(receipt, random) : receipt.Copy(), path);
				}

				break;
			case DocumentKind.ScannedPdf:
				using (var page = DrawPage(spec))
				{
					WriteImagePdf(spec, PoorScan(page, random), path);
				}

				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(spec), spec.Kind, "Unknown document kind.");
		}
	}

	private static void WritePdf(DocumentSpec spec, string path)
	{
		using var stream = new SKFileWStream(path);
		using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Title = spec.Id, Creator = "invoice-extractor dataset generator" });
		var canvas = document.BeginPage(PdfPageWidth, PdfPageHeight);
		canvas.Scale(PdfPageWidth / PageWidth);
		DrawInvoice(canvas, spec);
		document.EndPage();
		document.Close();
	}

	// The page is a JPEG inside the PDF, as a scanner writes it: Skia passes encoded JPEG data through as is.
	private static void WriteImagePdf(DocumentSpec spec, SKBitmap scan, string path)
	{
		using (scan)
		{
			using var jpeg = scan.Encode(SKEncodedImageFormat.Jpeg, ScannedPdfJpegQuality);
			using var image = SKImage.FromEncodedData(jpeg);
			using var stream = new SKFileWStream(path);
			using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Title = spec.Id, Creator = "invoice-extractor dataset generator" });
			var canvas = document.BeginPage(PdfPageWidth, PdfPageHeight);
			canvas.DrawImage(image, new SKRect(0, 0, PdfPageWidth, PdfPageHeight), Sampling);
			document.EndPage();
			document.Close();
		}
	}

	private static SKTypeface Bundled(string file) =>
		SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "fonts", file))
			?? throw new FileNotFoundException("Bundled font missing from the build output.", file);

	private static SKBitmap DrawPage(DocumentSpec spec)
	{
		var bitmap = new SKBitmap(PageWidth, PageHeight);
		using var canvas = new SKCanvas(bitmap);
		canvas.Clear(SKColors.White);
		DrawInvoice(canvas, spec);
		return bitmap;
	}

	private static void DrawInvoice(SKCanvas canvas, DocumentSpec spec)
	{
		var invoice = spec.Printed;
		var labels = Labels.For(spec.Market);
		var ink = new SKColor(0x1F, 0x29, 0x37);
		var muted = new SKColor(0x55, 0x60, 0x6E);

		Text(canvas, invoice.IssuerName ?? string.Empty, 90, 130, 40, Bold, ink);
		Text(canvas, spec.Issuer.Address, 90, 178, 24, Regular, muted);
		Text(canvas, spec.Issuer.City, 90, 210, 24, Regular, muted);
		Text(canvas, $"{labels.TaxId}: {invoice.IssuerTaxId}", 90, 248, 24, Regular, ink);

		Text(canvas, labels.Title, PageWidth - 90, 130, 46, Bold, ink, SKTextAlign.Right);
		Text(canvas, $"{labels.Number}: {invoice.Number}", PageWidth - 90, 182, 26, Regular, ink, SKTextAlign.Right);
		Text(canvas, $"{labels.Date}: {FormatDate(invoice.IssueDate!.Value, spec.Market)}", PageWidth - 90, 218, 26, Regular, ink, SKTextAlign.Right);
		Text(canvas, $"{labels.Currency}: {invoice.Currency}", PageWidth - 90, 254, 26, Regular, ink, SKTextAlign.Right);

		using var rule = new SKPaint { Color = new SKColor(0xD5, 0xDB, 0xE3), StrokeWidth = 2, IsAntialias = true };
		canvas.DrawLine(90, 300, PageWidth - 90, 300, rule);
		Text(canvas, $"{labels.Customer}: {invoice.ReceiverName}", 90, 352, 26, Regular, ink);

		// Distractors: each one is a value of the same kind as a key field, printed close to it.
		if (spec.Hard)
		{
			Text(canvas, $"{labels.CustomerTaxId}: {CustomerTaxId(spec)}", 90, 388, 24, Regular, ink);
			Text(canvas, $"{labels.Order}: {OrderNumber(invoice)}", PageWidth - 90, 352, 24, Regular, ink, SKTextAlign.Right);
			Text(canvas, $"{labels.DueDate}: {FormatDate(invoice.IssueDate!.Value.AddDays(DueInDays), spec.Market)}", PageWidth - 90, 388, 24, Regular, ink, SKTextAlign.Right);
		}

		// The line table.
		var top = spec.Hard ? 480f : 430f;
		using var header = new SKPaint { Color = new SKColor(0xEE, 0xF1, 0xF5) };
		canvas.DrawRect(90, top - 38, PageWidth - 180, 54, header);
		Text(canvas, labels.Description, 110, top, 24, Bold, ink);
		Text(canvas, labels.Quantity, 700, top, 24, Bold, ink, SKTextAlign.Right);
		Text(canvas, labels.UnitPrice, 920, top, 24, Bold, ink, SKTextAlign.Right);
		Text(canvas, labels.Amount, PageWidth - 110, top, 24, Bold, ink, SKTextAlign.Right);

		var y = top + 62;
		foreach (var line in invoice.Lines)
		{
			Text(canvas, line.Description, 110, y, 24, Regular, ink);
			Text(canvas, FormatQuantity(line.Quantity, spec.Market), 700, y, 24, Regular, ink, SKTextAlign.Right);
			Text(canvas, FormatAmount(line.UnitPrice, spec.Market), 920, y, 24, Regular, ink, SKTextAlign.Right);
			Text(canvas, FormatAmount(line.LineTotal, spec.Market), PageWidth - 110, y, 24, Regular, ink, SKTextAlign.Right);
			canvas.DrawLine(90, y + 22, PageWidth - 90, y + 22, rule);
			y += 58;
		}

		// Totals, right-aligned under the table.
		y += 40;
		foreach (var (label, amount, strong) in new[]
		{
			(labels.Subtotal, invoice.Subtotal!.Value, false),
			(labels.Tax, invoice.TaxTotal!.Value, false),
			(labels.Total, invoice.Total!.Value, true),
		})
		{
			var typeface = strong ? Bold : Regular;
			Text(canvas, label, 900, y, strong ? 30 : 26, typeface, ink, SKTextAlign.Right);
			Text(canvas, $"{spec.Market.Symbol} {FormatAmount(amount, spec.Market)}", PageWidth - 110, y, strong ? 30 : 26, typeface, ink, SKTextAlign.Right);
			y += 48;
		}

		Text(canvas, labels.ThankYou, PageWidth / 2f, PageHeight - 120, 22, Regular, muted, SKTextAlign.Center);

		if (spec.Hard)
		{
			Stamp(canvas, labels.Paid, 640, top + 150);
		}
	}

	// A rubber stamp across the table: translucent, so what is under it stays legible, but it is in the way.
	private static void Stamp(SKCanvas canvas, string text, float x, float y)
	{
		var red = new SKColor(0xC0, 0x1B, 0x1B, 0x8C);
		canvas.Save();
		canvas.RotateDegrees(-16, x, y);
		using var font = new SKFont(Bold, 88);
		using var paint = new SKPaint { Color = red, IsAntialias = true };
		var width = font.MeasureText(text);
		using var border = new SKPaint { Color = red, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7 };
		canvas.DrawRoundRect(new SKRect(x - (width / 2) - 30, y - 90, x + (width / 2) + 30, y + 26), 14, 14, border);
		canvas.DrawText(text, x, y, SKTextAlign.Center, font, paint);
		canvas.Restore();
	}

	// A purchase order number derived from the invoice number, so it regenerates identically and differs from it.
	private static string OrderNumber(ExtractedInvoice invoice)
	{
		var digits = new string((invoice.Number ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
		var n = digits.Length == 0 ? 0 : int.Parse(digits, CultureInfo.InvariantCulture);
		return ((n * 37 % 9000) + 1000).ToString(CultureInfo.InvariantCulture);
	}

	// The customer's tax id, in the issuer country's shape, so a model that takes it instead of the issuer's
	// is not caught by the format rule: that mistake must be measured, not masked.
	private static string CustomerTaxId(DocumentSpec spec)
	{
		var seed = (spec.Printed.ReceiverName ?? spec.Id).Aggregate(17, static (h, c) => unchecked((h * 31) + c)) & int.MaxValue;
		return (spec.Market.Code, spec.Market.Currency) switch
		{
			("NI", _) => string.Create(CultureInfo.InvariantCulture, $"J0310000{seed % 1_000_000:000000}"),
			("MX", _) => "XAXX010101000",
			(_, "USD") => string.Create(CultureInfo.InvariantCulture, $"84-{seed % 10_000_000:0000000}"),
			_ => string.Create(CultureInfo.InvariantCulture, $"B{seed % 100_000_000:00000000}"),
		};
	}

	private static SKBitmap DrawReceipt(DocumentSpec spec)
	{
		var invoice = spec.Printed;
		var labels = Labels.For(spec.Market);
		const float lineHeight = 34f;

		// Header, a two-row block per line, the totals and the footer.
		var height = (int)(lineHeight * (12 + (invoice.Lines.Count * 2) + 6 + (spec.Hard ? 1 : 0))) + 80;
		var bitmap = new SKBitmap(ReceiptWidth, height);
		using var canvas = new SKCanvas(bitmap);
		canvas.Clear(new SKColor(0xF7, 0xF5, 0xEF));

		// Thermal print fades: dark grey rather than black.
		var ink = new SKColor(0x3A, 0x3A, 0x3A);
		var center = ReceiptWidth / 2f;
		var y = 60f;
		void Row(string text, SKTextAlign align = SKTextAlign.Left, float x = 24)
		{
			Text(canvas, text, x, y, 22, Mono, ink, align);
			y += lineHeight;
		}

		Row(invoice.IssuerName ?? string.Empty, SKTextAlign.Center, center);
		Row($"{labels.TaxId} {invoice.IssuerTaxId}", SKTextAlign.Center, center);
		Row(spec.Issuer.City, SKTextAlign.Center, center);
		Row(new string('-', 38));
		Row($"{labels.Ticket} {invoice.Number}");
		Row($"{labels.Date} {FormatDate(invoice.IssueDate!.Value, spec.Market)}");
		if (spec.Hard)
		{
			Row($"{labels.Register} 02  Trans {OrderNumber(invoice)}");
		}
		Row(new string('-', 38));

		foreach (var line in invoice.Lines)
		{
			Row(line.Description.Length > 34 ? line.Description[..34] : line.Description);
			var detail = $"{FormatQuantity(line.Quantity, spec.Market)} x {FormatAmount(line.UnitPrice, spec.Market)}";
			Text(canvas, detail, 48, y, 22, Mono, ink);
			Row(FormatAmount(line.LineTotal, spec.Market), SKTextAlign.Right, ReceiptWidth - 24);
		}

		Row(new string('-', 38));
		foreach (var (label, amount) in new[]
		{
			(labels.Subtotal.ToUpperInvariant(), invoice.Subtotal!.Value),
			(labels.Tax.ToUpperInvariant(), invoice.TaxTotal!.Value),
			(labels.Total.ToUpperInvariant(), invoice.Total!.Value),
		})
		{
			Text(canvas, label, 24, y, 22, Mono, ink);
			Row($"{spec.Market.Symbol}{FormatAmount(amount, spec.Market)}", SKTextAlign.Right, ReceiptWidth - 24);
		}

		Row(new string('-', 38));
		Row(labels.ThankYou.ToUpperInvariant(), SKTextAlign.Center, center);
		return bitmap;
	}

#pragma warning disable CA5394 // Seeded on purpose: the dataset regenerates identically.
	private static SKBitmap Scanned(SKBitmap page, Random random)
	{
		var scan = new SKBitmap(page.Width, page.Height);
		using var canvas = new SKCanvas(scan);
		canvas.Clear(new SKColor(0xF4, 0xF4, 0xF0));
		canvas.RotateDegrees((float)((random.NextDouble() - 0.5) * 0.8), page.Width / 2f, page.Height / 2f);
		using var image = SKImage.FromBitmap(page);
		canvas.DrawImage(image, 0, 0, Sampling);
		return scan;
	}

	private static SKBitmap Photographed(SKBitmap page, Random random)
	{
		const int margin = 180;
		var width = page.Width + (margin * 2);
		var height = page.Height + (margin * 2);
		using var photo = new SKBitmap(width, height);
		using (var canvas = new SKCanvas(photo))
		{
			// A wooden table.
			using var table = new SKPaint
			{
				Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height), [new SKColor(0x8B, 0x6B, 0x4A), new SKColor(0x5E, 0x45, 0x2C)], SKShaderTileMode.Clamp),
			};
			canvas.DrawRect(0, 0, width, height, table);

			// The page, tilted, with a soft shadow.
			var tilt = (float)((random.NextDouble() < 0.5 ? -1 : 1) * (2 + (random.NextDouble() * 4)));
			canvas.Save();
			canvas.RotateDegrees(tilt, width / 2f, height / 2f);
			using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 90), ImageFilter = SKImageFilter.CreateBlur(18, 18) };
			canvas.DrawRect(margin + 14, margin + 18, page.Width, page.Height, shadow);
			using var soft = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(0.9f, 0.9f) };
			using var image = SKImage.FromBitmap(page);
			canvas.DrawImage(image, margin, margin, Sampling, soft);
			canvas.Restore();

			// Uneven light: brighter near a window on one side.
			using var light = new SKPaint
			{
				Shader = SKShader.CreateRadialGradient(new SKPoint(width * 0.2f, height * 0.15f), width * 1.1f, [new SKColor(255, 255, 255, 40), new SKColor(0, 0, 0, 70)], SKShaderTileMode.Clamp),
			};
			canvas.DrawRect(0, 0, width, height, light);
		}

		AddNoise(photo, random);
		return Downscaled(photo, PhotoMaxEdge);
	}

	// An office scanner at 100 dpi in greyscale with a tired lamp: smaller, washed out, crooked, noisy.
	private static SKBitmap PoorScan(SKBitmap page, Random random)
	{
		var width = (int)(page.Width * HardScanScale);
		var height = (int)(page.Height * HardScanScale);
		var scan = new SKBitmap(width, height);
		using (var canvas = new SKCanvas(scan))
		{
			canvas.Clear(new SKColor(0xEC, 0xEB, 0xE4));
			var tilt = (float)((random.NextDouble() < 0.5 ? -1 : 1) * (1 + (random.NextDouble() * 1.5)));
			canvas.RotateDegrees(tilt, width / 2f, height / 2f);
			using var paint = new SKPaint { ColorFilter = Washed(contrast: 0.72f, lift: 0.2f), ImageFilter = SKImageFilter.CreateBlur(0.7f, 0.7f) };
			using var image = SKImage.FromBitmap(page);
			canvas.DrawImage(image, new SKRect(0, 0, width, height), Sampling, paint);
		}

		AddNoise(scan, random, density: 15, amplitude: 40);
		return scan;
	}

	// A cheap phone at an angle in poor light: keystoned, more tilted, a hand's shadow across a corner, soft.
	private static SKBitmap PoorPhoto(SKBitmap page, Random random)
	{
		const int margin = 160;
		var width = page.Width + (margin * 2);
		var height = page.Height + (margin * 2);
		using var photo = new SKBitmap(width, height);
		using (var canvas = new SKCanvas(photo))
		{
			using var table = new SKPaint
			{
				Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height), [new SKColor(0x6E, 0x55, 0x3C), new SKColor(0x45, 0x33, 0x22)], SKShaderTileMode.Clamp),
			};
			canvas.DrawRect(0, 0, width, height, table);

			var tilt = (float)((random.NextDouble() < 0.5 ? -1 : 1) * (5 + (random.NextDouble() * 4)));
			canvas.Save();
			canvas.RotateDegrees(tilt, width / 2f, height / 2f);

			// Keystone: the far edge of the page is smaller than the near one.
			canvas.Concat(new SKMatrix(1, 0, 0, 0, 1, 0, 0.00009f, 0, 1));
			using var soft = new SKPaint { ColorFilter = Washed(contrast: 0.85f, lift: 0.04f), ImageFilter = SKImageFilter.CreateBlur(1.5f, 1.5f) };
			using var image = SKImage.FromBitmap(page);
			canvas.DrawImage(image, margin, margin, Sampling, soft);
			canvas.Restore();

			using var shadow = new SKPaint
			{
				Shader = SKShader.CreateLinearGradient(new SKPoint(width, height), new SKPoint(width * 0.55f, height * 0.55f), [new SKColor(0, 0, 0, 130), new SKColor(0, 0, 0, 0)], SKShaderTileMode.Clamp),
			};
			canvas.DrawRect(0, 0, width, height, shadow);
		}

		AddNoise(photo, random, density: 25, amplitude: 38);
		return Downscaled(photo, HardPhotoMaxEdge);
	}

	// A receipt that sat in a wallet: on a 58 mm roll, the print faded, worse towards one edge.
	private static SKBitmap Faded(SKBitmap receipt, Random random)
	{
		var height = (int)(receipt.Height * ((float)NarrowReceiptWidth / receipt.Width));
		var faded = new SKBitmap(NarrowReceiptWidth, height);
		using (var canvas = new SKCanvas(faded))
		{
			using var paint = new SKPaint { ColorFilter = Washed(contrast: 0.45f, lift: 0.45f) };
			using var image = SKImage.FromBitmap(receipt);
			canvas.DrawImage(image, new SKRect(0, 0, NarrowReceiptWidth, height), Sampling, paint);

			using var wear = new SKPaint
			{
				Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(NarrowReceiptWidth, 0), [new SKColor(255, 255, 255, 0), new SKColor(255, 255, 255, 90)], SKShaderTileMode.Clamp),
			};
			canvas.DrawRect(0, 0, NarrowReceiptWidth, height, wear);
		}

		AddNoise(faded, random, density: 20, amplitude: 30);
		return faded;
	}

	// Greyscale with reduced contrast: black lifts towards grey, white dims a little.
	private static SKColorFilter Washed(float contrast, float lift)
	{
		const float r = 0.299f, g = 0.587f, b = 0.114f;
		return SKColorFilter.CreateColorMatrix(
		[
			r * contrast, g * contrast, b * contrast, 0, lift,
			r * contrast, g * contrast, b * contrast, 0, lift,
			r * contrast, g * contrast, b * contrast, 0, lift,
			0, 0, 0, 1, 0,
		]);
	}

	private static void AddNoise(SKBitmap bitmap, Random random, int density = 40, int amplitude = 28)
	{
		// Sensor noise: a sparse sprinkle of slightly brighter and darker pixels, one per density pixels.
		for (var i = 0; i < bitmap.Width * bitmap.Height / density; i++)
		{
			var x = random.Next(bitmap.Width);
			var y = random.Next(bitmap.Height);
			var pixel = bitmap.GetPixel(x, y);
			var delta = random.Next(-amplitude, amplitude + 1);
			bitmap.SetPixel(x, y, new SKColor(Clamp(pixel.Red + delta), Clamp(pixel.Green + delta), Clamp(pixel.Blue + delta)));
		}
	}
#pragma warning restore CA5394

	private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

	private static SKBitmap Downscaled(SKBitmap source, int maxEdge)
	{
		var scale = Math.Min(1f, (float)maxEdge / Math.Max(source.Width, source.Height));
		var resized = new SKBitmap((int)(source.Width * scale), (int)(source.Height * scale));
		using var canvas = new SKCanvas(resized);
		using var image = SKImage.FromBitmap(source);
		canvas.DrawImage(image, new SKRect(0, 0, resized.Width, resized.Height), new SKSamplingOptions(SKCubicResampler.Mitchell));
		return resized;
	}

	private static void WritePng(SKBitmap bitmap, string path)
	{
		using (bitmap)
		{
			using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
			File.WriteAllBytes(path, data.ToArray());
		}
	}

	private static void WriteJpeg(SKBitmap bitmap, string path, int quality)
	{
		using (bitmap)
		{
			using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, quality);
			File.WriteAllBytes(path, data.ToArray());
		}
	}

	private static void Text(SKCanvas canvas, string text, float x, float y, float size, SKTypeface typeface, SKColor color, SKTextAlign align = SKTextAlign.Left)
	{
		using var font = new SKFont(typeface, size);
		using var paint = new SKPaint { Color = color, IsAntialias = true };
		canvas.DrawText(text, x, y, align, font, paint);
	}

	// InvariantGlobalization is on, so the European format (1.150,00) is produced by swapping separators.
	internal static string FormatAmount(decimal amount, Market market)
	{
		var text = amount.ToString("N2", CultureInfo.InvariantCulture);
		return market.DecimalComma ? text.Replace(',', '\u0001').Replace('.', ',').Replace('\u0001', '.') : text;
	}

	internal static string FormatQuantity(decimal quantity, Market market)
	{
		var text = quantity == decimal.Truncate(quantity)
			? quantity.ToString("0", CultureInfo.InvariantCulture)
			: quantity.ToString("0.0##", CultureInfo.InvariantCulture);
		return market.DecimalComma ? text.Replace('.', ',') : text;
	}

	// US documents print month first; every other market prints day first.
	internal static string FormatDate(DateOnly date, Market market) =>
		date.ToString(market.Currency == "USD" ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture);

	private sealed record Labels(
		string Title, string TaxId, string Number, string Date, string Currency, string Customer, string Description,
		string Quantity, string UnitPrice, string Amount, string Subtotal, string Tax, string Total, string ThankYou, string Ticket,
		string CustomerTaxId, string Order, string DueDate, string Paid, string Register)
	{
		public static Labels For(Market market) => (market.Language, market.Code, market.Currency) switch
		{
			("en", _, _) => new("INVOICE", "Tax ID", "Invoice #", "Date", "Currency", "Bill to", "Description", "Qty", "Unit price", "Amount", "Subtotal", "Sales tax", "Total", "Thank you for your business", "Receipt", "Customer tax ID", "PO #", "Due date", "PAID", "Register"),
			(_, "NI", _) => new("FACTURA", "RUC", "No.", "Fecha", "Moneda", "Cliente", "Descripción", "Cant.", "P. unitario", "Importe", "Subtotal", "IVA", "Total", "Gracias por su compra", "Ticket", "RUC cliente", "Pedido", "Vence", "PAGADO", "Caja"),
			(_, "MX", _) => new("FACTURA", "RFC", "Folio", "Fecha", "Moneda", "Cliente", "Descripción", "Cant.", "P. unitario", "Importe", "Subtotal", "IVA", "Total", "Gracias por su preferencia", "Ticket", "RFC cliente", "Orden de compra", "Vencimiento", "PAGADO", "Caja"),
			_ => new("FACTURA", "NIF", "Nº", "Fecha", "Moneda", "Cliente", "Concepto", "Cant.", "Precio", "Importe", "Base imponible", "IVA", "Total", "Gracias por su confianza", "Ticket", "NIF cliente", "Pedido", "Vencimiento", "PAGADO", "Caja"),
		};
	}
}
