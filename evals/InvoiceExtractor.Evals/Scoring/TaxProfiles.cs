namespace InvoiceExtractor.Evals.Scoring;

using InvoiceExtractor.Domain.Tax;

/// <summary>
/// The country profiles the evals validate with, at the default rates documented in
/// <c>docs/verification/tax-rules.md</c>. The application binds the same defaults from configuration.
/// </summary>
internal static class TaxProfiles
{
	private static readonly NicaraguaTaxProfile Nicaragua = new([0.15m, 0m]);
	private static readonly MexicoTaxProfile Mexico = new([0.16m, 0.08m, 0m]);
	private static readonly GenericTaxProfile Generic = new();

	/// <summary>The profile for a dataset country code.</summary>
	/// <param name="country">NI, MX or anything else for the generic profile.</param>
	/// <returns>The profile.</returns>
	public static ITaxProfile For(string country) => country switch
	{
		NicaraguaTaxProfile.Code => Nicaragua,
		MexicoTaxProfile.Code => Mexico,
		_ => Generic,
	};
}
