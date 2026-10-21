# ADR 0003: the work queue lives in Postgres

Status: accepted, 21 Oct 2026. Numbered as in the plan's list of decisions (§7).

## Context

An upload stores files and queues them; workers read each one with the model (seconds per document) and
record the outcome. Several workers run at once, possibly in several processes, and must never read the same
document twice: each read costs money and a second outcome would contradict the first. A worker can also die
mid-way, and its document must not stay stuck.

The volume is a small business's: tens to hundreds of documents a day, batches of up to 20.

## Decision

The `documents` table is the queue. A worker claims the oldest claimable row in one statement:

```sql
UPDATE documents SET status = 'Processing', attempts = attempts + 1, lease_until = @lease_until
WHERE id = (SELECT id FROM documents
            WHERE status = 'Queued' OR (status = 'Processing' AND lease_until < @now)
            ORDER BY created_at, id LIMIT 1
            FOR UPDATE SKIP LOCKED)
RETURNING *, xmin
```

- `FOR UPDATE SKIP LOCKED`: concurrent claims step over a row another worker is locking instead of waiting
  for it or taking it too.
- A **lease**, not a held lock or transaction, keeps the document while the model is called. If the worker
  dies, the lease runs out and the next claim takes the document back.
- `xmin` as a concurrency token: a worker whose lease ran out cannot save over the worker that took over.
- Attempts are counted on claim, so a document that kills its worker every time fails after
  `Processing:MaxAttempts` instead of taking the workers down in turn forever.
- A daily spend cap (`Processing:DailyBudgetUsd`) stops claims, not calls in flight.

## Alternatives

- **RabbitMQ (or another broker).** One more service to run, secure and monitor, and two stores to keep
  consistent: a message for a row that was rolled back, or a row whose message was lost. Its strengths
  (throughput, fan-out, routing) are not needed at this volume. MassTransit on top is out by licence.
- **Hangfire.** Mature, but its Postgres storage is a community package and its dashboard and job model add
  surface this project does not use; the queue here is fifteen lines of SQL.
- **An in-process `Channel<T>`** (as in docs-assistant). Loses queued work on restart and cannot be shared
  between processes; docs-assistant compensates with a sweep of pending rows, which is this design with more
  moving parts.

## Consequences

- Workers scale out by running more API instances or more loops (`Processing:Workers`), with no
  coordination beyond the database.
- Polling: an idle worker queries every `Processing:PollInterval` (2 s). Postgres `LISTEN/NOTIFY` could wake
  it instead, if the latency between upload and first read ever matters.
- Proven by `WorkerTests`: four workers over a hundred documents process each exactly once, and the test
  fails (51 of 100) when `SKIP LOCKED` is removed.
