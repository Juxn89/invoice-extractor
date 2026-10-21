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
dotnet run --project evals/InvoiceExtractor.Evals -- generate   # evals/dataset, evals/samples, evals/dataset-hard
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
dotnet run --project evals/InvoiceExtractor.Evals -- run claude --allow-paid [--set hard] [--limit N | --only id,id] [--max-cost USD]
```

The paid run reads `ANTHROPIC_API_KEY` from the git-ignored `.env` (see `.env.template`) and drives the same
classes the API uses: format detection, inspection, preprocessing and `ExtractionPipeline` (read, validate,
retry once with the failed checks as feedback, decide with `ReviewPolicy`). One run scores two
configurations: the first reading alone (`<model>.json`, no retry) and the final verdict (`<model>-retry.json`).
The thresholds apply to the second, which is what the app ships. Before each
call, a guard refuses it if its worst case (estimated input plus the whole output cap) could take the run past
`--max-calls` or `--max-cost`, itself capped by what is left of this project's allocation in the shared budget
ledger (`~/.anthropic-budget/ledger.tsv`). The run is recorded in the ledger even when it stops halfway.
Without `--allow-paid` it refuses to start.

Results, 21 Oct 2026 (`claude-haiku-5-5`, adaptive thinking at `effort: low`, structured outputs), two runs:

| Metric | Without retry | With retry | Pass mark |
|---|---|---|---|
| Key fields (total, date, tax id, number) | 1.000 each | 1.000 each | ≥ 0.95 |
| Line F1 | 1.000 | 1.000 | ≥ 0.90 |
| Auto-approved / precision / false | 25 / 1.000 / 0 | 25 / 1.000 / 0 | ≥ 0.98, ≤ 1 |
| Planted errors sent to review | 5 / 5 | 5 / 5 | |
| Cost per document (USD) | 0.000495, 0.000525 | 0.000624, 0.000654 | ≤ 0.001 |

A perfect score says the harness and the extractor work end to end. It also says this set is easy for the
model: clean renders, legible print. The retry changes no verdict here; its extra cost is spent on the five
planted documents, finding out they are really wrong. Hence the hard set.

## The hard set

`evals/dataset-hard`: 12 more documents, scored on their own (`--set hard`) so the standard set stays a
stable baseline. Every one is degraded, and the invoices carry distractors printed next to the real fields:

| What | How |
|---|---|
| Scanned PDF (3) | A PDF whose page is only a picture, no text layer: the path a digital PDF never takes |
| Poor scan (3) | 100 dpi, greyscale, washed out, tilted up to 2.5°, noisy |
| Poor photo (3) | Keystoned, tilted 5-9°, a shadow across a corner, softer, 1100 px, JPEG quality 50 |
| Faded receipt (2) | A 58 mm roll, washed-out print fading towards one edge |
| Digital PDF (1) | Clean, distractors only |
| Distractors | The customer's tax id (in the issuer's country format, so the format rule cannot catch a swap), a due date, a purchase order number, a translucent PAID stamp across the line table |

Two are inconsistent on purpose (`ni-h04`, `mx-h01`). Each document stays legible to a person: the point is
to make the model work, not to make the task impossible.

Results, 21 Oct 2026, same settings, two runs:

| Metric | Without retry | With retry | Pass mark |
|---|---|---|---|
| Tax id accuracy | 0.917, 0.917 | **1.000, 1.000** | ≥ 0.95 |
| Total, date, number; line F1 | 1.000 | 1.000 | ≥ 0.95; ≥ 0.90 |
| Auto-approved (of 10 consistent) / false | 7, 8 / 0 | **9, 9 / 0** | ≤ 1 false |
| Planted errors sent to review | 2 / 2 | 2 / 2 | |
| Cost per document (USD) | 0.000611, 0.000682 | 0.000972, 0.000971 | ≤ 0.001 |

Without the retry the set fails the key-field mark; with it, it passes, and nothing wrong is approved in any
run. What goes wrong is always caught by a rule: an RFC's letter O read as a zero on the faded receipt
(`mx-h03`), and quantities under the PAID stamp read as 1 or 3 (`ni-h02`, `mx-h02`). The retry fixes most of
them; a quantity it reads wrong twice goes to review with the line highlighted. Which document the retry
rescues changes from run to run (the model is not deterministic); the totals held.

The decision and its trade-offs are in [ADR 0002](../docs/adr/0002-retry-with-validation-feedback.md).
