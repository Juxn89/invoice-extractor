"use client";

import { CheckCircle2 } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

// The queue's entry point: straight to the oldest document waiting, or an empty state.
export default function ReviewQueuePage() {
	const { t } = useI18n();
	const router = useRouter();
	const [empty, setEmpty] = useState(false);

	useEffect(() => {
		let active = true;
		api
			.reviewQueue()
			.then((queue) => {
				if (!active) {
					return;
				}

				const first = queue[0];
				if (first) {
					router.replace(`/review/${first.id}`);
				} else {
					setEmpty(true);
				}
			})
			.catch(() => active && setEmpty(true));
		return () => {
			active = false;
		};
	}, [router]);

	if (!empty) {
		return <p className="pt-10 text-muted">{t.review.loading}</p>;
	}

	return (
		<div className="mx-auto mt-16 max-w-md rounded-xl border border-line bg-card p-8 text-center shadow-sm">
			<CheckCircle2 className="mx-auto size-10 text-brand" aria-hidden />
			<h1 className="mt-3 text-lg font-semibold">{t.review.empty}</h1>
			<p className="mt-1 text-sm text-muted">{t.review.emptyHint}</p>
			<Link href="/" className="mt-5 inline-block rounded-md bg-brand px-4 py-2 text-sm font-semibold text-white hover:bg-brand-dark">
				{t.review.back}
			</Link>
		</div>
	);
}
