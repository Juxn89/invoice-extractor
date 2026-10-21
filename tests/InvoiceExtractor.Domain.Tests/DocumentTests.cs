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
}
