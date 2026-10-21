# invoice-extractor

**Work in progress.** Reads invoices and receipts (PDF, JPG, PNG) with Claude Haiku, checks every extraction
against deterministic accounting rules, sends what it cannot confirm to a human review queue, and exports to
Excel.

So far: the domain — the extracted invoice with `decimal` amounts, the validator (line arithmetic, totals,
tax rate, tax-id format, dates) and country profiles for Nicaragua, Mexico and a generic fallback.

The evals come first: a synthetic dataset of 30 invoices and receipts with planted errors, and the scorer
that every extractor will be measured with. See [`evals/README.md`](evals/README.md).

```bash
dotnet test
```

Rules for contributors and agents are in [`AGENTS.md`](AGENTS.md); where the tax rules come from is in
[`docs/verification/tax-rules.md`](docs/verification/tax-rules.md).

Released under the [MIT License](LICENSE).
