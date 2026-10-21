"use client";

import { ArrowRight, FileSpreadsheet, FileText } from "lucide-react";
import Link from "next/link";
import { useEffect, useState, type ReactNode } from "react";
import { StatusBar } from "@/components/StatusBar";
import { api, type Dashboard } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

// The numbers move as the workers finish documents; a few seconds is fresh enough to watch a batch land.
const RefreshMs = 5000;

export default function DashboardPage() {
	const { locale, t } = useI18n();
	const [data, setData] = useState<Dashboard | null>(null);

	useEffect(() => {
		let active = true;
		const load = () =>
			api
				.dashboard()
				.then((latest) => active && setData(latest))
				.catch(() => undefined);
		void load();
		const timer = window.setInterval(load, RefreshMs);
		return () => {
			active = false;
			window.clearInterval(timer);
		};
	}, []);

	const count = new Intl.NumberFormat(locale);
	const percent = new Intl.NumberFormat(locale, { style: "percent", maximumFractionDigits: 0 });
	// Model costs are fractions of a cent: four decimals for totals, six for the per-document average.
	const usd = (value: number, digits: number) =>
		`USD ${new Intl.NumberFormat(locale, { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(value)}`;

	return (
		<div className="pt-8">
			<h1 className="text-2xl font-semibold tracking-tight">{t.dashboard.title}</h1>
			<p className="mt-1 text-muted">{t.dashboard.subtitle}</p>

			{!data ? (
				<p className="mt-8 text-muted">{t.review.loading}</p>
			) : (
				<>
					<section className="mt-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
						<Tile label={t.dashboard.documents} value={count.format(data.documents)} hint={t.dashboard.documentsHint(data.decided)} />
						<Tile
							label={t.dashboard.autoApproved}
							value={percent.format(data.autoApprovedRate)}
							hint={`${count.format(data.byStatus.AutoApproved)} · ${t.dashboard.autoApprovedHint}`}
						/>
						<Tile
							label={t.dashboard.inReview}
							value={count.format(data.byStatus.NeedsReview)}
							hint={
								<Link href="/review" className="inline-flex items-center gap-1 text-brand-dark hover:underline">
									{t.dashboard.inReviewHint}
									<ArrowRight className="size-3.5" aria-hidden />
								</Link>
							}
						/>
						<Tile label={t.dashboard.cost} value={usd(data.costUsd, 4)} hint={t.dashboard.costHint(data.modelCalls, data.retries)} />
						<Tile label={t.dashboard.perDocument} value={usd(data.costPerDocumentUsd, 6)} hint={t.dashboard.perDocumentHint} />
						<Tile
							label={t.dashboard.today}
							value={usd(data.spentTodayUsd, 4)}
							hint={t.dashboard.todayHint(new Intl.NumberFormat(locale, { minimumFractionDigits: 2 }).format(data.dailyBudgetUsd))}
							meter={data.dailyBudgetUsd > 0 ? data.spentTodayUsd / data.dailyBudgetUsd : 0}
						/>
					</section>

					<div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
						<section aria-labelledby="status-title" className="rounded-xl border border-line bg-card p-5 shadow-sm">
							<h2 id="status-title" className="mb-5 font-semibold">
								{t.dashboard.statusTitle}
							</h2>
							<StatusBar byStatus={data.byStatus} />
						</section>

						<section aria-labelledby="export-title" className="rounded-xl border border-line bg-card p-5 shadow-sm">
							<h2 id="export-title" className="font-semibold">
								{t.dashboard.exportTitle}
							</h2>
							<p className="mt-1 text-sm text-muted">{t.dashboard.exportHint}</p>
							<div className="mt-4 flex flex-wrap gap-2">
								<a
									href={api.exportUrl("xlsx", locale)}
									className="inline-flex items-center gap-2 rounded-md bg-brand px-4 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-brand-dark"
								>
									<FileSpreadsheet className="size-4" aria-hidden />
									{t.dashboard.excel}
								</a>
								<a
									href={api.exportUrl("csv", locale)}
									className="inline-flex items-center gap-2 rounded-md border border-line px-4 py-2.5 text-sm font-medium hover:border-brand hover:text-brand-dark"
								>
									<FileText className="size-4" aria-hidden />
									{t.dashboard.csv}
								</a>
							</div>
						</section>
					</div>
				</>
			)}
		</div>
	);
}

function Tile({ label, value, hint, meter }: { label: string; value: string; hint: ReactNode; meter?: number }) {
	return (
		<div className="rounded-xl border border-line bg-card p-5 shadow-sm">
			<p className="text-sm text-muted">{label}</p>
			<p className="tabular mt-1 text-3xl font-semibold tracking-tight">{value}</p>
			{meter !== undefined && (
				<div className="mt-3 h-1.5 overflow-hidden rounded-full bg-brand-soft" aria-hidden>
					<div className="h-full rounded-full bg-brand" style={{ width: `${Math.min(100, meter * 100)}%` }} />
				</div>
			)}
			<p className="mt-2 text-xs text-muted">{hint}</p>
		</div>
	);
}
