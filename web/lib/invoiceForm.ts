import type { Invoice, ValidationIssue } from "./api";

// The form keeps every value as the text the reviewer typed; numbers are parsed only when approving, so a
// half-typed "1.2" or an empty field never fights the input.
export type LineForm = { description: string; quantity: string; unitPrice: string; lineTotal: string };

export type InvoiceForm = {
	issuerName: string;
	issuerTaxId: string;
	receiverName: string;
	number: string;
	issueDate: string;
	currency: string;
	subtotal: string;
	taxTotal: string;
	total: string;
	lines: LineForm[];
};

export type ScalarField = Exclude<keyof InvoiceForm, "lines">;

const text = (value: string | number | null | undefined) => (value === null || value === undefined ? "" : String(value));

// Money shows with its cents (1531 reads as 1531.00), as it is printed; quantities stay as read (1.5, 2).
const money = (value: number | null | undefined) => (value === null || value === undefined ? "" : value.toFixed(2));

export function toForm(invoice: Invoice | null): InvoiceForm {
	return {
		issuerName: text(invoice?.issuerName),
		issuerTaxId: text(invoice?.issuerTaxId),
		receiverName: text(invoice?.receiverName),
		number: text(invoice?.number),
		issueDate: text(invoice?.issueDate),
		currency: text(invoice?.currency),
		subtotal: money(invoice?.subtotal),
		taxTotal: money(invoice?.taxTotal),
		total: money(invoice?.total),
		lines: (invoice?.lines ?? []).map((line) => ({
			description: line.description,
			quantity: text(line.quantity),
			unitPrice: money(line.unitPrice),
			lineTotal: money(line.lineTotal),
		})),
	};
}

/** "1234,56" (Spanish keyboards) and "1234.56" both read as 1234.56; anything else is not a number. */
export function parseAmount(value: string): number | null {
	const trimmed = value.trim();
	if (trimmed === "") {
		return null;
	}

	const normalised = trimmed.includes(".") ? trimmed.replaceAll(",", "") : trimmed.replace(",", ".");
	return /^-?\d+(\.\d+)?$/.test(normalised) ? Number(normalised) : Number.NaN;
}

export function isInvalidAmount(value: string) {
	return Number.isNaN(parseAmount(value));
}

const orNull = (value: string) => (value.trim() === "" ? null : value.trim());

export function toInvoice(form: InvoiceForm): Invoice {
	return {
		issuerName: orNull(form.issuerName),
		issuerTaxId: orNull(form.issuerTaxId),
		receiverName: orNull(form.receiverName),
		number: orNull(form.number),
		issueDate: orNull(form.issueDate),
		currency: orNull(form.currency)?.toUpperCase() ?? null,
		subtotal: parseAmount(form.subtotal),
		taxTotal: parseAmount(form.taxTotal),
		total: parseAmount(form.total),
		lines: form.lines.map((line) => ({
			description: line.description.trim(),
			quantity: parseAmount(line.quantity) ?? 0,
			unitPrice: parseAmount(line.unitPrice) ?? 0,
			lineTotal: parseAmount(line.lineTotal) ?? 0,
		})),
	};
}

/** Whether every amount the reviewer typed is a number, so approving cannot send NaN. */
export function hasInvalidAmounts(form: InvoiceForm) {
	return (
		[form.subtotal, form.taxTotal, form.total].some(isInvalidAmount) ||
		form.lines.some((line) => [line.quantity, line.unitPrice, line.lineTotal].some(isInvalidAmount))
	);
}

/** Issues keyed by the field they point at; a line's issue (lines[2].lineTotal) also marks line 2. */
export function issuesByField(issues: ValidationIssue[]) {
	const map = new Map<string, ValidationIssue[]>();
	for (const issue of issues) {
		map.set(issue.field, [...(map.get(issue.field) ?? []), issue]);
	}

	return map;
}
