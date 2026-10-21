namespace InvoiceExtractor.Evals.Scoring;

using InvoiceExtractor.Application.Features.Processing;
using InvoiceExtractor.Domain.Tax;
using Microsoft.Extensions.Options;

/// <summary>
/// The country profiles the evals validate with: the app's catalog at its default rates (see
/// <c>docs/verification/tax-rules.md</c>), so the evals cannot drift from what the app runs.
/// </summary>
internal static class TaxProfiles
{
	private static readonly TaxProfileCatalog Catalog = new(Options.Create(new TaxOptions()));

	/// <summary>The profile for a dataset country code.</summary>
	/// <param name="country">NI, MX or XX.</param>
	/// <returns>The profile.</returns>
	public static ITaxProfile For(string country) => Catalog.For(country);
}
