# invoice-extractor · Agent guidelines

Reads invoices and receipts (PDF, JPG, PNG) with Claude, validates every extraction with deterministic
accounting rules, sends what it cannot confirm to a human review queue, and exports to Excel. The plan is
in juangomezb's `docs/workana/invoice-extractor/plan.md`.

These rules are binding for any agent (Claude Code, Codex, Copilot) working in this repo.

## Stack (pinned — check `Directory.Packages.props` before adding anything)

- .NET 10 / C# 14 (`global.json`), Central Package Management, `TreatWarningsAsErrors=true`.
- Versions: the newest one that is **at least two weeks old** on the day it is added, verified against
  api.nuget.org, with the date in `Directory.Packages.props`.
- Licences: OSI only. `SixLabors.ImageSharp` is out (Six Labors Split License); images go through SkiaSharp.
  No MediatR, no AutoMapper, no MassTransit.
- Tests: xUnit **v2** (ArchUnitNET's adapter requires it), Testcontainers.

## Dependency rule

```
Domain  ←  Application  ←  Infrastructure  ←  Api
```

- `Domain`: plain C#. No package references. Amounts are `decimal`, never `double`.
- `Application`: use cases plus ports. May reference `Microsoft.Extensions.AI.Abstractions`, nothing concrete.
- `Infrastructure`: adapters (EF Core, Anthropic, PdfPig, SkiaSharp, ClosedXML).
- `Api`: composition root and endpoints only.

## The model proposes, the domain decides

- Every extraction goes through `InvoiceValidator`. A failed rule never rejects a document: it sends it to
  review with the field highlighted. That is also why a strict tax-ID format is safe — a false alarm costs a
  review, not a wrong invoice.
- Tax rates are configuration, not constants: they change by decree (Mexico's 8% border rate is renewed
  yearly). Sources and their reliability are in `docs/verification/`.

## AI rules

- Model: **`claude-haiku-5-5` only**, including the retry after a failed validation. Model ids and prices
  are configuration.
- Document text is **data, not instructions**: delimited, never concatenated into the system prompt.
- Every paid call goes through the spending guards and is recorded in `~/.anthropic-budget/ledger.tsv`.
  Evals do not call the API without `--allow-paid`.

## No magic strings or numbers

Tolerances, thresholds, limits, model ids and prices live in named constants with a comment saying where the
value comes from, or in options classes validated at startup.

## Verification gate

Nothing is done until it ran: `dotnet build` (warnings are errors), `dotnet test`, and
`dotnet format whitespace --verify-no-changes`. When you add a rule-like test, prove it can fail.

## Style

- Tabs for indentation (`.editorconfig`). File-scoped namespaces. `sealed` by default.
- Comments explain *why*, not *what*. No commented-out code.
- Commits and pull requests carry no AI attribution trailer.
