namespace InvoiceExtractor.Domain.Tests;

using InvoiceExtractor.Domain.Tax;

// Format cases only: made-up ids with the documented shape, never a real person's or company's.
public sealed class TaxProfileTests
{
	private static readonly NicaraguaTaxProfile Nicaragua = new([0.15m, 0m]);
	private static readonly MexicoTaxProfile Mexico = new([0.16m, 0.08m, 0m]);
	private static readonly GenericTaxProfile Generic = new();

	[Theory]
	[InlineData("J0310000252297")]
	[InlineData("j031-0000-25229-7")]
	[InlineData("0010101900001A")]
	[InlineData("001-010190-0001A")]
	public void Nicaragua_accepts_a_company_RUC_and_a_cedula(string taxId) => Assert.True(Nicaragua.IsValidTaxId(taxId));

	[Theory]
	[InlineData("J031000025229")]
	[InlineData("J03100002522970")]
	[InlineData("X0310000252297")]
	[InlineData("00101019000011")]
	[InlineData("")]
	public void Nicaragua_rejects_the_wrong_length_or_shape(string taxId) => Assert.False(Nicaragua.IsValidTaxId(taxId));

	[Theory]
	[InlineData("ABC010203XY1")]
	[InlineData("GOMJ890215AB3")]
	[InlineData("XAXX010101000")]
	[InlineData("XEXX010101000")]
	[InlineData("ÑAB000229123")]
	[InlineData("gomj-890215-ab3")]
	public void Mexico_accepts_company_and_individual_RFCs(string taxId) => Assert.True(Mexico.IsValidTaxId(taxId));

	[Theory]
	[InlineData("AB010203XY1")]
	[InlineData("GOMJ891315AB3")]
	[InlineData("GOMJ890231AB3")]
	[InlineData("GOMJ890215AB")]
	[InlineData("12MJ890215AB3")]
	public void Mexico_rejects_a_bad_shape_or_an_impossible_date(string taxId) => Assert.False(Mexico.IsValidTaxId(taxId));

	[Theory]
	[InlineData(0.15, true)]
	[InlineData(0.1520, true)]
	[InlineData(0, true)]
	[InlineData(0.16, false)]
	[InlineData(0.13, false)]
	public void Nicaragua_rates(double rate, bool plausible) => Assert.Equal(plausible, Nicaragua.IsPlausibleRate((decimal)rate));

	[Theory]
	[InlineData(0.16, true)]
	[InlineData(0.08, true)]
	[InlineData(0, true)]
	[InlineData(0.15, false)]
	public void Mexico_rates(double rate, bool plausible) => Assert.Equal(plausible, Mexico.IsPlausibleRate((decimal)rate));

	[Theory]
	[InlineData(0.21, true)]
	[InlineData(0, true)]
	[InlineData(0.35, false)]
	[InlineData(-0.01, false)]
	public void Generic_accepts_any_rate_a_country_could_charge(double rate, bool plausible) =>
		Assert.Equal(plausible, Generic.IsPlausibleRate((decimal)rate));

	[Theory]
	[InlineData("DE123456789", true)]
	[InlineData("B-12.345.678", true)]
	[InlineData("12", false)]
	[InlineData("Not a tax id at all", false)]
	public void Generic_tax_ids(string taxId, bool valid) => Assert.Equal(valid, Generic.IsValidTaxId(taxId));

	[Fact]
	public void Rates_come_from_configuration_and_are_checked()
	{
		Assert.Throws<ArgumentException>(() => new NicaraguaTaxProfile([]));
		Assert.Throws<ArgumentException>(() => new MexicoTaxProfile([16m]));
		Assert.Equal([0.15m, 0m], Nicaragua.Rates);
	}
}
