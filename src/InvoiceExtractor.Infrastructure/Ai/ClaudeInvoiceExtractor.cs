namespace InvoiceExtractor.Infrastructure.Ai;

using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Invoices;
using Microsoft.Extensions.Options;

/// <summary>The model and what it costs, bound from the <c>Anthropic</c> section.</summary>
public sealed class AnthropicOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Anthropic";

	/// <summary>The API key. Optional at startup, so the app runs without extraction; required on first use.</summary>
	public string? ApiKey { get; set; }

	/// <summary>Claude Haiku 5.5 for every call, the retry included (AGENTS.md).</summary>
	[Required]
	public string Model { get; set; } = "claude-haiku-5-5";

	/// <summary>
	/// The answer is a few hundred tokens of JSON for a long invoice, plus Haiku 5.5's adaptive thinking, which
	/// counts toward this cap. 4096 leaves room for both; hitting it means the answer went wrong, not long.
	/// </summary>
	[Range(256, 32_000)]
	public int MaxOutputTokens { get; set; } = 4096;

	/// <summary>
	/// Haiku 5.5 thinks by default; the migration guide's advice is to lower effort rather than disable it.
	/// Reading printed fields needs little reasoning, so <c>low</c>; the evals measure what it costs.
	/// </summary>
	[Required]
	public string Effort { get; set; } = "low";

	/// <summary>USD per million input tokens: Haiku 5.5 for prompts up to 100K tokens (models reference, 21 Oct 2026).</summary>
	[Range(0, 100)]
	public decimal InputUsdPerMillionTokens { get; set; } = 0.10m;

	/// <summary>USD per million output tokens, thinking included, same source and date.</summary>
	[Range(0, 100)]
	public decimal OutputUsdPerMillionTokens { get; set; } = 0.50m;

	/// <summary>A single extraction should take seconds; past this something is stuck.</summary>
	[Range(typeof(TimeSpan), "00:00:05", "00:10:00")]
	public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Reads an invoice with Claude through structured outputs: the answer is constrained to the schema generated
/// from <see cref="InvoiceExtraction"/>, then parsed with exact decimals. It returns what was read and what it
/// cost; deciding whether to trust it is the validator's job, not this class's.
/// </summary>
internal sealed class ClaudeInvoiceExtractor(IOptions<AnthropicOptions> options) : IInvoiceExtractor, IDisposable
{
	private const string RefusalStopReason = "refusal";
	private const string MaxTokensStopReason = "max_tokens";
	private const string Instruction = "Extract this document.";
	private const decimal TokensPerMillion = 1_000_000m;

	private static readonly string SystemPrompt = PromptLibrary.Read("extract-invoice.md");
	private static readonly Dictionary<string, JsonElement> Schema = InvoiceExtractionSchema.AsDictionary();

	private readonly Lazy<AnthropicClient> _client = new(() => new AnthropicClient
	{
		ApiKey = options.Value.ApiKey is { Length: > 0 } key
			? key
			: throw new InvalidOperationException("Anthropic:ApiKey is not set; extraction needs an API key."),
		Timeout = options.Value.Timeout,
	});

	public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

	public async Task<InvoiceExtractionResult> ExtractAsync(PreparedDocument document, CancellationToken cancellationToken)
	{
		var settings = options.Value;
		var response = await _client.Value.Messages.Create(
			new MessageCreateParams
			{
				Model = settings.Model,
				MaxTokens = settings.MaxOutputTokens,
				System = SystemPrompt,
				Thinking = new ThinkingConfigAdaptive(),
				OutputConfig = new OutputConfig
				{
					Effort = settings.Effort,
					Format = new JsonOutputFormat { Schema = Schema },
				},
				Messages = [new() { Role = Role.User, Content = Content(document) }],
			},
			cancellationToken).ConfigureAwait(false);

		var usage = new ExtractionUsage(
			settings.Model,
			response.Usage.InputTokens,
			response.Usage.OutputTokens,
			((response.Usage.InputTokens * settings.InputUsdPerMillionTokens) + (response.Usage.OutputTokens * settings.OutputUsdPerMillionTokens)) / TokensPerMillion);

		if (response.StopReason == RefusalStopReason)
		{
			return Failed(usage, "The model declined to read the document.");
		}

		if (response.StopReason == MaxTokensStopReason)
		{
			return Failed(usage, $"The answer was cut off at {settings.MaxOutputTokens} output tokens.");
		}

		// Thinking blocks may come first; the answer is the text.
		var json = new StringBuilder();
		foreach (var block in response.Content)
		{
			if (block.TryPickText(out var text))
			{
				json.Append(text.Text);
			}
		}

		InvoiceExtraction? answer;
		try
		{
			answer = JsonSerializer.Deserialize<InvoiceExtraction>(json.ToString(), InvoiceExtractionSchema.Json);
		}
		catch (JsonException exception)
		{
			return Failed(usage, $"The answer is not valid JSON for the schema: {exception.Message}");
		}

		if (answer is null)
		{
			return Failed(usage, "The answer was empty.");
		}

		var (invoice, confidence) = InvoiceExtractionSchema.ToDomain(answer);
		return new InvoiceExtractionResult(invoice, confidence, usage, null);
	}

	public void Dispose()
	{
		if (_client.IsValueCreated)
		{
			_client.Value.Dispose();
		}
	}

	// The document goes in a document or image block, never pasted into the instruction: the model sees it as
	// material to read, which is the first line of defence against text in it posing as instructions.
	private static List<ContentBlockParam> Content(PreparedDocument document)
	{
		ContentBlockParam source = document switch
		{
			PreparedText text => new DocumentBlockParam { Source = new PlainTextSource { Data = text.Text } },
			PreparedPdf pdf => new DocumentBlockParam { Source = new Base64PdfSource { Data = Convert.ToBase64String(pdf.Content.Span) } },
			PreparedImage image => new ImageBlockParam
			{
				Source = new Base64ImageSource { Data = Convert.ToBase64String(image.Jpeg.Span), MediaType = MediaType.ImageJpeg },
			},
			_ => throw new ArgumentOutOfRangeException(nameof(document), document.GetType().Name, "Unknown prepared document."),
		};

		return [source, new TextBlockParam { Text = Instruction }];
	}

	private static InvoiceExtractionResult Failed(ExtractionUsage usage, string failure) =>
		new(null, new Dictionary<string, FieldConfidence>(), usage, failure);
}

/// <summary>Prompts are Markdown files embedded in this assembly, versioned and reviewed like code.</summary>
internal static class PromptLibrary
{
	/// <summary>Reads one prompt.</summary>
	/// <param name="name">The file name under <c>Ai/Prompts</c>.</param>
	/// <returns>Its text.</returns>
	public static string Read(string name)
	{
		var assembly = typeof(PromptLibrary).Assembly;
		var resource = assembly.GetManifestResourceNames().Single(r => r.EndsWith("." + name, StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(resource)!;
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd().Trim();
	}
}
