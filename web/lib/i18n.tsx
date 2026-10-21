"use client";

import { createContext, useContext, useEffect, useSyncExternalStore, type ReactNode } from "react";
import { en } from "@/messages/en";
import { es, type Messages } from "@/messages/es";

export const locales = ["es", "en"] as const;
export type Locale = (typeof locales)[number];

const dictionaries: Record<Locale, Messages> = { es, en };
const storageKey = "invoice-extractor.locale";
const changeEvent = "invoice-extractor:locale";
const fallback: Locale = "es";

function isLocale(value: string | null): value is Locale {
	return value !== null && (locales as readonly string[]).includes(value);
}

// The choice lives in localStorage; until the user picks one, the browser's language decides. Read through
// useSyncExternalStore so the server render (always the fallback) and the client agree on first paint.
function readLocale(): Locale {
	try {
		const stored = window.localStorage.getItem(storageKey);
		if (isLocale(stored)) {
			return stored;
		}
	} catch {
		// Storage can be blocked (private mode); the browser language still works.
	}

	return navigator.language.toLowerCase().startsWith("en") ? "en" : fallback;
}

function subscribe(onChange: () => void) {
	window.addEventListener("storage", onChange);
	window.addEventListener(changeEvent, onChange);
	return () => {
		window.removeEventListener("storage", onChange);
		window.removeEventListener(changeEvent, onChange);
	};
}

export function setLocale(locale: Locale) {
	try {
		window.localStorage.setItem(storageKey, locale);
	} catch {
		// Not persisted, but the event below still switches the page.
	}

	window.dispatchEvent(new Event(changeEvent));
}

const I18nContext = createContext<{ locale: Locale; t: Messages }>({ locale: fallback, t: es });

export function I18nProvider({ children }: { children: ReactNode }) {
	const locale = useSyncExternalStore(subscribe, readLocale, () => fallback);

	useEffect(() => {
		document.documentElement.lang = locale;
	}, [locale]);

	return <I18nContext.Provider value={{ locale, t: dictionaries[locale] }}>{children}</I18nContext.Provider>;
}

export function useI18n() {
	return useContext(I18nContext);
}
