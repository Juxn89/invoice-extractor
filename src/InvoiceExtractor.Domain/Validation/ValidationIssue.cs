namespace InvoiceExtractor.Domain.Validation;

/// <summary>One failed rule, pointing at the field a reviewer should look at.</summary>
/// <param name="Field">The field path, from <see cref="InvoiceFields"/>.</param>
/// <param name="Code">Which rule failed.</param>
/// <param name="Detail">
/// The numbers behind the failure, written for a person and for the retry prompt, e.g. "Lines add up to
/// 1150.00, subtotal reads 1510.00."
/// </param>
public sealed record ValidationIssue(string Field, IssueCode Code, string Detail);

/// <summary>The outcome of validating one extraction.</summary>
/// <param name="Issues">Every failed rule; empty when the extraction is consistent.</param>
public sealed record InvoiceValidation(IReadOnlyList<ValidationIssue> Issues)
{
	/// <summary>Whether every rule passed, so the document can be approved without review.</summary>
	public bool IsValid => Issues.Count == 0;
}
