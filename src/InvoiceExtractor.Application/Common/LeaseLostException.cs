namespace InvoiceExtractor.Application.Common;

/// <summary>
/// A worker tried to save a document it no longer holds: its lease ran out and another worker claimed it. The
/// late result is dropped (the other worker's stands), so a document never ends with two outcomes.
/// </summary>
public sealed class LeaseLostException : Exception
{
	/// <summary>Creates the exception.</summary>
	public LeaseLostException()
	{
	}

	/// <summary>Creates the exception.</summary>
	/// <param name="message">The message.</param>
	public LeaseLostException(string message)
		: base(message)
	{
	}

	/// <summary>Creates the exception.</summary>
	/// <param name="message">The message.</param>
	/// <param name="innerException">The concurrency failure.</param>
	public LeaseLostException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
