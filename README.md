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
- **Processing:** a queue in Postgres (`FOR UPDATE SKIP LOCKED` with a lease, see
  [ADR 0003](docs/adr/0003-queue-in-postgres.md)) feeds workers that read each document with Claude Haiku 5.5,
  validate it with the batch's country rules and approve it or send it to review. A reading that breaks a
  rule is read once more with the failed checks as feedback
  ([ADR 0002](docs/adr/0002-retry-with-validation-feedback.md)). Every call's cost is
  recorded, and a daily budget stops the workers when it is spent.
- **Evals:** a synthetic dataset of 30 invoices and receipts with planted errors, and the scorer every
  extractor will be measured with. See [`evals/README.md`](evals/README.md).

The workers need an Anthropic API key; without one, uploads are queued and wait:

```bash
dotnet user-secrets set Anthropic:ApiKey <key> --project src/InvoiceExtractor.Api
```

```bash
dotnet run --project src/InvoiceExtractor.AppHost   # Postgres + API, through .NET Aspire (needs Docker)
dotnet test                                         # unit, architecture and integration (Testcontainers)
```

Rules for contributors and agents are in [`AGENTS.md`](AGENTS.md); where the tax rules come from is in
[`docs/verification/tax-rules.md`](docs/verification/tax-rules.md).

Released under the [MIT License](LICENSE).
