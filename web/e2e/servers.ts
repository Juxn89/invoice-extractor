import type { PlaywrightTestConfig } from "@playwright/test";
import { tmpdir } from "node:os";
import path from "node:path";

// Ports of their own, so a run never lands on the dev servers (5110 and 3010).
const apiPort = 5199;
const webPort = 3011;

export const baseURL = `http://localhost:${webPort}`;

/**
 * The real API, queue and workers, and a production build of the web app. Uploads are idempotent by content: the
 * samples must be new to the app on every run, so each run gets its own database and file store. Postgres is the
 * one CI starts as a service, or `pnpm e2e:db` starts locally.
 * @param model How documents are read: the API settings for recorded answers, or for the real model.
 */
export function servers(model: Record<string, string>): PlaywrightTestConfig["webServer"] {
	const run = (process.env.E2E_RUN ??= `${Date.now()}`);
	const database =
		process.env.E2E_DATABASE ?? `Host=localhost;Port=5433;Username=postgres;Password=postgres;Database=invoices_e2e_${run}`;

	return [
		{
			command: "dotnet run --project ../src/InvoiceExtractor.Api --no-launch-profile",
			url: `http://localhost:${apiPort}/health`,
			env: {
				ASPNETCORE_ENVIRONMENT: "Development",
				ASPNETCORE_URLS: `http://localhost:${apiPort}`,
				ConnectionStrings__invoicesdb: database,
				FileStore__RootPath: path.join(tmpdir(), `invoice-extractor-e2e-${run}`),
				Processing__PollInterval: "00:00:00.500",
				...model,
			},
			timeout: 180_000,
		},
		{
			// The rewrite to the API is fixed at build time, hence API_URL here.
			command: `pnpm build && pnpm exec next start --port ${webPort}`,
			url: baseURL,
			env: { API_URL: `http://localhost:${apiPort}`, NEXT_TELEMETRY_DISABLED: "1" },
			timeout: 180_000,
		},
	];
}
