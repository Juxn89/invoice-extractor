# ADR 0004: one invoice is settled once, by its business key

Status: accepted, 22 Oct 2026.

## Context

Uploads are idempotent by content: the same file uploaded twice is one document (its SHA-256 is unique). But
the same invoice often arrives as different files: the supplier's PDF by email and a phone photo of the
printed copy, or two scans of one receipt. Each is a new document, each is read, and if both are approved the
export puts the purchase in the books twice. That is the error an accountant notices first.

## Decision

An invoice is identified by its **business key**: the issuer's tax id, the invoice number and the total,
normalised (tax id without separators, number with letters and digits only, both upper-cased, the total with
two decimals) and stored as their SHA-256 in `documents.invoice_key`.

- Only a **settled** document carries the key: auto-approved, or approved by a person. A partial **unique
  index** (`WHERE invoice_key IS NOT NULL`) makes the database refuse a second settled document of the same
  invoice, whatever the application does.
- After a reading, the worker looks the key up. If another document settled it, this one ends as
  **`Duplicate`**, pointing at the original (`duplicate_of`), whatever its verdict would have been: no one has
  to review a copy of an invoice already in the books. Its readings are kept, so what it cost stays visible.
- Two workers can check at the same moment and both find nothing. The second save then breaks the unique
  index; the worker discards its changes, finds the original, and records the same readings again with the
  document as the duplicate. No second model call: the readings are already paid for.
- A reviewer approving data whose key is already settled gets **409 `review.duplicate_invoice`** with the
  original's id; the document stays in review. The same holds if another approval wins the race.
- Duplicates are not exported (the export takes settled documents only) and show as a separate, neutral
  segment on the dashboard.

## Consequences

- A misreading that changes the key (a digit of the total) hides a duplicate. The validator catches most of
  those first: a wrong total rarely still adds up. Two different invoices with the same issuer, number and
  total are treated as one; for one issuer, a repeated number is itself an error worth a look.
- A document marked duplicate by mistake has no "not a duplicate" action yet; the reading and the link to the
  original are there for a person to judge. Adding that action is a domain transition, not a schema change.
- Documents settled before this change have no key and are not compared.
- The key is a hash, so the column says nothing on its own; the readings it was computed from are in
  `extractions` and `reviewed_invoice`.
