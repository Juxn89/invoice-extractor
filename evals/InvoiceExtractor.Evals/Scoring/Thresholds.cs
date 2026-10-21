namespace InvoiceExtractor.Evals.Scoring;

using System.Globalization;
using System.Text.Json;
using InvoiceExtractor.Evals.Dataset;

/// <summary>The pass marks from the plan (§5), read from <c>evals/thresholds.json</c>.</summary>
/// <param name="MinKeyFieldAccuracy">Each of total, date, tax id and number.</param>
/// <param name="MinLineF1">Line F1 over the dataset.</param>
/// <param name="MinAutoApprovedPrecision">Share of auto-approved documents that deserved it.</param>
/// <param name="MaxFalseAutoApproved">Wrong documents approved without review.</param>
/// <param name="MaxCostPerDocumentUsd">Average model cost per document.</param>
internal sealed record Thresholds(
	double MinKeyFieldAccuracy,
	double MinLineF1,
	double MinAutoApprovedPrecision,
	int MaxFalseAutoApproved,
	decimal MaxCostPerDocumentUsd)
{
	/// <summary>Reads the thresholds file.</summary>
	/// <param name="path">The file.</param>
	/// <returns>The thresholds.</returns>
	/// <exception cref="InvalidDataException">The file is empty.</exception>
	public static Thresholds Read(string path)
	{
		using var stream = File.OpenRead(path);
		return JsonSerializer.Deserialize<Thresholds>(stream, DatasetJson.Options)
			?? throw new InvalidDataException($"{path} is empty.");
	}

	/// <summary>Every threshold the report misses, as readable lines; empty when it passes.</summary>
	/// <param name="report">The report.</param>
	/// <returns>The failures.</returns>
	public IReadOnlyList<string> Failures(EvalReport report)
	{
		ArgumentNullException.ThrowIfNull(report);
		var failures = new List<string>();
		void Check(bool ok, FormattableString message)
		{
			if (!ok)
			{
				failures.Add(message.ToString(CultureInfo.InvariantCulture));
			}
		}

		Check(report.KeyFieldAccuracy >= MinKeyFieldAccuracy, $"key-field accuracy {report.KeyFieldAccuracy:0.000} < {MinKeyFieldAccuracy:0.000}");
		Check(report.LineF1 >= MinLineF1, $"line F1 {report.LineF1:0.000} < {MinLineF1:0.000}");
		Check(report.AutoApprovedPrecision >= MinAutoApprovedPrecision, $"auto-approved precision {report.AutoApprovedPrecision:0.000} < {MinAutoApprovedPrecision:0.000}");
		Check(report.FalseAutoApproved <= MaxFalseAutoApproved, $"{report.FalseAutoApproved} falsely auto-approved > {MaxFalseAutoApproved}");
		Check(report.CostPerDocumentUsd <= MaxCostPerDocumentUsd, $"cost per document {report.CostPerDocumentUsd:0.000000} > {MaxCostPerDocumentUsd:0.000000}");
		return failures;
	}
}
