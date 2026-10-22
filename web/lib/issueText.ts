// The numbers behind a failed rule, as the API sends them (invariant: "1234.50", "0.1500", "2026-10-02"),
// formatted for the reader's language. The sentences around them live in messages/es.ts and messages/en.ts.
export type IssueText = {
	amount: (key: string) => string;
	number: (key: string) => string;
	percent: (key: string) => string;
	date: (key: string) => string;
	text: (key: string) => string;
	country: (key: string) => string;
};

export function issueText(values: Record<string, string>, locale: string, countries: Record<string, string>): IssueText {
	const amount = new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
	const number = new Intl.NumberFormat(locale, { maximumFractionDigits: 4 });
	const percent = new Intl.NumberFormat(locale, { style: "percent", maximumFractionDigits: 2 });
	const date = new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeZone: "UTC" });
	const raw = (key: string) => values[key] ?? "";
	const numeric = (key: string, format: Intl.NumberFormat) => {
		const value = Number(raw(key));
		return raw(key) === "" || Number.isNaN(value) ? raw(key) : format.format(value);
	};

	return {
		amount: (key) => numeric(key, amount),
		number: (key) => numeric(key, number),
		percent: (key) => numeric(key, percent),
		date: (key) => {
			const value = new Date(`${raw(key)}T00:00:00Z`);
			return Number.isNaN(value.getTime()) ? raw(key) : date.format(value);
		},
		text: raw,
		country: (key) => countries[raw(key)] ?? raw(key),
	};
}
