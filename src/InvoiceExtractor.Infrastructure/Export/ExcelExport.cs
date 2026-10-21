namespace InvoiceExtractor.Infrastructure.Export;

using ClosedXML.Excel;
using InvoiceExtractor.Application.Abstractions;

/// <summary>
/// A workbook with two sheets, each an Excel table (filter, banding, header frozen): one row per invoice, and
/// one row per line with its invoice's number and issuer to join on. Amounts are numbers and dates are dates,
/// so they sum, sort and filter in Excel and LibreOffice without conversion.
/// </summary>
internal sealed class ExcelExport : IAccountingExport
{
	private const string AmountFormat = "#,##0.00";
	private const string QuantityFormat = "#,##0.####";
	private const string DateFormat = "yyyy-mm-dd";

	// Fixed widths, in characters: measuring text would need font files, which a slim container lacks.
	private static readonly double[] InvoiceWidths = [38, 24, 20, 8, 34, 20, 26, 16, 12, 9, 14, 14, 14];
	private static readonly double[] LineWidths = [16, 20, 7, 40, 11, 15, 14];

	public ExportFormat Format => ExportFormat.Xlsx;

	public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

	public string Extension => ".xlsx";

	public byte[] Write(IReadOnlyList<ExportRow> rows, ExportLanguage language)
	{
		ArgumentNullException.ThrowIfNull(rows);
		var c = ExportColumns.For(language);
		using var workbook = new XLWorkbook();

		var invoices = workbook.AddWorksheet(c.InvoicesSheet);
		Header(invoices, [c.Document, c.File, c.ApprovedBy, c.Country, c.Issuer, c.IssuerTaxId, c.Receiver, c.Number, c.IssueDate, c.Currency, c.Subtotal, c.Tax, c.Total]);
		for (var i = 0; i < rows.Count; i++)
		{
			var row = rows[i];
			var invoice = row.Invoice;
			var r = i + 2;
			invoices.Cell(r, 1).Value = row.DocumentId.ToString();
			invoices.Cell(r, 2).Value = row.FileName;
			invoices.Cell(r, 3).Value = c.Approval(row.ApprovedBy);
			invoices.Cell(r, 4).Value = row.Country;
			invoices.Cell(r, 5).Value = invoice.IssuerName;
			invoices.Cell(r, 6).Value = invoice.IssuerTaxId;
			invoices.Cell(r, 7).Value = invoice.ReceiverName;
			invoices.Cell(r, 8).Value = invoice.Number;
			if (invoice.IssueDate is { } date)
			{
				invoices.Cell(r, 9).Value = date.ToDateTime(TimeOnly.MinValue);
			}

			invoices.Cell(r, 10).Value = invoice.Currency;
			Amount(invoices.Cell(r, 11), invoice.Subtotal);
			Amount(invoices.Cell(r, 12), invoice.TaxTotal);
			Amount(invoices.Cell(r, 13), invoice.Total);
		}

		invoices.Column(9).Style.NumberFormat.Format = DateFormat;
		Finish(invoices, rows.Count, InvoiceWidths, c.InvoicesSheet);

		var lines = workbook.AddWorksheet(c.LinesSheet);
		Header(lines, [c.Number, c.IssuerTaxId, c.LineNumber, c.Description, c.Quantity, c.UnitPrice, c.LineTotal]);
		var line = 2;
		foreach (var row in rows)
		{
			for (var i = 0; i < row.Invoice.Lines.Count; i++, line++)
			{
				var item = row.Invoice.Lines[i];
				lines.Cell(line, 1).Value = row.Invoice.Number;
				lines.Cell(line, 2).Value = row.Invoice.IssuerTaxId;
				lines.Cell(line, 3).Value = i + 1;
				lines.Cell(line, 4).Value = item.Description;
				lines.Cell(line, 5).Value = item.Quantity;
				lines.Cell(line, 5).Style.NumberFormat.Format = QuantityFormat;
				Amount(lines.Cell(line, 6), item.UnitPrice);
				Amount(lines.Cell(line, 7), item.LineTotal);
			}
		}

		Finish(lines, line - 2, LineWidths, c.LinesSheet);

		using var stream = new MemoryStream();
		workbook.SaveAs(stream);
		return stream.ToArray();
	}

	private static void Header(IXLWorksheet sheet, string[] titles)
	{
		for (var i = 0; i < titles.Length; i++)
		{
			sheet.Cell(1, i + 1).Value = titles[i];
		}
	}

	private static void Amount(IXLCell cell, decimal? amount)
	{
		if (amount is { } value)
		{
			cell.Value = value;
			cell.Style.NumberFormat.Format = AmountFormat;
		}
	}

	private static void Finish(IXLWorksheet sheet, int rows, double[] widths, string name)
	{
		// An Excel table needs a data row; an empty export keeps the bold header without one.
		var header = sheet.Range(1, 1, 1, widths.Length);
		if (rows > 0)
		{
			sheet.Range(1, 1, rows + 1, widths.Length).CreateTable(name.Replace(' ', '_'));
		}
		else
		{
			header.Style.Font.Bold = true;
		}

		sheet.SheetView.FreezeRows(1);
		for (var i = 0; i < widths.Length; i++)
		{
			sheet.Column(i + 1).Width = widths[i];
		}
	}
}
