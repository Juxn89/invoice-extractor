namespace InvoiceExtractor.Domain.Tax;

using System.Text.RegularExpressions;

/// <summary>
/// Any country without a profile of its own. It cannot know the rates or the id format, so it only catches
/// what is implausible anywhere: a negative or very high rate, an id with no letters or digits to speak of.
/// </summary>
public sealed partial class GenericTaxProfile : ITaxProfile
{
	/// <summary>The code used when the country is unknown.</summary>
	public const string Code = "XX";

	/// <summary>
	/// The highest standard VAT/GST rate in use anywhere is in the high twenties; anything above this is far
	/// more likely a misread amount than a real tax.
	/// </summary>
	public const decimal MaxPlausibleRate = 0.30m;

	/// <inheritdoc />
	public string CountryCode => Code;

	/// <inheritdoc />
	public bool IsPlausibleRate(decimal impliedRate) => impliedRate is >= 0 and <= MaxPlausibleRate;

	/// <inheritdoc />
	public bool IsValidTaxId(string taxId) => AnyTaxId().IsMatch(TaxId.Normalize(taxId));

	/// <inheritdoc />
	public string TaxIdFormat => "3 to 20 letters or digits, at least one of them a digit";

	// 3 to 20 letters or digits covers the national formats seen in practice, from short European VAT
	// suffixes to 20-character ids. At least one digit, because every national format has digits: without
	// it, a field that read as words ("NOT A TAX ID" once spaces are dropped) would pass.
	[GeneratedRegex(@"^(?=.*\d)[A-Z0-9]{3,20}$", RegexOptions.CultureInvariant)]
	private static partial Regex AnyTaxId();
}
