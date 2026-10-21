namespace InvoiceExtractor.Integration.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceExtractor.Application.Features.Documents;
using InvoiceExtractor.Application.Features.Upload;
using InvoiceExtractor.Domain.Documents;

[Collection(PostgresCollection.Name)]
public sealed class UploadApiTests(PostgresFixture fixture) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

	private readonly HttpClient _client = fixture.Api.CreateClient();

	public Task InitializeAsync() => fixture.ResetAsync();

	public Task DisposeAsync()
	{
		_client.Dispose();
		return Task.CompletedTask;
	}

	private async Task<HttpResponseMessage> UploadAsync(params (string Name, byte[] Content)[] files)
	{
		using var form = new MultipartFormDataContent();
		foreach (var (name, content) in files)
		{
			var part = new ByteArrayContent(content);
			part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
			form.Add(part, "files", name);
		}

		return await _client.PostAsync(new Uri("/v1/batches", UriKind.Relative), form);
	}

	[Fact]
	public async Task A_pdf_a_scan_and_a_photo_are_queued_with_what_was_learned_about_each()
	{
		using var response = await UploadAsync(("factura.pdf", Samples.DigitalPdf), ("escaneo.png", Samples.Scan), ("foto.jpg", Samples.Photo));

		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
		var upload = (await response.Content.ReadFromJsonAsync<UploadResponse>(Json))!;
		Assert.All(upload.Files, static f => Assert.Equal(UploadOutcome.Queued, f.Outcome));
		Assert.Equal([DocumentFormat.Pdf, DocumentFormat.Png, DocumentFormat.Jpeg], upload.Files.Select(static f => f.Document!.Format));
		Assert.Equal([true, false, false], upload.Files.Select(static f => f.Document!.HasTextLayer));
		Assert.Equal($"/v1/batches/{upload.BatchId}", response.Headers.Location?.OriginalString);

		var batch = await _client.GetFromJsonAsync<BatchResponse>($"/v1/batches/{upload.BatchId}", Json);
		Assert.Equal(3, batch!.Documents.Count);
		Assert.All(batch.Documents, static d => Assert.Equal(DocumentStatus.Queued, d.Status));

		var stored = Directory.EnumerateFiles(fixture.StorageRoot, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToHashSet();
		Assert.Contains(ContentHash.Compute(Samples.Photo).Value + ".jpg", stored);
	}

	[Fact]
	public async Task The_same_file_uploaded_again_is_a_duplicate_with_no_new_document()
	{
		using var first = await UploadAsync(("factura.pdf", Samples.DigitalPdf));
		var original = (await first.Content.ReadFromJsonAsync<UploadResponse>(Json))!.Files[0].Document!;

		using var second = await UploadAsync(("copia.pdf", Samples.DigitalPdf));

		Assert.Equal(HttpStatusCode.OK, second.StatusCode);
		var again = Assert.Single((await second.Content.ReadFromJsonAsync<UploadResponse>(Json))!.Files);
		Assert.Equal(UploadOutcome.Duplicate, again.Outcome);
		Assert.Equal(original.Id, again.Document!.Id);

		await using var db = fixture.CreateContext();
		Assert.Equal(1, db.Documents.Count());
	}

	[Fact]
	public async Task A_file_that_is_not_an_invoice_format_is_rejected_by_its_bytes_whatever_its_name()
	{
		using var response = await UploadAsync(("factura.pdf", "MZ\u0090\u0000 not a pdf"u8.ToArray()));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var file = Assert.Single((await response.Content.ReadFromJsonAsync<UploadResponse>(Json))!.Files);
		Assert.Equal(UploadOutcome.Rejected, file.Outcome);
		Assert.Equal(UploadErrors.UnsupportedFormat, file.Error!.Code);
	}

	[Fact]
	public async Task Too_many_files_is_a_problem_with_a_stable_code()
	{
		var files = Enumerable.Range(0, new UploadOptions().MaxFilesPerBatch + 1).Select(static i => ($"{i}.pdf", Samples.DigitalPdf)).ToArray();

		using var response = await UploadAsync(files);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		Assert.Equal(UploadErrors.TooManyFiles, problem.RootElement.GetProperty("code").GetString());
	}

	[Fact]
	public async Task An_unknown_document_is_a_404()
	{
		using var response = await _client.GetAsync(new Uri($"/v1/documents/{Guid.NewGuid()}", UriKind.Relative));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task A_document_can_be_read_by_its_id()
	{
		using var upload = await UploadAsync(("foto.jpg", Samples.Photo));
		var id = (await upload.Content.ReadFromJsonAsync<UploadResponse>(Json))!.Files[0].Document!.Id;

		var document = await _client.GetFromJsonAsync<DocumentResponse>($"/v1/documents/{id}", Json);

		Assert.Equal("foto.jpg", document!.FileName);
		Assert.Equal(Samples.Photo.Length, document.SizeBytes);
	}
}
