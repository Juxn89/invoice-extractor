namespace InvoiceExtractor.Evals.Scoring;

using System.Text.RegularExpressions;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Tax;
using InvoiceExtractor.Domain.Validation;
using InvoiceExtractor.Evals.Dataset;
using InvoiceExtractor.Evals.Extraction;

/// <summary>How one document fared.</summary>
/// <param name="Id">The document.</param>
/// <param name="TotalMatches">The total was read exactly.</param>
/// <param name="DateMatches">The issue date was read exactly.</param>
/// <param name="TaxIdMatches">The tax id was read, separators aside.</param>
/// <param name="NumberMatches">The number was read, case and surrounding spaces aside.</param>
/// <param name="ExpectedLines">Lines printed.</param>
/// <param name="ExtractedLines">Lines read.</param>
/// <param name="MatchedLines">Lines read with the right description and total.</param>
/// <param name="AutoApproved">The extraction passed every rule.</param>
/// <param name="ShouldReview">The document is inconsistent on purpose.</param>
/// <param name="Issues">The rules the extraction broke.</param>
/// <param name="CostUsd">What the extraction cost.</param>
/// <param name="Error">Why extraction failed, when it did.</param>
internal sealed record DocumentScore(
	string Id,
	bool TotalMatches,
	bool DateMatches,
	bool TaxIdMatches,
	bool NumberMatches,
	int ExpectedLines,
	int ExtractedLines,
	int MatchedLines,
	bool AutoApproved,
	bool ShouldReview,
	IReadOnlyList<ValidationIssue> Issues,
	decimal CostUsd,
	string? Error)
{
	/// <summary>Every key field was read correctly.</summary>
	public bool KeyFieldsCorrect => TotalMatches && DateMatches && TaxIdMatches && NumberMatches;

	/// <summary>Approved without review although something is wrong: the failure that matters most.</summary>
	public bool FalselyAutoApproved => AutoApproved && (ShouldReview || !KeyFieldsCorrect);
}

/// <summary>The dataset-level numbers the thresholds are checked against.</summary>
/// <param name="Extractor">Which extractor ran.</param>
/// <param name="Documents">How many documents.</param>
/// <param name="TotalAccuracy">Share of documents whose total was read exactly.</param>
/// <param name="DateAccuracy">Same, issue date.</param>
/// <param name="TaxIdAccuracy">Same, tax id.</param>
/// <param name="NumberAccuracy">Same, number.</param>
/// <param name="LineF1">Line F1 over every line in the dataset.</param>
/// <param name="AutoApproved">Documents approved without review.</param>
/// <param name="AutoApprovedPrecision">Share of those that deserved it (1 when none were approved).</param>
/// <param name="FalseAutoApproved">Documents approved that should have gone to review.</param>
/// <param name="PlantedSentToReview">Planted documents caught, out of <see cref="Planted"/>.</param>
/// <param name="Planted">Documents inconsistent on purpose.</param>
/// <param name="TotalCostUsd">What the run cost.</param>
/// <param name="CostPerDocumentUsd">Average per document.</param>
/// <param name="Scores">Per-document detail.</param>
internal sealed record EvalReport(
	string Extractor,
	int Documents,
	double TotalAccuracy,
	double DateAccuracy,
	double TaxIdAccuracy,
	double NumberAccuracy,
	double LineF1,
	int AutoApproved,
	double AutoApprovedPrecision,
	int FalseAutoApproved,
	int PlantedSentToReview,
	int Planted,
	decimal TotalCostUsd,
	decimal CostPerDocumentUsd,
	IReadOnlyList<DocumentScore> Scores)
{
	/// <summary>The weakest of the four key-field accuracies, which is what the threshold applies to.</summary>
	public double KeyFieldAccuracy => new[] { TotalAccuracy, DateAccuracy, TaxIdAccuracy, NumberAccuracy }.Min();
}

/// <summary>Scores extractions against the dataset's truth.</summary>
internal static partial class EvalScorer
{
	/// <summary>Scores one document.</summary>
	/// <param name="entry">The truth.</param>
	/// <param name="attempt">What the extractor returned.</param>
	/// <param name="asOf">The dataset's "today".</param>
	/// <returns>The score.</returns>
	public static DocumentScore Score(DatasetEntry entry, ExtractionAttempt attempt, DateOnly asOf)
	{
		var expected = entry.Expected;
		var shouldReview = entry.Outcome == ExpectedOutcome.Review;

		if (attempt.Invoice is not { } read)
		{
			// A failed extraction goes to review: nothing is approved, nothing is matched.
			return new DocumentScore(entry.Id, false, false, false, false, expected.Lines.Count, 0, 0, false, shouldReview, [], attempt.CostUsd, attempt.Error);
		}

		var validation = InvoiceValidator.Validate(read, TaxProfiles.For(entry.Country), asOf);
		return new DocumentScore(
			entry.Id,
			TotalMatches: read.Total == expected.Total,
			DateMatches: read.IssueDate == expected.IssueDate,
			TaxIdMatches: read.IssuerTaxId is not null && expected.IssuerTaxId is not null
				&& TaxId.Normalize(read.IssuerTaxId) == TaxId.Normalize(expected.IssuerTaxId),
			NumberMatches: Same(read.Number, expected.Number),
			ExpectedLines: expected.Lines.Count,
			ExtractedLines: read.Lines.Count,
			MatchedLines: MatchLines(expected.Lines, read.Lines),
			AutoApproved: validation.IsValid,
			ShouldReview: shouldReview,
			Issues: validation.Issues,
			CostUsd: attempt.CostUsd,
			Error: attempt.Error);
	}

	/// <summary>Aggregates document scores.</summary>
	/// <param name="extractor">Which extractor ran.</param>
	/// <param name="scores">One score per document.</param>
	/// <returns>The report.</returns>
	public static EvalReport Aggregate(string extractor, IReadOnlyList<DocumentScore> scores)
	{
		ArgumentNullException.ThrowIfNull(scores);
		var count = scores.Count;
		double Share(Func<DocumentScore, bool> predicate) => count == 0 ? 0 : (double)scores.Count(predicate) / count;

		var matched = scores.Sum(static s => s.MatchedLines);
		var precision = Ratio(matched, scores.Sum(static s => s.ExtractedLines));
		var recall = Ratio(matched, scores.Sum(static s => s.ExpectedLines));
		var autoApproved = scores.Where(static s => s.AutoApproved).ToList();
		var total = scores.Sum(static s => s.CostUsd);

		return new EvalReport(
			extractor,
			count,
			Share(static s => s.TotalMatches),
			Share(static s => s.DateMatches),
			Share(static s => s.TaxIdMatches),
			Share(static s => s.NumberMatches),
			precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall),
			autoApproved.Count,
			autoApproved.Count == 0 ? 1 : (double)autoApproved.Count(static s => !s.FalselyAutoApproved) / autoApproved.Count,
			autoApproved.Count(static s => s.FalselyAutoApproved),
			scores.Count(static s => s.ShouldReview && !s.AutoApproved),
			scores.Count(static s => s.ShouldReview),
			total,
			count == 0 ? 0 : Math.Round(total / count, 6),
			scores);
	}

	private static double Ratio(int part, int whole) => whole == 0 ? 0 : (double)part / whole;

	private static bool Same(string? read, string? expected) =>
		read is not null && expected is not null
		&& string.Equals(read.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

	// Each printed line can be claimed once, by a read line with the same description and total.
	private static int MatchLines(IReadOnlyList<InvoiceLine> expected, IReadOnlyList<InvoiceLine> read)
	{
		var unclaimed = expected.ToList();
		var matched = 0;
		foreach (var line in read)
		{
			var index = unclaimed.FindIndex(e => e.LineTotal == line.LineTotal && Description(e) == Description(line));
			if (index >= 0)
			{
				unclaimed.RemoveAt(index);
				matched++;
			}
		}

		return matched;
	}

	private static string Description(InvoiceLine line) => Spaces().Replace(line.Description.Trim(), " ").ToUpperInvariant();

	[GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
	private static partial Regex Spaces();
}
