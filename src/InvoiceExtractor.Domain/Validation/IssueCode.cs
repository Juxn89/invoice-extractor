namespace InvoiceExtractor.Domain.Validation;

/// <summary>Why a field failed validation. Stable: the review queue and the evals key off these values.</summary>
public enum IssueCode
{
	/// <summary>A key field the model could not read.</summary>
	MissingField = 1,

	/// <summary>The line totals do not add up to the subtotal.</summary>
	LinesDoNotMatchSubtotal = 2,

	/// <summary>A line's quantity × unit price is not its printed total.</summary>
	LineArithmetic = 3,

	/// <summary>Subtotal plus tax is not the total.</summary>
	TotalMismatch = 4,

	/// <summary>Tax divided by subtotal is not a rate the country uses.</summary>
	ImplausibleTaxRate = 5,

	/// <summary>The issuer's tax id does not have the country's format.</summary>
	InvalidTaxId = 6,

	/// <summary>The issue date is after today.</summary>
	FutureDate = 7,

	/// <summary>The issue date is older than the accepted window.</summary>
	DateTooOld = 8,
}
