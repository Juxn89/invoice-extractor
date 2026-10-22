import { defineConfig } from "@playwright/test";
import { servers } from "./e2e/servers";

// Records the demo video and screenshots (docs/demo) with the real model: a few tenths of a cent per run, so it
// is run by hand, never in CI. Needs ANTHROPIC_API_KEY in the environment, and Docker for the video encoder.
const key = process.env.ANTHROPIC_API_KEY;
if (!key) {
	throw new Error("The demo reads documents with Claude: set ANTHROPIC_API_KEY.");
}

export default defineConfig({
	testDir: "./e2e/demo",
	workers: 1,
	reporter: "list",
	timeout: 300_000,
	expect: { timeout: 60_000 },
	use: {
		// Slow enough for a viewer to follow every click.
		launchOptions: { slowMo: 150 },
	},
	webServer: servers({ Anthropic__ApiKey: key }),
});
