namespace InvoiceExtractor.Application.Abstractions;

using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;

/// <summary>
/// The work queue: documents waiting in the database, taken one at a time so that any number of workers, in
/// any number of processes, never take the same one. One instance per unit of work (it tracks what it loaded).
/// </summary>
public interface IDocumentQueue
{
	/// <summary>
	/// Takes the oldest document that is queued, or whose lease has run out (its worker died), and holds it
	/// until <paramref name="leaseUntil"/>. Documents other workers hold are skipped, not waited for.
	/// </summary>
	/// <param name="now">The current time.</param>
	/// <param name="leaseUntil">When the claim lapses.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The claimed document, tracked, or <see langword="null"/> when there is no work.</returns>
	Task<Document?> ClaimNextAsync(DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

	/// <summary>Loads a document, tracked, to give it back after a failed attempt.</summary>
	/// <param name="id">The document.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The document, or <see langword="null"/>.</returns>
	Task<Document?> FindAsync(DocumentId id, CancellationToken cancellationToken);

	/// <summary>The settled document (auto-approved or approved) of an invoice, if there is one.</summary>
	/// <param name="key">The invoice's key.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>Its id, or <see langword="null"/>.</returns>
	Task<DocumentId?> FindSettledAsync(InvoiceKey key, CancellationToken cancellationToken);

	/// <summary>Forgets every change since the last save, after a save that failed.</summary>
	void DiscardChanges();

	/// <summary>The country a batch was uploaded with.</summary>
	/// <param name="id">The batch.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>Its tax profile's code.</returns>
	Task<string> CountryOfAsync(BatchId id, CancellationToken cancellationToken);

	/// <summary>Model spend recorded since a moment, for the daily budget.</summary>
	/// <param name="since">The start of the window.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The sum of the extractions' cost.</returns>
	Task<decimal> SpentSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

	/// <summary>Stages an extraction to be saved with the document's new state.</summary>
	/// <param name="extraction">The extraction.</param>
	void Add(Extraction extraction);

	/// <summary>Saves the document's new state and the staged extractions, in one transaction.</summary>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>A task.</returns>
	/// <exception cref="Common.LeaseLostException">Another worker changed the document since it was loaded.</exception>
	Task SaveChangesAsync(CancellationToken cancellationToken);
}
