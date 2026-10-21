var builder = DistributedApplication.CreateBuilder(args);

// Pinned so local runs and the integration tests (Testcontainers) use the same Postgres.
//
// The volume goes on /var/lib/postgresql, not the data folder: the Postgres 18 images moved PGDATA to a
// versioned subdirectory and refuse to start with a volume at the old path (found in docs-assistant).
var postgres = builder.AddPostgres("postgres")
	.WithImage("postgres")
	.WithImageTag("18.6-alpine")
	.WithVolume("invoice-extractor-pgdata", "/var/lib/postgresql")
	.WithLifetime(ContainerLifetime.Persistent);

var database = postgres.AddDatabase("invoicesdb");

builder.AddProject<Projects.InvoiceExtractor_Api>("api")
	.WithReference(database)
	.WaitFor(database)
	.WithHttpHealthCheck("/health");

await builder.Build().RunAsync();
