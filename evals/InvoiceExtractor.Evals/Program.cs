using System.Globalization;
using System.Text.Json;
using InvoiceExtractor.Evals.Dataset;
using InvoiceExtractor.Evals.Extraction;
using InvoiceExtractor.Evals.Generation;
using InvoiceExtractor.Evals.Paid;
using InvoiceExtractor.Evals.Scoring;
using InvoiceExtractor.Infrastructure.Ai;

// Paths are relative to the repo root, found by walking up to the solution file, so the tool runs the
// same from the root, from evals/ or from `dotnet run --project`.
const string SolutionFile = "InvoiceExtractor.slnx";
const string DatasetFolder = "evals/dataset";
const string HardDatasetFolder = "evals/dataset-hard";
const string HardSet = "hard";
const string SamplesFolder = "evals/samples";
const string ResultsFolder = "evals/results";
const string ThresholdsFile = "evals/thresholds.json";
const string ApiKeyVariable = "ANTHROPIC_API_KEY";

// A full run is 30 calls; the default leaves room for one more run without editing the command.
const int DefaultMaxCalls = 40;
const decimal DefaultMaxCostUsd = 0.10m;

var root = FindRoot(Directory.GetCurrentDirectory());
var command = args.Length > 0 ? args[0] : string.Empty;
var extractorName = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : "oracle";
var hard = Text(args, "--set") == HardSet;

return command switch
{
	"generate" => Generate(root),
	"run" when extractorName == "oracle" => await RunAsync(root, new OracleExtractor(), limit: null, guard: null, hard).ConfigureAwait(false),
	"run" when extractorName == "claude" => await RunPaidAsync(root, args, hard).ConfigureAwait(false),
	_ => Usage(),
};

static int Usage()
{
	Console.Error.WriteLine("""
		usage:
		  evals generate
		  evals run oracle [--set hard]
		  evals run claude --allow-paid [--set hard] [--limit N | --only id,id] [--max-calls N] [--max-cost USD]
		""");
	return 2;
}

static int Generate(string root)
{
	Write(Path.Combine(root, DatasetFolder), DatasetPlan.Evaluation(), DatasetPlan.EvalSeed);
	Write(Path.Combine(root, SamplesFolder), DatasetPlan.Samples(), DatasetPlan.SamplesSeed);
	Write(Path.Combine(root, HardDatasetFolder), DatasetPlan.Hard(), DatasetPlan.HardSeed);
	return 0;

	static void Write(string folder, IReadOnlyList<DocumentSpec> specs, int seed)
	{
		Directory.CreateDirectory(folder);
#pragma warning disable CA5394 // Seeded on purpose: the renders regenerate byte for byte.
		var random = new Random(seed);
#pragma warning restore CA5394
		foreach (var spec in specs)
		{
			DocumentRenderer.Render(spec, folder, random);
		}

		DatasetJson.Write(folder, new DatasetManifest(DatasetPlan.AsOf, seed, [.. specs.Select(static s => s.ToEntry())]));
		Console.WriteLine($"{specs.Count} documents -> {folder}");
	}
}

// Calls the paid API only with --allow-paid, within the run's limits and what the shared ledger says is left,
// and records the run in the ledger even when it stops halfway.
static async Task<int> RunPaidAsync(string root, string[] args, bool hard)
{
	if (!args.Contains("--allow-paid"))
	{
		Console.Error.WriteLine("This run calls the Anthropic API and costs money. Add --allow-paid to proceed.");
		return 2;
	}

	var apiKey = DotEnv.Read(root, ApiKeyVariable);
	if (string.IsNullOrEmpty(apiKey))
	{
		Console.Error.WriteLine($"{ApiKeyVariable} is not set in .env or the environment.");
		return 2;
	}

	var ledger = Ledger.Default;
	var remaining = ledger.Remaining();
	var maxCost = Math.Min(Option(args, "--max-cost", DefaultMaxCostUsd), remaining);
	if (maxCost <= 0)
	{
		Console.Error.WriteLine($"Nothing left of the {ledger.SessionCapUsd} USD allocated to {ledger.Session}.");
		return 2;
	}

	var limit = args.Contains("--limit") ? (int)Option(args, "--limit", 0m)
		: Text(args, "--only") is { } only ? only.Split(',').Length
		: (int?)null;
	var guard = new SpendingGuard((int)Option(args, "--max-calls", DefaultMaxCalls), maxCost);
	var options = new AnthropicOptions { ApiKey = apiKey };
	Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{options.Model}: up to {maxCost:F4} USD ({remaining:F4} left in {ledger.Session})"));

	var note = "completed";
	try
	{
		using var extractor = new ClaudeExtractor(options, guard);
		return await RunAsync(root, extractor, limit, guard, hard).ConfigureAwait(false);
	}
	catch (BudgetExceededException exception)
	{
		note = $"stopped by the guard: {exception.Message}";
		Console.Error.WriteLine(note);
		return 1;
	}
	catch (Exception exception)
	{
		note = $"failed: {exception.GetType().Name}";
		throw;
	}
	finally
	{
		if (guard.Calls > 0)
		{
			var experiment = (limit is null ? "eval-full" : $"eval-smoke-{limit}") + (hard ? "-hard" : string.Empty);
			ledger.Append(experiment, options.Model, guard, $"invoice-extractor evals, {note}; usage from each response");
			Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ledger: {guard.Calls} calls, {guard.CostUsd:F6} USD recorded"));
		}
	}
}

static async Task<int> RunAsync(string root, IDocumentExtractor extractor, int? limit, SpendingGuard? guard, bool hard)
{
	var dataset = Path.Combine(root, hard ? HardDatasetFolder : DatasetFolder);
	var manifest = DatasetJson.Read(dataset);
	var only = Text(Environment.GetCommandLineArgs(), "--only")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
	var entries = only is not null
		? [.. manifest.Documents.Where(e => only.Contains(e.Id))]
		: limit is { } n ? manifest.Documents.Take(n).ToList() : manifest.Documents;
	limit ??= only?.Length;
	var first = new List<DocumentScore>(entries.Count);
	var final = new List<DocumentScore>(entries.Count);
	foreach (var entry in entries)
	{
		var run = await extractor.ExtractAsync(entry, Path.Combine(dataset, entry.File), CancellationToken.None).ConfigureAwait(false);
		first.Add(EvalScorer.Score(entry, run.First, manifest.AsOf));
		final.Add(EvalScorer.Score(entry, run.Final, manifest.AsOf));
		if (guard is not null)
		{
			Console.WriteLine(Line(final[^1]) + (run.Retried ? $"  (retried; first: {Verdict(first[^1])})" : string.Empty));
		}
	}

	// The first reading alone is the configuration without retry; with a pipeline that retries, the final
	// verdict is the configuration with it. The thresholds apply to what the app ships: the final one.
	var suffix = hard ? "-" + HardSet : string.Empty;
	var reports = new List<(string File, EvalReport Report)> { ($"{extractor.Name}{suffix}.json", EvalScorer.Aggregate(extractor.Name, first)) };
	if (extractor.Retries)
	{
		reports.Add(($"{extractor.Name}-retry{suffix}.json", EvalScorer.Aggregate($"{extractor.Name} + retry", final)));
	}

	foreach (var (file, report) in reports)
	{
		// Only a full run is a result worth versioning; a smoke run is printed and forgotten.
		if (limit is null)
		{
			var results = Path.Combine(root, ResultsFolder);
			Directory.CreateDirectory(results);
			await File.WriteAllTextAsync(Path.Combine(results, file), JsonSerializer.Serialize(report, DatasetJson.Options)).ConfigureAwait(false);
		}

		Console.WriteLine(string.Create(
			CultureInfo.InvariantCulture,
			$"""
			{report.Extractor}{(hard ? " (hard set)" : string.Empty)}: {report.Documents} documents
			  key fields  total {report.TotalAccuracy:0.000}  date {report.DateAccuracy:0.000}  tax id {report.TaxIdAccuracy:0.000}  number {report.NumberAccuracy:0.000}
			  line F1     {report.LineF1:0.000}
			  approved    {report.AutoApproved} (precision {report.AutoApprovedPrecision:0.000}, {report.FalseAutoApproved} false)
			  planted     {report.PlantedSentToReview}/{report.Planted} sent to review
			  cost        USD {report.TotalCostUsd:0.000000} ({report.CostPerDocumentUsd:0.000000} per document)
			"""));
	}

	var failures = Thresholds.Read(Path.Combine(root, ThresholdsFile)).Failures(reports[^1].Report);
	foreach (var failure in failures)
	{
		Console.Error.WriteLine($"FAIL {failure}");
	}

	return failures.Count == 0 ? 0 : 1;
}

static string Verdict(DocumentScore s) => s.AutoApproved ? "approved" : "review";

static string Line(DocumentScore s) => string.Create(
	CultureInfo.InvariantCulture,
	$"  {s.Id,-6} total {Mark(s.TotalMatches)} date {Mark(s.DateMatches)} id {Mark(s.TaxIdMatches)} no {Mark(s.NumberMatches)} lines {s.MatchedLines}/{s.ExpectedLines} {(s.AutoApproved ? "approved" : "review")} {s.CostUsd:F6}{(s.Error is null ? string.Empty : " ERROR " + s.Error)}{(s.Issues.Count == 0 ? string.Empty : " [" + string.Join(", ", s.Issues.Select(static i => i.Code)) + "]")}");

static string Mark(bool ok) => ok ? "ok" : "--";

static string? Text(string[] args, string name)
{
	var index = Array.IndexOf(args, name);
	return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static decimal Option(string[] args, string name, decimal fallback)
{
	var index = Array.IndexOf(args, name);
	return index >= 0 && index + 1 < args.Length
		? decimal.Parse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture)
		: fallback;
}

static string FindRoot(string start)
{
	for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
	{
		if (File.Exists(Path.Combine(folder.FullName, SolutionFile)))
		{
			return folder.FullName;
		}
	}

	throw new DirectoryNotFoundException($"{SolutionFile} not found above {start}.");
}
