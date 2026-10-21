namespace InvoiceExtractor.Evals.Tests;

using System.Text.Json;
using InvoiceExtractor.Domain.Validation;
using InvoiceExtractor.Evals.Dataset;
using InvoiceExtractor.Evals.Generation;
using InvoiceExtractor.Evals.Scoring;

// The dataset is only worth scoring against if its truth is right: consistent documents pass every rule and
// each planted one breaks exactly the rule it was planted for. None of these render a file.
public sealed class DatasetIntegrityTests
{
	public static TheoryData<string> Sets => ["evaluation", "samples"];

	private static IReadOnlyList<DocumentSpec> Set(string name) =>
		name == "evaluation" ? DatasetPlan.Evaluation() : DatasetPlan.Samples();

	[Theory]
	[MemberData(nameof(Sets))]
	public void Consistent_documents_pass_every_rule(string set)
	{
		foreach (var spec in Set(set).Where(static s => s.Planted is null))
		{
			var result = InvoiceValidator.Validate(spec.Printed, TaxProfiles.For(spec.Market.Code), DatasetPlan.AsOf);

			Assert.True(result.IsValid, $"{spec.Id}: {string.Join("; ", result.Issues.Select(static i => i.Detail))}");
		}
	}

	[Theory]
	[MemberData(nameof(Sets))]
	public void Each_planted_document_breaks_only_its_rule(string set)
	{
		foreach (var spec in Set(set).Where(static s => s.Planted is not null))
		{
			var codes = InvoiceValidator.Validate(spec.Printed, TaxProfiles.For(spec.Market.Code), DatasetPlan.AsOf)
				.Issues.Select(static i => i.Code).Distinct().ToList();

			Assert.True(codes.SequenceEqual([spec.Planted!.Value]), $"{spec.Id}: expected only {spec.Planted}, got {string.Join(", ", codes)}");
		}
	}

	[Fact]
	public void The_evaluation_set_has_the_mix_the_plan_describes()
	{
		var specs = DatasetPlan.Evaluation();

		Assert.Equal(30, specs.Count);
		Assert.Equal(30, specs.Select(static s => s.Id).Distinct().Count());
		Assert.Equal(12, specs.Count(static s => s.Market.Code == "NI"));
		Assert.Equal(12, specs.Count(static s => s.Market.Code == "MX"));
		Assert.Equal(10, specs.Count(static s => s.Kind == DocumentKind.DigitalPdf));
		Assert.Equal(10, specs.Count(static s => s.Kind == DocumentKind.Scan));
		Assert.Equal(6, specs.Count(static s => s.Kind == DocumentKind.Photo));
		Assert.Equal(4, specs.Count(static s => s.Kind == DocumentKind.ThermalReceipt));
		Assert.Equal(5, specs.Count(static s => s.Planted is not null));
	}

	[Fact]
	public void Samples_are_not_the_documents_scored()
	{
		var scored = DatasetPlan.Evaluation().Select(static s => s.Printed).ToList();

		Assert.DoesNotContain(DatasetPlan.Samples(), s => scored.Any(e => e.Number == s.Printed.Number && e.IssuerTaxId == s.Printed.IssuerTaxId));
	}

	[Fact]
	public void Generation_is_reproducible()
	{
		static string Snapshot() => JsonSerializer.Serialize(DatasetPlan.Evaluation().Select(static s => s.ToEntry()), DatasetJson.Options);

		Assert.Equal(Snapshot(), Snapshot());
	}

	[Fact]
	public void The_manifest_round_trips_amounts_exactly()
	{
		var manifest = new DatasetManifest(DatasetPlan.AsOf, DatasetPlan.EvalSeed, [.. DatasetPlan.Evaluation().Select(static s => s.ToEntry())]);
		var folder = Directory.CreateTempSubdirectory().FullName;
		try
		{
			DatasetJson.Write(folder, manifest);
			var read = DatasetJson.Read(folder);

			Assert.Equal(
				manifest.Documents.Select(static d => (d.Id, d.Expected.Subtotal, d.Expected.TaxTotal, d.Expected.Total)),
				read.Documents.Select(static d => (d.Id, d.Expected.Subtotal, d.Expected.TaxTotal, d.Expected.Total)));
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
		}
	}
}
