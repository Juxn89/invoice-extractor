namespace InvoiceExtractor.Infrastructure.Persistence;

using InvoiceExtractor.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>The database. Tables and columns are snake_case (see <see cref="PersistenceSetup"/>).</summary>
/// <param name="options">The options.</param>
public sealed class InvoiceExtractorDbContext(DbContextOptions<InvoiceExtractorDbContext> options) : DbContext(options)
{
	/// <summary>Uploads.</summary>
	public DbSet<Batch> Batches => Set<Batch>();

	/// <summary>Uploaded files.</summary>
	public DbSet<Document> Documents => Set<Document>();

	/// <summary>Model calls and what came of them.</summary>
	public DbSet<Extraction> Extractions => Set<Extraction>();

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ArgumentNullException.ThrowIfNull(modelBuilder);
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(InvoiceExtractorDbContext).Assembly);
	}

	/// <inheritdoc />
	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
	{
		ArgumentNullException.ThrowIfNull(configurationBuilder);
		configurationBuilder.Properties<BatchId>().HaveConversion<BatchIdConverter>();
		configurationBuilder.Properties<DocumentId>().HaveConversion<DocumentIdConverter>();
		configurationBuilder.Properties<ExtractionId>().HaveConversion<ExtractionIdConverter>();
		configurationBuilder.Properties<ContentHash>().HaveConversion<ContentHashConverter>();
	}
}

/// <summary>How every context is configured: the app's, the tests' and the design-time one.</summary>
internal static class PersistenceSetup
{
	public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
		options
			.UseNpgsql(connectionString)
			.UseSnakeCaseNamingConvention();
}

internal sealed class BatchIdConverter() : ValueConverter<BatchId, Guid>(id => id.Value, value => new BatchId(value));

internal sealed class DocumentIdConverter() : ValueConverter<DocumentId, Guid>(id => id.Value, value => new DocumentId(value));

internal sealed class ExtractionIdConverter() : ValueConverter<ExtractionId, Guid>(id => id.Value, value => new ExtractionId(value));

internal sealed class ContentHashConverter()
	: ValueConverter<ContentHash, string>(hash => hash.Value, value => ContentHash.FromHex(value));
