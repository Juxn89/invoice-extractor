import { expect, test, type Page } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import path from "node:path";

// The 60-90 second walkthrough for the README and the Workana listing, recorded against the real model
// (playwright.demo.config.ts). It writes the video, the screenshots and the exported workbook to docs/demo.

const samples = path.resolve(__dirname, "../../../evals/samples");
const out = path.resolve(__dirname, "../../../docs/demo");

// A digital PDF, a scan and a phone photo of a receipt, all under the generic rules.
const files = ["sample-09.pdf", "sample-10.png", "sample-11.jpg"];

declare global {
	interface Window {
		demoCaption?: (text: string) => void;
	}
}

// A recorded browser shows neither the mouse nor what is going on: a pointer that follows the mouse, and a caption.
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
async function shot(page: Page, name: string) {
	const hidden = ["demo-caption", "demo-pointer"];
	await page.evaluate((ids) => ids.forEach((id) => document.getElementById(id)?.style.setProperty("visibility", "hidden")), hidden);
	await page.screenshot({ path: path.join(out, name) });
	await page.evaluate((ids) => ids.forEach((id) => document.getElementById(id)?.style.removeProperty("visibility")), hidden);
}

test("demo", async ({ page }) => {
	await mkdir(out, { recursive: true });
	await addOverlay(page);
	await page.goto("/");
	await page.mouse.move(640, 300);
	await say(page, "Sube fotos o PDFs de facturas y recibos: salen como datos listos para contabilidad.", 5000);

	await page.locator('input[type="file"]').setInputFiles(files.map((file) => path.join(samples, file)));
	await page.getByLabel("País de las facturas").selectOption("XX");
	await say(page, "Un PDF, un escaneo y la foto de un recibo.", 4000);
	await shot(page, "01-subida.png");

	await page.getByRole("button", { name: "Procesar 3 archivos" }).click();
	const batch = page.getByRole("article").first();
	await batch.scrollIntoViewIfNeeded();
	await say(page, "Claude Haiku lee cada documento, y reglas contables comprueban lo que leyó: sumas, impuestos, ID fiscal, fechas.", 0);
	await expect(batch.getByText(/En cola|Leyendo…/)).toHaveCount(0, { timeout: 120_000 });
	await page.waitForTimeout(1500);

	const automatic = await batch.getByText("Aprobado automáticamente").count();
	await say(page, `${automatic} de ${files.length} cumplen todas las reglas: se aprueban sin que nadie las toque.`, 5500);
	await shot(page, "02-lote.png");

	await say(page, "Lo que no cuadra no pasa: va a revisión humana.", 2500);
	await batch.getByRole("link").filter({ hasText: "Requiere revisión" }).first().click();
	const approve = page.getByRole("button", { name: "Aprobar" });
	await expect(approve).toBeVisible();
	await page.waitForTimeout(1000);
	await say(page, "La revisión muestra el original al lado y marca el campo que falla: subtotal + impuesto no da el total.", 6500);
	await shot(page, "03-revision.png");

	const zoomIn = page.getByRole("button", { name: "Acercar" });
	if (await zoomIn.isVisible()) {
		await zoomIn.click();
		await zoomIn.click();
		await page.waitForTimeout(2500);
	}

	const amount = async (label: string) => Number((await page.getByLabel(label, { exact: true }).inputValue()).replace(",", "."));
	const corrected = ((await amount("Subtotal")) + (await amount("Impuesto"))).toFixed(2);
	await say(page, `En el recibo, el total impreso está mal sumado. La revisora lo corrige: ${corrected}.`, 1500);
	const total = page.getByLabel("Total", { exact: true });
	await total.click();
	await total.fill("");
	await total.pressSequentially(corrected, { delay: 140 });
	await page.waitForTimeout(1200);
	await approve.click();
	await expect(page.getByText("Este documento no está en revisión: solo lectura.")).toBeVisible();
	await say(page, "Aprobada: queda registrado quién la aprobó y con qué valores.", 4500);
	await shot(page, "04-aprobada.png");

	await page.getByRole("link", { name: "Panel" }).click();
	await expect(page.getByText("Costo medio por documento")).toBeVisible();
	await page.mouse.move(640, 250);
	await say(page, "El panel: cuántas se aprueban solas, cuántas esperan revisión y cuánto cuesta leer cada una.", 7000);
	await shot(page, "05-panel.png");

	const excel = page.getByRole("link", { name: "Descargar Excel" });
	await excel.hover();
	await say(page, "Exporta a Excel o CSV: montos como números y fechas como fechas.", 3000);
	const downloading = page.waitForEvent("download");
	await excel.click();
	await (await downloading).saveAs(path.join(out, "facturas-ejemplo.xlsx"));
	await say(page, "Extractor de facturas · .NET 10 · Claude Haiku · github.com/Juxn89/invoice-extractor", 5000);

	const video = page.video();
	await page.close();
	await video?.saveAs(path.join(out, "demo.webm"));
});
