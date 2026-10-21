import type { Metadata } from "next";
import type { ReactNode } from "react";
import { Header } from "@/components/Header";
import { I18nProvider } from "@/lib/i18n";
import "./globals.css";

export const metadata: Metadata = {
	title: "Invoice extractor",
	description: "Invoices and receipts to bookkeeping-ready data, validated and reviewed by a person.",
};

export default function RootLayout({ children }: { children: ReactNode }) {
	return (
		<html lang="es">
			<body className="min-h-screen antialiased">
				<I18nProvider>
					<Header />
					<main className="mx-auto w-full max-w-[1440px] px-4 pb-16 sm:px-6">{children}</main>
				</I18nProvider>
			</body>
		</html>
	);
}
