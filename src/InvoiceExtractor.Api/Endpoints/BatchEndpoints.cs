namespace InvoiceExtractor.Api.Endpoints;

using InvoiceExtractor.Application.Features.Documents;
using InvoiceExtractor.Application.Features.Upload;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

/// <summary>Uploads and batch status. Mapping only: parse, call the use case, translate the result.</summary>
internal static class BatchEndpoints
{
	/// <summary>Form fields other than files that a request may carry before it is refused.</summary>
	internal const int FormFieldAllowance = 8;

	/// <summary>Multipart framing (boundaries and part headers) per file, on top of the file itself.</summary>
	private const int MultipartOverheadBytesPerFile = 16 * 1024;

	/// <summary>The largest body a valid batch can have.</summary>
	/// <param name="options">The upload limits.</param>
	/// <returns>The byte count.</returns>
	public static long MaxRequestBytes(UploadOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return (long)options.MaxFilesPerBatch * (options.MaxFileBytes + MultipartOverheadBytesPerFile);
	}

	/// <summary>Maps the routes.</summary>
	/// <param name="app">The app.</param>
	/// <param name="maxRequestBytes">The body limit, enforced before anything is buffered.</param>
	/// <returns>The group.</returns>
	public static RouteGroupBuilder MapBatchEndpoints(this IEndpointRouteBuilder app, long maxRequestBytes)
	{
		var batches = app.MapGroup("/v1/batches").WithTags("Batches");

		batches.MapPost("/", UploadAsync)
			// The API has no cookie or other ambient credential for a cross-site form to ride on, so an
			// antiforgery token would protect nothing. It runs locally in this MVP (plan §9).
			.DisableAntiforgery()
			.WithMetadata(new RequestSizeLimitAttribute(maxRequestBytes));
		batches.MapGet("/{id:guid}", GetAsync);

		return batches;
	}

	private static async Task<IResult> UploadAsync(
		IFormFileCollection files,
		[FromForm] string? country,
		UploadBatch useCase,
		IOptions<UploadOptions> options,
		CancellationToken cancellationToken)
	{
		var limits = options.Value;
		var batchTooLarge = files.Count > limits.MaxFilesPerBatch;
		var uploads = new List<UploadedFile>(files.Count);
		foreach (var file in files)
		{
			// Nothing is read that the use case is going to refuse: an oversized file, or any file of a batch
			// with too many. It still gets its size, so the refusal says which file and why.
			if (batchTooLarge || file.Length > limits.MaxFileBytes)
			{
				uploads.Add(new UploadedFile(file.FileName, file.Length, ReadOnlyMemory<byte>.Empty));
				continue;
			}

			using var buffer = new MemoryStream((int)file.Length);
			await file.CopyToAsync(buffer, cancellationToken);
			uploads.Add(new UploadedFile(file.FileName, file.Length, buffer.ToArray()));
		}

		var result = await useCase.HandleAsync(uploads, country, cancellationToken);
		return result.ToHttp(response => response.BatchId is { } batchId
			? Results.Accepted($"/v1/batches/{batchId}", response)
			: Results.Ok(response));
	}

	private static async Task<IResult> GetAsync(Guid id, GetBatch useCase, CancellationToken cancellationToken) =>
		(await useCase.HandleAsync(id, cancellationToken)).ToHttp(Results.Ok);
}

/// <summary>One document's status.</summary>
internal static class DocumentEndpoints
{
	/// <summary>Maps the routes.</summary>
	/// <param name="app">The app.</param>
	/// <returns>The group.</returns>
	public static RouteGroupBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
	{
		var documents = app.MapGroup("/v1/documents").WithTags("Documents");
		documents.MapGet("/{id:guid}", GetAsync);
		return documents;
	}

	private static async Task<IResult> GetAsync(Guid id, GetDocument useCase, CancellationToken cancellationToken) =>
		(await useCase.HandleAsync(id, cancellationToken)).ToHttp(Results.Ok);
}
