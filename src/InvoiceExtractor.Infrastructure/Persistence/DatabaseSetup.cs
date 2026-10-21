namespace InvoiceExtractor.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Applies migrations.</summary>
public static class DatabaseMigrator
{
	/// <summary>
	/// Applies pending migrations. Called at startup in Development; a deployed environment would run it as a
	/// job, never on every start, where several replicas would race.
	/// </summary>
	/// <param name="services">The app's services.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>A task.</returns>
	public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
	{
		await using var scope = services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<InvoiceExtractorDbContext>();
		await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
	}
}

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>, which never opens a connection: the connection string is a
/// placeholder, not a secret.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<InvoiceExtractorDbContext>
{
	private const string PlaceholderConnectionString = "Host=localhost;Database=invoice_extractor_design";

	public InvoiceExtractorDbContext CreateDbContext(string[] args)
	{
		var options = new DbContextOptionsBuilder<InvoiceExtractorDbContext>();
		PersistenceSetup.Configure(options, PlaceholderConnectionString);
		return new InvoiceExtractorDbContext(options.Options);
	}
}
