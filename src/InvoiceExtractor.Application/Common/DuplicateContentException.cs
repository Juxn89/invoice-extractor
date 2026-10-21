namespace InvoiceExtractor.Application.Common;

/// <summary>
/// Two requests uploaded the same file at the same moment and the other one stored it first. The unique
/// index on the content hash is what catches it; the upload retries and reports the file as a duplicate.
/// </summary>
public sealed class DuplicateContentException : Exception
{
	/// <summary>Creates the exception.</summary>
	public DuplicateContentException()
	{
	}

	/// <summary>Creates the exception.</summary>
	/// <param name="message">The message.</param>
	public DuplicateContentException(string message)
		: base(message)
	{
	}

	/// <summary>Creates the exception.</summary>
	/// <param name="message">The message.</param>
	/// <param name="innerException">The database error.</param>
	public DuplicateContentException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
