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
the diff. The Haiku extractor, without and with the retry, lands on day 4 of the plan. It calls the paid
API, so it only runs by hand, behind a spending cap.
