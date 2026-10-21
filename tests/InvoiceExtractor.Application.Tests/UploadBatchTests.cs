namespace InvoiceExtractor.Application.Tests;

using InvoiceExtractor.Application.Abstractions;
using InvoiceExtractor.Application.Common;
using InvoiceExtractor.Application.Features.Processing;
using InvoiceExtractor.Application.Features.Upload;
using InvoiceExtractor.Domain.Documents;
using Microsoft.Extensions.Options;

public sealed class UploadBatchTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

	private readonly FakeDocumentRepository _documents = new();
	private readonly FakeFileStore _files = new();
	private readonly FakeInspector _inspector = new();
	private readonly UploadOptions _options = new();

	private UploadBatch UseCase() =>
		new(_documents, _files, _inspector, new TaxProfileCatalog(Options.Create(new TaxOptions())), Options.Create(_options), new FixedClock(Now));

	private static UploadedFile File(string name, byte[] content) => new(name, content.Length, content);

	private async Task<UploadResponse> UploadAsync(params UploadedFile[] uploads)
	{
		var result = await UseCase().HandleAsync(uploads, null, CancellationToken.None);
		Assert.True(result.IsSuccess, result.Error?.Message);
		return result.Value;
	}

	[Fact]
	public async Task New_files_are_stored_and_queued_in_one_batch()
	{
		var response = await UploadAsync(File("a.pdf", Files.Pdf("a")), File("b.jpg", Files.Jpeg("b")), File("c.png", Files.Png("c")));

		Assert.NotNull(response.BatchId);
		Assert.All(response.Files, static f => Assert.Equal(UploadOutcome.Queued, f.Outcome));
		Assert.Equal([DocumentFormat.Pdf, DocumentFormat.Jpeg, DocumentFormat.Png], response.Files.Select(static f => f.Document!.Format));
		Assert.Single(_documents.Batches);
		Assert.Equal(3, _documents.Batches[0].FileCount);
		Assert.Equal(3, _documents.Documents.Count);
		Assert.Equal(3, _files.Files.Count);
	}

	[Fact]
	public async Task A_file_uploaded_again_is_a_duplicate_and_nothing_new_is_stored()
	{
		var first = await UploadAsync(File("factura.pdf", Files.Pdf("same")));

		var second = await UploadAsync(File("otra-vez.pdf", Files.Pdf("same")));

		Assert.Null(second.BatchId);
		var result = Assert.Single(second.Files);
		Assert.Equal(UploadOutcome.Duplicate, result.Outcome);
		Assert.Equal(first.Files[0].Document!.Id, result.Document!.Id);
		Assert.Single(_documents.Documents);
		Assert.Equal(1, _documents.Saves);
	}

	[Fact]
	public async Task The_same_file_twice_in_one_batch_is_stored_once()
	{
		var response = await UploadAsync(File("a.pdf", Files.Pdf("same")), File("a (1).pdf", Files.Pdf("same")));

		Assert.Equal([UploadOutcome.Queued, UploadOutcome.Duplicate], response.Files.Select(static f => f.Outcome));
		Assert.Equal(response.Files[0].Document!.Id, response.Files[1].Document!.Id);
		Assert.Equal(1, _documents.Batches[0].FileCount);
	}

	[Fact]
	public async Task A_bad_file_is_rejected_on_its_own_and_the_rest_go_through()
	{
		_inspector.ByContent[Convert.ToBase64String(Files.Pdf("long"))] = new DocumentInspection(Pages: 6, HasTextLayer: true);
		_inspector.ByContent[Convert.ToBase64String(Files.Jpeg("broken"))] = Error.Validation(UploadErrors.Unreadable, "broken");

		var response = await UploadAsync(
			File("ok.png", Files.Png("ok")),
			File("virus.exe", "MZ\u0090"u8.ToArray()),
			File("empty.pdf", []),
			File("long.pdf", Files.Pdf("long")),
			File("broken.jpg", Files.Jpeg("broken")),
			new UploadedFile("huge.jpg", _options.MaxFileBytes + 1L, ReadOnlyMemory<byte>.Empty));

		Assert.Equal(
			[null, UploadErrors.UnsupportedFormat, UploadErrors.EmptyFile, UploadErrors.TooManyPages, UploadErrors.Unreadable, UploadErrors.TooLarge],
			response.Files.Select(static f => f.Error?.Code));
		Assert.Equal(UploadOutcome.Queued, response.Files[0].Outcome);
		Assert.All(response.Files.Skip(1), static f => Assert.Equal(UploadOutcome.Rejected, f.Outcome));
		Assert.Single(_documents.Documents);
	}

	[Fact]
	public async Task A_batch_where_nothing_is_new_creates_no_batch()
	{
		var response = await UploadAsync(File("virus.exe", "MZ"u8.ToArray()));

		Assert.Null(response.BatchId);
		Assert.Empty(_documents.Batches);
		Assert.Equal(0, _documents.Saves);
	}

	[Theory]
	[InlineData(0, UploadErrors.EmptyBatch)]
	[InlineData(21, UploadErrors.TooManyFiles)]
	public async Task The_batch_itself_must_have_between_one_and_the_maximum_files(int count, string code)
	{
		var uploads = Enumerable.Range(0, count).Select(i => File($"{i}.pdf", Files.Pdf(i.ToString(System.Globalization.CultureInfo.InvariantCulture)))).ToList();

		var result = await UseCase().HandleAsync(uploads, null, CancellationToken.None);

		Assert.Equal(code, result.Error?.Code);
		Assert.Empty(_documents.Documents);
	}

	[Fact]
	public async Task When_another_request_stores_the_same_file_first_it_is_reported_as_a_duplicate()
	{
		// Both requests looked before either saved; the other one committed first.
		var theirs = Document.Queue(BatchId.New(), "theirs.pdf", ContentHash.Compute(Files.Pdf("race")), DocumentFormat.Pdf, 1, true, 10, Now);
		_documents.CommittedByOthersOnNextSave.Add(theirs);

		var response = await UploadAsync(File("mine.pdf", Files.Pdf("race")), File("other.png", Files.Png("other")));

		Assert.Equal([UploadOutcome.Duplicate, UploadOutcome.Queued], response.Files.Select(static f => f.Outcome));
		Assert.Equal(theirs.Id.Value, response.Files[0].Document!.Id);
		Assert.Equal(2, _documents.Saves);
	}

	[Fact]
	public async Task A_batch_takes_the_default_country_unless_one_is_given()
	{
		var byDefault = await UploadAsync(File("a.pdf", Files.Pdf("a")));
		var mexican = await UseCase().HandleAsync([File("b.pdf", Files.Pdf("b"))], " mx ", CancellationToken.None);

		Assert.Equal(_options.DefaultCountry, byDefault.Country);
		Assert.Equal("MX", mexican.Value.Country);
		Assert.Equal(["NI", "MX"], _documents.Batches.Select(static b => b.Country));
	}

	[Fact]
	public async Task An_unknown_country_is_refused_before_anything_is_stored()
	{
		var result = await UseCase().HandleAsync([File("a.pdf", Files.Pdf("a"))], "CO", CancellationToken.None);

		Assert.Equal(UploadErrors.UnknownCountry, result.Error?.Code);
		Assert.Empty(_files.Files);
	}

	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}
}

public sealed class FormatDetectorTests
{
	[Fact]
	public void Formats_are_told_by_their_magic_bytes_not_their_name()
	{
		Assert.Equal(DocumentFormat.Pdf, FormatDetector.Detect(Files.Pdf("x")).Value);
		Assert.Equal(DocumentFormat.Jpeg, FormatDetector.Detect(Files.Jpeg("x")).Value);
		Assert.Equal(DocumentFormat.Png, FormatDetector.Detect(Files.Png("x")).Value);
	}

	[Theory]
	[InlineData("MZ")] // a Windows executable
	[InlineData("GIF89a")]
	[InlineData("<html><body>%PDF-</body></html>")] // the signature must be at the start
	[InlineData("%PD")] // truncated
	public void Anything_else_is_unsupported(string content) =>
		Assert.Equal(UploadErrors.UnsupportedFormat, FormatDetector.Detect(System.Text.Encoding.ASCII.GetBytes(content)).Error?.Code);
}
