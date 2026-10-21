namespace InvoiceExtractor.Evals.Generation;

using System.Globalization;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;
using InvoiceExtractor.Evals.Dataset;

/// <summary>Everything the renderer needs for one document.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Kind">How it is rendered.</param>
/// <param name="Market">Currency, language and number format.</param>
/// <param name="Issuer">Who issues it.</param>
/// <param name="Printed">Exactly what the document shows.</param>
/// <param name="Planted">The one rule it breaks on purpose, if any.</param>
internal sealed record DocumentSpec(
	string Id,
	DocumentKind Kind,
	Market Market,
	Issuer Issuer,
	ExtractedInvoice Printed,
	IssueCode? Planted)
{
	/// <summary>The file name the renderer writes.</summary>
	public string FileName => Id + (Kind switch
	{
		DocumentKind.DigitalPdf => ".pdf",
		DocumentKind.Photo => ".jpg",
		_ => ".png",
	});

	/// <summary>The manifest entry for this document.</summary>
	public DatasetEntry ToEntry() => new(
		Id,
		FileName,
		Kind,
		Market.Code,
		Market.Language,
		Printed,
		Planted is null ? ExpectedOutcome.AutoApprove : ExpectedOutcome.Review,
		Planted);
}

/// <summary>
/// Which documents the dataset holds, decided up front rather than drawn at random so the mix is exactly the
/// one the plan describes: 12 Nicaragua, 12 Mexico, 6 generic; 10 digital PDFs, 10 scans, 6 photos, 4 thermal
/// receipts; 5 documents inconsistent on purpose. Only item choices, quantities, prices and dates are random,
/// from a fixed seed.
/// </summary>
internal static class DatasetPlan
{
	/// <summary>The evaluation set's seed.</summary>
	public const int EvalSeed = 2026;

	/// <summary>The UI samples' seed: different, so the samples a visitor tries are not the documents scored.</summary>
	public const int SamplesSeed = 2027;

	/// <summary>The date validation treats as today, for both sets.</summary>
	public static DateOnly AsOf { get; } = new(2026, 10, 21);

	private static readonly DateOnly FirstIssueDate = new(2026, 1, 5);
	private static readonly DateOnly LastIssueDate = new(2026, 10, 15);

	private static readonly (string Id, DocumentKind Kind, Market Market, int IssuerIndex, IssueCode? Planted)[] EvalLayout =
	[
		("ni-01", DocumentKind.DigitalPdf, Catalog.Nicaragua, 0, null),
		("ni-02", DocumentKind.DigitalPdf, Catalog.Nicaragua, 2, IssueCode.LinesDoNotMatchSubtotal),
		("ni-03", DocumentKind.DigitalPdf, Catalog.Nicaragua, 1, null),
		("ni-04", DocumentKind.DigitalPdf, Catalog.Nicaragua, 0, null),
		("ni-05", DocumentKind.Scan, Catalog.Nicaragua, 2, null),
		("ni-06", DocumentKind.Scan, Catalog.Nicaragua, 0, IssueCode.ImplausibleTaxRate),
		("ni-07", DocumentKind.Scan, Catalog.Nicaragua, 1, null),
		("ni-08", DocumentKind.Scan, Catalog.Nicaragua, 2, null),
		("ni-09", DocumentKind.Photo, Catalog.Nicaragua, 0, null),
		("ni-10", DocumentKind.Photo, Catalog.Nicaragua, 1, null),
		("ni-11", DocumentKind.ThermalReceipt, Catalog.Nicaragua, 1, null),
		("ni-12", DocumentKind.ThermalReceipt, Catalog.Nicaragua, 0, null),
		("mx-01", DocumentKind.DigitalPdf, Catalog.Mexico, 0, IssueCode.InvalidTaxId),
		("mx-02", DocumentKind.DigitalPdf, Catalog.Mexico, 1, null),
		("mx-03", DocumentKind.DigitalPdf, Catalog.Mexico, 0, null),
		("mx-04", DocumentKind.Scan, Catalog.Mexico, 1, null),
		("mx-05", DocumentKind.Scan, Catalog.Mexico, 0, IssueCode.LineArithmetic),
		("mx-06", DocumentKind.Scan, Catalog.Mexico, 2, null),
		("mx-07", DocumentKind.Scan, Catalog.Mexico, 1, null),
		("mx-08", DocumentKind.Photo, Catalog.Mexico, 2, null),
		("mx-09", DocumentKind.Photo, Catalog.Mexico, 0, null),
		("mx-10", DocumentKind.Photo, Catalog.Mexico, 1, null),
		("mx-11", DocumentKind.ThermalReceipt, Catalog.Mexico, 2, null),
		("mx-12", DocumentKind.ThermalReceipt, Catalog.Mexico, 1, null),
		("xx-01", DocumentKind.DigitalPdf, Catalog.UnitedStates, 0, null),
		("xx-02", DocumentKind.DigitalPdf, Catalog.UnitedStates, 1, null),
		("xx-03", DocumentKind.DigitalPdf, Catalog.Spain, 0, null),
		("xx-04", DocumentKind.Scan, Catalog.UnitedStates, 1, null),
		("xx-05", DocumentKind.Scan, Catalog.UnitedStates, 0, null),
		("xx-06", DocumentKind.Photo, Catalog.UnitedStates, 0, IssueCode.TotalMismatch),
	];

	// The UI samples: a smaller mix with two documents that must end in review.
	private static readonly (string Id, DocumentKind Kind, Market Market, int IssuerIndex, IssueCode? Planted)[] SamplesLayout =
	[
		("sample-01", DocumentKind.DigitalPdf, Catalog.Nicaragua, 0, null),
		("sample-02", DocumentKind.Scan, Catalog.Nicaragua, 2, null),
		("sample-03", DocumentKind.Photo, Catalog.Nicaragua, 1, null),
		("sample-04", DocumentKind.ThermalReceipt, Catalog.Nicaragua, 0, null),
		("sample-05", DocumentKind.DigitalPdf, Catalog.Mexico, 0, null),
		("sample-06", DocumentKind.Scan, Catalog.Mexico, 1, IssueCode.LinesDoNotMatchSubtotal),
		("sample-07", DocumentKind.Photo, Catalog.Mexico, 2, null),
		("sample-08", DocumentKind.ThermalReceipt, Catalog.Mexico, 1, null),
		("sample-09", DocumentKind.DigitalPdf, Catalog.UnitedStates, 0, null),
		("sample-10", DocumentKind.Scan, Catalog.UnitedStates, 1, null),
		("sample-11", DocumentKind.Photo, Catalog.UnitedStates, 0, IssueCode.TotalMismatch),
		("sample-12", DocumentKind.DigitalPdf, Catalog.Spain, 0, null),
	];

	/// <summary>The 30 documents the evals score.</summary>
	public static IReadOnlyList<DocumentSpec> Evaluation() => Build(EvalLayout, EvalSeed);

	/// <summary>The 12 documents offered in the UI as "try an example".</summary>
	public static IReadOnlyList<DocumentSpec> Samples() => Build(SamplesLayout, SamplesSeed);

	private static List<DocumentSpec> Build(
		(string Id, DocumentKind Kind, Market Market, int IssuerIndex, IssueCode? Planted)[] layout,
		int seed)
	{
		// Not a security use: a seeded generator is the point, so the dataset regenerates byte for byte.
#pragma warning disable CA5394
		var random = new Random(seed);
		var specs = new List<DocumentSpec>(layout.Length);
		var number = 1000;

		foreach (var (id, kind, market, issuerIndex, planted) in layout)
		{
			var issuer = market.Issuers[issuerIndex];
			var lineCount = kind == DocumentKind.ThermalReceipt ? random.Next(1, 4) : random.Next(2, 7);
			var lines = Enumerable.Range(0, lineCount).Select(_ => NewLine(random, market)).ToList();
			number += random.Next(3, 40);

			var invoice = Priced(
				new ExtractedInvoice(
					issuer.Name,
					issuer.TaxId,
					kind == DocumentKind.ThermalReceipt ? null : market.Receivers[random.Next(market.Receivers.Count)],
					kind == DocumentKind.ThermalReceipt
						? string.Create(CultureInfo.InvariantCulture, $"T-{number:00000}")
						: string.Create(CultureInfo.InvariantCulture, $"{market.NumberPrefix}{number:000000}"),
					FirstIssueDate.AddDays(random.Next(LastIssueDate.DayNumber - FirstIssueDate.DayNumber + 1)),
					market.Currency,
					lines,
					Subtotal: null,
					TaxTotal: null,
					Total: null),
				issuer.TaxRate);

			specs.Add(new DocumentSpec(id, kind, market, issuer, planted is { } code ? Plant(invoice, code, issuer) : invoice, planted));
		}
#pragma warning restore CA5394

		return specs;
	}

#pragma warning disable CA5394
	private static InvoiceLine NewLine(Random random, Market market)
	{
		var item = Catalog.Items[random.Next(Catalog.Items.Count)];
		var quantity = item.ByWeight ? random.Next(1, 6) * 0.5m : random.Next(1, 11);
		var unitPrice = Math.Round((item.MinPrice + ((item.MaxPrice - item.MinPrice) * (decimal)random.NextDouble())) * market.PriceFactor, 2);
		var description = market.Language == "en" ? item.English : item.Spanish;
		return new InvoiceLine(description, quantity, unitPrice, Math.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero));
	}
#pragma warning restore CA5394

	// Totals follow the printed lines, so every document is consistent unless a rule is broken on purpose.
	private static ExtractedInvoice Priced(ExtractedInvoice invoice, decimal rate)
	{
		var subtotal = invoice.Lines.Sum(static line => line.LineTotal);
		var tax = Math.Round(subtotal * rate, 2, MidpointRounding.AwayFromZero);
		return invoice with { Subtotal = subtotal, TaxTotal = tax, Total = subtotal + tax };
	}

	// Each plant breaks exactly one rule and keeps every other rule passing; a test holds it to that.
	private static ExtractedInvoice Plant(ExtractedInvoice invoice, IssueCode code, Issuer issuer)
	{
		var subtotal = invoice.Subtotal!.Value;
		return code switch
		{
			// The classic misread printed by the issuer: two digits swapped, totals computed from the wrong one.
			IssueCode.LinesDoNotMatchSubtotal => Recomputed(invoice, SwapLeadingDigits(subtotal), issuer.TaxRate),
			IssueCode.LineArithmetic => WithWrongFirstLine(invoice, issuer.TaxRate),
			IssueCode.TotalMismatch => invoice with { Total = invoice.Total + 10m },
			// Mexico's rate on a Nicaraguan invoice.
			IssueCode.ImplausibleTaxRate => Recomputed(invoice, subtotal, 0.16m),
			// One character short of an RFC.
			IssueCode.InvalidTaxId => invoice with { IssuerTaxId = issuer.TaxId[..^1] },
			_ => throw new ArgumentOutOfRangeException(nameof(code), code, "No plant defined for this rule."),
		};
	}

	private static ExtractedInvoice Recomputed(ExtractedInvoice invoice, decimal subtotal, decimal rate)
	{
		var tax = Math.Round(subtotal * rate, 2, MidpointRounding.AwayFromZero);
		return invoice with { Subtotal = subtotal, TaxTotal = tax, Total = subtotal + tax };
	}

	private static ExtractedInvoice WithWrongFirstLine(ExtractedInvoice invoice, decimal rate)
	{
		var lines = invoice.Lines.ToList();
		var first = lines[0];
		// Off by a whole currency unit per item, far beyond rounding: quantity × price no longer gives the total.
		lines[0] = first with { LineTotal = first.LineTotal + Math.Max(1m, first.Quantity) };
		return Recomputed(invoice with { Lines = lines }, lines.Sum(static line => line.LineTotal), rate);
	}

	// Swaps the first two digits of the whole part (1530 → 5130); when they are equal or there is only one,
	// shifts the amount by 500 so it still reads as a plausible but wrong number.
	private static decimal SwapLeadingDigits(decimal amount)
	{
		var whole = decimal.Truncate(amount).ToString(CultureInfo.InvariantCulture);
		if (whole.Length < 2 || whole[0] == whole[1])
		{
			return amount + 500m;
		}

		var swapped = decimal.Parse(string.Concat(whole[1], whole[0], whole[2..]), CultureInfo.InvariantCulture);
		return swapped + (amount - decimal.Truncate(amount));
	}
}
