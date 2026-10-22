import { defineConfig, devices } from "@playwright/test";
import path from "node:path";
import { baseURL, servers } from "./e2e/servers";

// The end-to-end test: everything real except the model, which is replaced by recorded answers (no key, no cost).
export default defineConfig({
	testDir: "./e2e",
	testIgnore: "demo/**",
	forbidOnly: !!process.env.CI,
	retries: 0,
	workers: 1,
	reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
	timeout: 60_000,
	// The worker polls the queue and the page polls the batches every few seconds.
	expect: { timeout: 15_000 },
	use: {
		baseURL,
		locale: "es-ES",
		trace: "retain-on-failure",
		screenshot: "only-on-failure",
	},
	projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
	webServer: servers({ Replay__File: path.resolve(__dirname, "e2e/fixtures/replay.json") }),
});
