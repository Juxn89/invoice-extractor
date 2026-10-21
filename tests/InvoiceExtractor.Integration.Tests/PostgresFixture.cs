namespace InvoiceExtractor.Integration.Tests;

using InvoiceExtractor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

/// <summary>
/// One real Postgres per test run, migrated once, plus the API on top of it. Same image as the AppHost, so
/// the tests run against the database the app runs against.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
	public const string Image = "postgres:18.6-alpine";

	private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

	private WebApplicationFactory<Program>? _api;

	public string ConnectionString => _container.GetConnectionString();

	/// <summary>Where the API under test stores uploads; deleted at the end of the run.</summary>
	public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "invoice-extractor-tests", Guid.NewGuid().ToString("N"));

	public WebApplicationFactory<Program> Api => _api ?? throw new InvalidOperationException("Not initialised.");

	public async Task InitializeAsync()
	{
		await _container.StartAsync();
		await using (var db = CreateContext())
		{
			await db.Database.MigrateAsync();
		}

		_api = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
			.UseEnvironment("Testing")
			.UseSetting("ConnectionStrings:invoicesdb", ConnectionString)
			.UseSetting("FileStore:RootPath", StorageRoot)
			// The API tests exercise the upload side; the worker has its own tests with a fake model.
			.UseSetting("Processing:Enabled", "false"));
	}

	public async Task DisposeAsync()
	{
		if (_api is not null)
		{
			await _api.DisposeAsync();
		}

		await _container.DisposeAsync();
		if (Directory.Exists(StorageRoot))
		{
			Directory.Delete(StorageRoot, recursive: true);
		}
	}

	public InvoiceExtractorDbContext CreateContext()
	{
		var options = new DbContextOptionsBuilder<InvoiceExtractorDbContext>();
		PersistenceSetup.Configure(options, ConnectionString);
		return new InvoiceExtractorDbContext(options.Options);
	}

	/// <summary>Empties the tables, so each test starts without the previous one's documents.</summary>
	/// <returns>A task.</returns>
	public async Task ResetAsync()
	{
		await using var db = CreateContext();
		await db.Database.ExecuteSqlRawAsync("TRUNCATE extractions, documents, batches");
	}
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
	public const string Name = "postgres";
}

/// <summary>The generated samples, copied next to the tests.</summary>
internal static class Samples
{
	public static byte[] DigitalPdf => Read("sample-01.pdf");

	public static byte[] Scan => Read("sample-02.png");

	public static byte[] Photo => Read("sample-03.jpg");

	private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", name));
}
