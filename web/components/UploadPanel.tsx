"use client";

import { FileImage, FileText, UploadCloud, X } from "lucide-react";
import { useEffect, useRef, useState, type DragEvent } from "react";
import { api, ApiProblem, type Country, type Sample, type UploadResponse } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

const countries: Country[] = ["NI", "MX", "XX"];
const accepted = ".pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png";

// The API checks the real limits (and the real format, from the bytes); these only spare a pointless upload.
const MaxFiles = 20;

type Props = { onUploaded: (response: UploadResponse) => void };

export function UploadPanel({ onUploaded }: Props) {
	const { t } = useI18n();
	const input = useRef<HTMLInputElement>(null);
	const [files, setFiles] = useState<File[]>([]);
	const [country, setCountry] = useState<Country>("NI");
	const [dragging, setDragging] = useState(false);
	const [sending, setSending] = useState(false);
	const [result, setResult] = useState<UploadResponse | null>(null);
	const [error, setError] = useState<string | null>(null);
	const [samples, setSamples] = useState<Sample[]>([]);
	const [fetching, setFetching] = useState<string | null>(null);

	// Offered only when the API has examples configured; without them the section is simply not there.
	useEffect(() => {
		let active = true;
		api
			.samples()
			.then((list) => active && setSamples(list))
			.catch(() => undefined);
		return () => {
			active = false;
		};
	}, []);

	const add = (incoming: FileList | File[] | null) => {
		if (!incoming) {
			return;
		}

		setResult(null);
		setError(null);
		setFiles((current) => {
			const known = new Set(current.map((f) => `${f.name}:${f.size}`));
			const fresh = [...incoming].filter((f) => !known.has(`${f.name}:${f.size}`));
			return [...current, ...fresh].slice(0, MaxFiles);
		});
	};

	const addSample = async (sample: Sample) => {
		setFetching(sample.file);
		setError(null);
		try {
			const file = await api.sampleFile(sample);
			// The first example picks the rules; a batch is validated with one country.
			if (files.length === 0) {
				setCountry(sample.country);
			}

			add([file]);
		} catch {
			setError(t.upload.failed);
		} finally {
			setFetching(null);
		}
	};

	const onDrop = (event: DragEvent) => {
		event.preventDefault();
		setDragging(false);
		add(event.dataTransfer.files);
	};

	const send = async () => {
		setSending(true);
		setError(null);
		try {
			const response = await api.upload(files, country);
			setResult(response);
			setFiles([]);
			onUploaded(response);
		} catch (problem) {
			const code = problem instanceof ApiProblem ? problem.code : "unknown";
			setError(t.errors[code as keyof typeof t.errors] ?? t.upload.failed);
		} finally {
			setSending(false);
		}
	};

	return (
		<section aria-labelledby="upload-title" className="rounded-xl border border-line bg-card p-5 shadow-sm sm:p-6">
			<h2 id="upload-title" className="text-lg font-semibold tracking-tight">
				{t.upload.title}
			</h2>

			<div
				onDragOver={(event) => {
					event.preventDefault();
					setDragging(true);
				}}
				onDragLeave={() => setDragging(false)}
				onDrop={onDrop}
				className={`mt-4 flex flex-col items-center justify-center gap-3 rounded-lg border-2 border-dashed px-6 py-10 text-center transition-colors ${
					dragging ? "border-brand bg-brand-soft" : "border-line bg-paper/60"
				}`}
			>
				<UploadCloud className="size-9 text-brand" aria-hidden />
				<p className="font-medium">{t.upload.dropTitle}</p>
				<p className="text-sm text-muted">{t.upload.dropHint}</p>
				<button
					type="button"
					onClick={() => input.current?.click()}
					className="mt-1 rounded-md border border-line bg-card px-4 py-2 text-sm font-medium shadow-sm transition-colors hover:border-brand hover:text-brand-dark"
				>
					{t.upload.choose}
				</button>
				<input
					ref={input}
					type="file"
					multiple
					accept={accepted}
					className="sr-only"
					onChange={(event) => {
						add(event.target.files);
						event.target.value = "";
					}}
				/>
			</div>

			{samples.length > 0 && (
				<div className="mt-4">
					<p className="text-sm font-medium">{t.upload.samples.title}</p>
					<p className="text-xs text-muted">{t.upload.samples.hint}</p>
					<div className="mt-2 space-y-1.5">
						{countries
							.filter((code) => samples.some((s) => s.country === code))
							.map((code) => (
								<div key={code} className="flex flex-wrap items-center gap-1.5 text-sm">
									<span className="w-20 shrink-0 text-xs text-muted">{t.upload.samples.countries[code]}</span>
									{samples
										.filter((s) => s.country === code)
										.map((sample, index, group) => (
											<button
												key={sample.file}
												type="button"
												onClick={() => void addSample(sample)}
												disabled={fetching !== null || files.some((f) => f.name === sample.file)}
												aria-label={t.upload.samples.add(sample.file)}
												title={sample.file}
												className="rounded-full border border-line bg-card px-2.5 py-1 text-xs font-medium transition-colors hover:border-brand hover:text-brand-dark disabled:cursor-not-allowed disabled:opacity-40"
											>
												{t.upload.samples.kinds[sample.kind]}
												{/* A second example of the same kind in one country gets a number: "PDF", "PDF 2". */}
												{group.slice(0, index).some((other) => other.kind === sample.kind) &&
													` ${group.slice(0, index + 1).filter((other) => other.kind === sample.kind).length}`}
											</button>
										))}
								</div>
							))}
					</div>
				</div>
			)}

			{files.length > 0 && (
				<ul className="mt-4 divide-y divide-line rounded-lg border border-line">
					{files.map((file) => (
						<li key={`${file.name}:${file.size}`} className="flex items-center gap-3 px-3 py-2 text-sm">
							{file.type === "application/pdf" ? (
								<FileText className="size-4 shrink-0 text-muted" aria-hidden />
							) : (
								<FileImage className="size-4 shrink-0 text-muted" aria-hidden />
							)}
							<span className="truncate">{file.name}</span>
							<span className="tabular ml-auto text-xs text-muted">{(file.size / 1024).toFixed(0)} KB</span>
							<button
								type="button"
								onClick={() => setFiles((current) => current.filter((f) => f !== file))}
								aria-label={`${t.upload.remove} ${file.name}`}
								className="rounded p-1 text-muted hover:bg-black/5 hover:text-ink"
							>
								<X className="size-4" aria-hidden />
							</button>
						</li>
					))}
				</ul>
			)}

			<div className="mt-5 flex flex-col gap-4 sm:flex-row sm:items-end">
				<label className="flex-1 text-sm">
					<span className="font-medium">{t.upload.country}</span>
					<select
						value={country}
						onChange={(event) => setCountry(event.target.value as Country)}
						className="mt-1.5 block w-full rounded-md border border-line bg-card px-3 py-2 focus:border-brand focus:ring-2 focus:ring-brand/20 focus:outline-none"
					>
						{countries.map((code) => (
							<option key={code} value={code}>
								{t.countries[code]}
							</option>
						))}
					</select>
					<span className="mt-1 block text-xs text-muted">{t.upload.countryHint}</span>
				</label>

				<div className="flex gap-2">
					{files.length > 0 && (
						<button
							type="button"
							onClick={() => setFiles([])}
							className="rounded-md px-4 py-2.5 text-sm font-medium text-muted hover:bg-black/5 hover:text-ink"
						>
							{t.upload.clear}
						</button>
					)}
					<button
						type="button"
						onClick={send}
						disabled={files.length === 0 || sending}
						className="rounded-md bg-brand px-5 py-2.5 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-brand-dark disabled:cursor-not-allowed disabled:opacity-40"
					>
						{sending ? t.upload.sending : t.upload.send(files.length)}
					</button>
				</div>
			</div>

			{error && <p className="mt-4 rounded-md bg-danger-soft px-3 py-2 text-sm text-danger">{error}</p>}

			{result && (
				<ul className="mt-4 space-y-1 text-sm">
					{result.files.map((file, index) => (
						<li key={`${file.fileName}-${index}`} className="flex flex-wrap items-baseline gap-x-2">
							<span className="font-medium">{file.fileName}</span>
							<span className={file.outcome === "Rejected" ? "text-danger" : "text-muted"}>
								· {t.upload.outcomes[file.outcome]}
								{file.error && ` — ${t.errors[file.error.code as keyof typeof t.errors] ?? file.error.message}`}
							</span>
						</li>
					))}
				</ul>
			)}
		</section>
	);
}
