"use client";

import { useState } from "react";
import { BatchList } from "@/components/BatchList";
import { UploadPanel } from "@/components/UploadPanel";
import { useI18n } from "@/lib/i18n";

export default function HomePage() {
	const { t } = useI18n();
	const [refreshKey, setRefreshKey] = useState(0);

	return (
		<div className="pt-8">
			<p className="max-w-2xl text-muted">{t.app.tagline}</p>
			<div className="mt-6 grid gap-8 lg:grid-cols-[minmax(0,5fr)_minmax(0,6fr)] lg:items-start">
				<UploadPanel onUploaded={() => setRefreshKey((key) => key + 1)} />
				<BatchList refreshKey={refreshKey} />
			</div>
		</div>
	);
}
