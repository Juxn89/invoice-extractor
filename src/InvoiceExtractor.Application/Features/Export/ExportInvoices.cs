namespace InvoiceExtractor.Application.Features.Export;

using System.Globalization;
using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Domain.Documents;

/// <summary>An export, ready to download.</summary>
/// <param name="Content">The bytes.</param>
/// <param name="ContentType">The media type.</param>
/// <param name="FileName">A file name with the date.</param>
public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Exports the settled invoices: approved by the rules alone (the last reading) or by a person (what they
/// approved). Documents in review, failed or duplicated are left out: only data someone can book goes out.
/// </summary>
/// <param name="documents">The repository.</param>
/// <param name="exports">The formats.</param>
/// <param name="clock">The clock, for the file name.</param>
public sealed class ExportInvoices(IDocumentRepository documents, IEnumerable<IAccountingExport> exports, TimeProvider clock)
{
	/// <summary>Exports.</summary>
	/// <param name="format">The file format.</param>
	/// <param name="language">The headers' language.</param>
	/// <param name="batch">One batch, or <see langword="null"/> for everything settled.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The file.</returns>
	public async Task<Result<ExportFile>> HandleAsync(ExportFormat format, ExportLanguage language, Guid? batch, CancellationToken cancellationToken)
	{
		var exporter = exports.FirstOrDefault(e => e.Format == format)
			?? throw new InvalidOperationException($"No exporter is registered for {format}.");

		var settled = await documents.ListApprovedAsync(batch is { } id ? new BatchId(id) : null, cancellationToken).ConfigureAwait(false);
		var countries = await documents.CountriesOfAsync([.. settled.Select(static d => d.BatchId).Distinct()], cancellationToken).ConfigureAwait(false);
		var readings = await documents.FinalReadingsAsync([.. settled.Where(static d => d.ReviewedInvoice is null).Select(static d => d.Id)], cancellationToken).ConfigureAwait(false);

		var rows = new List<ExportRow>(settled.Count);
		foreach (var document in settled)
		{
			// A person's approval wins over the reading; an auto-approved document always has one.
			var invoice = document.ReviewedInvoice ?? (readings.TryGetValue(document.Id, out var read) ? read : null);
			if (invoice is not null)
			{
				rows.Add(new ExportRow(document.Id.Value, document.FileName, countries[document.BatchId], document.Status, invoice));
			}
		}

		var name = language == ExportLanguage.Es ? "facturas" : "invoices";
		var date = clock.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		return new ExportFile(exporter.Write(rows, language), exporter.ContentType, $"{name}-{date}{exporter.Extension}");
	}
}
