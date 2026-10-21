namespace InvoiceExtractor.Application.Features.Upload;

using System.ComponentModel.DataAnnotations;

/// <summary>Upload limits, bound from the <c>Upload</c> section and validated at startup.</summary>
public sealed class UploadOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Upload";

	/// <summary>10 MB, from the plan: a 12 MP phone photo is 3-5 MB, a scanned invoice PDF far less.</summary>
	[Range(1, 50 * 1024 * 1024)]
	public int MaxFileBytes { get; set; } = 10 * 1024 * 1024;

	/// <summary>20 files per batch, from the plan: a month of a small business's receipts in two drops.</summary>
	[Range(1, 100)]
	public int MaxFilesPerBatch { get; set; } = 20;

	/// <summary>
	/// An invoice is one or two pages; five leaves room for an annex while capping what one scanned PDF can
	/// cost, since the model reads every page as an image.
	/// </summary>
	[Range(1, 100)]
	public int MaxPdfPages { get; set; } = 5;

	/// <summary>
	/// The country a batch uploaded without one is validated as. A small business invoices in one country, so
	/// a deployment sets its own; Nicaragua is the default because the demo's samples start there.
	/// </summary>
	[Required]
	[StringLength(2, MinimumLength = 2)]
	public string DefaultCountry { get; set; } = "NI";
}
