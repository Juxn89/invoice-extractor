namespace InvoiceExtractor.Domain.Documents;

using System.Security.Cryptography;

/// <summary>
/// SHA-256 of a file's bytes, lower-case hex. It is the upload's idempotency key: the same file uploaded twice
/// resolves to the document already there, without a second extraction or a second model call.
/// </summary>
public sealed record ContentHash
{
	/// <summary>SHA-256 is 32 bytes: 64 hex characters.</summary>
	public const int HexLength = 64;

	private ContentHash(string value) => Value = value;

	/// <summary>The lower-case hex digest.</summary>
	public string Value { get; }

	/// <summary>Hashes a file.</summary>
	/// <param name="content">The bytes.</param>
	/// <returns>The hash.</returns>
	public static ContentHash Compute(ReadOnlySpan<byte> content) =>
		new(Convert.ToHexStringLower(SHA256.HashData(content)));

	/// <summary>Reads a stored hash back.</summary>
	/// <param name="hex">64 hex characters, any case.</param>
	/// <returns>The hash.</returns>
	/// <exception cref="ArgumentException">Not a SHA-256 hex digest.</exception>
	public static ContentHash FromHex(string hex)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(hex);
		if (hex.Length != HexLength || !hex.All(Uri.IsHexDigit))
		{
			throw new ArgumentException("A content hash is 64 hexadecimal characters.", nameof(hex));
		}

		return new ContentHash(hex.ToLowerInvariant());
	}

	/// <inheritdoc />
	public override string ToString() => Value;
}
