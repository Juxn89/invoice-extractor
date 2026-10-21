"use client";

import { ChevronRight, FileImage, FileText } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { api, inFlight, type BatchResponse } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { StatusBadge } from "./StatusBadge";

// While a document is queued or being read, its status changes within seconds; afterwards, rarely.
const BusyPollMs = 2000;
const IdlePollMs = 15000;

export function BatchList({ refreshKey }: { refreshKey: number }) {
	const { locale, t } = useI18n();
	const [batches, setBatches] = useState<BatchResponse[] | null>(null);

	useEffect(() => {
		let active = true;
		let timer: number | undefined;
		const load = async () => {
			try {
				const latest = await api.batches();
				if (!active) {
					return;
				}

				setBatches(latest);
				const busy = latest.some((b) => b.documents.some((d) => inFlight.includes(d.status)));
				timer = window.setTimeout(load, busy ? BusyPollMs : IdlePollMs);
			} catch {
				if (active) {
					timer = window.setTimeout(load, IdlePollMs);
				}
			}
		};

		void load();
		return () => {
			active = false;
			window.clearTimeout(timer);
		};
	}, [refreshKey]);

	const date = new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" });

	return (
		<section aria-labelledby="batches-title" className="space-y-3">
			<h2 id="batches-title" className="text-lg font-semibold tracking-tight">
				{t.batches.title}
			</h2>

			{batches?.length === 0 && <p className="rounded-xl border border-dashed border-line p-6 text-sm text-muted">{t.batches.empty}</p>}

			{batches?.map((batch) => (
				<article key={batch.id} className="overflow-hidden rounded-xl border border-line bg-card shadow-sm">
					<header className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-line bg-paper/60 px-4 py-2.5 text-sm">
						<time dateTime={batch.createdAt} className="font-medium">
							{date.format(new Date(batch.createdAt))}
						</time>
						<span className="text-muted">· {t.countries[batch.country]}</span>
						<span className="ml-auto text-muted">{t.batches.documents(batch.documents.length)}</span>
					</header>
					<ul className="divide-y divide-line">
						{batch.documents.map((document) => (
							<li key={document.id}>
								<Link
									href={`/review/${document.id}`}
									className="group flex items-center gap-3 px-4 py-2.5 text-sm transition-colors hover:bg-paper/70"
								>
									{document.format === "Pdf" ? (
										<FileText className="size-4 shrink-0 text-muted" aria-hidden />
									) : (
										<FileImage className="size-4 shrink-0 text-muted" aria-hidden />
									)}
									<span className="truncate">{document.fileName}</span>
									<span className="ml-auto">
										<StatusBadge status={document.status} />
									</span>
									<ChevronRight className="size-4 text-muted opacity-0 transition-opacity group-hover:opacity-100" aria-label={t.batches.open} />
								</Link>
							</li>
						))}
					</ul>
				</article>
			))}
		</section>
	);
}
