namespace InvoiceExtractor.Domain.Validation;

/// <summary>One failed rule, pointing at the field a reviewer should look at.</summary>
/// <param name="Field">The field path, from <see cref="InvoiceFields"/>.</param>
/// <param name="Code">Which rule failed.</param>
/// <param name="Detail">
/// The numbers behind the failure, in English, for the retry prompt and the logs, e.g. "Lines add up to 1150.00,
/// subtotal reads 1510.00."
/// </param>
/// <param name="Values">
/// The same numbers one by one, keyed by <see cref="IssueValues"/> and written invariantly (amounts with two
/// decimals, dates ISO 8601), so a screen in any language can word the rule itself. Absent on issues recorded
/// before they existed.
/// </param>
public sealed record ValidationIssue(string Field, IssueCode Code, string Detail, IReadOnlyDictionary<string, string>? Values = null);

/// <summary>The outcome of validating one extraction.</summary>
/// <param name="Issues">Every failed rule; empty when the extraction is consistent.</param>
public sealed record InvoiceValidation(IReadOnlyList<ValidationIssue> Issues)
{
	/// <summary>Whether every rule passed, so the document can be approved without review.</summary>
	public bool IsValid => Issues.Count == 0;
}

/// <summary>The keys of <see cref="ValidationIssue.Values"/>; the web app's messages use the same names.</summary>
public static class IssueValues
{
	/// <summary>A line's position, one-based.</summary>
	public const string Line = "line";

	/// <summary>A line's quantity.</summary>
	public const string Quantity = "quantity";

	/// <summary>A line's unit price.</summary>
	public const string UnitPrice = "unitPrice";

	/// <summary>What a computation should have given.</summary>
	public const string Expected = "expected";

	/// <summary>The amount the document shows instead.</summary>
	public const string Actual = "actual";

	/// <summary>The subtotal.</summary>
	public const string Subtotal = "subtotal";

	/// <summary>The tax.</summary>
	public const string Tax = "tax";

	/// <summary>The tax rate as a fraction, four decimals.</summary>
	public const string Rate = "rate";

	/// <summary>The tax profile's country code.</summary>
	public const string Country = "country";

	/// <summary>The tax id as read.</summary>
	public const string TaxId = "taxId";

	/// <summary>The tax id format the country expects.</summary>
	public const string Format = "format";

	/// <summary>A date, yyyy-MM-dd.</summary>
	public const string Date = "date";

	/// <summary>A number of years.</summary>
	public const string Years = "years";
}
