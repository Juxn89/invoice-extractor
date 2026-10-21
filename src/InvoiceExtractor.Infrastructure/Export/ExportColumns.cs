namespace InvoiceExtractor.Infrastructure.Export;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Documents;

/// <summary>The headers and labels both exports share, in each language.</summary>
internal sealed record ExportColumns(
	string InvoicesSheet,
	string LinesSheet,
	string Document,
	string File,
	string ApprovedBy,
	string Country,
	string Issuer,
	string IssuerTaxId,
	string Receiver,
	string Number,
	string IssueDate,
	string Currency,
	string Subtotal,
	string Tax,
	string Total,
	string LineNumber,
	string Description,
	string Quantity,
	string UnitPrice,
	string LineTotal,
	string ByRules,
	string ByPerson)
{
	private static readonly ExportColumns Spanish = new(
		"Facturas", "Líneas", "Documento", "Archivo", "Aprobada por", "País", "Emisor", "ID fiscal emisor", "Cliente",
		"Número", "Fecha", "Moneda", "Subtotal", "Impuesto", "Total", "Línea", "Descripción", "Cantidad", "Precio unitario",
		"Importe", "Reglas (automático)", "Revisión humana");

	private static readonly ExportColumns English = new(
		"Invoices", "Lines", "Document", "File", "Approved by", "Country", "Issuer", "Issuer tax id", "Customer",
		"Number", "Date", "Currency", "Subtotal", "Tax", "Total", "Line", "Description", "Quantity", "Unit price",
		"Amount", "Rules (automatic)", "Human review");

	public static ExportColumns For(ExportLanguage language) => language == ExportLanguage.En ? English : Spanish;

	public string Approval(DocumentStatus status) => status == DocumentStatus.Approved ? ByPerson : ByRules;
}
