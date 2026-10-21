namespace InvoiceExtractor.Domain.Validation;

using System.Globalization;

/// <summary>
/// Field paths an issue points at. They name the JSON properties the model returns, so the review screen can
/// highlight the exact field and the retry prompt can name it back to the model.
/// </summary>
public static class InvoiceFields
{
	/// <summary>The issuer's tax id.</summary>
	public const string IssuerTaxId = "issuerTaxId";

	/// <summary>The invoice number.</summary>
	public const string Number = "number";

	/// <summary>The issue date.</summary>
	public const string IssueDate = "issueDate";

	/// <summary>The subtotal.</summary>
	public const string Subtotal = "subtotal";

	/// <summary>The total tax.</summary>
	public const string TaxTotal = "taxTotal";

	/// <summary>The amount payable.</summary>
	public const string Total = "total";

	/// <summary>The line total of one line, zero-based.</summary>
	/// <param name="index">The line's position.</param>
	/// <returns>A path such as <c>lines[2].lineTotal</c>.</returns>
	public static string LineTotal(int index) => string.Create(CultureInfo.InvariantCulture, $"lines[{index}].lineTotal");
}
