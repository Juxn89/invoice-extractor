namespace InvoiceExtractor.Domain.Invoices;

/// <summary>One line item as printed on the document.</summary>
/// <param name="Description">What was sold.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="UnitPrice">Price per unit.</param>
/// <param name="LineTotal">The line's printed total, which should equal quantity × unit price.</param>
public sealed record InvoiceLine(string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal);
