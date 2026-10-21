namespace InvoiceExtractor.Infrastructure.Processing;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Application.Features.Processing;
using InvoiceExtractor.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Runs <see cref="ProcessingOptions.Workers"/> loops, each claiming one document at a time from the queue.
/// Any number of these can run, in this process or others: the claim keeps them from sharing a document, the
/// lease returns a crashed worker's document to the rest, and the daily budget caps what they can spend.
/// </summary>
/// <param name="scopes">A scope per document: its own unit of work.</param>
/// <param name="extractor">Checked once at start: without an API key there is nothing to do.</param>
/// <param name="options">The worker settings.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">The log.</param>
internal sealed partial class ExtractionWorker(
	IServiceScopeFactory scopes,
	IInvoiceExtractor extractor,
	IOptions<ProcessingOptions> options,
	TimeProvider clock,
	ILogger<ExtractionWorker> logger) : BackgroundService
{
	private DateOnly _budgetLoggedOn;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var settings = options.Value;
		if (!settings.Enabled)
		{
			LogDisabled();
			return;
		}

		if (!extractor.IsConfigured)
		{
			LogNotConfigured();
			return;
		}

		await Task.WhenAll(Enumerable.Range(0, settings.Workers).Select(_ => RunAsync(stoppingToken))).ConfigureAwait(false);
	}

	/// <summary>Takes and processes at most one document.</summary>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>Whether a document was taken, so the loop knows whether to wait before the next one.</returns>
	internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
	{
		var settings = options.Value;
		var scope = scopes.CreateAsyncScope();
		await using (scope.ConfigureAwait(false))
		{
			var queue = scope.ServiceProvider.GetRequiredService<IDocumentQueue>();
			var now = clock.GetUtcNow();

			var spent = await queue.SpentSinceAsync(StartOfDay(now), cancellationToken).ConfigureAwait(false);
			if (spent >= settings.DailyBudgetUsd)
			{
				LogBudgetReachedOncePerDay(now, spent, settings.DailyBudgetUsd);
				return false;
			}

			var document = await queue.ClaimNextAsync(now, now + settings.Lease, cancellationToken).ConfigureAwait(false);
			if (document is null)
			{
				return false;
			}

			// Taken again after its lease ran out with every attempt used: its earlier workers died mid-way
			// (each death costs an attempt). Failing it here keeps a document that crashes the worker from
			// crashing every worker in turn, forever.
			if (document.Attempts > settings.MaxAttempts)
			{
				document.Release(settings.MaxAttempts, now);
				await queue.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
				LogGaveUp(document.Id, document.Attempts - 1);
				return true;
			}

			var lease = document.LeaseUntil;
			try
			{
				var outcome = await scope.ServiceProvider.GetRequiredService<ProcessDocument>()
					.HandleAsync(document, cancellationToken)
					.ConfigureAwait(false);
				LogProcessed(document.Id, outcome, document.Attempts);
			}
			catch (LeaseLostException)
			{
				LogLeaseLost(document.Id);
			}
			catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
			{
				LogAttemptFailed(exception, document.Id, document.Attempts);
				await ReleaseAsync(document.Id, lease, cancellationToken).ConfigureAwait(false);
			}

			return true;
		}
	}

	private async Task RunAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			bool processed;
			try
			{
				processed = await ProcessNextAsync(stoppingToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
#pragma warning disable CA1031 // The loop must outlive a database outage; it logs, waits and tries again.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				LogLoopFailed(exception);
				processed = false;
			}

			if (!processed)
			{
				try
				{
					await Task.Delay(options.Value.PollInterval, clock, stoppingToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}
		}
	}

	// A fresh unit of work: the failed one may hold half-applied changes. The document is given back only if
	// this worker still holds it (same lease), so a late release cannot undo another worker's claim.
	private async Task ReleaseAsync(DocumentId id, DateTimeOffset? lease, CancellationToken cancellationToken)
	{
		var scope = scopes.CreateAsyncScope();
		await using (scope.ConfigureAwait(false))
		{
			var queue = scope.ServiceProvider.GetRequiredService<IDocumentQueue>();
			var document = await queue.FindAsync(id, cancellationToken).ConfigureAwait(false);
			if (document is not { Status: DocumentStatus.Processing } || document.LeaseUntil != lease)
			{
				return;
			}

			document.Release(options.Value.MaxAttempts, clock.GetUtcNow());
			try
			{
				await queue.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (LeaseLostException)
			{
				LogLeaseLost(id);
			}
		}
	}

	private void LogBudgetReachedOncePerDay(DateTimeOffset now, decimal spent, decimal budget)
	{
		var today = DateOnly.FromDateTime(now.UtcDateTime);
		if (_budgetLoggedOn != today)
		{
			_budgetLoggedOn = today;
			LogBudgetReached(spent, budget);
		}
	}

	private static DateTimeOffset StartOfDay(DateTimeOffset now) => new(now.UtcDateTime.Date, TimeSpan.Zero);

	[LoggerMessage(Level = LogLevel.Information, Message = "Extraction worker disabled (Processing:Enabled is false).")]
	private partial void LogDisabled();

	[LoggerMessage(Level = LogLevel.Warning, Message = "Extraction worker idle: no Anthropic API key is configured (Anthropic:ApiKey). Uploads are queued but not read.")]
	private partial void LogNotConfigured();

	[LoggerMessage(Level = LogLevel.Warning, Message = "Daily model budget reached: {SpentUsd} of {BudgetUsd} USD. Workers pause until 00:00 UTC.")]
	private partial void LogBudgetReached(decimal spentUsd, decimal budgetUsd);

	[LoggerMessage(Level = LogLevel.Information, Message = "Document {DocumentId} processed on attempt {Attempt}: {Outcome}.")]
	private partial void LogProcessed(DocumentId documentId, DocumentStatus outcome, int attempt);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Document {DocumentId}: lease lost to another worker; this result is dropped.")]
	private partial void LogLeaseLost(DocumentId documentId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Document {DocumentId}: attempt {Attempt} failed; it goes back to the queue unless out of attempts.")]
	private partial void LogAttemptFailed(Exception exception, DocumentId documentId, int attempt);

	[LoggerMessage(Level = LogLevel.Error, Message = "Document {DocumentId} failed: its workers stopped mid-way {Attempts} times.")]
	private partial void LogGaveUp(DocumentId documentId, int attempts);

	[LoggerMessage(Level = LogLevel.Error, Message = "Extraction worker loop failed; retrying after the poll interval.")]
	private partial void LogLoopFailed(Exception exception);
}
