"use client";

import { CheckCircle2, CircleAlert, CircleDashed, Copy, LoaderCircle, ShieldCheck, XCircle } from "lucide-react";
import type { DocumentStatus } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

const styles: Record<DocumentStatus, { className: string; Icon: typeof CheckCircle2; spin?: boolean }> = {
	Queued: { className: "bg-black/5 text-muted", Icon: CircleDashed },
	Processing: { className: "bg-info-soft text-info", Icon: LoaderCircle, spin: true },
	AutoApproved: { className: "bg-brand-soft text-brand-dark", Icon: ShieldCheck },
	NeedsReview: { className: "bg-warn-soft text-warn", Icon: CircleAlert },
	Approved: { className: "bg-brand-soft text-brand-dark", Icon: CheckCircle2 },
	Failed: { className: "bg-danger-soft text-danger", Icon: XCircle },
	Duplicate: { className: "bg-black/5 text-muted", Icon: Copy },
};

export function StatusBadge({ status }: { status: DocumentStatus }) {
	const { t } = useI18n();
	const { className, Icon, spin } = styles[status];
	return (
		<span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium whitespace-nowrap ${className}`}>
			<Icon className={`size-3.5 ${spin ? "animate-spin" : ""}`} aria-hidden />
			{t.status[status]}
		</span>
	);
}
