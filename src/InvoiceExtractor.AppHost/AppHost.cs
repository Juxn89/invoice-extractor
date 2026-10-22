const int WebPort = 3010;

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

var api = builder.AddProject<Projects.InvoiceExtractor_Api>("api")
	.WithReference(database)
	.WaitFor(database)
	.WithHttpHealthCheck("/health");

// The workers read documents with Claude only when the developer's environment has a key: it is passed on as a
// secret parameter (masked in the dashboard), never written to a file in the repo. Without it the API still
// runs, and uploads wait in the queue.
if (Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 0 } key)
{
	api.WithEnvironment("Anthropic__ApiKey", builder.AddParameter("anthropic-api-key", key, secret: true));
}

// The web app on http://localhost:3010, with /api forwarded to the API wherever Aspire runs it. Aspire's proxy holds
// 3010 and hands Next its own port in PORT, which "dev:aspire" ("next dev" without --port) honours; "pnpm dev" on
// its own still serves 3010 directly. AddNextJsApp would do the same but is marked experimental (ASPIREJAVASCRIPT001).
builder.AddJavaScriptApp("web", "../../web", "dev:aspire")
	.WithPnpm(install: false)
	.WithHttpEndpoint(port: WebPort, env: "PORT")
	.WithEnvironment("API_URL", api.GetEndpoint("http"))
	.WithEnvironment("NEXT_TELEMETRY_DISABLED", "1")
	.WithExternalHttpEndpoints()
	.WaitFor(api);

await builder.Build().RunAsync();
