import { expect, test, type Browser, type Page } from "@playwright/test";
import { execFileSync } from "node:child_process";
import { mkdir, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { baseURL } from "../servers";

// The 70-second walkthrough for the README, the portfolio and the Workana listing, recorded against the real
// model (playwright.demo.config.ts). It writes the video, its poster, the screenshots and the exported workbook
// to docs/demo, and the English review screen the README showcase is composed from to docs/readme/shots.
//
// Frames come from Chromium's screencast at 1920×1200 (a 1280×800 page at device scale 1.5), so the text stays
// sharp; Playwright's own recorder writes low-bitrate VP8. The MP4 is encoded by ffmpeg in a container (pinned
// image, no network), so nothing is installed on the host.

const out = path.resolve(__dirname, "../../../docs/demo");
const readmeShots = path.resolve(__dirname, "../../../docs/readme/shots");
const ffmpegImage = "jrottenberg/ffmpeg:9.0.1-ubuntu2404@sha256:7e8154ec4779d335054de5e173f1ef52032abaa12ac7416cc7f0c88f4973bd14";

const viewport = { width: 1280, height: 800 };
const scale = 1.5;
const fps = 30;

// A digital PDF, a scan and a phone photo of a receipt, all under the generic rules.
const files = ["sample-09.pdf", "sample-10.png", "sample-11.jpg"];

declare global {
	interface Window {
		demoCaption?: (text: string) => void;
	}
}

// The screencast shows neither the mouse nor what is going on: a pointer that follows the mouse, and a caption.
async function addOverlay(page: Page) {
	await page.addInitScript(() => {
		const ensure = (id: string, style: string) => {
			let element = document.getElementById(id);
			if (!element) {
				element = document.createElement("div");
				element.id = id;
				element.setAttribute("style", style);
				document.body.appendChild(element);
			}

			return element;
		};

		const pointer = () =>
			ensure(
				"demo-pointer",
				"position:fixed;z-index:2147483647;width:22px;height:22px;margin:-11px 0 0 -11px;border-radius:50%;background:rgba(13,148,136,.3);border:2px solid #0f766e;pointer-events:none;transition:transform .12s",
			);
		window.addEventListener("mousemove", (event) => {
			pointer().style.left = `${event.clientX}px`;
			pointer().style.top = `${event.clientY}px`;
		});
		window.addEventListener("mousedown", () => (pointer().style.transform = "scale(.6)"));
		window.addEventListener("mouseup", () => (pointer().style.transform = "scale(1)"));

		window.demoCaption = (text: string) => {
			const caption = ensure(
				"demo-caption",
				"position:fixed;z-index:2147483646;left:50%;bottom:28px;transform:translateX(-50%);width:max-content;max-width:min(980px,92vw);padding:14px 24px;border-radius:12px;background:rgba(17,24,39,.92);color:#fff;font:500 20px/1.45 system-ui,sans-serif;text-align:center;box-shadow:0 12px 32px rgba(0,0,0,.28);pointer-events:none",
			);
			caption.textContent = text;
			caption.style.display = text ? "block" : "none";
		};
	});
}

async function say(page: Page, text: string, holdMs: number) {
	await page.evaluate((caption) => window.demoCaption?.(caption), text);
	await page.waitForTimeout(holdMs);
}

// Screenshots without the caption and the pointer: they stand on their own in the listing.
async function shot(page: Page, file: string) {
	const hidden = ["demo-caption", "demo-pointer"];
	await page.evaluate((ids) => ids.forEach((id) => document.getElementById(id)?.style.setProperty("visibility", "hidden")), hidden);
	await page.screenshot(file.endsWith(".jpg") ? { path: file, type: "jpeg", quality: 85 } : { path: file });
	await page.evaluate((ids) => ids.forEach((id) => document.getElementById(id)?.style.removeProperty("visibility")), hidden);
}

// One JPEG per composited frame, with its timestamp: the screencast only sends what changed.
async function record(page: Page, dir: string) {
	const frames: { file: string; at: number }[] = [];
	const cdp = await page.context().newCDPSession(page);
	cdp.on("Page.screencastFrame", async ({ data, metadata, sessionId }) => {
		const file = `f${String(frames.length).padStart(5, "0")}.jpg`;
		frames.push({ file, at: metadata.timestamp ?? Date.now() / 1000 });
		await writeFile(path.join(dir, file), Buffer.from(data, "base64"));
		await cdp.send("Page.screencastFrameAck", { sessionId }).catch(() => undefined);
	});
	await cdp.send("Page.startScreencast", {
		format: "jpeg",
		quality: 92,
		maxWidth: viewport.width * scale,
		maxHeight: viewport.height * scale,
		everyNthFrame: 1,
	});

	return async () => {
		await cdp.send("Page.stopScreencast");
		return { frames, end: Date.now() / 1000 };
	};
}

// The same review screen in English at device scale 2, for the README.
async function captureShowcase(browser: Browser, url: string) {
	const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2, locale: "en-US", baseURL });
	const page = await context.newPage();
	await page.addInitScript(() => window.localStorage.setItem("invoice-extractor.locale", "en"));
	await page.goto(url);
	await expect(page.getByRole("button", { name: "Approve" })).toBeVisible();
	await page.waitForTimeout(1000);
	await mkdir(readmeShots, { recursive: true });
	await page.screenshot({ path: path.join(readmeShots, "review.png") });
	await context.close();
}

test("demo", async ({ browser }) => {
	await mkdir(out, { recursive: true });
	const framesDir = test.info().outputPath("frames");
	await rm(framesDir, { recursive: true, force: true });
	await mkdir(framesDir, { recursive: true });

	const context = await browser.newContext({ viewport, deviceScaleFactor: scale, locale: "es-ES", baseURL });
	const page = await context.newPage();
	await addOverlay(page);
	await page.goto("/");
	await page.mouse.move(640, 300);
	const stop = await record(page, framesDir);

	await say(page, "Sube fotos o PDFs de facturas y recibos: salen como datos listos para contabilidad.", 5000);

	await say(page, "¿Sin facturas a mano? La app trae ejemplos: un PDF, un escaneo y la foto de un recibo.", 0);
	for (const file of files) {
		await page.getByRole("button", { name: `Agregar el ejemplo ${file}` }).click();
		await expect(page.getByRole("listitem").filter({ hasText: file })).toBeVisible();
		await page.waitForTimeout(700);
	}

	await expect(page.getByLabel("País de las facturas")).toHaveValue("XX");
	await page.waitForTimeout(2500);
	await shot(page, path.join(out, "01-subida.png"));

	await page.getByRole("button", { name: "Procesar 3 archivos" }).click();
	const batch = page.getByRole("article").first();
	await batch.scrollIntoViewIfNeeded();
	await say(page, "Claude Haiku lee cada documento, y reglas contables comprueban lo que leyó: sumas, impuestos, ID fiscal, fechas.", 0);
	await expect(batch.getByText(/En cola|Leyendo…/)).toHaveCount(0, { timeout: 120_000 });
	await page.waitForTimeout(1500);

	const automatic = await batch.getByText("Aprobado automáticamente").count();
	await say(page, `${automatic} de ${files.length} cumplen todas las reglas: se aprueban sin que nadie las toque.`, 5500);
	await shot(page, path.join(out, "02-lote.png"));

	await say(page, "Lo que no cuadra no pasa: va a revisión humana.", 2500);
	await batch.getByRole("link").filter({ hasText: "Requiere revisión" }).first().click();
	const approve = page.getByRole("button", { name: "Aprobar" });
	await expect(approve).toBeVisible();
	await captureShowcase(browser, new URL(page.url()).pathname);
	await page.waitForTimeout(1000);
	await say(page, "La revisión muestra el original al lado y marca el campo que falla: subtotal + impuesto no da el total.", 6500);
	await shot(page, path.join(out, "03-revision.png"));
	await shot(page, path.join(out, "poster.jpg"));

	const zoomIn = page.getByRole("button", { name: "Acercar" });
	if (await zoomIn.isVisible()) {
		await zoomIn.click();
		await zoomIn.click();
		await page.waitForTimeout(2500);
	}

	const amount = async (label: string) => Number((await page.getByLabel(label, { exact: true }).inputValue()).replace(",", "."));
	const [subtotal, tax, printed] = [await amount("Subtotal"), await amount("Impuesto"), await amount("Total")];
	const corrected = (subtotal + tax).toFixed(2);
	const es = (value: number) => value.toFixed(2).replace(".", ",");
	const total = page.getByLabel("Total", { exact: true });
	await total.scrollIntoViewIfNeeded();
	await say(page, `Debajo, los números de la regla: ${es(subtotal)} + ${es(tax)} da ${es(subtotal + tax)}, pero el recibo dice ${es(printed)}.`, 5000);
	await say(page, `El total impreso está mal sumado. La revisora lo corrige: ${corrected}.`, 1500);
	await total.click();
	await total.fill("");
	await total.pressSequentially(corrected, { delay: 140 });
	await page.waitForTimeout(1200);
	await approve.click();
	await expect(page.getByText("Este documento no está en revisión: solo lectura.")).toBeVisible();
	await say(page, "Aprobada: queda registrado quién la aprobó y con qué valores.", 4500);
	await shot(page, path.join(out, "04-aprobada.png"));

	await page.getByRole("link", { name: "Panel" }).click();
	await expect(page.getByText("Costo medio por documento")).toBeVisible();
	await page.mouse.move(640, 250);
	await say(page, "El panel: cuántas se aprueban solas, cuántas esperan revisión y cuánto cuesta leer cada una.", 7000);
	await shot(page, path.join(out, "05-panel.png"));

	const excel = page.getByRole("link", { name: "Descargar Excel" });
	await excel.hover();
	await say(page, "Exporta a Excel o CSV: montos como números y fechas como fechas.", 3000);
	const downloading = page.waitForEvent("download");
	await excel.click();
	await (await downloading).saveAs(path.join(out, "facturas-ejemplo.xlsx"));
	await say(page, "Extractor de facturas · .NET 10 · Claude Haiku · github.com/Juxn89/invoice-extractor", 5000);

	const { frames, end } = await stop();
	await context.close();

	// Each frame lasts until the next one.
	const list = frames.map((frame, i) => `file '${frame.file}'\nduration ${((frames[i + 1]?.at ?? end) - frame.at).toFixed(4)}`).join("\n");
	await writeFile(path.join(framesDir, "frames.txt"), `${list}\nfile '${frames.at(-1)?.file}'\n`);
	execFileSync(
		"docker",
		[
			"run", "--rm", "--network", "none", "--read-only", "--tmpfs", "/tmp",
			"-v", `${framesDir}:/work`, "-v", `${out}:/out`, "-w", "/work", ffmpegImage,
			"-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", "frames.txt",
			"-vf", `fps=${fps},scale=${viewport.width * scale}:${viewport.height * scale}:flags=lanczos,format=yuv420p`,
			"-c:v", "libx264", "-preset", "slow", "-crf", "20", "-movflags", "+faststart",
			"/out/demo.mp4",
		],
		{ stdio: "inherit" },
	);
	console.log(`${frames.length} frames, ${(end - (frames[0]?.at ?? end)).toFixed(1)} s → ${path.join(out, "demo.mp4")}`);
});
