namespace InvoiceExtractor.Domain.Tax;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Mexico: IVA at 16% (Ley del IVA), 0% for zero-rated goods, and 8% in the border regions — which comes from
/// a yearly decree of fiscal incentives, not from the law, so whether it applies in a given year is a
/// configuration decision (see <c>docs/verification/tax-rules.md</c>).
/// <para>
/// The RFC is 12 characters for a company (3 letters from the name, the incorporation date as YYMMDD, a
/// 3-character homoclave) and 13 for an individual (4 letters, the birth date, the homoclave). The generic
/// RFCs <c>XAXX010101000</c> and <c>XEXX010101000</c> fit the same shape. The homoclave's check digit is not
/// verified: the SAT assigns it, and a wrong one only sends the document to review.
/// </para>
/// </summary>
public sealed partial class MexicoTaxProfile : RateListTaxProfile
{
	/// <summary>The ISO 3166-1 alpha-2 code.</summary>
	public const string Code = "MX";

	/// <summary>Creates the profile with the configured rates.</summary>
	/// <param name="rates">IVA rates as fractions; the documented defaults are 0.16, 0.08 and 0.</param>
	public MexicoTaxProfile(IEnumerable<decimal> rates)
		: base(rates)
	{
	}

	/// <inheritdoc />
	public override string CountryCode => Code;

	/// <inheritdoc />
	public override bool IsValidTaxId(string taxId)
	{
		var match = RfcFormat().Match(TaxId.Normalize(taxId));
		return match.Success && IsCalendarDate(match.Groups["date"].Value);
	}

	// The two-digit year cannot be placed in a century, so the check is month and day only; 29 February
	// is accepted because some year with those two digits is a leap year.
	private static bool IsCalendarDate(string yymmdd)
	{
		var month = int.Parse(yymmdd.AsSpan(2, 2), CultureInfo.InvariantCulture);
		var day = int.Parse(yymmdd.AsSpan(4, 2), CultureInfo.InvariantCulture);
		return month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(2000, month);
	}

	[GeneratedRegex(@"^[A-ZÑ&]{3,4}(?<date>\d{6})[A-Z0-9]{3}$", RegexOptions.CultureInvariant)]
	private static partial Regex RfcFormat();
}
