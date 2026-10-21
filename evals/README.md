# Evals

How well extraction works is measured, not asserted: 30 synthetic invoices and receipts, each with the
exact JSON a perfect extraction returns, scored against the pass marks in [`thresholds.json`](thresholds.json).

## The dataset

| | |
|---|---|
| Countries | 12 Nicaragua (RUC, IVA 15 %), 12 Mexico (RFC, IVA 16 % / 8 %), 6 generic (US in English, Spain) |
| Formats | 10 digital PDFs with a text layer, 10 scans, 6 phone photos (tilted, uneven light, JPEG), 4 thermal receipts |
| Planted errors | 5 documents that are wrong on paper, each breaking one rule: lines vs subtotal (`ni-02`), tax rate (`ni-06`), RFC (`mx-01`), line arithmetic (`mx-05`), total (`xx-06`). Even a perfect extraction must send them to review |

Everything is fictitious: issuers, tax ids and customers are invented, and the dates are fixed against
`AsOf` 2026-10-21 so the date rules stay reproducible. The generator is seeded, so the files regenerate
identically. `evals/samples/` holds 12 more documents, from a different seed, which the UI offers as
"try an example"; they are never scored.

```bash
dotnet run --project evals/InvoiceExtractor.Evals -- generate   # writes evals/dataset and evals/samples
```

## Metrics

| Metric | Pass mark |
|---|---|
| Accuracy of each key field: total, issue date, tax id (separators aside), number (case aside) | ≥ 0.95 |
| Line F1: a read line matches a printed one with the same description and total | ≥ 0.90 |
| Precision of auto-approval: approved documents with every key field right and nothing planted | ≥ 0.98 |
| False auto-approvals | ≤ 1 of 30 |
| Average model cost per document | ≤ USD 0.001 |

A failed extraction counts as wrong on every field and goes to review.

## Running

```bash
dotnet run --project evals/InvoiceExtractor.Evals -- run oracle
```

The **oracle** returns exactly what each document prints. It scores the harness rather than a model, so it
must reach 1.000 everywhere, approve the 25 consistent documents and catch the 5 planted ones; anything
else is a bug in the dataset, the validator or the scorer. CI runs it on every push, next to the tests in
`tests/InvoiceExtractor.Evals.Tests` (each consistent document passes every rule; each planted one breaks
only its own).

Results are written to `evals/results/<extractor>.json` and versioned, so a change in quality shows up in
the diff.

## Claude Haiku 5.5

```bash
dotnet run --project evals/InvoiceExtractor.Evals -- run claude --allow-paid [--limit N] [--max-cost USD]
```

The paid run reads `ANTHROPIC_API_KEY` from the git-ignored `.env` (see `.env.template`) and drives the same
classes the API uses: format detection, inspection, preprocessing and `ClaudeInvoiceExtractor`. Before each
call, a guard refuses it if its worst case (estimated input plus the whole output cap) could take the run past
`--max-calls` or `--max-cost`, itself capped by what is left of this project's allocation in the shared budget
ledger (`~/.anthropic-budget/ledger.tsv`). The run is recorded in the ledger even when it stops halfway.
Without `--allow-paid` it refuses to start.

First run, 21 Oct 2026 (`claude-haiku-5-5`, adaptive thinking at `effort: low`, structured outputs):

| Metric | Result | Pass mark |
|---|---|---|
| Key fields (total, date, tax id, number) | 1.000 each | ≥ 0.95 |
| Line F1 | 1.000 | ≥ 0.90 |
| Auto-approved / precision / false | 25 / 1.000 / 0 | ≥ 0.98, ≤ 1 |
| Planted errors sent to review | 5 / 5 | |
| Cost per document | USD 0.000509 (total 0.015270, 94,106 input and 11,719 output tokens) | ≤ USD 0.001 |

A perfect score says the harness and the extractor work end to end. It also says this dataset is easy for
the model: clean renders, legible print. It cannot yet tell the retry (day 6 of the plan) from no retry,
and it has no scanned PDF. Harder documents are the next thing it needs.
