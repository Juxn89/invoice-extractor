namespace InvoiceExtractor.Evals.Dataset;

using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;

/// <summary>How a document was produced; each kind stresses a different part of extraction.</summary>
internal enum DocumentKind
{
	/// <summary>A PDF with a text layer, as an accounting system exports it.</summary>
	DigitalPdf = 1,

	/// <summary>A flat-bed scan: an image, upright, clean.</summary>
	Scan = 2,

	/// <summary>A phone photo: tilted, uneven light, noise and blur.</summary>
	Photo = 3,

	/// <summary>A narrow thermal till receipt, monospaced.</summary>
	ThermalReceipt = 4,
}

/// <summary>What should happen to a document once its extraction has been validated.</summary>
internal enum ExpectedOutcome
{
	/// <summary>Every rule passes on a correct extraction.</summary>
	AutoApprove = 1,

	/// <summary>The document itself is inconsistent, so even a perfect extraction must go to review.</summary>
	Review = 2,
}

/// <summary>One document of the dataset and the truth about it.</summary>
/// <param name="Id">Stable id, e.g. <c>ni-03</c>.</param>
/// <param name="File">The file name inside the dataset folder.</param>
/// <param name="Kind">How it was produced.</param>
/// <param name="Country">The issuer's country (<c>NI</c>, <c>MX</c> or <c>XX</c> for the generic profile).</param>
/// <param name="Language">The language printed on the document.</param>
/// <param name="Expected">Exactly what is printed: the target a perfect extraction reproduces.</param>
/// <param name="Outcome">Whether a perfect extraction is approved or sent to review.</param>
/// <param name="PlantedIssue">For a document made inconsistent on purpose, the one rule it breaks.</param>
internal sealed record DatasetEntry(
	string Id,
	string File,
	DocumentKind Kind,
	string Country,
	string Language,
	ExtractedInvoice Expected,
	ExpectedOutcome Outcome,
	IssueCode? PlantedIssue);

/// <summary>The dataset's index, written next to the documents as <c>manifest.json</c>.</summary>
/// <param name="AsOf">The "today" validation runs against, fixed so the date rules stay reproducible.</param>
/// <param name="Seed">The generator's seed.</param>
/// <param name="Documents">Every document.</param>
internal sealed record DatasetManifest(DateOnly AsOf, int Seed, IReadOnlyList<DatasetEntry> Documents);
