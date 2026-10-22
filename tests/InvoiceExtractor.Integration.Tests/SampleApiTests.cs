namespace InvoiceExtractor.Integration.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InvoiceExtractor.Application.Features.Samples;

[Collection(PostgresCollection.Name)]
public sealed class SampleApiTests(PostgresFixture fixture) : IDisposable
{
	private readonly HttpClient _client = fixture.Api.CreateClient();

	public void Dispose() => _client.Dispose();

	[Fact]
	public async Task The_examples_are_listed_from_the_manifest_with_their_country_and_kind()
	{
		var samples = await _client.GetFromJsonAsync<List<SampleResponse>>("/v1/samples", new JsonSerializerOptions(JsonSerializerDefaults.Web));

		Assert.Equal(12, samples!.Count);
		Assert.Equal(new SampleResponse("sample-01.pdf", "NI", "DigitalPdf", "es"), samples[0]);
		Assert.Equal(["MX", "NI", "XX"], samples.Select(static s => s.Country).Distinct().Order());
	}

	[Fact]
	public async Task An_example_is_served_as_the_file_it_is()
	{
		using var response = await _client.GetAsync(new Uri("/v1/samples/sample-03.jpg", UriKind.Relative));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
		Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
		Assert.Equal(Samples.Photo, await response.Content.ReadAsByteArrayAsync());
	}

	[Theory]
	[InlineData("manifest.json")]
	[InlineData("..%2Fmanifest.json")]
	[InlineData("sample-99.pdf")]
	public async Task Only_a_file_the_manifest_names_is_ever_served(string file)
	{
		using var response = await _client.GetAsync(new Uri($"/v1/samples/{file}", UriKind.Relative));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
		Assert.Equal(SampleErrors.NotFound, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
	}
}
