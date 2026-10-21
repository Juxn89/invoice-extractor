using System.Text.Json.Serialization;
using InvoiceExtractor.Api.Endpoints;
using InvoiceExtractor.Application.Features.Upload;
using InvoiceExtractor.Infrastructure;
using InvoiceExtractor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
	options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddInfrastructure(builder.Configuration);

// A batch is up to MaxFilesPerBatch files of MaxFileBytes each; the framework's form limits (128 MB body,
// and Kestrel's 30 MB request) must not cut it short, nor let anything larger through.
var upload = builder.Configuration.GetSection(UploadOptions.SectionName).Get<UploadOptions>() ?? new UploadOptions();
var maxBatchBytes = BatchEndpoints.MaxRequestBytes(upload);
builder.Services.Configure<FormOptions>(form =>
{
	form.MultipartBodyLengthLimit = maxBatchBytes;
	form.ValueCountLimit = upload.MaxFilesPerBatch + BatchEndpoints.FormFieldAllowance;
});

var app = builder.Build();

// A malformed request (bad multipart, wrong content type) is the client's error, not a 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
	StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
		? badRequest.StatusCode
		: StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
	await DatabaseMigrator.MigrateAsync(app.Services);
}

app.MapDefaultEndpoints();
app.MapBatchEndpoints(maxBatchBytes);
app.MapDocumentEndpoints();
app.MapExportEndpoints();

// Fails fast on bad configuration even before the first request touches the options.
_ = app.Services.GetRequiredService<IOptions<UploadOptions>>().Value;

await app.RunAsync();
