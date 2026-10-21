namespace InvoiceExtractor.Domain.Documents;

/// <summary>
/// A batch's id. Version 7 GUIDs are time-ordered, so they index well as primary keys, and a typed id can
/// never be passed where another entity's is expected.
/// </summary>
/// <param name="Value">The GUID.</param>
public readonly record struct BatchId(Guid Value)
{
	/// <summary>A new time-ordered id.</summary>
	/// <returns>The id.</returns>
	public static BatchId New() => new(Guid.CreateVersion7());

	/// <inheritdoc />
	public override string ToString() => Value.ToString();
}

/// <summary>A document's id. See <see cref="BatchId"/>.</summary>
/// <param name="Value">The GUID.</param>
public readonly record struct DocumentId(Guid Value)
{
	/// <summary>A new time-ordered id.</summary>
	/// <returns>The id.</returns>
	public static DocumentId New() => new(Guid.CreateVersion7());

	/// <inheritdoc />
	public override string ToString() => Value.ToString();
}

/// <summary>An extraction's id. See <see cref="BatchId"/>.</summary>
/// <param name="Value">The GUID.</param>
public readonly record struct ExtractionId(Guid Value)
{
	/// <summary>A new time-ordered id.</summary>
	/// <returns>The id.</returns>
	public static ExtractionId New() => new(Guid.CreateVersion7());

	/// <inheritdoc />
	public override string ToString() => Value.ToString();
}
