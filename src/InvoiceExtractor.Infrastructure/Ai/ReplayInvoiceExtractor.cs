namespace InvoiceExtractor.Infrastructure.Ai;

using System.Text.Json;
using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;
using Microsoft.Extensions.Options;

/// <summary>
/// Recorded model answers in place of the model, bound from the <c>Replay</c> section. Only for the end-to-end
/// tests: the API refuses to start with it outside Development.
/// </summary>
public sealed class ReplayOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Replay";

	/// <summary>The JSON file with the recorded answers; when set, the model is never called.</summary>
	public string? File { get; set; }
}

/// <summary>
/// Answers from a file of recorded answers instead of calling Claude, so the whole app runs end to end in CI with
/// no key and no cost. Each answer is matched to a digital PDF by a text its pages contain (the invoice number),
/// and is parsed by the same code as a real answer, so the validator and the review policy decide exactly as
/// they would. A retry gets the same answer again, which is what a model that keeps misreading a field does.
/// </summary>
internal sealed class ReplayInvoiceExtractor : IInvoiceExtractor
{
	private const string Model = "replay";

	private static readonly ExtractionUsage NoCost = new(Model, 0, 0, 0m);

	private readonly IReadOnlyList<RecordedAnswer> _answers;

	public ReplayInvoiceExtractor(IOptions<ReplayOptions> options)
	{
		ArgumentNullException.ThrowIfNull(options);
		var path = options.Value.File ?? throw new InvalidOperationException("Replay:File is not set.");
		using var stream = System.IO.File.OpenRead(path);
		_answers = JsonSerializer.Deserialize<RecordedAnswers>(stream, InvoiceExtractionSchema.Json)?.Answers
			?? throw new InvalidOperationException($"'{path}' holds no recorded answers.");
	}

	public bool IsConfigured => true;

	public Task<InvoiceExtractionResult> ExtractAsync(PreparedDocument document, IReadOnlyList<ValidationIssue> feedback, CancellationToken cancellationToken)
	{
		var recorded = document is PreparedText text
			? _answers.FirstOrDefault(answer => text.Text.Contains(answer.Match, StringComparison.Ordinal))
			: null;

		if (recorded is null)
		{
			return Task.FromResult(new InvoiceExtractionResult(
				null,
				new Dictionary<string, FieldConfidence>(),
				NoCost,
				"No recorded answer for this document."));
		}

		var (invoice, confidence) = InvoiceExtractionSchema.ToDomain(recorded.Answer);
		return Task.FromResult(new InvoiceExtractionResult(invoice, confidence, NoCost, null));
	}

	private sealed record RecordedAnswers(IReadOnlyList<RecordedAnswer> Answers);

	/// <param name="Match">A text only that document contains.</param>
	/// <param name="Answer">What the model answered, in the structured-outputs shape.</param>
	private sealed record RecordedAnswer(string Match, InvoiceExtraction Answer);
}
