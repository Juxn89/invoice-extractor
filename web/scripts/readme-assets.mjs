// Generates the README artwork in docs/readme/: hero banner, metrics strip, feature icons and divider (SVG,
// light and dark), and the product showcase (PNG), composed from the English review screen that `pnpm demo`
// captures to docs/readme/shots/review.png.
//
//   node scripts/readme-assets.mjs
//
// GitHub shows README images through <img>: plain SVG, no external fonts or scripts. CSS animations inside the
// SVG do run there, and they stop under prefers-reduced-motion.
import { chromium } from "@playwright/test";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const out = fileURLToPath(new URL("../../docs/readme/", import.meta.url));
await mkdir(out, { recursive: true });

// Its own brand, distinct from docs-assistant (teal) and claude-pr-review-bot (violet); amber is the shared accent.
const brand = { blue: "#1d4ed8", sky: "#38bdf8", amber: "#f59e0b", red: "#dc2626", green: "#16a34a" };

const themes = {
	dark: { bg: "#0a0e13", panel: "#111823", card: "#151d2a", border: "#22304a", text: "#f1f5f9", muted: "#94a3b8", line: "#2a3a52", dot: "#ffffff", dotOpacity: 0.06, orb: 0.3 },
	light: { bg: "#f7f9fd", panel: "#ffffff", card: "#ffffff", border: "#e2e8f0", text: "#0f172a", muted: "#475569", line: "#cbd5e1", dot: "#1d4ed8", dotOpacity: 0.08, orb: 0.15 },
};

const sans = `'Segoe UI', Inter, -apple-system, BlinkMacSystemFont, 'Helvetica Neue', Arial, sans-serif`;
const mono = `'Cascadia Code', 'SFMono-Regular', Consolas, Menlo, monospace`;
const escape = (text) => text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

function defs(t) {
	return `
	<linearGradient id="brand" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="${brand.sky}"/><stop offset=".6" stop-color="${brand.blue}"/><stop offset="1" stop-color="${brand.amber}"/></linearGradient>
	<linearGradient id="metric" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${brand.sky}"/><stop offset=".55" stop-color="${brand.blue}"/><stop offset="1" stop-color="${brand.amber}"/></linearGradient>
	<radialGradient id="orbBlue"><stop offset="0" stop-color="${brand.blue}" stop-opacity="${t.orb}"/><stop offset="1" stop-color="${brand.blue}" stop-opacity="0"/></radialGradient>
	<radialGradient id="orbAmber"><stop offset="0" stop-color="${brand.amber}" stop-opacity="${t.orb * 0.7}"/><stop offset="1" stop-color="${brand.amber}" stop-opacity="0"/></radialGradient>
	<pattern id="dots" width="22" height="22" patternUnits="userSpaceOnUse"><circle cx="2" cy="2" r="1.1" fill="${t.dot}" fill-opacity="${t.dotOpacity}"/></pattern>`;
}

// ── Hero: what it does, drawn — a crooked receipt becomes checked fields, one flagged, then a spreadsheet ──
function hero(name) {
	const t = themes[name];
	const W = 1280;
	const H = 460;
	const accent = name === "dark" ? brand.sky : brand.blue;
	const chips = ["Claude Haiku 5.5", "Accounting rules", "Human review", "Postgres queue", "Excel & CSV"];
	let x = 72;
	const chipSvg = chips
		.map((chip) => {
			const w = chip.length * 8.4 + 30;
			const g = `<g transform="translate(${x} 352)"><rect width="${w}" height="32" rx="16" fill="${t.panel}" stroke="${t.border}"/><text x="${w / 2}" y="21" text-anchor="middle" font-family="${sans}" font-size="14" font-weight="600" fill="${t.text}">${escape(chip)}</text></g>`;
			x += w + 8;
			return g;
		})
		.join("");

	const line = (y, w, x0 = 18) => `<rect x="${x0}" y="${y}" width="${w}" height="6" rx="3" fill="${t.line}"/>`;
	const check = (cx, cy, color) =>
		`<circle cx="${cx}" cy="${cy}" r="8" fill="${color}" fill-opacity=".16"/><path d="M${cx - 3.5} ${cy}l2.5 2.5 4.5-5" fill="none" stroke="${color}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>`;
	const field = (y, label, value, ok) => `
		<g transform="translate(16 ${y})">
			<rect width="190" height="34" rx="9" fill="${ok ? t.panel : brand.red}" fill-opacity="${ok ? 1 : 0.1}" stroke="${ok ? t.border : brand.red}" stroke-opacity="${ok ? 1 : 0.7}"/>
			<text x="12" y="22" font-family="${sans}" font-size="12" fill="${t.muted}">${label}</text>
			<text x="150" y="22" text-anchor="end" font-family="${mono}" font-size="12.5" font-weight="600" fill="${ok ? t.text : brand.red}">${value}</text>
			${ok ? check(172, 17, brand.green) : `<circle cx="172" cy="17" r="8" fill="${brand.red}" fill-opacity=".16"/><text x="172" y="21.5" text-anchor="middle" font-family="${sans}" font-size="12" font-weight="800" fill="${brand.red}">!</text>`}
		</g>`;

	return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="title desc">
<title id="title">invoice-extractor</title>
<desc id="desc">Turns photos and PDFs of invoices into rows your accountant can use. Claude reads them, accounting rules check every reading, and a person reviews what does not add up.</desc>
<style>
	.flow { stroke-dasharray: 6 7; animation: flow 1.6s linear infinite; }
	.pop { opacity: 0; animation: pop .5s ease-out forwards; }
	.glow { animation: glow 2.4s ease-in-out infinite alternate; }
	@keyframes flow { to { stroke-dashoffset: -26; } }
	@keyframes pop { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: none; } }
	@keyframes glow { from { opacity: .55; } to { opacity: 1; } }
	@media (prefers-reduced-motion: reduce) { .flow, .glow { animation: none; } .pop { animation: none; opacity: 1; } }
</style>
<defs>${defs(t)}
	<clipPath id="frame"><rect width="${W}" height="${H}" rx="28"/></clipPath>
	<filter id="soft" x="-20%" y="-20%" width="140%" height="160%"><feDropShadow dx="0" dy="14" stdDeviation="16" flood-color="#000" flood-opacity="${name === "dark" ? 0.45 : 0.12}"/></filter>
</defs>
<g clip-path="url(#frame)">
	<rect width="${W}" height="${H}" fill="${t.bg}"/>
	<rect width="${W}" height="${H}" fill="url(#dots)"/>
	<circle cx="180" cy="40" r="340" fill="url(#orbBlue)"/>
	<circle cx="1060" cy="470" r="360" fill="url(#orbAmber)"/>
</g>
<rect x=".5" y=".5" width="${W - 1}" height="${H - 1}" rx="28" fill="none" stroke="${t.border}"/>

<g transform="translate(72 70)">
	<rect width="236" height="30" rx="15" fill="${t.panel}" stroke="${t.border}"/>
	<circle cx="18" cy="15" r="4.5" fill="${brand.sky}" class="glow"/>
	<text x="31" y="20" font-family="${sans}" font-size="12.5" font-weight="700" letter-spacing=".1em" fill="${t.muted}">OPEN SOURCE · .NET 10 · MIT</text>
</g>
<text x="68" y="172" font-family="${sans}" font-size="62" font-weight="800" letter-spacing="-.03em" fill="${t.text}">invoice<tspan fill="url(#brand)">-extractor</tspan></text>
<text x="72" y="230" font-family="${sans}" font-size="24" font-weight="600" fill="${t.text}">From a photo or PDF to a row in your books.</text>
<text x="72" y="268" font-family="${sans}" font-size="20" fill="${t.muted}">Claude reads it; accounting rules check every number.</text>
<text x="72" y="300" font-family="${sans}" font-size="20" fill="${t.muted}">What does not add up goes to a person, never straight through.</text>
${chipSvg}

<!-- A receipt, photographed at an angle -->
<g transform="translate(700 92) rotate(-6)" filter="url(#soft)">
	<rect width="150" height="226" rx="12" fill="${t.card}" stroke="${t.border}"/>
	<rect x="18" y="20" width="84" height="9" rx="4.5" fill="${t.text}" fill-opacity=".85"/>
	${line(40, 60)}${line(64, 114)}${line(78, 96)}${line(92, 108)}
	<rect x="18" y="116" width="114" height="1.5" fill="${t.line}"/>
	${line(130, 50, 82)}${line(144, 50, 82)}
	<rect x="70" y="156" width="68" height="24" rx="6" fill="${brand.amber}" fill-opacity="${name === "dark" ? 0.22 : 0.18}" class="glow"/>
	<rect x="82" y="165" width="50" height="7" rx="3.5" fill="${brand.amber}"/>
	<text x="18" y="208" font-family="${mono}" font-size="11" fill="${t.muted}">receipt.jpg</text>
</g>

<path d="M850 205 C 880 205, 872 150, 908 150" fill="none" stroke="${accent}" stroke-width="2.2" class="flow"/>
<circle cx="850" cy="205" r="4.5" fill="${accent}"/>

<!-- The reading, checked field by field -->
<g transform="translate(902 60)" filter="url(#soft)">
	<rect width="222" height="220" rx="18" fill="${t.card}" stroke="${t.border}"/>
	<text x="18" y="32" font-family="${sans}" font-size="13" font-weight="700" fill="${t.text}">Checked reading</text>
	<g class="pop" style="animation-delay:.2s">${field(46, "Subtotal", "65.14", true)}</g>
	<g class="pop" style="animation-delay:.6s">${field(86, "Tax", "6.68", true)}</g>
	<g class="pop" style="animation-delay:1s">${field(126, "Total", "81.82", false)}</g>
	<g class="pop" style="animation-delay:1.4s">
		<text x="18" y="190" font-family="${sans}" font-size="11.5" font-weight="600" fill="${brand.red}">65.14 + 6.68 ≠ 81.82</text>
		<text x="18" y="207" font-family="${sans}" font-size="11.5" fill="${t.muted}">→ sent to human review</text>
	</g>
</g>

<!-- What a person approved lands in the books -->
<path d="M1013 282 C 1013 320, 1060 330, 1080 352" fill="none" stroke="${brand.amber}" stroke-width="2.2" class="flow"/>
<g transform="translate(1040 350)" filter="url(#soft)">
	<rect width="196" height="74" rx="14" fill="${t.card}" stroke="${t.border}"/>
	<rect x="14" y="14" width="22" height="22" rx="6" fill="${brand.green}"/>
	<path d="M20 20h10M20 25h10M20 30h10M25 20v10" stroke="#fff" stroke-width="1.6"/>
	<text x="44" y="30" font-family="${sans}" font-size="13" font-weight="700" fill="${t.text}">facturas.xlsx</text>
	<g class="pop" style="animation-delay:1.8s">
		<rect x="14" y="46" width="168" height="16" rx="5" fill="${accent}" fill-opacity=".14"/>
		<text x="22" y="58" font-family="${mono}" font-size="10.5" fill="${t.text}">INV-001277</text>
		<text x="174" y="58" text-anchor="end" font-family="${mono}" font-size="10.5" font-weight="700" fill="${t.text}">71.82</text>
	</g>
</g>
</svg>
`;
}

// ── Metrics: measured by the evals in this repo (evals/results, 21 Oct 2026) ─────────────────────────────
const metricItems = [
	["0 / 42", "false auto-approvals", "Haiku 5.5 + retry, both eval sets"],
	["1.000", "key-field accuracy", "total, date, tax id, number"],
	["7 / 7", "planted errors caught", "sent to review, never approved"],
	["USD 0.00065", "per document", "standard set, retry included"],
];

function metrics(name) {
	const t = themes[name];
	const W = 1280;
	const H = 150;
	const col = W / metricItems.length;
	const cells = metricItems
		.map(
			([value, label, note], i) => `
	<g transform="translate(${col * i + col / 2} 0)">
		<text y="64" text-anchor="middle" font-family="${sans}" font-size="40" font-weight="800" letter-spacing="-.02em" fill="url(#metric)">${escape(value)}</text>
		<text y="94" text-anchor="middle" font-family="${sans}" font-size="16" font-weight="600" fill="${t.text}">${escape(label)}</text>
		<text y="116" text-anchor="middle" font-family="${sans}" font-size="13.5" fill="${t.muted}">${escape(note)}</text>
	</g>${i > 0 ? `<line x1="${col * i}" y1="36" x2="${col * i}" y2="114" stroke="${t.border}"/>` : ""}`,
		)
		.join("");
	const label = metricItems.map(([value, label, note]) => `${value} ${label}, ${note}.`).join(" ");
	return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img" aria-label="${escape(label)}">
<defs>${defs(t)}</defs>
<rect x=".5" y=".5" width="${W - 1}" height="${H - 1}" rx="22" fill="${t.panel}" stroke="${t.border}"/>
${cells}
</svg>
`;
}

// ── Feature icons: Lucide glyphs (ISC license) on a gradient tile ─────────────────────────────────────────
const glyphs = {
	read: '<path d="M3 7V5a2 2 0 0 1 2-2h2"/><path d="M17 3h2a2 2 0 0 1 2 2v2"/><path d="M21 17v2a2 2 0 0 1-2 2h-2"/><path d="M7 21H5a2 2 0 0 1-2-2v-2"/><path d="M7 8h8"/><path d="M7 12h10"/><path d="M7 16h6"/>',
	rules: '<path d="m3 17 2 2 4-4"/><path d="m3 7 2 2 4-4"/><path d="M13 6h8"/><path d="M13 12h8"/><path d="M13 18h8"/>',
	retry: '<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/>',
	review: '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><polyline points="16 11 18 13 22 9"/>',
	queue: '<path d="M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83Z"/><path d="m22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65"/><path d="m22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65"/>',
	export: '<rect width="18" height="18" x="3" y="3" rx="2"/><path d="M3 9h18"/><path d="M3 15h18"/><path d="M9 9v12"/><path d="M15 9v12"/>',
	evals: '<path d="M14 2v6a2 2 0 0 0 .245.96l5.51 10.08A2 2 0 0 1 18 22H6a2 2 0 0 1-1.755-2.96l5.51-10.08A2 2 0 0 0 10 8V2"/><path d="M6.453 15h11.094"/><path d="M8.5 2h7"/>',
	cost: '<circle cx="8" cy="8" r="6"/><path d="M18.09 10.37A6 6 0 1 1 10.34 18"/><path d="M7 6h1v4"/><path d="m16.71 13.88.7.71-2.82 2.82"/>',
	shield: '<path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/>',
};

const tiles = {
	read: ["#1d4ed8", "#38bdf8"],
	rules: ["#065f46", "#10b981"],
	retry: ["#0e7490", "#22d3ee"],
	review: ["#b45309", "#fbbf24"],
	queue: ["#334155", "#64748b"],
	export: ["#166534", "#4ade80"],
	evals: ["#7c2d12", "#f97316"],
	cost: ["#1e3a8a", "#60a5fa"],
	shield: ["#9f1239", "#fb7185"],
};

function iconSvg(name) {
	const [from, to] = tiles[name];
	return `<svg xmlns="http://www.w3.org/2000/svg" width="56" height="56" viewBox="0 0 56 56" aria-hidden="true">
<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${from}"/><stop offset="1" stop-color="${to}"/></linearGradient></defs>
<rect width="56" height="56" rx="16" fill="url(#g)"/>
<rect x=".75" y=".75" width="54.5" height="54.5" rx="15.25" fill="none" stroke="#fff" stroke-opacity=".18" stroke-width="1.5"/>
<g transform="translate(16 16)" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${glyphs[name]}</g>
</svg>
`;
}

const divider = `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="24" viewBox="0 0 1280 24" aria-hidden="true">
<defs><linearGradient id="d" x1="0" x2="1"><stop offset="0" stop-color="${brand.sky}" stop-opacity="0"/><stop offset=".3" stop-color="${brand.blue}"/><stop offset=".7" stop-color="${brand.amber}"/><stop offset="1" stop-color="${brand.amber}" stop-opacity="0"/></linearGradient></defs>
<rect y="11" width="1280" height="2" rx="1" fill="url(#d)"/>
<rect x="632" y="4" width="16" height="16" rx="5" fill="${brand.blue}"/><path d="M636.5 12l2.5 2.5 4.5-5" fill="none" stroke="#fff" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
`;

for (const name of ["dark", "light"]) {
	await writeFile(`${out}hero-${name}.svg`, hero(name));
	await writeFile(`${out}metrics-${name}.svg`, metrics(name));
}

for (const name of Object.keys(glyphs)) {
	await writeFile(`${out}icon-${name}.svg`, iconSvg(name));
}

await writeFile(`${out}divider.svg`, divider);

// ── Showcase: the real review screen in a browser window, on each theme's background ──────────────────────
const review = `data:image/png;base64,${(await readFile(`${out}shots/review.png`)).toString("base64")}`;
const browser = await chromium.launch();

for (const name of ["dark", "light"]) {
	const t = themes[name];
	const page = await browser.newPage({ viewport: { width: 1600, height: 1040 }, deviceScaleFactor: 1.5 });
	await page.setContent(
		`<!doctype html><html><head><meta charset="utf-8"><style>
		* { box-sizing: border-box; margin: 0; } body { width: 100vw; height: 100vh; overflow: hidden; position: relative; background: ${t.bg}; font-family: ${sans}; }
		.dots { position: absolute; inset: 0; background-image: radial-gradient(${t.dot} 1px, transparent 1.2px); background-size: 22px 22px; opacity: ${t.dotOpacity * 1.4}; }
		.orb { position: absolute; border-radius: 50%; filter: blur(100px); }
		.o1 { width: 700px; height: 700px; left: -200px; top: -260px; background: ${brand.blue}; opacity: ${name === "dark" ? 0.35 : 0.16}; }
		.o2 { width: 700px; height: 700px; right: -220px; bottom: -300px; background: ${brand.amber}; opacity: ${name === "dark" ? 0.25 : 0.16}; }
		.window { position: absolute; left: 80px; top: 52px; width: 1440px; border-radius: 16px; overflow: hidden; background: ${t.panel}; border: 1px solid ${t.border};
			box-shadow: 0 50px 100px -20px rgba(0,0,0,${name === "dark" ? 0.65 : 0.25}); }
		.bar { height: 44px; display: flex; align-items: center; gap: 8px; padding: 0 16px; border-bottom: 1px solid ${t.border}; }
		.d { width: 12px; height: 12px; border-radius: 50%; }
		.url { margin: 0 auto; width: 380px; height: 28px; border-radius: 8px; background: ${t.bg}; border: 1px solid ${t.border}; color: ${t.muted};
			font-size: 13px; display: flex; align-items: center; justify-content: center; }
		.window img { display: block; width: 100%; }
		</style></head><body><div class="dots"></div><div class="orb o1"></div><div class="orb o2"></div>
		<div class="window"><div class="bar"><span class="d" style="background:#f87171"></span><span class="d" style="background:#fbbf24"></span><span class="d" style="background:#34d399"></span>
			<span class="url">localhost:3010/review</span></div><img src="${review}"></div></body></html>`,
		{ waitUntil: "load" },
	);
	await page.screenshot({ path: `${out}showcase-${name}.png` });
	await page.close();
}

await browser.close();
console.log(`README artwork written to ${out}`);
