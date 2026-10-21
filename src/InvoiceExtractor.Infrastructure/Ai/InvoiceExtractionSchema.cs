namespace InvoiceExtractor.Infrastructure.Ai;

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;

/// <summary>
/// The shape the model must answer in. Amounts and dates travel as strings: a JSON number is a double to most
/// parsers, and 0.1 + 0.2 is not 0.3 in a double, so a decimal is parsed from the exact digits instead.
/// </summary>
internal sealed record InvoiceExtraction(
	[property: Description("Who issued the document.")] string? IssuerName,
	[property: Description("The issuer's tax id exactly as printed.")] string? IssuerTaxId,
	[property: Description("Who the document is addressed to.")] string? ReceiverName,
	[property: Description("Invoice, folio or ticket number, without its label.")] string? Number,
	[property: Description("Issue date, YYYY-MM-DD.")] string? IssueDate,
	[property: Description("ISO 4217 code, only when stated or unambiguous.")] string? Currency,
	[property: Description("Line items in printed order.")] IReadOnlyList<LineExtraction> Lines,
	[property: Description("Amount before tax, plain decimal string.")] string? Subtotal,
	[property: Description("Total tax, plain decimal string.")] string? TaxTotal,
	[property: Description("Amount payable, plain decimal string.")] string? Total,
	ConfidenceExtraction Confidence);

internal sealed record LineExtraction(
	string Description,
	[property: Description("Plain decimal string.")] string? Quantity,
	[property: Description("Plain decimal string.")] string? UnitPrice,
	[property: Description("Plain decimal string.")] string? LineTotal);

internal sealed record ConfidenceExtraction(
	ConfidenceLevel IssuerTaxId,
	ConfidenceLevel Number,
	ConfidenceLevel IssueDate,
	ConfidenceLevel Lines,
	ConfidenceLevel Subtotal,
	ConfidenceLevel TaxTotal,
	ConfidenceLevel Total);

[JsonConverter(typeof(JsonStringEnumConverter<ConfidenceLevel>))]
internal enum ConfidenceLevel
{
	Low = 1,
	Medium = 2,
	High = 3,
}

/// <summary>Generates the JSON Schema from the records above and maps an answer back to the domain.</summary>
internal static class InvoiceExtractionSchema
{
	private const string DateFormat = "yyyy-MM-dd";

	/// <summary>How the model's JSON is read: camelCase, enums as lower-case names.</summary>
	public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
	{
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
		RespectNullableAnnotations = true,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
	};

	/// <summary>
	/// The schema in the structured-outputs dialect: every object closed (<c>additionalProperties: false</c>)
	/// with every property required (nullable ones may be null), and no validation keywords, which the API
	/// rejects. Generated rather than hand-written, so it cannot drift from the records.
	/// </summary>
	public static JsonObject Schema { get; } = Build();

	/// <summary>The schema as the SDK takes it.</summary>
	/// <returns>Top-level keywords to values.</returns>
	public static Dictionary<string, JsonElement> AsDictionary() =>
		Schema.ToDictionary(static p => p.Key, static p => JsonSerializer.SerializeToElement(p.Value));

	/// <summary>Maps a parsed answer to the domain. Unparseable amounts or dates become null and go to review.</summary>
	/// <param name="answer">The model's answer.</param>
	/// <returns>The invoice and the confidence per field.</returns>
	public static (ExtractedInvoice Invoice, IReadOnlyDictionary<string, FieldConfidence> Confidence) ToDomain(InvoiceExtraction answer)
	{
		ArgumentNullException.ThrowIfNull(answer);

		var lines = answer.Lines.Select(ToLine).OfType<InvoiceLine>().ToList();
		var invoice = new ExtractedInvoice(
			Trimmed(answer.IssuerName),
			Trimmed(answer.IssuerTaxId),
			Trimmed(answer.ReceiverName),
			Trimmed(answer.Number),
			DateOnly.TryParseExact(answer.IssueDate, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
			Trimmed(answer.Currency)?.ToUpperInvariant(),
			lines,
			Amount(answer.Subtotal),
			Amount(answer.TaxTotal),
			Amount(answer.Total));

		var c = answer.Confidence;
		var confidence = new Dictionary<string, FieldConfidence>
		{
			[InvoiceFields.IssuerTaxId] = (FieldConfidence)c.IssuerTaxId,
			[InvoiceFields.Number] = (FieldConfidence)c.Number,
			[InvoiceFields.IssueDate] = (FieldConfidence)c.IssueDate,
			[InvoiceFields.Lines] = (FieldConfidence)c.Lines,
			[InvoiceFields.Subtotal] = (FieldConfidence)c.Subtotal,
			[InvoiceFields.TaxTotal] = (FieldConfidence)c.TaxTotal,
			[InvoiceFields.Total] = (FieldConfidence)c.Total,
		};

		return (invoice, confidence);
	}

	/// <summary>A strict decimal: optional minus, digits, optional dot and digits. "1,234.56" is not one.</summary>
	/// <param name="value">The string.</param>
	/// <returns>The amount, or <see langword="null"/>.</returns>
	internal static decimal? Amount(string? value) =>
		decimal.TryParse(value?.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
			? amount
			: null;

	// The prompt tells the model what to do when a receipt omits a quantity or unit price; a line with
	// neither price is dropped, and the missing amount then fails the subtotal rule and sends it to review.
	private static InvoiceLine? ToLine(LineExtraction line)
	{
		var total = Amount(line.LineTotal);
		var unit = Amount(line.UnitPrice) ?? total;
		if (unit is null)
		{
			return null;
		}

		var quantity = Amount(line.Quantity) ?? 1m;
		return new InvoiceLine(line.Description.Trim(), quantity, unit.Value, total ?? quantity * unit.Value);
	}

	private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static JsonObject Build()
	{
		var exporter = new JsonSchemaExporterOptions
		{
			TreatNullObliviousAsNonNullable = true,
			TransformSchemaNode = static (_, node) =>
			{
				if (node is JsonObject schema && schema["properties"] is JsonObject properties)
				{
					schema["additionalProperties"] = false;
					schema["required"] = new JsonArray([.. properties.Select(static p => JsonValue.Create(p.Key))]);
				}

				return node;
			},
		};

		return (JsonObject)Json.GetJsonSchemaAsNode(typeof(InvoiceExtraction), exporter);
	}
}
