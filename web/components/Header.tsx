"use client";

import { ReceiptText } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { locales, setLocale, useI18n } from "@/lib/i18n";

// How often the review badge refreshes: the queue changes only as the workers finish documents.
const QueuePollMs = 5000;

export function Header() {
	const { locale, t } = useI18n();
	const pathname = usePathname();
	const [pending, setPending] = useState<number | null>(null);

	useEffect(() => {
		let active = true;
		const load = () =>
			api
				.reviewQueue()
				.then((queue) => active && setPending(queue.length))
				.catch(() => active && setPending(null));
		void load();
		const timer = window.setInterval(load, QueuePollMs);
		return () => {
			active = false;
			window.clearInterval(timer);
		};
	}, [pathname]);

	const link = (href: string, label: string, badge?: number | null) => {
		const current = href === "/" ? pathname === "/" : pathname.startsWith(href);
		return (
			<Link
				href={href}
				aria-current={current ? "page" : undefined}
				className={`inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
					current ? "bg-brand-soft text-brand-dark" : "text-muted hover:bg-black/5 hover:text-ink"
				}`}
			>
				{label}
				{badge ? (
					<span className="tabular rounded-full bg-warn px-1.5 py-0.5 text-[11px] leading-none font-semibold text-white">{badge}</span>
				) : null}
			</Link>
		);
	};

	return (
		<header className="sticky top-0 z-20 border-b border-line bg-paper/90 backdrop-blur">
			<div className="mx-auto flex h-14 w-full max-w-[1440px] items-center gap-4 px-4 sm:px-6">
				<Link href="/" className="flex items-center gap-2.5">
					<span className="grid size-8 place-items-center rounded-md bg-brand text-white">
						<ReceiptText className="size-4.5" aria-hidden />
					</span>
					<span className="hidden font-semibold tracking-tight sm:block">{t.app.name}</span>
				</Link>

				<nav className="flex items-center gap-1">
					{link("/", t.nav.upload)}
					{link("/review", t.nav.review, pending)}
				</nav>

				<div role="group" aria-label={t.nav.language} className="ml-auto flex rounded-md border border-line bg-card p-0.5 text-xs font-semibold">
					{locales.map((code) => (
						<button
							key={code}
							type="button"
							onClick={() => setLocale(code)}
							aria-pressed={locale === code}
							className={`rounded px-2 py-1 uppercase transition-colors ${
								locale === code ? "bg-ink text-white" : "text-muted hover:text-ink"
							}`}
						>
							{code}
						</button>
					))}
				</div>
			</div>
		</header>
	);
}
