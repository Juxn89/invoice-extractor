namespace InvoiceExtractor.Domain.Validation;

using System.Globalization;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Tax;

/// <summary>
/// The deterministic half of extraction: the model proposes, this decides. Every rule that fails produces an
/// issue pointing at a field; none of them throws, and none rejects a document — a failed rule sends it to
/// review with that field highlighted.
/// </summary>
public static class InvoiceValidator
{
	/// <summary>
	/// Rounding allowance per amount. Documents round every line and total to the cent, so a correct invoice
	/// can be off by half a cent per rounded amount; one cent per amount absorbs that with margin.
	/// </summary>
	public const decimal CentTolerance = 0.01m;

	/// <summary>How far back an issue date is still plausible for a document someone is processing now.</summary>
	public const int MaxAgeInYears = 10;

	/// <summary>Validates one extraction.</summary>
	/// <param name="invoice">What the model read.</param>
	/// <param name="profile">The rules of the issuer's country.</param>
	/// <param name="today">The current date, passed in so the rule is testable and timezone-explicit.</param>
	/// <returns>Every failed rule; empty when the extraction is consistent.</returns>
	public static InvoiceValidation Validate(ExtractedInvoice invoice, ITaxProfile profile, DateOnly today)
	{
		ArgumentNullException.ThrowIfNull(invoice);
		ArgumentNullException.ThrowIfNull(profile);

		var issues = new List<ValidationIssue>();

		RequireKeyFields(invoice, issues);
		CheckLines(invoice, issues);
		CheckTotals(invoice, issues);
		CheckTaxRate(invoice, profile, issues);
		CheckTaxId(invoice, profile, issues);
		CheckDate(invoice, today, issues);

		return new InvoiceValidation(issues);
	}

	// The fields a bookkeeper cannot work without. Subtotal and tax may be absent on a simple receipt.
	private static void RequireKeyFields(ExtractedInvoice invoice, List<ValidationIssue> issues)
	{
		if (string.IsNullOrWhiteSpace(invoice.IssuerTaxId))
		{
			issues.Add(new(InvoiceFields.IssuerTaxId, IssueCode.MissingField, "The issuer's tax id was not read."));
		}

		if (string.IsNullOrWhiteSpace(invoice.Number))
		{
			issues.Add(new(InvoiceFields.Number, IssueCode.MissingField, "The invoice number was not read."));
		}

		if (invoice.IssueDate is null)
		{
			issues.Add(new(InvoiceFields.IssueDate, IssueCode.MissingField, "The issue date was not read."));
		}

		if (invoice.Total is null)
		{
			issues.Add(new(InvoiceFields.Total, IssueCode.MissingField, "The total was not read."));
		}
	}

	private static void CheckLines(ExtractedInvoice invoice, List<ValidationIssue> issues)
	{
		for (var index = 0; index < invoice.Lines.Count; index++)
		{
			var line = invoice.Lines[index];
			var expected = line.Quantity * line.UnitPrice;
			if (Math.Abs(expected - line.LineTotal) > CentTolerance)
			{
				issues.Add(new(
					InvoiceFields.LineTotal(index),
					IssueCode.LineArithmetic,
					Describe($"Line {index + 1}: {line.Quantity} × {line.UnitPrice} is {expected:0.00}, the line total reads {line.LineTotal:0.00}.")));
			}
		}

		if (invoice.Lines.Count == 0 || invoice.Subtotal is not { } subtotal)
		{
			return;
		}

		var sum = invoice.Lines.Sum(static line => line.LineTotal);
		if (Math.Abs(sum - subtotal) > CentTolerance * invoice.Lines.Count)
		{
			issues.Add(new(
				InvoiceFields.Subtotal,
				IssueCode.LinesDoNotMatchSubtotal,
				Describe($"Lines add up to {sum:0.00}, the subtotal reads {subtotal:0.00}.")));
		}
	}

	private static void CheckTotals(ExtractedInvoice invoice, List<ValidationIssue> issues)
	{
		if (invoice is not { Subtotal: { } subtotal, Total: { } total })
		{
			return;
		}

		// A receipt that prints no tax line is treated as tax 0, which is what its total then has to show.
		// Two rounded amounts meet here, subtotal and tax, so two cents of allowance.
		var tax = invoice.TaxTotal ?? 0;
		if (Math.Abs(subtotal + tax - total) > CentTolerance * 2)
		{
			issues.Add(new(
				InvoiceFields.Total,
				IssueCode.TotalMismatch,
				Describe($"Subtotal {subtotal:0.00} plus tax {tax:0.00} is {subtotal + tax:0.00}, the total reads {total:0.00}.")));
		}
	}

	private static void CheckTaxRate(ExtractedInvoice invoice, ITaxProfile profile, List<ValidationIssue> issues)
	{
		if (invoice is not { Subtotal: > 0 and var subtotal, TaxTotal: { } tax })
		{
			return;
		}

		var impliedRate = tax / subtotal;
		if (!profile.IsPlausibleRate(impliedRate))
		{
			issues.Add(new(
				InvoiceFields.TaxTotal,
				IssueCode.ImplausibleTaxRate,
				Describe($"Tax {tax:0.00} on a subtotal of {subtotal:0.00} is a rate of {impliedRate:P2}, which {profile.CountryCode} does not charge.")));
		}
	}

	private static void CheckTaxId(ExtractedInvoice invoice, ITaxProfile profile, List<ValidationIssue> issues)
	{
		if (string.IsNullOrWhiteSpace(invoice.IssuerTaxId) || profile.IsValidTaxId(invoice.IssuerTaxId))
		{
			return;
		}

		issues.Add(new(
			InvoiceFields.IssuerTaxId,
			IssueCode.InvalidTaxId,
			Describe($"\"{invoice.IssuerTaxId}\" does not have the format of a {profile.CountryCode} tax id: {profile.TaxIdFormat}.")));
	}

	private static void CheckDate(ExtractedInvoice invoice, DateOnly today, List<ValidationIssue> issues)
	{
		if (invoice.IssueDate is not { } date)
		{
			return;
		}

		if (date > today)
		{
			issues.Add(new(InvoiceFields.IssueDate, IssueCode.FutureDate, Describe($"The issue date {date:yyyy-MM-dd} is after today.")));
		}
		else if (date < today.AddYears(-MaxAgeInYears))
		{
			issues.Add(new(
				InvoiceFields.IssueDate,
				IssueCode.DateTooOld,
				Describe($"The issue date {date:yyyy-MM-dd} is more than {MaxAgeInYears} years old.")));
		}
	}

	// Details are read by people and fed back to the model, so the numbers are always written the same way.
	private static string Describe(FormattableString detail) => detail.ToString(CultureInfo.InvariantCulture);
}
