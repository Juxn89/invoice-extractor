namespace InvoiceExtractor.Domain.Invoices;

/// <summary>How sure the model says it is of a field. A hint for the reviewer, never a reason to approve.</summary>
public enum FieldConfidence
{
	/// <summary>Hard to read, inferred or guessed.</summary>
	Low = 1,

	/// <summary>Read, with some doubt.</summary>
	Medium = 2,

	/// <summary>Printed clearly.</summary>
	High = 3,
}
