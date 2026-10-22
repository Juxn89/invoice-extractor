namespace InvoiceExtractor.Application.Common;

/// <summary>
/// Another document settled the same invoice (same <c>InvoiceKey</c>) between this one's check and its save. The
/// unique index on the key is what catches it; the use case looks the original up and records the duplicate.
/// </summary>
public sealed class DuplicateInvoiceException : Exception
{
	/// <summary>Creates the exception.</summary>
	public DuplicateInvoiceException()
	{
	}

	/// <summary>Creates the exception.</summary>
	/// <param name="message">The message.</param>
	public DuplicateInvoiceException(string message)
		: base(message)
	{
	}

	/// <summary>Creates the exception.</summary>
	/// <param name="message">The message.</param>
	/// <param name="innerException">The database error.</param>
	public DuplicateInvoiceException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
