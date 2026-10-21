# ADR 0002: retry once with Haiku and the failed checks, instead of escalating the model

Status: accepted, 21 Oct 2026. Numbered as in the plan's list of decisions (§7).

## Context

When a reading breaks a rule (lines that do not add up, a tax id with the wrong shape) it is either a
misread, which a second look can fix, or a document wrong on paper, which must go to a person untouched.
Sending every broken rule to review is safe but leaves work a machine could have done. The usual fix is to
escalate to a larger model; the project's rule is Claude Haiku 5.5 for every call.

## Decision

One retry, with the same model, as a fresh single-turn request carrying the document again and the failed
checks with their numbers ("Line 2: 1 × 56.98 is 56.98, the line total reads 85.47"; for a tax id, the
format the country expects). The prompt (`Ai/Prompts/retry-feedback.md`) draws the line: correct a value
only where the document shows something different, and when the document itself prints the failing values,
return them as printed. The retry's reading replaces the first only if it read something, and the verdict
is the same as for a first reading: every rule passes and no key field is marked low.

- Not a follow-up turn: Haiku 5.5's thinking blocks would have to be sent back untouched, and the checks'
  details already carry the values that were read.
- Not on a failed call (refusal, truncated answer): there is nothing to point the model at.
- Not on low confidence alone: the model already said it is guessing.
- Switchable (`Processing:RetryOnFailedRules`), and both calls are stored in `extractions`.

## Evidence

The evals run the app's own pipeline. One run scores both configurations, since the first reading is the
configuration without retry. Two runs each, 21 Oct 2026:

| Set | | Without retry | With retry |
|---|---|---|---|
| Standard (30) | auto-approved, false | 25, 0 / 25, 0 | 25, 0 / 25, 0 |
| | cost per document (USD) | 0.000495 / 0.000525 | 0.000624 / 0.000654 |
| Hard (12) | auto-approved, false | 7, 0 / 8, 0 | **9, 0 / 9, 0** |
| | tax id accuracy | 0.917 / 0.917 | **1.000 / 1.000** |
| | cost per document (USD) | 0.000611 / 0.000682 | 0.000972 / 0.000971 |

- On the hard set the retry recovers one or two documents in twelve that the first reading sent to review,
  lifts tax-id accuracy over the 0.95 mark, and approves nothing wrong.
- The documents wrong on paper (5 standard, 2 hard) were retried and stayed in review in every run: the
  retry did not "fix" them.
- On the standard set the retry changes no verdict. Its whole cost there (about +25%) is spent on the
  planted documents, finding out they are really wrong.
- Not every misread is fixable: a quantity of 1.5 under the PAID stamp was read as 1 or 3, and then again
  wrong, in most runs. It goes to review with the line highlighted, which is the right outcome.
- The model is not deterministic: which hard document the retry rescues changes from run to run. The
  aggregate held across both runs.

## Consequences

- Documents that break a rule cost about twice as much (two calls); the average stays under the
  USD 0.001 per document mark on both sets.
- A larger model on retry was not tried: the project uses Haiku 5.5 only. If it were allowed, the hard
  set is where to measure it.
