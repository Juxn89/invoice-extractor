namespace InvoiceExtractor.Evals.Dataset;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>How the manifest is written and read. Amounts stay <c>decimal</c> end to end: System.Text.Json
/// writes a decimal's digits as they are and reads them back exactly.</summary>
internal static class DatasetJson
{
	/// <summary>The manifest's file name inside a dataset folder.</summary>
	public const string ManifestFileName = "manifest.json";

	/// <summary>Shared options: camelCase, enums as names, indented for reviewable diffs.</summary>
	public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
	};

	/// <summary>Reads a dataset's manifest.</summary>
	/// <param name="folder">The dataset folder.</param>
	/// <returns>The manifest.</returns>
	/// <exception cref="InvalidDataException">The file is empty or not a manifest.</exception>
	public static DatasetManifest Read(string folder)
	{
		using var stream = File.OpenRead(Path.Combine(folder, ManifestFileName));
		return JsonSerializer.Deserialize<DatasetManifest>(stream, Options)
			?? throw new InvalidDataException($"{ManifestFileName} in {folder} is empty.");
	}

	/// <summary>Writes a dataset's manifest.</summary>
	/// <param name="folder">The dataset folder.</param>
	/// <param name="manifest">The manifest.</param>
	public static void Write(string folder, DatasetManifest manifest)
	{
		using var stream = File.Create(Path.Combine(folder, ManifestFileName));
		JsonSerializer.Serialize(stream, manifest, Options);
	}
}
