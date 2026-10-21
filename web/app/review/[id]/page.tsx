"use client";

import { useParams } from "next/navigation";
import { ReviewScreen } from "@/components/ReviewScreen";

export default function ReviewPage() {
	const { id } = useParams<{ id: string }>();
	// Keyed by id: moving to the next document starts from a fresh form, not the previous one's edits.
	return <ReviewScreen key={id} id={id} />;
}
