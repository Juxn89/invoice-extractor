namespace InvoiceExtractor.Api.Endpoints;

using InvoiceExtractor.Application.Features.Samples;

/// <summary>The example documents the web app offers as "try an example".</summary>
internal static class SampleEndpoints
{
	/// <summary>Maps the routes.</summary>
	/// <param name="app">The app.</param>
	/// <returns>The app.</returns>
	public static IEndpointRouteBuilder MapSampleEndpoints(this IEndpointRouteBuilder app)
	{
		var samples = app.MapGroup("/v1/samples").WithTags("Samples");
		samples.MapGet("/", ListAsync);
		samples.MapGet("/{file}", GetAsync);
		return app;
	}

	private static async Task<IResult> ListAsync(ISampleLibrary samples, CancellationToken cancellationToken) =>
		Results.Ok(await samples.ListAsync(cancellationToken));

	// Downloaded by the web app and uploaded like any picked file; nosniff, like every file the API serves.
	private static async Task<IResult> GetAsync(string file, GetSample useCase, HttpContext context, CancellationToken cancellationToken) =>
		(await useCase.HandleAsync(file, cancellationToken)).ToHttp(sample =>
		{
			context.Response.Headers.XContentTypeOptions = "nosniff";
			context.Response.Headers.CacheControl = "public, max-age=86400";
			return Results.File(sample.Content, sample.ContentType, sample.FileName);
		});
}
