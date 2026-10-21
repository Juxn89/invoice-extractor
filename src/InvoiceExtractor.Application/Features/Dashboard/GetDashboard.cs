namespace InvoiceExtractor.Application.Features.Dashboard;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Features.Processing;
using InvoiceExtractor.Domain.Documents;
using Microsoft.Extensions.Options;

/// <summary>The numbers the dashboard shows.</summary>
/// <param name="Documents">Every document uploaded.</param>
/// <param name="ByStatus">How many are in each status (every status present, zero included).</param>
/// <param name="Decided">Documents the pipeline finished with a verdict: auto-approved, in review or approved.</param>
/// <param name="AutoApprovedRate">Share of <paramref name="Decided"/> approved by the rules alone.</param>
/// <param name="ReviewRate">Share of <paramref name="Decided"/> sent to a person (waiting or already approved).</param>
/// <param name="ModelCalls">Model calls made.</param>
/// <param name="Retries">How many of them were retries.</param>
/// <param name="CostUsd">What every call cost.</param>
/// <param name="CostPerDocumentUsd">Average per document the model was called on, retries included.</param>
/// <param name="SpentTodayUsd">Spend since 00:00 UTC.</param>
/// <param name="DailyBudgetUsd">The daily cap the workers stop at.</param>
public sealed record DashboardResponse(
	int Documents,
	IReadOnlyDictionary<DocumentStatus, int> ByStatus,
	int Decided,
	double AutoApprovedRate,
	double ReviewRate,
	int ModelCalls,
	int Retries,
	decimal CostUsd,
	decimal CostPerDocumentUsd,
	decimal SpentTodayUsd,
	decimal DailyBudgetUsd);

/// <summary>Adds up documents and model spend.</summary>
/// <param name="documents">The repository.</param>
/// <param name="options">The daily budget.</param>
/// <param name="clock">The clock.</param>
public sealed class GetDashboard(IDocumentRepository documents, IOptions<ProcessingOptions> options, TimeProvider clock)
{
	/// <summary>Reads it.</summary>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The dashboard.</returns>
	public async Task<DashboardResponse> HandleAsync(CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(options);
		var counts = await documents.CountByStatusAsync(cancellationToken).ConfigureAwait(false);
		var all = await documents.ExtractionTotalsAsync(since: null, cancellationToken).ConfigureAwait(false);
		var now = clock.GetUtcNow();
		var today = await documents.ExtractionTotalsAsync(new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero), cancellationToken).ConfigureAwait(false);

		var byStatus = Enum.GetValues<DocumentStatus>().ToDictionary(static s => s, s => counts.GetValueOrDefault(s));
		var auto = byStatus[DocumentStatus.AutoApproved];
		var review = byStatus[DocumentStatus.NeedsReview] + byStatus[DocumentStatus.Approved];
		var decided = auto + review;

		return new DashboardResponse(
			byStatus.Values.Sum(),
			byStatus,
			decided,
			decided == 0 ? 0 : (double)auto / decided,
			decided == 0 ? 0 : (double)review / decided,
			all.Calls,
			all.Retries,
			all.CostUsd,
			all.Documents == 0 ? 0 : Math.Round(all.CostUsd / all.Documents, 6),
			today.CostUsd,
			options.Value.DailyBudgetUsd);
	}
}
