using System.Globalization;
using System.Text.Json;
using InvoiceExtractor.Evals.Dataset;
using InvoiceExtractor.Evals.Extraction;
using InvoiceExtractor.Evals.Generation;
using InvoiceExtractor.Evals.Scoring;

// Paths are relative to the repo root, found by walking up to the solution file, so the tool runs the
// same from the root, from evals/ or from `dotnet run --project`.
const string SolutionFile = "InvoiceExtractor.slnx";
const string DatasetFolder = "evals/dataset";
const string SamplesFolder = "evals/samples";
const string ResultsFolder = "evals/results";
const string ThresholdsFile = "evals/thresholds.json";

var root = FindRoot(Directory.GetCurrentDirectory());
var command = args.Length > 0 ? args[0] : string.Empty;

return command switch
{
	"generate" => Generate(root),
	"run" => await RunAsync(root, args.Length > 1 ? args[1] : "oracle").ConfigureAwait(false),
	_ => Usage(),
};

static int Usage()
{
	Console.Error.WriteLine("usage: evals generate | evals run [oracle]");
	return 2;
}

static int Generate(string root)
{
	Write(Path.Combine(root, DatasetFolder), DatasetPlan.Evaluation(), DatasetPlan.EvalSeed);
	Write(Path.Combine(root, SamplesFolder), DatasetPlan.Samples(), DatasetPlan.SamplesSeed);
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

static async Task<int> RunAsync(string root, string extractorName)
{
	IDocumentExtractor extractor = extractorName switch
	{
		"oracle" => new OracleExtractor(),
		_ => throw new ArgumentException($"Unknown extractor '{extractorName}'.", nameof(extractorName)),
	};

	var dataset = Path.Combine(root, DatasetFolder);
	var manifest = DatasetJson.Read(dataset);
	var scores = new List<DocumentScore>(manifest.Documents.Count);
	foreach (var entry in manifest.Documents)
	{
		var attempt = await extractor.ExtractAsync(entry, Path.Combine(dataset, entry.File), CancellationToken.None).ConfigureAwait(false);
		scores.Add(EvalScorer.Score(entry, attempt, manifest.AsOf));
	}

	var report = EvalScorer.Aggregate(extractor.Name, scores);
	var results = Path.Combine(root, ResultsFolder);
	Directory.CreateDirectory(results);
	await File.WriteAllTextAsync(
		Path.Combine(results, $"{extractor.Name}.json"),
		JsonSerializer.Serialize(report, DatasetJson.Options)).ConfigureAwait(false);

	Console.WriteLine(string.Create(
		CultureInfo.InvariantCulture,
		$"""
		{report.Extractor}: {report.Documents} documents
		  key fields  total {report.TotalAccuracy:0.000}  date {report.DateAccuracy:0.000}  tax id {report.TaxIdAccuracy:0.000}  number {report.NumberAccuracy:0.000}
		  line F1     {report.LineF1:0.000}
		  approved    {report.AutoApproved} (precision {report.AutoApprovedPrecision:0.000}, {report.FalseAutoApproved} false)
		  planted     {report.PlantedSentToReview}/{report.Planted} sent to review
		  cost        USD {report.TotalCostUsd:0.000000} ({report.CostPerDocumentUsd:0.000000} per document)
		"""));

	var failures = Thresholds.Read(Path.Combine(root, ThresholdsFile)).Failures(report);
	foreach (var failure in failures)
	{
		Console.Error.WriteLine($"FAIL {failure}");
	}

	return failures.Count == 0 ? 0 : 1;
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
