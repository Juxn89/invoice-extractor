namespace InvoiceExtractor.Domain.Tests;

using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Tax;
using InvoiceExtractor.Domain.Validation;

public sealed class InvoiceValidatorTests
{
	private static readonly DateOnly Today = new(2026, 10, 21);
	private static readonly NicaraguaTaxProfile Nicaragua = new([0.15m, 0m]);

	// Two lines at 15% IVA that add up: 2 × 250 + 1 × 500 = 1000, tax 150, total 1150.
	private static ExtractedInvoice Valid() => new(
		IssuerName: "Distribuidora El Mirador, S.A.",
		IssuerTaxId: "J0310000252297",
		ReceiverName: "Café Mirlo",
		Number: "F-000123",
		IssueDate: new DateOnly(2026, 10, 2),
		Currency: "NIO",
		Lines:
		[
			new("Café en grano 1 kg", 2, 250.00m, 500.00m),
			new("Filtros de papel", 1, 500.00m, 500.00m),
		],
		Subtotal: 1000.00m,
		TaxTotal: 150.00m,
		Total: 1150.00m);

	[Fact]
	public void A_consistent_invoice_passes_every_rule()
	{
		var result = InvoiceValidator.Validate(Valid(), Nicaragua, Today);

		Assert.True(result.IsValid);
		Assert.Empty(result.Issues);
	}

	[Fact]
	public void Rounding_by_a_cent_is_not_an_error()
	{
		// 3 × 33.33 = 99.99 printed as 100.00 on the line, and the totals follow the printed line.
		var invoice = Valid() with
		{
			Lines = [new("Servicio", 3, 33.33m, 100.00m)],
			Subtotal = 100.00m,
			TaxTotal = 15.00m,
			Total = 115.00m,
		};

		Assert.True(InvoiceValidator.Validate(invoice, Nicaragua, Today).IsValid);
	}

	[Fact]
	public void Lines_that_do_not_add_up_point_at_the_subtotal()
	{
		// The classic misread: 1150 read as 1510.
		var invoice = Valid() with { Subtotal = 1510.00m, TaxTotal = 226.50m, Total = 1736.50m };

		var issue = Assert.Single(InvoiceValidator.Validate(invoice, Nicaragua, Today).Issues);
		Assert.Equal(InvoiceFields.Subtotal, issue.Field);
		Assert.Equal(IssueCode.LinesDoNotMatchSubtotal, issue.Code);
		Assert.Equal("Lines add up to 1000.00, the subtotal reads 1510.00.", issue.Detail);
	}

	[Fact]
	public void Each_issue_carries_its_numbers_one_by_one_for_any_language_to_word()
	{
		var invoice = Valid() with
		{
			Lines = [new("Café en grano 1 kg", 2, 250.00m, 600.00m)],
			Subtotal = 600.00m,
			TaxTotal = 90.00m,
			Total = 609.00m,
			IssueDate = Today.AddYears(-11),
		};

		var issues = InvoiceValidator.Validate(invoice, Nicaragua, Today).Issues.ToDictionary(static i => i.Code);

		Assert.Equal(
			new Dictionary<string, string> { ["line"] = "1", ["quantity"] = "2", ["unitPrice"] = "250.00", ["expected"] = "500.00", ["actual"] = "600.00" },
			issues[IssueCode.LineArithmetic].Values);
		Assert.Equal(
			new Dictionary<string, string> { ["subtotal"] = "600.00", ["tax"] = "90.00", ["expected"] = "690.00", ["actual"] = "609.00" },
			issues[IssueCode.TotalMismatch].Values);
		Assert.Equal(new Dictionary<string, string> { ["date"] = "2015-10-21", ["years"] = "10" }, issues[IssueCode.DateTooOld].Values);
	}

	[Fact]
	public void A_line_whose_arithmetic_is_wrong_points_at_that_line()
	{
		var invoice = Valid() with
		{
			Lines =
			[
				new("Café en grano 1 kg", 2, 250.00m, 500.00m),
				new("Filtros de papel", 2, 250.00m, 600.00m),
			],
			Subtotal = 1100.00m,
			TaxTotal = 165.00m,
			Total = 1265.00m,
		};

		var issue = Assert.Single(InvoiceValidator.Validate(invoice, Nicaragua, Today).Issues);
		Assert.Equal(InvoiceFields.LineTotal(1), issue.Field);
		Assert.Equal(IssueCode.LineArithmetic, issue.Code);
	}

	[Fact]
	public void Subtotal_plus_tax_must_be_the_total()
	{
		var invoice = Valid() with { Total = 1250.00m };

		var issue = Assert.Single(InvoiceValidator.Validate(invoice, Nicaragua, Today).Issues);
		Assert.Equal(InvoiceFields.Total, issue.Field);
		Assert.Equal(IssueCode.TotalMismatch, issue.Code);
	}

	[Fact]
	public void A_rate_the_country_does_not_charge_points_at_the_tax()
	{
		// 16% is Mexico's rate, not Nicaragua's.
		var invoice = Valid() with { TaxTotal = 160.00m, Total = 1160.00m };

		var issue = Assert.Single(InvoiceValidator.Validate(invoice, Nicaragua, Today).Issues);
		Assert.Equal(InvoiceFields.TaxTotal, issue.Field);
		Assert.Equal(IssueCode.ImplausibleTaxRate, issue.Code);
	}

	[Fact]
	public void An_exempt_sale_at_zero_tax_is_plausible()
	{
		var invoice = Valid() with { TaxTotal = 0m, Total = 1000.00m };

		Assert.True(InvoiceValidator.Validate(invoice, Nicaragua, Today).IsValid);
	}

	[Fact]
	public void A_receipt_with_no_tax_line_needs_a_total_equal_to_its_subtotal()
	{
		var invoice = Valid() with { Lines = [], TaxTotal = null, Total = 1000.00m };

		Assert.True(InvoiceValidator.Validate(invoice, Nicaragua, Today).IsValid);
	}

	[Fact]
	public void A_badly_formed_tax_id_points_at_the_tax_id()
	{
		var invoice = Valid() with { IssuerTaxId = "J03100" };

		var issue = Assert.Single(InvoiceValidator.Validate(invoice, Nicaragua, Today).Issues);
		Assert.Equal(InvoiceFields.IssuerTaxId, issue.Field);
		Assert.Equal(IssueCode.InvalidTaxId, issue.Code);
	}

	[Theory]
	[InlineData(2026, 10, 22, IssueCode.FutureDate)]
	[InlineData(2016, 10, 20, IssueCode.DateTooOld)]
	public void Implausible_dates_point_at_the_issue_date(int year, int month, int day, IssueCode expected)
	{
		var invoice = Valid() with { IssueDate = new DateOnly(year, month, day) };

		var issue = Assert.Single(InvoiceValidator.Validate(invoice, Nicaragua, Today).Issues);
		Assert.Equal(InvoiceFields.IssueDate, issue.Field);
		Assert.Equal(expected, issue.Code);
	}

	[Fact]
	public void Dates_on_both_edges_of_the_window_are_accepted()
	{
		Assert.True(InvoiceValidator.Validate(Valid() with { IssueDate = Today }, Nicaragua, Today).IsValid);
		Assert.True(InvoiceValidator.Validate(Valid() with { IssueDate = Today.AddYears(-10) }, Nicaragua, Today).IsValid);
	}

	[Fact]
	public void Every_missing_key_field_is_reported_rather_than_the_first_one()
	{
		var invoice = Valid() with { IssuerTaxId = null, Number = " ", IssueDate = null, Total = null };

		var issues = InvoiceValidator.Validate(invoice, Nicaragua, Today).Issues;

		Assert.All(issues, static issue => Assert.Equal(IssueCode.MissingField, issue.Code));
		Assert.Equal(
			[InvoiceFields.IssuerTaxId, InvoiceFields.Number, InvoiceFields.IssueDate, InvoiceFields.Total],
			issues.Select(static issue => issue.Field));
	}

	[Fact]
	public void Several_broken_rules_are_all_reported()
	{
		var invoice = Valid() with { Subtotal = 1510.00m, IssuerTaxId = "123" };

		var codes = InvoiceValidator.Validate(invoice, Nicaragua, Today).Issues.Select(static issue => issue.Code).ToHashSet();

		Assert.Contains(IssueCode.LinesDoNotMatchSubtotal, codes);
		Assert.Contains(IssueCode.TotalMismatch, codes);
		Assert.Contains(IssueCode.InvalidTaxId, codes);
	}
}
