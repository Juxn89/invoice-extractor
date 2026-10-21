"use client";

import { Maximize, ZoomIn, ZoomOut } from "lucide-react";
import { useRef, useState, type PointerEvent, type WheelEvent } from "react";
import type { DocumentFormat } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

const MinZoom = 1;
const MaxZoom = 5;
const ZoomStep = 0.25;

type Props = { src: string; format: DocumentFormat };

// A PDF goes to the browser's own viewer (zoom, pages, search). An image gets zoom on the wheel and buttons,
// and drag to pan, so a reviewer can check a smudged digit without leaving the form.
export function DocumentViewer({ src, format }: Props) {
	const { t } = useI18n();
	const [zoom, setZoom] = useState(MinZoom);
	const [offset, setOffset] = useState({ x: 0, y: 0 });
	const drag = useRef<{ x: number; y: number } | null>(null);

	if (format === "Pdf") {
		return <iframe src={src} title={t.viewer.alt} className="h-full w-full rounded-lg border border-line bg-card" />;
	}

	const zoomTo = (next: number) => {
		const clamped = Math.min(MaxZoom, Math.max(MinZoom, next));
		setZoom(clamped);
		if (clamped === MinZoom) {
			setOffset({ x: 0, y: 0 });
		}
	};

	const onWheel = (event: WheelEvent) => zoomTo(zoom + (event.deltaY < 0 ? ZoomStep : -ZoomStep));

	const onPointerDown = (event: PointerEvent) => {
		if (zoom === MinZoom) {
			return;
		}

		event.currentTarget.setPointerCapture(event.pointerId);
		drag.current = { x: event.clientX - offset.x, y: event.clientY - offset.y };
	};

	const onPointerMove = (event: PointerEvent) => {
		if (drag.current) {
			setOffset({ x: event.clientX - drag.current.x, y: event.clientY - drag.current.y });
		}
	};

	const tool = "grid size-8 place-items-center rounded-md text-muted transition-colors hover:bg-black/5 hover:text-ink disabled:opacity-30";

	return (
		<div className="relative h-full overflow-hidden rounded-lg border border-line bg-[#e9e7df]">
			<div
				onWheel={onWheel}
				onPointerDown={onPointerDown}
				onPointerMove={onPointerMove}
				onPointerUp={() => (drag.current = null)}
				className={`flex h-full w-full touch-none items-center justify-center p-4 ${zoom > MinZoom ? "cursor-grab active:cursor-grabbing" : ""}`}
			>
				{/* eslint-disable-next-line @next/next/no-img-element -- an API file behind the rewrite, shown as is */}
				<img
					src={src}
					alt={t.viewer.alt}
					draggable={false}
					style={{ transform: `translate(${offset.x}px, ${offset.y}px) scale(${zoom})` }}
					className="max-h-full max-w-full rounded shadow-md transition-transform duration-100 select-none"
				/>
			</div>

			<div className="absolute right-3 bottom-3 flex items-center gap-0.5 rounded-lg border border-line bg-card/95 p-1 shadow-sm">
				<button type="button" className={tool} onClick={() => zoomTo(zoom - ZoomStep)} disabled={zoom <= MinZoom} aria-label={t.viewer.zoomOut}>
					<ZoomOut className="size-4" aria-hidden />
				</button>
				<span className="tabular w-12 text-center text-xs text-muted">{Math.round(zoom * 100)}%</span>
				<button type="button" className={tool} onClick={() => zoomTo(zoom + ZoomStep)} disabled={zoom >= MaxZoom} aria-label={t.viewer.zoomIn}>
					<ZoomIn className="size-4" aria-hidden />
				</button>
				<button type="button" className={tool} onClick={() => zoomTo(MinZoom)} aria-label={t.viewer.fit}>
					<Maximize className="size-4" aria-hidden />
				</button>
			</div>
		</div>
	);
}
