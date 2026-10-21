namespace InvoiceExtractor.Evals.Tests;

using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Evals.Dataset;
using InvoiceExtractor.Evals.Extraction;
using InvoiceExtractor.Evals.Generation;
using InvoiceExtractor.Evals.Scoring;

public sealed class EvalScorerTests
{
	private static readonly IReadOnlyList<DatasetEntry> Entries = [.. DatasetPlan.Evaluation().Select(static s => s.ToEntry())];

	// The pass marks from the plan, §5.
	private static readonly Thresholds PlanThresholds = new(0.95, 0.90, 0.98, 1, 0.001m);

	private static DatasetEntry Consistent() => Entries.First(static e => e.Outcome == ExpectedOutcome.AutoApprove);

	private static EvalReport Run(Func<DatasetEntry, ExtractionAttempt> extract) =>
		EvalScorer.Aggregate("test", [.. Entries.Select(e => EvalScorer.Score(e, extract(e), DatasetPlan.AsOf))]);

	[Fact]
	public void The_oracle_scores_perfectly_and_passes_the_thresholds()
	{
		var report = Run(static e => new ExtractionAttempt(e.Expected, 0m));

		Assert.Equal(1, report.KeyFieldAccuracy);
		Assert.Equal(1, report.LineF1);
		Assert.Equal(25, report.AutoApproved);
		Assert.Equal(0, report.FalseAutoApproved);
		Assert.Equal(report.Planted, report.PlantedSentToReview);
		Assert.Empty(PlanThresholds.Failures(report));
	}

	[Fact]
	public void A_misread_total_is_caught_by_the_validator_and_not_approved()
	{
		var entry = Consistent();
		var misread = entry.Expected with { Total = entry.Expected.Total + 10m };

		var score = EvalScorer.Score(entry, new ExtractionAttempt(misread, 0m), DatasetPlan.AsOf);

		Assert.False(score.TotalMatches);
		Assert.False(score.AutoApproved);
		Assert.False(score.FalselyAutoApproved);
	}

	[Fact]
	public void A_misread_number_that_passes_validation_is_a_false_auto_approval()
	{
		var entry = Consistent();
		var misread = entry.Expected with { Number = entry.Expected.Number + "9" };

		var score = EvalScorer.Score(entry, new ExtractionAttempt(misread, 0m), DatasetPlan.AsOf);

		Assert.True(score.AutoApproved);
		Assert.True(score.FalselyAutoApproved);
	}

	[Fact]
	public void Tax_id_and_number_are_compared_without_separators_or_case()
	{
		var entry = Consistent();
		var read = entry.Expected with
		{
			IssuerTaxId = string.Join('-', entry.Expected.IssuerTaxId!.Chunk(4).Select(static c => new string(c))).ToLowerInvariant(),
			Number = $" {entry.Expected.Number!.ToLowerInvariant()} ",
		};

		var score = EvalScorer.Score(entry, new ExtractionAttempt(read, 0m), DatasetPlan.AsOf);

		Assert.True(score.TaxIdMatches);
		Assert.True(score.NumberMatches);
	}

	[Fact]
	public void An_invented_line_lowers_precision_but_not_recall()
	{
		var entry = Consistent();
		InvoiceLine invented = new("Línea inventada", 1, 1m, 1m);
		var read = entry.Expected with { Lines = [.. entry.Expected.Lines, invented] };

		var score = EvalScorer.Score(entry, new ExtractionAttempt(read, 0m), DatasetPlan.AsOf);

		Assert.Equal(entry.Expected.Lines.Count, score.MatchedLines);
		Assert.Equal(entry.Expected.Lines.Count + 1, score.ExtractedLines);
	}

	[Fact]
	public void A_failed_extraction_goes_to_review_and_counts_against_accuracy()
	{
		var report = Run(static _ => new ExtractionAttempt(null, 0.0005m, "timeout"));

		Assert.Equal(0, report.KeyFieldAccuracy);
		Assert.Equal(0, report.AutoApproved);
		Assert.Equal(1, report.AutoApprovedPrecision);
		Assert.Equal(0.0005m, report.CostPerDocumentUsd);
		Assert.Contains(PlanThresholds.Failures(report), static f => f.StartsWith("key-field accuracy", StringComparison.Ordinal));
	}

	[Fact]
	public void Cost_over_the_cap_fails_the_thresholds()
	{
		var report = Run(static e => new ExtractionAttempt(e.Expected, 0.002m));

		Assert.Equal(["cost per document 0.002000 > 0.001000"], PlanThresholds.Failures(report));
	}

	[Fact]
	public void The_committed_thresholds_are_the_plans()
	{
		var path = Path.Combine(Root(), "evals", "thresholds.json");

		Assert.Equal(PlanThresholds, Thresholds.Read(path));
	}

	private static string Root()
	{
		for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
		{
			if (File.Exists(Path.Combine(folder.FullName, "InvoiceExtractor.slnx")))
			{
				return folder.FullName;
			}
		}

		throw new DirectoryNotFoundException("InvoiceExtractor.slnx not found.");
	}
}
