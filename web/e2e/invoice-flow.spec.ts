import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { readFirstSheet } from "./xlsx";

// Columns of the "Facturas" sheet (src/InvoiceExtractor.Infrastructure/Export/ExportColumns.cs).
const FileColumn = 1;
const ApprovedByColumn = 2;
const TotalColumn = 12;

// The recorded answers (e2e/fixtures/replay.json) read sample-09 and sample-12 right, and misread sample-01's
// total as 7571.61; the printed total is 7517.61.
test("a batch goes from upload to an approved Excel row, through human review", async ({ page }) => {
	await page.goto("/");
	// The examples the API offers, fetched and uploaded like picked files; the first one sets the country.
	for (const file of ["sample-09.pdf", "sample-12.pdf", "sample-01.pdf"]) {
		await page.getByRole("button", { name: `Agregar el ejemplo ${file}` }).click();
		await expect(page.getByRole("listitem").filter({ hasText: file })).toBeVisible();
	}

	await expect(page.getByLabel("País de las facturas")).toHaveValue("XX");
	await page.getByRole("button", { name: "Procesar 3 archivos" }).click();

	const batch = page.getByRole("article").first();
	const row = (file: string) => batch.getByRole("link", { name: new RegExp(file) });
	await expect(row("sample-09.pdf")).toContainText("Aprobado automáticamente");
	await expect(row("sample-12.pdf")).toContainText("Aprobado automáticamente");
	await expect(row("sample-01.pdf")).toContainText("Requiere revisión");

	await row("sample-01.pdf").click();
	await expect(page.getByRole("heading", { name: "sample-01.pdf" })).toBeVisible();
	// The rules failed, the model was asked again with what failed, and misread it again.
	await expect(page.getByText("2 lecturas del modelo")).toBeVisible();
	await expect(page.getByText("Subtotal más impuesto no da el total.").first()).toBeVisible();
	// The rule's numbers, worded in Spanish with Spanish number formatting.
	await expect(page.getByText("da 7517,61; el total dice 7571,61.")).toBeVisible();

	const total = page.getByLabel("Total", { exact: true });
	await expect(total).toHaveValue("7571.61");
	await expect(total).toHaveAttribute("aria-invalid", "true");
	await total.fill("7517.61");
	await page.getByRole("button", { name: "Aprobar" }).click();
	await expect(page.getByText("Este documento no está en revisión: solo lectura.")).toBeVisible();
	await expect(page.getByText("Todas las reglas se cumplen.")).toBeVisible();

	await page.getByRole("link", { name: "Volver a los lotes" }).click();
	const downloading = page.waitForEvent("download");
	await page.getByRole("link", { name: "Excel del lote" }).first().click();
	const download = await downloading;
	expect(download.suggestedFilename()).toMatch(/^facturas-\d{4}-\d{2}-\d{2}\.xlsx$/);

	const workbook = test.info().outputPath(download.suggestedFilename());
	await download.saveAs(workbook);
	const [header, ...rows] = readFirstSheet(await readFile(workbook));
	expect(header?.[TotalColumn]).toBe("Total");
	expect(rows).toHaveLength(3);

	const byFile = new Map(rows.map((cells) => [cells[FileColumn], cells]));
	expect(byFile.get("sample-01.pdf")?.[ApprovedByColumn]).toBe("Revisión humana");
	expect(byFile.get("sample-01.pdf")?.[TotalColumn]).toBe("7517.61");
	expect(byFile.get("sample-09.pdf")?.[ApprovedByColumn]).toBe("Reglas (automático)");
	expect(byFile.get("sample-12.pdf")?.[ApprovedByColumn]).toBe("Reglas (automático)");
});
