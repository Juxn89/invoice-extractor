// The API's contract, as the .NET side serialises it (camelCase, enums as names, decimals as numbers).

export type DocumentStatus = "Queued" | "Processing" | "AutoApproved" | "NeedsReview" | "Approved" | "Failed" | "Duplicate";
export type DocumentFormat = "Pdf" | "Jpeg" | "Png";
export type Country = "NI" | "MX" | "XX";
export type FieldConfidence = "Low" | "Medium" | "High";
export type IssueCode =
	| "MissingField"
	| "LinesDoNotMatchSubtotal"
	| "LineArithmetic"
	| "TotalMismatch"
	| "ImplausibleTaxRate"
	| "InvalidTaxId"
	| "FutureDate"
	| "DateTooOld";

export type DocumentResponse = {
	id: string;
	batchId: string;
	fileName: string;
	format: DocumentFormat;
	pages: number;
	hasTextLayer: boolean;
	sizeBytes: number;
	status: DocumentStatus;
	attempts: number;
	createdAt: string;
	processedAt: string | null;
	// When Duplicate: the settled document of the same invoice (same issuer tax id, number and total).
	duplicateOf: string | null;
};

export type BatchResponse = {
	id: string;
	country: Country;
	createdAt: string;
	documents: DocumentResponse[];
};

export type ApiError = { code: string; message: string; kind: string };

export type FileResult = {
	fileName: string;
	outcome: "Queued" | "Duplicate" | "Rejected";
	document: DocumentResponse | null;
	error: ApiError | null;
};

export type UploadResponse = { batchId: string | null; country: Country; files: FileResult[] };

export type InvoiceLine = { description: string; quantity: number; unitPrice: number; lineTotal: number };

export type Invoice = {
	issuerName: string | null;
	issuerTaxId: string | null;
	receiverName: string | null;
	number: string | null;
	issueDate: string | null;
	currency: string | null;
	lines: InvoiceLine[];
	subtotal: number | null;
	taxTotal: number | null;
	total: number | null;
};

// values: the rule's numbers, invariant, for the screen to word in its language (absent on older readings).
export type ValidationIssue = { field: string; code: IssueCode; detail: string; values?: Record<string, string> | null };

export type ReviewResponse = {
	document: DocumentResponse;
	country: Country;
	taxIdFormat: string;
	invoice: Invoice | null;
	issues: ValidationIssue[];
	confidence: Record<string, FieldConfidence>;
	failure: string | null;
	calls: number;
	costUsd: number;
};

/** A request the API refused, carrying its stable error code (docs/errors.md). */
export class ApiProblem extends Error {
	constructor(
		readonly status: number,
		readonly code: string,
		message: string,
		// The problem's other members, e.g. originalId on review.duplicate_invoice.
		readonly details: Record<string, unknown> = {},
	) {
		super(message);
	}
}

const base = "/api/v1";

async function send<T>(path: string, init?: RequestInit): Promise<T> {
	const response = await fetch(`${base}${path}`, { cache: "no-store", ...init });
	if (!response.ok) {
		const problem = (await response.json().catch(() => null)) as ({ code?: string; title?: string } & Record<string, unknown>) | null;
		throw new ApiProblem(response.status, problem?.code ?? "unknown", problem?.title ?? response.statusText, problem ?? {});
	}

	return (await response.json()) as T;
}

export type SampleKind = "DigitalPdf" | "Scan" | "Photo" | "ThermalReceipt";
export type Sample = { file: string; country: Country; kind: SampleKind; language: string };

export type Dashboard = {
	documents: number;
	byStatus: Record<DocumentStatus, number>;
	decided: number;
	autoApprovedRate: number;
	reviewRate: number;
	modelCalls: number;
	retries: number;
	costUsd: number;
	costPerDocumentUsd: number;
	spentTodayUsd: number;
	dailyBudgetUsd: number;
};

export const api = {
	dashboard: () => send<Dashboard>("/dashboard"),
	exportUrl: (format: "xlsx" | "csv", lang: string, batchId?: string) =>
		`${base}/export?format=${format}&lang=${lang}${batchId ? `&batchId=${batchId}` : ""}`,
	batches: () => send<BatchResponse[]>("/batches"),
	upload: (files: File[], country: Country) => {
		const form = new FormData();
		for (const file of files) {
			form.append("files", file, file.name);
		}

		form.append("country", country);
		return send<UploadResponse>("/batches", { method: "POST", body: form });
	},
	reviewQueue: () => send<DocumentResponse[]>("/review-queue"),
	review: (id: string) => send<ReviewResponse>(`/documents/${id}/review`),
	approve: (id: string, invoice: Invoice) =>
		send<ReviewResponse>(`/documents/${id}/approve`, {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify({ invoice }),
		}),
	fileUrl: (id: string) => `${base}/documents/${id}/file`,
	samples: () => send<Sample[]>("/samples"),
	// The example as a File, uploaded afterwards like any file the user picked.
	sampleFile: async (sample: Sample) => {
		const response = await fetch(`${base}/samples/${encodeURIComponent(sample.file)}`);
		if (!response.ok) {
			throw new ApiProblem(response.status, "sample.not_found", response.statusText);
		}

		const blob = await response.blob();
		return new File([blob], sample.file, { type: blob.type });
	},
};

/** Statuses a document can still leave on its own, so the upload screen keeps polling while one is shown. */
export const inFlight: readonly DocumentStatus[] = ["Queued", "Processing"];
