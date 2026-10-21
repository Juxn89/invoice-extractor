namespace InvoiceExtractor.Domain.Tax;

/// <summary>
/// The country-specific half of validation: which tax rates are plausible and what a tax id looks like.
/// One implementation per country (Strategy); the validator never branches on a country code.
/// </summary>
public interface ITaxProfile
{
	/// <summary>ISO 3166-1 alpha-2 code, or <c>XX</c> for the generic profile.</summary>
	string CountryCode { get; }

	/// <summary>Whether tax ÷ subtotal is a rate this country charges.</summary>
	/// <param name="impliedRate">The rate the document's own numbers imply, e.g. 0.15.</param>
	/// <returns><see langword="true"/> when plausible.</returns>
	bool IsPlausibleRate(decimal impliedRate);

	/// <summary>Whether a tax id has this country's format. Format only: no registry lookup.</summary>
	/// <param name="taxId">The id as printed; the profile normalises separators and case itself.</param>
	/// <returns><see langword="true"/> when the format matches.</returns>
	bool IsValidTaxId(string taxId);
}
