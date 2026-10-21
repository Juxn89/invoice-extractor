# invoice-extractor

**Work in progress.** Reads invoices and receipts (PDF, JPG, PNG) with Claude Haiku, checks every extraction
against deterministic accounting rules, sends what it cannot confirm to a human review queue, and exports to
Excel.

So far:

- **Domain:** the extracted invoice with `decimal` amounts, the validator (line arithmetic, totals, tax rate,
  tax-id format, dates) and country profiles for Nicaragua, Mexico and a generic fallback.
- **Upload:** `POST /v1/batches` takes up to 20 PDF, JPG or PNG files. Each file is checked on its own: real
  format from its bytes, size, page count, readable, password protection. Files already uploaded are
  recognised by their SHA-256 and cost nothing a second time. Errors are listed in
  [`docs/errors.md`](docs/errors.md).
- **Preprocessing:** a digital PDF goes to the model as text (PdfPig), a scanned one as the PDF itself, and a
  photo as an upright JPEG no larger than the model uses (SkiaSharp, EXIF orientation applied).
- **Evals:** a synthetic dataset of 30 invoices and receipts with planted errors, and the scorer every
  extractor will be measured with. See [`evals/README.md`](evals/README.md).

```bash
dotnet run --project src/InvoiceExtractor.AppHost   # Postgres + API, through .NET Aspire (needs Docker)
dotnet test                                         # unit, architecture and integration (Testcontainers)
```

Rules for contributors and agents are in [`AGENTS.md`](AGENTS.md); where the tax rules come from is in
[`docs/verification/tax-rules.md`](docs/verification/tax-rules.md).

Released under the [MIT License](LICENSE).
