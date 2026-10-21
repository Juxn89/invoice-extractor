"use client";

import { ArrowLeft, Check, ChevronLeft, ChevronRight, CircleAlert, Info, Plus, Trash2 } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { api, ApiProblem, type ReviewResponse, type ValidationIssue } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import {
	hasInvalidAmounts,
	isInvalidAmount,
	issuesByField,
	toForm,
	toInvoice,
	type InvoiceForm,
	type LineForm,
	type ScalarField,
} from "@/lib/invoiceForm";
import { DocumentViewer } from "./DocumentViewer";
import { StatusBadge } from "./StatusBadge";

const amountFields: ScalarField[] = ["subtotal", "taxTotal", "total"];
const headerFields: ScalarField[] = ["issuerName", "issuerTaxId", "receiverName", "number", "issueDate", "currency"];

type Loaded = { review: ReviewResponse; form: InvoiceForm };

export function ReviewScreen({ id }: { id: string }) {
	const { t } = useI18n();
	const router = useRouter();
	const [loaded, setLoaded] = useState<Loaded | null>(null);
	const [missing, setMissing] = useState(false);
	const [queue, setQueue] = useState<string[]>([]);
	const [saving, setSaving] = useState(false);
	const [error, setError] = useState<string | null>(null);
	const [notice, setNotice] = useState<string | null>(null);

	useEffect(() => {
		let active = true;
		api
			.review(id)
			.then((review) => active && setLoaded({ review, form: toForm(review.invoice) }))
			.catch((problem) => active && problem instanceof ApiProblem && problem.status === 404 && setMissing(true));
		api
			.reviewQueue()
			.then((documents) => active && setQueue(documents.map((d) => d.id)))
			.catch(() => undefined);
		return () => {
			active = false;
		};
	}, [id]);

	const issues = useMemo(() => issuesByField(loaded?.review.issues ?? []), [loaded]);

	if (missing) {
		return <p className="pt-10 text-muted">{t.review.notFound}</p>;
	}

	if (!loaded) {
		return <p className="pt-10 text-muted">{t.review.loading}</p>;
	}

	const { review, form } = loaded;
	const editable = review.document.status === "NeedsReview";
	const position = queue.indexOf(id);
	const previous = position > 0 ? queue[position - 1] : undefined;
	const next = position >= 0 && position < queue.length - 1 ? queue[position + 1] : undefined;
	const cost = review.costUsd.toFixed(6);

	const update = (change: (form: InvoiceForm) => InvoiceForm) => setLoaded({ review, form: change(form) });
	const setField = (field: ScalarField, value: string) => update((f) => ({ ...f, [field]: value }));
	const setLine = (index: number, field: keyof LineForm, value: string) =>
		update((f) => ({ ...f, lines: f.lines.map((line, i) => (i === index ? { ...line, [field]: value } : line)) }));

	const approve = async () => {
		setSaving(true);
		setError(null);
		try {
			const approved = await api.approve(id, toInvoice(form));
			const following = queue.filter((other) => other !== id);
			const after = following[position] ?? following[position - 1];
			if (after) {
				router.push(`/review/${after}`);
				return;
			}

			setLoaded({ review: approved, form: toForm(approved.invoice) });
			setQueue(following);
			setNotice(approved.issues.length > 0 ? t.review.approvedWithIssues : null);
		} catch {
			setError(t.review.approveFailed);
		} finally {
			setSaving(false);
		}
	};

	const field = (name: ScalarField) => {
		const fieldIssues = issues.get(name) ?? [];
		const unsure = review.confidence[name] === "Low";
		const invalid = amountFields.includes(name) && isInvalidAmount(form[name]);
		return (
			<Field
				key={name}
				id={`field-${name}`}
				label={t.review.fields[name]}
				value={form[name]}
				onChange={(value) => setField(name, value)}
				disabled={!editable}
				issues={fieldIssues}
				unsure={unsure}
				invalid={invalid}
				numeric={amountFields.includes(name)}
				hint={name === "issuerTaxId" && fieldIssues.some((i) => i.code === "InvalidTaxId") ? `${t.review.expectedFormat} ${review.taxIdFormat}` : undefined}
			/>
		);
	};

	return (
		<div className="pt-5">
			<div className="mb-4 flex flex-wrap items-center gap-x-4 gap-y-2">
				<Link href="/" className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-ink">
					<ArrowLeft className="size-4" aria-hidden />
					{t.review.back}
				</Link>
				<h1 className="min-w-0 truncate font-semibold">{review.document.fileName}</h1>
				<StatusBadge status={review.document.status} />
				<span className="tabular text-xs text-muted">{t.review.cost(review.calls, cost)}</span>

				{position >= 0 && (
					<nav aria-label={t.review.title} className="ml-auto flex items-center gap-1 text-sm">
						<span className="mr-1 text-muted">
							{t.review.title} · {t.review.position(position + 1, queue.length)}
						</span>
						<NavButton href={previous && `/review/${previous}`} label={t.review.previous}>
							<ChevronLeft className="size-4" aria-hidden />
						</NavButton>
						<NavButton href={next && `/review/${next}`} label={t.review.next}>
							<ChevronRight className="size-4" aria-hidden />
						</NavButton>
					</nav>
				)}
			</div>

			<div className="grid gap-6 lg:grid-cols-2">
				<div className="h-[70vh] lg:sticky lg:top-[4.5rem] lg:h-[calc(100vh-10rem)]">
					<DocumentViewer src={api.fileUrl(id)} format={review.document.format} />
				</div>

				<form
					onSubmit={(event) => {
						event.preventDefault();
						void approve();
					}}
					className="space-y-5 rounded-xl border border-line bg-card p-5 shadow-sm"
				>
					<IssueSummary issues={review.issues} failure={review.failure} />
					{!editable && <p className="rounded-md bg-black/5 px-3 py-2 text-sm text-muted">{t.review.readOnly}</p>}
					{notice && <p className="rounded-md bg-warn-soft px-3 py-2 text-sm text-warn">{notice}</p>}

					<div className="grid gap-4 sm:grid-cols-2">{headerFields.map(field)}</div>

					<fieldset>
						<legend className="mb-2 flex w-full items-center text-sm font-semibold">
							{t.review.lines.title}
							{review.confidence.lines === "Low" && <span className="ml-2 text-xs font-normal text-warn">{t.review.lowConfidence}</span>}
						</legend>
						<div className="overflow-x-auto rounded-lg border border-line">
							<table className="w-full text-sm">
								<thead className="bg-paper/70 text-left text-xs text-muted">
									<tr>
										<th className="px-2 py-2 font-medium">{t.review.lines.description}</th>
										<th className="w-20 px-2 py-2 text-right font-medium">{t.review.lines.quantity}</th>
										<th className="w-28 px-2 py-2 text-right font-medium">{t.review.lines.unitPrice}</th>
										<th className="w-28 px-2 py-2 text-right font-medium">{t.review.lines.lineTotal}</th>
										{editable && <th className="w-9" />}
									</tr>
								</thead>
								<tbody className="divide-y divide-line">
									{form.lines.map((line, index) => {
										const lineIssues = issues.get(`lines[${index}].lineTotal`) ?? [];
										return (
											<tr key={index} className={lineIssues.length > 0 ? "bg-danger-soft/60" : undefined}>
												<td className="px-1 py-1">
													<CellInput value={line.description} onChange={(v) => setLine(index, "description", v)} disabled={!editable} label={t.review.lines.description} />
												</td>
												{(["quantity", "unitPrice", "lineTotal"] as const).map((name) => (
													<td key={name} className="px-1 py-1">
														<CellInput
															value={line[name]}
															onChange={(v) => setLine(index, name, v)}
															disabled={!editable}
															numeric
															invalid={isInvalidAmount(line[name])}
															flagged={name === "lineTotal" && lineIssues.length > 0}
															label={t.review.lines[name]}
														/>
													</td>
												))}
												{editable && (
													<td className="px-1 text-center">
														<button
															type="button"
															onClick={() => update((f) => ({ ...f, lines: f.lines.filter((_, i) => i !== index) }))}
															aria-label={t.review.lines.remove}
															className="rounded p-1 text-muted hover:bg-black/5 hover:text-danger"
														>
															<Trash2 className="size-4" aria-hidden />
														</button>
													</td>
												)}
											</tr>
										);
									})}
								</tbody>
							</table>
						</div>
						{form.lines.some((_, index) => issues.has(`lines[${index}].lineTotal`)) && (
							<IssueMessages issues={form.lines.flatMap((_, index) => issues.get(`lines[${index}].lineTotal`) ?? [])} />
						)}
						{editable && (
							<button
								type="button"
								onClick={() => update((f) => ({ ...f, lines: [...f.lines, { description: "", quantity: "1", unitPrice: "", lineTotal: "" }] }))}
								className="mt-2 inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-sm text-brand-dark hover:bg-brand-soft"
							>
								<Plus className="size-4" aria-hidden />
								{t.review.lines.add}
							</button>
						)}
					</fieldset>

					<div className="grid gap-4 sm:ml-auto sm:max-w-sm">{amountFields.map(field)}</div>

					{error && <p className="rounded-md bg-danger-soft px-3 py-2 text-sm text-danger">{error}</p>}

					{editable && (
						<div className="flex justify-end border-t border-line pt-4">
							<button
								type="submit"
								disabled={saving || hasInvalidAmounts(form)}
								className="inline-flex items-center gap-2 rounded-md bg-brand px-5 py-2.5 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-brand-dark disabled:cursor-not-allowed disabled:opacity-40"
							>
								<Check className="size-4" aria-hidden />
								{saving ? t.review.approving : t.review.approve}
							</button>
						</div>
					)}
				</form>
			</div>
		</div>
	);
}

function NavButton({ href, label, children }: { href: string | undefined; label: string; children: React.ReactNode }) {
	const className = "grid size-8 place-items-center rounded-md border border-line bg-card";
	return href ? (
		<Link href={href} aria-label={label} className={`${className} hover:border-brand hover:text-brand-dark`}>
			{children}
		</Link>
	) : (
		<span aria-hidden className={`${className} opacity-30`}>
			{children}
		</span>
	);
}

function IssueSummary({ issues, failure }: { issues: ValidationIssue[]; failure: string | null }) {
	const { t } = useI18n();
	if (failure) {
		return (
			<p className="flex gap-2 rounded-md bg-danger-soft px-3 py-2.5 text-sm text-danger">
				<CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
				<span>
					{t.review.failure} {failure}
				</span>
			</p>
		);
	}

	if (issues.length === 0) {
		return (
			<p className="flex items-center gap-2 rounded-md bg-brand-soft px-3 py-2.5 text-sm text-brand-dark">
				<Check className="size-4" aria-hidden />
				{t.review.noIssues}
			</p>
		);
	}

	return (
		<div className="rounded-md border border-danger/20 bg-danger-soft px-3 py-2.5 text-sm text-danger">
			<p className="flex items-center gap-2 font-semibold">
				<CircleAlert className="size-4" aria-hidden />
				{t.review.issues(issues.length)}
			</p>
			<ul className="mt-1.5 list-disc space-y-0.5 pl-6">
				{issues.map((issue, index) => (
					<li key={index}>{t.issues[issue.code]}</li>
				))}
			</ul>
		</div>
	);
}

function IssueMessages({ issues }: { issues: ValidationIssue[] }) {
	const { t } = useI18n();
	return (
		<ul className="mt-1.5 space-y-0.5 text-xs text-danger">
			{issues.map((issue, index) => (
				<li key={index}>
					<span className="font-medium">{t.issues[issue.code]}</span>{" "}
					{/* The rule's own wording, with the numbers it compared; written by the domain, in English. */}
					<span className="text-danger/70">({issue.detail})</span>
				</li>
			))}
		</ul>
	);
}

type FieldProps = {
	id: string;
	label: string;
	value: string;
	onChange: (value: string) => void;
	disabled: boolean;
	issues: ValidationIssue[];
	unsure: boolean;
	invalid: boolean;
	numeric: boolean;
	hint?: string;
};

function Field({ id, label, value, onChange, disabled, issues, unsure, invalid, numeric, hint }: FieldProps) {
	const { t } = useI18n();
	const flagged = issues.length > 0 || invalid;
	const tone = flagged
		? "border-danger ring-2 ring-danger/15"
		: unsure
			? "border-warn ring-2 ring-warn/15"
			: "border-line focus:border-brand focus:ring-2 focus:ring-brand/20";
	return (
		<div>
			<label htmlFor={id} className="flex items-center gap-1.5 text-sm font-medium">
				{label}
				{flagged && <CircleAlert className="size-3.5 text-danger" aria-hidden />}
				{!flagged && unsure && <Info className="size-3.5 text-warn" aria-hidden />}
			</label>
			<input
				id={id}
				value={value}
				onChange={(event) => onChange(event.target.value)}
				disabled={disabled}
				inputMode={numeric ? "decimal" : undefined}
				aria-invalid={flagged || undefined}
				className={`mt-1 block w-full rounded-md border bg-card px-3 py-2 text-sm outline-none disabled:bg-paper/60 disabled:text-ink ${numeric ? "tabular text-right" : ""} ${tone}`}
			/>
			{issues.length > 0 && <IssueMessages issues={issues} />}
			{hint && <p className="mt-1 text-xs text-muted">{hint}</p>}
			{!flagged && unsure && <p className="mt-1 text-xs text-warn">{t.review.lowConfidence}</p>}
		</div>
	);
}

type CellProps = {
	value: string;
	onChange: (value: string) => void;
	disabled: boolean;
	label: string;
	numeric?: boolean;
	invalid?: boolean;
	flagged?: boolean;
};

function CellInput({ value, onChange, disabled, label, numeric, invalid, flagged }: CellProps) {
	return (
		<input
			value={value}
			onChange={(event) => onChange(event.target.value)}
			disabled={disabled}
			aria-label={label}
			inputMode={numeric ? "decimal" : undefined}
			aria-invalid={invalid || flagged || undefined}
			className={`w-full rounded border bg-transparent px-2 py-1.5 outline-none disabled:text-ink ${numeric ? "tabular text-right" : ""} ${
				invalid || flagged ? "border-danger bg-card" : "border-transparent hover:border-line focus:border-brand focus:bg-card"
			}`}
		/>
	);
}
