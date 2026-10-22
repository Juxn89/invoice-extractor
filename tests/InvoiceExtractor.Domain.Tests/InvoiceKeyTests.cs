namespace InvoiceExtractor.Domain.Tests;

using InvoiceExtractor.Domain.Documents;
using InvoiceExtractor.Domain.Invoices;

public sealed class InvoiceKeyTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

	private static ExtractedInvoice Invoice(string? taxId = "J0310000252297", string? number = "F-001037", decimal? total = 7517.61m) =>
		new("Distribuidora El Mirador, S.A.", taxId, "Escuela Rubén Darío", number, new DateOnly(2026, 6, 9), "NIO", [], 6537.05m, 980.56m, total);

	private static Document Processing()
	{
		var document = Document.Queue(BatchId.New(), "factura.pdf", ContentHash.Compute(Guid.NewGuid().ToByteArray()), DocumentFormat.Pdf, 1, true, 1024, Now);
		document.Claim(Now, Now.AddMinutes(5));
		return document;
	}

	[Fact]
	public void The_same_invoice_printed_or_read_differently_has_one_key()
	{
		var key = InvoiceKey.From(Invoice());

		Assert.Equal(key, InvoiceKey.From(Invoice(taxId: "j031-0000-25229-7", number: "f 001037")));
		Assert.Equal(key, InvoiceKey.From(Invoice(number: "F001037", total: 7517.610m)));
		Assert.Equal(InvoiceKey.HexLength, key!.Value.Length);
	}

	[Fact]
	public void A_different_issuer_number_or_total_is_a_different_invoice()
	{
		var key = InvoiceKey.From(Invoice());

		Assert.NotEqual(key, InvoiceKey.From(Invoice(taxId: "J0310000252298")));
		Assert.NotEqual(key, InvoiceKey.From(Invoice(number: "F-001038")));
		Assert.NotEqual(key, InvoiceKey.From(Invoice(total: 7571.61m)));
	}

	[Fact]
	public void Without_every_part_there_is_no_key()
	{
		Assert.Null(InvoiceKey.From(Invoice(taxId: null)));
		Assert.Null(InvoiceKey.From(Invoice(number: " ")));
		Assert.Null(InvoiceKey.From(Invoice(total: null)));
	}

	[Fact]
	public void Only_a_settled_document_carries_its_key()
	{
		var key = InvoiceKey.From(Invoice());
		var approved = Processing();
		var inReview = Processing();

		approved.Complete(DocumentStatus.AutoApproved, Now, key);
		inReview.Complete(DocumentStatus.NeedsReview, Now, key);

		Assert.Equal(key, approved.InvoiceKey);
		Assert.Null(inReview.InvoiceKey);

		inReview.Approve(Invoice(), Now);
		Assert.Equal(key, inReview.InvoiceKey);
	}

	[Fact]
	public void A_duplicate_points_at_the_settled_document_and_is_never_settled_itself()
	{
		var original = Processing();
		var duplicate = Processing();

		duplicate.MarkDuplicate(original.Id, Now);

		Assert.Equal((DocumentStatus.Duplicate, original.Id, Now), (duplicate.Status, duplicate.DuplicateOf!.Value, duplicate.ProcessedAt!.Value));
		Assert.Null(duplicate.InvoiceKey);
		Assert.Null(duplicate.LeaseUntil);
		var self = Processing();
		Assert.Throws<ArgumentException>(() => self.MarkDuplicate(self.Id, Now));
	}
}
