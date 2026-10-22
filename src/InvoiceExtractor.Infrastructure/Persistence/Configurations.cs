namespace InvoiceExtractor.Infrastructure.Persistence;

using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;
using InvoiceExtractor.Domain.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
	public void Configure(EntityTypeBuilder<Batch> builder)
	{
		builder.HasKey(b => b.Id);
		builder.Property(b => b.Country).HasMaxLength(Batch.CountryLength).IsFixedLength();
	}
}

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
	public void Configure(EntityTypeBuilder<Document> builder)
	{
		builder.HasKey(d => d.Id);
		builder.HasOne<Batch>().WithMany().HasForeignKey(d => d.BatchId).OnDelete(DeleteBehavior.Cascade);

		builder.Property(d => d.FileName).HasMaxLength(Document.FileNameMaxLength);
		builder.Property(d => d.Format).HasConversion<string>().HasMaxLength(EnumColumnLength);
		builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(EnumColumnLength);
		builder.Property(d => d.ContentHash).HasMaxLength(ContentHash.HexLength).IsFixedLength();
		builder.Property(d => d.ReviewedInvoice).HasColumnType("jsonb").HasConversion(JsonColumn<ExtractedInvoice?>.Converter, JsonColumn<ExtractedInvoice?>.Comparer);

		// Idempotent upload: the same bytes are one document, whoever uploads them and however many times.
		builder.HasIndex(d => d.ContentHash).IsUnique();

		// One invoice is settled once, however many files of it arrive (ADR 0004). Only settled documents carry a
		// key, so the index stays small, and two workers settling the same invoice at once cannot both succeed.
		builder.Property(d => d.InvoiceKey).HasMaxLength(InvoiceKey.HexLength).IsFixedLength();
		builder.HasIndex(d => d.InvoiceKey).IsUnique().HasFilter("invoice_key IS NOT NULL").HasDatabaseName(InvoiceKeyIndex);
		builder.HasOne<Document>().WithMany().HasForeignKey(d => d.DuplicateOf).OnDelete(DeleteBehavior.Restrict);

		builder.HasIndex(d => d.BatchId);

		// The review queue lists what waits for a person, oldest first.
		builder.HasIndex(d => d.CreatedAt, "ix_documents_review_queue").HasFilter("status = 'NeedsReview'").HasDatabaseName("ix_documents_review_queue");

		// The worker polls for queued work in arrival order; a partial index keeps that scan small.
		builder.HasIndex(d => d.CreatedAt, "ix_documents_created_at").HasFilter("status = 'Queued'");

		// Optimistic concurrency on Postgres' xmin system column: no extra column, and a worker whose lease ran
		// out cannot overwrite the outcome of the worker that took the document over.
		builder.Property<uint>(RowVersion).IsRowVersion();
	}

	/// <summary>Postgres' row version, which the claim statement returns along with the row.</summary>
	internal const string RowVersion = "xmin";

	/// <summary>The unique index on settled invoices, named so a violation of it can be told from others.</summary>
	internal const string InvoiceKeyIndex = "ix_documents_invoice_key";

	// Enums are stored by name, so the table reads on its own and reordering the enum cannot corrupt it.
	private const int EnumColumnLength = 32;
}

internal sealed class ExtractionConfiguration : IEntityTypeConfiguration<Extraction>
{
	// Written once and read whole with their extraction, never queried field by field: jsonb, not tables.
	private const string Json = "jsonb";

	public void Configure(EntityTypeBuilder<Extraction> builder)
	{
		builder.HasKey(e => e.Id);
		builder.HasOne<Document>().WithMany().HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);
		builder.HasIndex(e => e.DocumentId);

		// The daily budget sums today's cost.
		builder.HasIndex(e => e.CreatedAt);

		builder.Property(e => e.Model).HasMaxLength(Extraction.ModelMaxLength);
		builder.Property(e => e.Failure).HasMaxLength(Extraction.FailureMaxLength);

		// numeric(12,6): a call costs fractions of a cent, and the sum must be exact.
		builder.Property(e => e.CostUsd).HasPrecision(12, 6);

		builder.Property(e => e.Invoice).HasColumnType(Json).HasConversion(JsonColumn<ExtractedInvoice?>.Converter, JsonColumn<ExtractedInvoice?>.Comparer);
		builder.Property(e => e.Confidence).HasColumnType(Json).HasConversion(JsonColumn<IReadOnlyDictionary<string, FieldConfidence>>.Converter, JsonColumn<IReadOnlyDictionary<string, FieldConfidence>>.Comparer);
		builder.Property(e => e.Issues).HasColumnType(Json).HasConversion(JsonColumn<IReadOnlyList<ValidationIssue>>.Converter, JsonColumn<IReadOnlyList<ValidationIssue>>.Comparer);
	}
}

/// <summary>A value stored as JSON, compared by its JSON so change tracking sees edits inside it.</summary>
/// <typeparam name="T">The value's type.</typeparam>
internal static class JsonColumn<T>
{
	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		Converters = { new JsonStringEnumConverter() },
	};

	public static ValueConverter<T, string> Converter { get; } = new(
		value => JsonSerializer.Serialize(value, Options),
		json => JsonSerializer.Deserialize<T>(json, Options)!);

	public static ValueComparer<T> Comparer { get; } = new(
		(left, right) => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options),
		value => JsonSerializer.Serialize(value, Options).GetHashCode(StringComparison.Ordinal),
		value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!);
}
