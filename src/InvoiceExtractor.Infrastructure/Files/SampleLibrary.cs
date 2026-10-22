namespace InvoiceExtractor.Infrastructure.Files;

using System.Text.Json;
using InvoiceExtractor.Application.Features.Samples;
using Microsoft.Extensions.Options;

/// <summary>Where the example documents are, bound from the <c>Samples</c> section.</summary>
public sealed class SamplesOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Samples";

	/// <summary>
	/// The folder with the examples and their <c>manifest.json</c> (the repo's <c>evals/samples</c>). Unset, the app
	/// offers no examples.
	/// </summary>
	public string? Path { get; set; }
}

/// <summary>
/// The examples listed in <c>manifest.json</c>, the file the dataset generator writes next to them. Only a file
/// the manifest names is ever read, so a request cannot reach anything else in the folder or outside it.
/// </summary>
internal sealed class SampleLibrary(IOptions<SamplesOptions> options) : ISampleLibrary
{
	private const string ManifestName = "manifest.json";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		[".pdf"] = "application/pdf",
		[".jpg"] = "image/jpeg",
		[".jpeg"] = "image/jpeg",
		[".png"] = "image/png",
	};

	public async Task<IReadOnlyList<SampleResponse>> ListAsync(CancellationToken cancellationToken)
	{
		if (Folder() is not { } folder)
		{
			return [];
		}

		await using var stream = File.OpenRead(Path.Combine(folder, ManifestName));
		var manifest = await JsonSerializer.DeserializeAsync<Manifest>(stream, Json, cancellationToken).ConfigureAwait(false);
		return [.. (manifest?.Documents ?? []).Select(static d => new SampleResponse(d.File, d.Country, d.Kind, d.Language))];
	}

	public async Task<SampleFile?> ReadAsync(string file, CancellationToken cancellationToken)
	{
		var sample = (await ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(s => string.Equals(s.File, file, StringComparison.Ordinal));
		if (sample is null || Folder() is not { } folder || !ContentTypes.TryGetValue(Path.GetExtension(sample.File), out var contentType))
		{
			return null;
		}

		var path = Path.Combine(folder, Path.GetFileName(sample.File));
		if (!File.Exists(path))
		{
			return null;
		}

		var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
		return new SampleFile(content, contentType, sample.File);
	}

	private string? Folder() =>
		options.Value.Path is { Length: > 0 } path && File.Exists(Path.Combine(path, ManifestName)) ? Path.GetFullPath(path) : null;

	private sealed record Manifest(IReadOnlyList<ManifestEntry>? Documents);

	private sealed record ManifestEntry(string File, string Country, string Kind, string Language);
}
