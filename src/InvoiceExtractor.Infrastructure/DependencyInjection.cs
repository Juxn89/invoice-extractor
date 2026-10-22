namespace InvoiceExtractor.Infrastructure;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Features.Dashboard;
using InvoiceExtractor.Application.Features.Documents;
using InvoiceExtractor.Application.Features.Export;
using InvoiceExtractor.Application.Features.Processing;
using InvoiceExtractor.Application.Features.Review;
using InvoiceExtractor.Application.Features.Samples;
using InvoiceExtractor.Application.Features.Upload;
using InvoiceExtractor.Infrastructure.Ai;
using InvoiceExtractor.Infrastructure.Documents;
using InvoiceExtractor.Infrastructure.Export;
using InvoiceExtractor.Infrastructure.Files;
using InvoiceExtractor.Infrastructure.Persistence;
using InvoiceExtractor.Infrastructure.Processing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>Wires the adapters and the use cases.</summary>
public static class DependencyInjection
{
	/// <summary>Connection string name. Aspire injects it as <c>ConnectionStrings__invoicesdb</c>.</summary>
	public const string DatabaseConnectionName = "invoicesdb";

	/// <summary>Registers everything the API needs.</summary>
	/// <param name="services">The services.</param>
	/// <param name="configuration">The configuration.</param>
	/// <returns>The services.</returns>
	/// <exception cref="InvalidOperationException">The connection string is missing.</exception>
	public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		var connectionString = configuration.GetConnectionString(DatabaseConnectionName)
			?? throw new InvalidOperationException(
				$"Missing connection string '{DatabaseConnectionName}'. Run through the AppHost, or set "
				+ $"ConnectionStrings__{DatabaseConnectionName}.");

		services.AddSingleton(TimeProvider.System);
		services.AddDbContext<InvoiceExtractorDbContext>(options => PersistenceSetup.Configure(options, connectionString));

		AddOptions<UploadOptions>(services, configuration, UploadOptions.SectionName);
		AddOptions<FileStoreOptions>(services, configuration, FileStoreOptions.SectionName);
		AddOptions<AnthropicOptions>(services, configuration, AnthropicOptions.SectionName);
		AddOptions<ProcessingOptions>(services, configuration, ProcessingOptions.SectionName);
		AddOptions<TaxOptions>(services, configuration, TaxOptions.SectionName);
		AddOptions<ReplayOptions>(services, configuration, ReplayOptions.SectionName);
		AddOptions<SamplesOptions>(services, configuration, SamplesOptions.SectionName);
		services.AddSingleton<TaxProfileCatalog>();

		services.AddScoped<IDocumentRepository, DocumentRepository>();
		services.AddScoped<IDocumentQueue, PgDocumentQueue>();
		services.AddSingleton<IFileStore, LocalFileStore>();
		services.AddSingleton<ISampleLibrary, SampleLibrary>();
		services.AddSingleton<IDocumentInspector, DocumentInspector>();
		services.AddSingleton<IDocumentPreprocessor, DocumentPreprocessor>();
		if (UsesReplay(configuration))
		{
			services.AddSingleton<IInvoiceExtractor, ReplayInvoiceExtractor>();
		}
		else
		{
			services.AddSingleton<IInvoiceExtractor, ClaudeInvoiceExtractor>();
		}

		services.AddSingleton<IAccountingExport, ExcelExport>();
		services.AddSingleton<IAccountingExport, CsvExport>();

		services.AddScoped<UploadBatch>();
		services.AddScoped<GetDocument>();
		services.AddScoped<GetBatch>();
		services.AddScoped<ListBatches>();
		services.AddScoped<GetReviewQueue>();
		services.AddScoped<GetDocumentReview>();
		services.AddScoped<GetDocumentFile>();
		services.AddScoped<ApproveDocument>();
		services.AddScoped<ExportInvoices>();
		services.AddScoped<GetDashboard>();
		services.AddScoped<GetSample>();
		services.AddSingleton<ExtractionPipeline>();
		services.AddScoped<ProcessDocument>();

		services.AddSingleton<ExtractionWorker>();
		services.AddHostedService(static provider => provider.GetRequiredService<ExtractionWorker>());

		return services;
	}

	/// <summary>Whether recorded answers replace the model (end-to-end tests only; see <see cref="ReplayOptions"/>).</summary>
	/// <param name="configuration">The configuration.</param>
	/// <returns><see langword="true"/> when a replay file is configured.</returns>
	public static bool UsesReplay(IConfiguration configuration) =>
		!string.IsNullOrWhiteSpace(configuration?.GetSection(ReplayOptions.SectionName)[nameof(ReplayOptions.File)]);

	/// <summary>Bound, validated with data annotations, and checked at startup rather than on first use.</summary>
	private static OptionsBuilder<TOptions> AddOptions<TOptions>(IServiceCollection services, IConfiguration configuration, string section)
		where TOptions : class =>
		services.AddOptions<TOptions>()
			.Bind(configuration.GetSection(section))
			.ValidateDataAnnotations()
			.ValidateOnStart();
}
