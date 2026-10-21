namespace InvoiceExtractor.Domain.Tests;

using InvoiceExtractor.Domain.Documents;

public sealed class DocumentTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
	private static readonly ContentHash Hash = ContentHash.Compute("factura"u8);

	private static Document Queue(string fileName = "factura.pdf", DocumentFormat format = DocumentFormat.Pdf, bool hasTextLayer = true) =>
		Document.Queue(BatchId.New(), fileName, Hash, format, pages: 1, hasTextLayer, sizeBytes: 1024, Now);

	[Fact]
	public void A_new_document_is_queued_with_no_attempts()
	{
		var document = Queue();

		Assert.Equal(DocumentStatus.Queued, document.Status);
		Assert.Equal(0, document.Attempts);
		Assert.Null(document.LeaseUntil);
		Assert.Null(document.ProcessedAt);
	}

	[Fact]
	public void Only_the_file_name_is_kept_never_a_path()
	{
		Assert.Equal("factura.pdf", Queue(fileName: "C:\\Users\\ana\\Downloads\\factura.pdf").FileName);
		Assert.Equal("factura.pdf", Queue(fileName: "../../etc/factura.pdf").FileName);
	}

	[Fact]
	public void A_long_file_name_is_cut_keeping_its_extension()
	{
		var name = Queue(fileName: new string('a', 400) + ".jpeg", format: DocumentFormat.Jpeg, hasTextLayer: false).FileName;

		Assert.Equal(Document.FileNameMaxLength, name.Length);
		Assert.EndsWith(".jpeg", name, StringComparison.Ordinal);
	}

	[Fact]
	public void A_file_without_a_name_gets_a_placeholder() =>
		Assert.Equal(Document.UnnamedFileName, Queue(fileName: "  ").FileName);

	[Fact]
	public void An_image_cannot_have_a_text_layer() =>
		Assert.Throws<ArgumentException>(() => Queue(format: DocumentFormat.Png, hasTextLayer: true));

	[Fact]
	public void The_content_hash_is_lower_case_sha256_and_round_trips()
	{
		var hash = ContentHash.Compute("abc"u8);

		Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash.Value);
		Assert.Equal(hash, ContentHash.FromHex(hash.Value.ToUpperInvariant()));
		Assert.Throws<ArgumentException>(() => ContentHash.FromHex("not-a-hash"));
	}

	[Fact]
	public void A_claim_takes_a_queued_document_and_counts_an_attempt()
	{
		var document = Queue();

		document.Claim(Now, Now.AddMinutes(5));

		Assert.Equal(DocumentStatus.Processing, document.Status);
		Assert.Equal(1, document.Attempts);
		Assert.Equal(Now.AddMinutes(5), document.LeaseUntil);
	}

	[Fact]
	public void A_held_document_can_be_claimed_again_only_once_its_lease_has_run_out()
	{
		var document = Queue();
		document.Claim(Now, Now.AddMinutes(5));

		Assert.Throws<InvalidOperationException>(() => document.Claim(Now.AddMinutes(1), Now.AddMinutes(6)));

		document.Claim(Now.AddMinutes(6), Now.AddMinutes(11));
		Assert.Equal(2, document.Attempts);
	}

	[Theory]
	[InlineData(DocumentStatus.AutoApproved)]
	[InlineData(DocumentStatus.NeedsReview)]
	public void Completing_ends_the_lease_and_stamps_the_time(DocumentStatus outcome)
	{
		var document = Queue();
		document.Claim(Now, Now.AddMinutes(5));

		document.Complete(outcome, Now.AddSeconds(4));

		Assert.Equal(outcome, document.Status);
		Assert.Null(document.LeaseUntil);
		Assert.Equal(Now.AddSeconds(4), document.ProcessedAt);
	}

	[Fact]
	public void Only_a_document_being_processed_can_complete_and_only_as_approved_or_in_review()
	{
		var document = Queue();
		Assert.Throws<InvalidOperationException>(() => document.Complete(DocumentStatus.AutoApproved, Now));

		document.Claim(Now, Now.AddMinutes(5));
		Assert.Throws<ArgumentOutOfRangeException>(() => document.Complete(DocumentStatus.Approved, Now));
	}

	[Fact]
	public void A_released_document_is_queued_again_until_it_runs_out_of_attempts()
	{
		const int maxAttempts = 2;
		var document = Queue();

		document.Claim(Now, Now.AddMinutes(5));
		document.Release(maxAttempts, Now);
		Assert.Equal(DocumentStatus.Queued, document.Status);
		Assert.Null(document.LeaseUntil);

		document.Claim(Now, Now.AddMinutes(5));
		document.Release(maxAttempts, Now.AddMinutes(1));
		Assert.Equal(DocumentStatus.Failed, document.Status);
		Assert.Equal(Now.AddMinutes(1), document.ProcessedAt);
	}

	[Fact]
	public void A_batch_keeps_its_country_in_upper_case_and_refuses_anything_but_two_letters()
	{
		Assert.Equal("MX", Batch.Start(1, "mx", Now).Country);
		Assert.Throws<ArgumentException>(() => Batch.Start(1, "MEX", Now));
	}
}
