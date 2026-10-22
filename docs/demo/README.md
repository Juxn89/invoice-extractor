# Demo

A 75-second walkthrough in Spanish, recorded against the real model:

1. Three of the built-in examples are picked and uploaded: a digital PDF, a scan and a phone photo of a receipt.
2. Two of them pass every rule and are approved automatically.
3. The third has a total that does not add up, so it goes to review, with the rule's numbers in Spanish.
4. The reviewer corrects the total and approves it.
5. The dashboard shows what the batch cost.

| File | What it shows |
|---|---|
| `demo.mp4` | The whole flow, with captions, 1920×1200 |
| `poster.jpg` | The video's poster: the review screen |
| `01-subida.png` | The upload screen with the batch picked |
| `02-lote.png` | The batch: two approved by the rules, one waiting for review |
| `03-revision.png` | The review screen: the photo beside the reading, the failed rule on top |
| `04-aprobada.png` | The same document after the reviewer's correction |
| `05-panel.png` | The dashboard: approval rate and cost per document |
| `facturas-ejemplo.xlsx` | The Excel export the dashboard downloads at the end |

The screenshots are 1920×1200 (a 1280×800 page at device scale 1.5). The same run captures the English review
screen the README showcase is made from (`docs/readme/shots/review.png`).

## Recording it again

```bash
cd web
pnpm e2e:db                          # a throwaway Postgres on port 5433
ANTHROPIC_API_KEY=... pnpm demo      # e2e/demo/demo.spec.ts; Docker encodes the MP4 (pinned ffmpeg image)
```

Each run starts the API and a production build of the web app against a new database, and reads the documents with
Claude Haiku 5.5. The last recording made 4 calls, one of them the retry, and cost USD 0.0022.
