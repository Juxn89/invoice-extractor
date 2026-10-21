namespace InvoiceExtractor.Integration.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;
using InvoiceExtractor.Infrastructure.Ai;

// The structured-outputs dialect is a subset of JSON Schema: a schema outside it is a 400 on every call, so
// it is checked here, for free, rather than discovered by a paid one.
public sealed class ExtractionSchemaTests
{
	private static readonly string[] UnsupportedKeywords =
	[
		"minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
		"minLength", "maxLength", "pattern", "format", "minItems", "maxItems", "uniqueItems", "$schema",
	];

	[Fact]
	public void Every_object_is_closed_and_requires_all_its_properties()
	{
		var objects = Objects(InvoiceExtractionSchema.Schema).ToList();

		Assert.Equal(3, objects.Count); // the invoice (the root), a line, the confidence
		foreach (var schema in objects)
		{
			Assert.False(schema["additionalProperties"]!.GetValue<bool>());
			var required = schema["required"]!.AsArray().Select(static r => r!.GetValue<string>()).Order();
			Assert.Equal(schema["properties"]!.AsObject().Select(static p => p.Key).Order(), required);
		}
	}

	[Fact]
	public void It_uses_no_keyword_the_dialect_rejects()
	{
		var json = InvoiceExtractionSchema.Schema.ToJsonString();

		foreach (var keyword in UnsupportedKeywords)
		{
			Assert.DoesNotContain($"\"{keyword}\"", json, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Amounts_are_nullable_strings_and_confidence_is_an_enum()
	{
		var properties = InvoiceExtractionSchema.Schema["properties"]!;

		Assert.Equal("[\"string\",\"null\"]", properties["total"]!["type"]!.ToJsonString());
		Assert.Equal("[\"low\",\"medium\",\"high\"]", properties["confidence"]!["properties"]!["total"]!["enum"]!.ToJsonString());
		Assert.Equal("array", properties["lines"]!["type"]!.GetValue<string>());
	}

	[Theory]
	[InlineData("1234.56", "1234.56")]
	[InlineData("-0.50", "-0.50")]
	[InlineData(" 10 ", "10")]
	[InlineData("1,234.56", null)] // thousands separators are the prompt's job, not a guess here
	[InlineData("1.234,56", null)]
	[InlineData("C$ 10.00", null)]
	[InlineData("1e3", null)]
	[InlineData(null, null)]
	public void Amounts_are_parsed_strictly(string? text, string? expected) =>
		Assert.Equal(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), InvoiceExtractionSchema.Amount(text));

	[Fact]
	public void An_answer_maps_to_the_domain_with_exact_decimals_and_confidence_by_field()
	{
		const string answer = """
			{
			  "issuerName": " Ferretería La Esperanza ", "issuerTaxId": "001-150385-0002K", "receiverName": null,
			  "number": "T-01237", "issueDate": "2026-07-25", "currency": "nio",
			  "lines": [
			    { "description": "Caja de tornillos", "quantity": null, "unitPrice": null, "lineTotal": "79.72" },
			    { "description": "Sin precio", "quantity": "2", "unitPrice": null, "lineTotal": null }
			  ],
			  "subtotal": "79.72", "taxTotal": "11.96", "total": "91.68",
			  "confidence": { "issuerTaxId": "high", "number": "high", "issueDate": "medium", "lines": "high",
			                  "subtotal": "high", "taxTotal": "high", "total": "low" }
			}
			""";

		var (invoice, confidence) = InvoiceExtractionSchema.ToDomain(JsonSerializer.Deserialize<InvoiceExtraction>(answer, InvoiceExtractionSchema.Json)!);

		Assert.Equal("Ferretería La Esperanza", invoice.IssuerName);
		Assert.Equal(new DateOnly(2026, 7, 25), invoice.IssueDate);
		Assert.Equal("NIO", invoice.Currency);
		var line = Assert.Single(invoice.Lines); // the line with no price at all is dropped
		Assert.Equal((1m, 79.72m, 79.72m), (line.Quantity, line.UnitPrice, line.LineTotal));
		Assert.Equal(91.68m, invoice.Total);
		Assert.Equal(FieldConfidence.Low, confidence[InvoiceFields.Total]);
		Assert.Equal(FieldConfidence.Medium, confidence[InvoiceFields.IssueDate]);
	}

	private static IEnumerable<JsonObject> Objects(JsonNode? node)
	{
		switch (node)
		{
			case JsonObject obj:
				if (obj["properties"] is JsonObject)
				{
					yield return obj;
				}

				foreach (var child in obj.SelectMany(static p => Objects(p.Value)))
				{
					yield return child;
				}

				break;
			case JsonArray array:
				foreach (var child in array.SelectMany(Objects))
				{
					yield return child;
				}

				break;
		}
	}
}
