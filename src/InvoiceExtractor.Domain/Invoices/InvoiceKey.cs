namespace InvoiceExtractor.Domain.Invoices;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using InvoiceExtractor.Domain.Tax;

/// <summary>
/// What makes two documents the same invoice for the books: the issuer's tax id, the invoice number and the
/// total. A photo and the PDF of one invoice are different files (different <c>ContentHash</c>) but the same key,
/// and only one of them may be settled; otherwise the export would count the purchase twice. Stored as the
/// SHA-256 of the normalised parts, so the column has a fixed length whatever the model read.
/// </summary>
public sealed record InvoiceKey
{
	/// <summary>SHA-256 is 32 bytes: 64 hex characters.</summary>
	public const int HexLength = 64;

	private InvoiceKey(string value) => Value = value;

	/// <summary>The lower-case hex digest.</summary>
	public string Value { get; }

	/// <summary>The key of an invoice, if it has every part.</summary>
	/// <param name="invoice">What was read or approved.</param>
	/// <returns>The key, or <see langword="null"/> when the tax id, the number or the total is missing.</returns>
	public static InvoiceKey? From(ExtractedInvoice invoice)
	{
		ArgumentNullException.ThrowIfNull(invoice);
		if (invoice is not { IssuerTaxId: { } taxId, Number: { } number, Total: { } total }
			|| string.IsNullOrWhiteSpace(taxId)
			|| string.IsNullOrWhiteSpace(number))
		{
			return null;
		}

		// "F-001037", "f 001037" and "F001037" are one number; the total with two decimals, as printed.
		var parts = string.Join('|', TaxId.Normalize(taxId), Normalize(number), total.ToString("0.00", CultureInfo.InvariantCulture));
		return new InvoiceKey(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(parts))));
	}

	/// <summary>Reads a stored key back.</summary>
	/// <param name="hex">64 hex characters, any case.</param>
	/// <returns>The key.</returns>
	/// <exception cref="ArgumentException">Not a SHA-256 hex digest.</exception>
	public static InvoiceKey FromHex(string hex)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(hex);
		if (hex.Length != HexLength || !hex.All(Uri.IsHexDigit))
		{
			throw new ArgumentException("An invoice key is 64 hexadecimal characters.", nameof(hex));
		}

		return new InvoiceKey(hex.ToLowerInvariant());
	}

	/// <inheritdoc />
	public override string ToString() => Value;

	private static string Normalize(string number) =>
		string.Concat(number.Where(char.IsLetterOrDigit).Select(static c => char.ToUpper(c, CultureInfo.InvariantCulture)));
}
