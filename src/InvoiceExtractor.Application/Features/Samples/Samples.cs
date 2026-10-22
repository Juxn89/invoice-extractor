namespace InvoiceExtractor.Application.Features.Samples;

using InvoiceExtractor.Application.Common;

/// <summary>One example document the app can be tried with (<c>evals/samples</c>).</summary>
/// <param name="File">Its file name, which is also how it is fetched.</param>
/// <param name="Country">The tax profile it was made for.</param>
/// <param name="Kind">What it looks like: <c>DigitalPdf</c>, <c>Scan</c>, <c>Photo</c> or <c>ThermalReceipt</c>.</param>
/// <param name="Language">The language it is printed in.</param>
public sealed record SampleResponse(string File, string Country, string Kind, string Language);

/// <summary>An example's bytes.</summary>
/// <param name="Content">The file.</param>
/// <param name="ContentType">Its media type.</param>
/// <param name="FileName">Its name.</param>
public sealed record SampleFile(byte[] Content, string ContentType, string FileName);

/// <summary>The example documents shipped with the app, if any are configured.</summary>
public interface ISampleLibrary
{
	/// <summary>Every example, in the manifest's order.</summary>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The examples; empty when none are configured.</returns>
	Task<IReadOnlyList<SampleResponse>> ListAsync(CancellationToken cancellationToken);

	/// <summary>One example's bytes.</summary>
	/// <param name="file">A file name from <see cref="ListAsync"/>; nothing else is ever read.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The file, or <see langword="null"/> when it is not an example.</returns>
	Task<SampleFile?> ReadAsync(string file, CancellationToken cancellationToken);
}

/// <summary>The examples' error codes.</summary>
public static class SampleErrors
{
	/// <summary>No example has that name.</summary>
	public const string NotFound = "sample.not_found";
}

/// <summary>
/// One example, fetched by the web app to upload it like any file the user picked: trying the app takes the
/// same path as using it, rejections and duplicates included.
/// </summary>
/// <param name="samples">The examples.</param>
public sealed class GetSample(ISampleLibrary samples)
{
	/// <summary>Reads it.</summary>
	/// <param name="file">The example's file name.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The file, or not found.</returns>
	public async Task<Result<SampleFile>> HandleAsync(string file, CancellationToken cancellationToken) =>
		await samples.ReadAsync(file, cancellationToken).ConfigureAwait(false) is { } sample
			? sample
			: Error.NotFound(SampleErrors.NotFound, $"There is no example called '{file}'.");
}
