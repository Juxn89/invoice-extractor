namespace InvoiceExtractor.Domain.Tax;

using System.Globalization;

/// <summary>
/// A country that charges a known list of rates. The list comes from configuration, not from code: rates
/// change by law and by decree, and the sources behind the defaults are in
/// <c>docs/verification/tax-rules.md</c>.
/// </summary>
public abstract class RateListTaxProfile : ITaxProfile
{
	/// <summary>
	/// How far the implied rate may sit from a listed one. Tax is rounded to the cent, which moves the implied
	/// rate by up to 0.005 ÷ subtotal: half a point on a 1.00 subtotal, far less on anything larger. Half a
	/// point covers receipts down to one unit and still keeps 15% apart from 16%.
	/// </summary>
	public const decimal RateTolerance = 0.005m;

	private readonly decimal[] rates;

	/// <summary>Creates the profile.</summary>
	/// <param name="rates">The rates the country charges, as fractions (0.15 for 15%). Include 0 for exempt sales.</param>
	/// <exception cref="ArgumentException">The list is empty or holds a rate outside [0, 1).</exception>
	protected RateListTaxProfile(IEnumerable<decimal> rates)
	{
		ArgumentNullException.ThrowIfNull(rates);

		this.rates = [.. rates];
		if (this.rates.Length == 0 || this.rates.Any(static rate => rate is < 0 or >= 1))
		{
			throw new ArgumentException(
				string.Create(CultureInfo.InvariantCulture, $"Tax rates must be a non-empty list of fractions in [0, 1); got [{string.Join(", ", this.rates)}]."),
				nameof(rates));
		}
	}

	/// <inheritdoc />
	public abstract string CountryCode { get; }

	/// <inheritdoc />
	public abstract string TaxIdFormat { get; }

	/// <summary>The configured rates.</summary>
	public IReadOnlyList<decimal> Rates => rates;

	/// <inheritdoc />
	public bool IsPlausibleRate(decimal impliedRate) =>
		rates.Any(rate => Math.Abs(impliedRate - rate) <= RateTolerance);

	/// <inheritdoc />
	public abstract bool IsValidTaxId(string taxId);
}
