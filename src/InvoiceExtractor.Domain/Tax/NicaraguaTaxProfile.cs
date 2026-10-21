namespace InvoiceExtractor.Domain.Tax;

using System.Text.RegularExpressions;

/// <summary>
/// Nicaragua: IVA at 15% (Ley 822, Ley de Concertación Tributaria) plus 0% for exports and exempt goods.
/// <para>
/// The RUC is 14 characters: a legal entity's starts with <c>J</c> followed by 13 digits, and an
/// individual's is their cédula, 13 digits and a check letter. Only third-party sources confirm this format
/// (see <c>docs/verification/tax-rules.md</c>); a document that fails it goes to review, it is not rejected.
/// </para>
/// </summary>
public sealed partial class NicaraguaTaxProfile : RateListTaxProfile
{
	/// <summary>The ISO 3166-1 alpha-2 code.</summary>
	public const string Code = "NI";

	/// <summary>Creates the profile with the configured rates.</summary>
	/// <param name="rates">IVA rates as fractions; the documented defaults are 0.15 and 0.</param>
	public NicaraguaTaxProfile(IEnumerable<decimal> rates)
		: base(rates)
	{
	}

	/// <inheritdoc />
	public override string CountryCode => Code;

	/// <inheritdoc />
	public override bool IsValidTaxId(string taxId) => RucFormat().IsMatch(TaxId.Normalize(taxId));

	[GeneratedRegex(@"^(J\d{13}|\d{13}[A-Z])$", RegexOptions.CultureInvariant)]
	private static partial Regex RucFormat();
}
