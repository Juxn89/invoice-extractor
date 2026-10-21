namespace InvoiceExtractor.Infrastructure.Files;

using System.ComponentModel.DataAnnotations;
using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Domain.Documents;
using Microsoft.Extensions.Options;

/// <summary>Where uploads are kept, bound from the <c>FileStore</c> section.</summary>
public sealed class FileStoreOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "FileStore";

	/// <summary>The folder; a relative path is resolved against the app's working directory.</summary>
	[Required]
	public string RootPath { get; set; } = string.Empty;
}

/// <summary>
/// Files on local disk, named by their hash and sharded by its first two characters so no folder grows to
/// millions of entries. Nothing outside the root is reachable: the only name component is a validated hex
/// hash, never the user's file name.
/// </summary>
/// <param name="options">The root folder.</param>
internal sealed class LocalFileStore(IOptions<FileStoreOptions> options) : IFileStore
{
	private const int ShardLength = 2;

	public async Task SaveAsync(ContentHash hash, DocumentFormat format, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
	{
		var path = PathFor(hash, format);
		if (File.Exists(path))
		{
			return;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		// Written under a temporary name and moved into place, so a reader never sees half a file.
		var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
		await File.WriteAllBytesAsync(temporary, content.ToArray(), cancellationToken).ConfigureAwait(false);
		try
		{
			File.Move(temporary, path, overwrite: false);
		}
		catch (IOException) when (File.Exists(path))
		{
			// Another request stored the same bytes first; the content is identical by construction.
			File.Delete(temporary);
		}
	}

	public async Task<byte[]> ReadAsync(ContentHash hash, DocumentFormat format, CancellationToken cancellationToken) =>
		await File.ReadAllBytesAsync(PathFor(hash, format), cancellationToken).ConfigureAwait(false);

	private string PathFor(ContentHash hash, DocumentFormat format)
	{
		ArgumentNullException.ThrowIfNull(hash);
		var extension = format switch
		{
			DocumentFormat.Pdf => ".pdf",
			DocumentFormat.Jpeg => ".jpg",
			DocumentFormat.Png => ".png",
			_ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format."),
		};

		var root = Path.GetFullPath(options.Value.RootPath);
		return Path.Combine(root, hash.Value[..ShardLength], hash.Value + extension);
	}
}
