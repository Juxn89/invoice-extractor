"use client";

import { useState } from "react";
import type { DocumentStatus } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

export type Segment = "AutoApproved" | "Approved" | "NeedsReview" | "Failed" | "InFlight";

// Fixed order and fixed colour per segment: a segment keeps its colour whatever the others' counts are.
const segments: { key: Segment; fill: string; statuses: DocumentStatus[] }[] = [
	{ key: "AutoApproved", fill: "bg-chart-auto", statuses: ["AutoApproved"] },
	{ key: "Approved", fill: "bg-chart-approved", statuses: ["Approved"] },
	{ key: "NeedsReview", fill: "bg-chart-review", statuses: ["NeedsReview"] },
	{ key: "Failed", fill: "bg-chart-failed", statuses: ["Failed"] },
	{ key: "InFlight", fill: "bg-chart-inflight", statuses: ["Queued", "Processing"] },
];

/** Part-to-whole: one horizontal bar of where every document is, with a legend that doubles as the table. */
export function StatusBar({ byStatus }: { byStatus: Record<DocumentStatus, number> }) {
	const { locale, t } = useI18n();
	const [hovered, setHovered] = useState<Segment | null>(null);
	const percent = new Intl.NumberFormat(locale, { style: "percent", maximumFractionDigits: 0 });

	const rows = segments.map((s) => ({ ...s, count: s.statuses.reduce((sum, status) => sum + (byStatus[status] ?? 0), 0) }));
	const total = rows.reduce((sum, row) => sum + row.count, 0);

	if (total === 0) {
		return <p className="text-sm text-muted">{t.dashboard.statusEmpty}</p>;
	}

	const share = (count: number) => t.dashboard.share(count, percent.format(count / total));
	const active = rows.find((row) => row.key === hovered);

	// The tooltip sits over the middle of its own segment: what comes before it, plus half of it.
	const before = active ? rows.slice(0, rows.indexOf(active)).reduce((sum, row) => sum + row.count, 0) : 0;
	const centre = active ? ((before + active.count / 2) / total) * 100 : 50;

	return (
		<div>
			<div className="relative">
				{/* The 2px gaps show the card through, so adjacent fills never touch. */}
				<div className="flex h-8 gap-0.5 overflow-hidden rounded" role="img" aria-label={rows.map((r) => `${t.dashboard.segments[r.key]}: ${share(r.count)}`).join("; ")}>
					{rows
						.filter((row) => row.count > 0)
						.map((row) => (
							<div
								key={row.key}
								tabIndex={0}
								onMouseEnter={() => setHovered(row.key)}
								onMouseLeave={() => setHovered(null)}
								onFocus={() => setHovered(row.key)}
								onBlur={() => setHovered(null)}
								style={{ flexGrow: row.count }}
								className={`${row.fill} min-w-1 basis-0 transition-opacity outline-none focus-visible:ring-2 focus-visible:ring-ink ${
									hovered && hovered !== row.key ? "opacity-40" : ""
								}`}
							/>
						))}
				</div>
				{active && (
					<div
						style={{ left: `clamp(4rem, ${centre}%, calc(100% - 4rem))` }}
						className="pointer-events-none absolute -top-11 -translate-x-1/2 rounded-md bg-ink px-2.5 py-1.5 text-xs whitespace-nowrap text-white shadow-lg"
					>
						<span className="font-medium">{t.dashboard.segments[active.key]}</span> · <span className="tabular">{share(active.count)}</span>
					</div>
				)}
			</div>

			<ul className="mt-4 grid gap-x-6 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3">
				{rows.map((row) => (
					<li
						key={row.key}
						onMouseEnter={() => setHovered(row.key)}
						onMouseLeave={() => setHovered(null)}
						className="flex items-center gap-2"
					>
						<span className={`size-3 shrink-0 rounded-sm ${row.fill}`} aria-hidden />
						<span>{t.dashboard.segments[row.key]}</span>
						<span className="tabular ml-auto text-muted">{share(row.count)}</span>
					</li>
				))}
			</ul>
		</div>
	);
}
