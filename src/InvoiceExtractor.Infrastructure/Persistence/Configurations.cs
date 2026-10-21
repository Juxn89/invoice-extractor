namespace InvoiceExtractor.Infrastructure.Persistence;

using InvoiceExtractor.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
	public void Configure(EntityTypeBuilder<Batch> builder)
	{
		builder.HasKey(b => b.Id);
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

		// Idempotent upload: the same bytes are one document, whoever uploads them and however many times.
		builder.HasIndex(d => d.ContentHash).IsUnique();

		builder.HasIndex(d => d.BatchId);

		// The worker (day 5) polls for queued work in arrival order; a partial index keeps that scan small.
		builder.HasIndex(d => d.CreatedAt).HasFilter("status = 'Queued'");
	}

	// Enums are stored by name, so the table reads on its own and reordering the enum cannot corrupt it.
	private const int EnumColumnLength = 32;
}
