namespace InvoiceExtractor.Api.Endpoints;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Application.Features.Dashboard;
using InvoiceExtractor.Application.Features.Export;

/// <summary>The settled invoices as a file, and the numbers behind the dashboard.</summary>
internal static class ExportEndpoints
{
	/// <summary>An unknown <c>format</c> or <c>lang</c>.</summary>
	internal const string UnknownOption = "export.unknown_option";

	/// <summary>Maps the routes.</summary>
	/// <param name="app">The app.</param>
	/// <returns>The app.</returns>
	public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet("/v1/export", ExportAsync).WithTags("Export");
		app.MapGet("/v1/dashboard", DashboardAsync).WithTags("Export");
		return app;
	}

	// GET /v1/export?format=xlsx|csv&lang=es|en&batchId={guid}. Both options default (xlsx, es).
	private static async Task<IResult> ExportAsync(string? format, string? lang, Guid? batchId, ExportInvoices useCase, CancellationToken cancellationToken)
	{
		if (!Enum.TryParse<ExportFormat>(format ?? nameof(ExportFormat.Xlsx), ignoreCase: true, out var exportFormat)
			|| !Enum.IsDefined(exportFormat)
			|| !Enum.TryParse<ExportLanguage>(lang ?? nameof(ExportLanguage.Es), ignoreCase: true, out var language)
			|| !Enum.IsDefined(language))
		{
			return Error.Validation(UnknownOption, "Use format=xlsx or csv, and lang=es or en.").ToProblem();
		}

		return (await useCase.HandleAsync(exportFormat, language, batchId, cancellationToken))
			.ToHttp(file => Results.File(file.Content, file.ContentType, file.FileName));
	}

	private static async Task<IResult> DashboardAsync(GetDashboard useCase, CancellationToken cancellationToken) =>
		Results.Ok(await useCase.HandleAsync(cancellationToken));
}
