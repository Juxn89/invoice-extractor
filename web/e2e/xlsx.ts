import { strFromU8, unzipSync } from "fflate";

/**
 * The first worksheet of an .xlsx as rows of cell text: enough to check an export without a spreadsheet
 * library. An .xlsx is a zip of XML parts, with or without a namespace prefix (ClosedXML writes <x:row>); text
 * cells point into the shared-strings part.
 */
export function readFirstSheet(file: Uint8Array): string[][] {
	const zip = unzipSync(file);
	const part = (name: string) => (zip[name] ? strFromU8(zip[name]) : "");

	const shared = [...part("xl/sharedStrings.xml").matchAll(/<(?:\w+:)?si>([\s\S]*?)<\/(?:\w+:)?si>/g)].map((item) =>
		[...(item[1] ?? "").matchAll(/<(?:\w+:)?t(?:\s[^>]*)?>([^<]*)<\/(?:\w+:)?t>/g)].map((t) => decode(t[1] ?? "")).join(""),
	);

	return [...part("xl/worksheets/sheet1.xml").matchAll(/<(?:\w+:)?row[^>]*>([\s\S]*?)<\/(?:\w+:)?row>/g)].map((row) => {
		const cells: string[] = [];
		for (const [, column = "", attributes = "", body = ""] of (row[1] ?? "").matchAll(/<(?:\w+:)?c r="([A-Z]+)\d+"([^>]*?)(?:\/>|>([\s\S]*?)<\/(?:\w+:)?c>)/g)) {
			const type = /t="(\w+)"/.exec(attributes)?.[1];
			const value = /<(?:\w+:)?v>([^<]*)<\/(?:\w+:)?v>/.exec(body)?.[1] ?? "";
			cells[columnIndex(column)] =
				type === "s" ? (shared[Number(value)] ?? "") : type === "inlineStr" ? decode(/<(?:\w+:)?t(?:\s[^>]*)?>([^<]*)<\/(?:\w+:)?t>/.exec(body)?.[1] ?? "") : decode(value);
		}

		return Array.from(cells, (cell) => cell ?? "");
	});
}

// "A" is 0, "Z" 25, "AA" 26.
function columnIndex(letters: string) {
	return [...letters].reduce((index, letter) => index * 26 + letter.charCodeAt(0) - 64, 0) - 1;
}

function decode(text: string) {
	return text
		.replaceAll("&lt;", "<")
		.replaceAll("&gt;", ">")
		.replaceAll("&quot;", '"')
		.replaceAll("&apos;", "'")
		.replaceAll("&amp;", "&");
}
