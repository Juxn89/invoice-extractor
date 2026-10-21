namespace InvoiceExtractor.Domain.Invoices;

/// <summary>
/// What the model read from a document, before anyone trusts it. Every scalar can be missing: the prompt
/// tells the model to leave a field null rather than guess, and a missing key field sends the document to
/// review instead of failing the extraction.
/// </summary>
/// <param name="IssuerName">Who issued the invoice.</param>
/// <param name="IssuerTaxId">The issuer's tax id, copied as printed (normalised only for validation).</param>
/// <param name="ReceiverName">Who the invoice is addressed to, when printed.</param>
/// <param name="Number">The invoice or receipt number.</param>
/// <param name="IssueDate">The issue date.</param>
/// <param name="Currency">ISO 4217 code, only when the document states it.</param>
/// <param name="Lines">The line items; empty for a receipt that prints only totals.</param>
/// <param name="Subtotal">Amount before tax.</param>
/// <param name="TaxTotal">Total tax.</param>
/// <param name="Total">Amount payable.</param>
public sealed record ExtractedInvoice(
	string? IssuerName,
	string? IssuerTaxId,
	string? ReceiverName,
	string? Number,
	DateOnly? IssueDate,
	string? Currency,
	IReadOnlyList<InvoiceLine> Lines,
	decimal? Subtotal,
	decimal? TaxTotal,
	decimal? Total);
