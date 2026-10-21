namespace InvoiceExtractor.Infrastructure.Export;

using System.Globalization;
using System.Text;
using InvoiceExtractor.Application.Abstractions;

/// <summary>
/// One row per invoice, RFC 4180: comma-separated, fields quoted when needed, UTF-8 with a byte-order mark so
/// Excel reads the accents. Amounts use a dot and no thousands separator and dates are ISO 8601, whatever the
/// server's culture, so the file imports the same everywhere.
/// </summary>
internal sealed class CsvExport : IAccountingExport
{
	private const char Separator = ',';
	private const string NewLine = "\r\n";

	// A cell starting with one of these is run as a formula by Excel and LibreOffice: a document could carry
	// "=HYPERLINK(...)" in a field the model copies faithfully. Prefixing an apostrophe keeps it text (OWASP,
	// CSV injection). Applied to text fields only: a negative amount is a number, not an attack.
	private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

	public ExportFormat Format => ExportFormat.Csv;

	public string ContentType => "text/csv; charset=utf-8";

	public string Extension => ".csv";

	public byte[] Write(IReadOnlyList<ExportRow> rows, ExportLanguage language)
	{
		ArgumentNullException.ThrowIfNull(rows);
		var c = ExportColumns.For(language);
		var csv = new StringBuilder();
		Row(csv, [c.Document, c.File, c.ApprovedBy, c.Country, c.Issuer, c.IssuerTaxId, c.Receiver, c.Number, c.IssueDate, c.Currency, c.Subtotal, c.Tax, c.Total]);
		foreach (var row in rows)
		{
			var invoice = row.Invoice;
			Row(csv,
			[
				row.DocumentId.ToString(),
				Text(row.FileName),
				c.Approval(row.ApprovedBy),
				row.Country,
				Text(invoice.IssuerName),
				Text(invoice.IssuerTaxId),
				Text(invoice.ReceiverName),
				Text(invoice.Number),
				invoice.IssueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
				Text(invoice.Currency),
				Amount(invoice.Subtotal),
				Amount(invoice.TaxTotal),
				Amount(invoice.Total),
			]);
		}

		return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
	}

	/// <summary>Neutralises a text value Excel would run as a formula.</summary>
	/// <param name="value">The text.</param>
	/// <returns>The text, safe to open.</returns>
	internal static string Text(string? value) =>
		value is { Length: > 0 } && FormulaTriggers.Contains(value[0]) ? "'" + value : value ?? string.Empty;

	private static string Amount(decimal? value) =>
		value?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;

	private static void Row(StringBuilder csv, IEnumerable<string> fields)
	{
		csv.AppendJoin(Separator, fields.Select(Quote)).Append(NewLine);
	}

	// RFC 4180: a field with a separator, a quote or a line break is quoted, and its quotes doubled.
	private static string Quote(string field) =>
		field.AsSpan().IndexOfAny([Separator, '"', '\r', '\n']) >= 0 ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : field;
}
