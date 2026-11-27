// Focused synthetic Instance status preview. This file changes no portal/runtime data.
// Root owns rendering and screenshot review; no production implementation is approved here.
import { createHash } from "node:crypto";
import { mkdirSync, mkdtempSync, readFileSync, rmdirSync, unlinkSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

const outputDirectory = path.dirname(fileURLToPath(import.meta.url));
const sharpNodeModules = process.env.CIS_SHARP_NODE_MODULES;
if (!sharpNodeModules) {
  throw new Error("Set CIS_SHARP_NODE_MODULES to the existing pinned Sharp runtime.");
}
const sharp = createRequire(path.join(sharpNodeModules, "..", "cis-design-runtime.cjs"))("sharp");

// Reuse only the existing verified Inter font, without importing/executing its renderer.
// The SVG embeds these exact bytes. Existing approved PNGs and renderers remain untouched.
const fontSource = path.join(outputDirectory, "render-rm09-ux-r09-screens.mjs");
const fontMatch = /const embeddedFontBase64 = "([A-Za-z0-9+/=]+)";/.exec(readFileSync(fontSource, "utf8"));
if (fontMatch === null) {
  throw new Error("Existing Inter font declaration is unavailable.");
}
const embeddedFontBase64 = fontMatch[1];
const fontBytes = Buffer.from(embeddedFontBase64, "base64");
const embeddedFontSha256 = "4989b125924991b90d05b2d16e0e388c48f7d5bb8b30539bbf9c755278d0ccaf";
if (sha(fontBytes) !== embeddedFontSha256) {
  throw new Error("Existing Inter font digest mismatch.");
}

export const previewInputs = Object.freeze({
  synthetic: true,
  application: "Country Services",
  configuration: "Country settings",
  environment: "Development",
  capturedAtLabel: "26 Nov 2026, 20:14 CET",
  deployed: Object.freeze({ note: "Country cache limits", savedAtLabel: "26 Nov 2026, 19:46 CET" }),
  rows: Object.freeze([
    Object.freeze({ instance: "country-api-eu-01", version: "Country cache limits", savedAtLabel: "26 Nov 2026, 19:46 CET",
      status: "Up to date", kind: "success", observation: "Reported 2 minutes ago" }),
    Object.freeze({ instance: "country-api-eu-02", version: "European defaults", savedAtLabel: "25 Nov 2026, 16:30 CET",
      status: "Needs restart", kind: "warning", observation: "Reported 2 minutes ago" }),
    Object.freeze({ instance: "country-api-eu-03", version: "Not reported", savedAtLabel: "No accepted version reported",
      status: "Failed", kind: "error", observation: "Reported 3 minutes ago" }),
    Object.freeze({ instance: "country-api-eu-04", version: "European defaults", savedAtLabel: "25 Nov 2026, 16:30 CET",
      status: "Unknown", kind: "neutral", observation: "Last report 2 hours ago · stale" }),
    Object.freeze({ instance: "country-api-eu-05", version: "Version details unavailable", savedAtLabel: "Version metadata is restricted",
      status: "Up to date", kind: "success", observation: "Reported 1 minute ago" })
  ])
});

export const reviewNotes = Object.freeze({
  status: "Synthetic proposed screenshots; exact user approval pending",
  layout: "One backoffice desktop table with exactly Instance, Configuration version in use and Status. The second frame reuses the existing right-side details panel, not an extra screen.",
  version: "The visible version is the last reported accepted/in-use immutable release, labeled only with authorized saved note and date. It is not desired generation, a new ordinal or proof of current live values. Restricted metadata says Version details unavailable.",
  statuses: "Up to date, Needs restart and Failed require known corresponding native/report facts. Stale or missing observations remain neutral Unknown. A notification fault alone is not Failed. The Failed example has no currently reported accepted version; historical success does not populate the main cell.",
  authority: "Existing AdoptionRead-only access remains sufficient for these observations. Missing version-label metadata must not add a content/definition prerequisite or disclose full native identities.",
  diagnostics: "Native identities, generations, raw facets and historical last-successful facts are available only inside the existing panel's collapsed Technical details when authorized. This preview adds no fleet totals, percentages or process identity.",
  time: "Human dates use the explicitly synthetic Malta/CET labels shown. Report times are independently dated observations, not publication or assignment timestamps.",
  limits: "No portal source, backend, live fixture, hosted publication, new dependency or native verification is performed by this design artifact. Existing source/template approval is preserved; these new pixels await approval."
});

const palette = Object.freeze({ background: "#F8FAFC", white: "#FFFFFF", ink: "#0F172A", text: "#334155",
  muted: "#64748B", border: "#D7DFE9", blue: "#2563EB", selected: "#EFF6FF" });
const width = 1600, height = 1000, left = 248, right = 1552, tableTop = 332;
function sha(value) { return createHash("sha256").update(value).digest("hex"); }
function esc(value) {
  return String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
}
function rect(x, y, w, h, fill = palette.white, stroke = palette.border, radius = 7, extra = "") {
  return `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${radius}" fill="${fill}" stroke="${stroke}" ${extra}/>`;
}
function text(value, x, y, size = 14, fill = palette.text, weight = 400, extra = "") {
  return `<text x="${x}" y="${y}" font-family="Inter Variable" font-size="${size}" font-weight="${weight}" fill="${fill}" ${extra}>${esc(value)}</text>`;
}
function line(x1, y, x2, color = palette.border) {
  return `<line x1="${x1}" y1="${y}" x2="${x2}" y2="${y}" stroke="${color}"/>`;
}
function button(label, x, y, w = 112) {
  return rect(x, y, w, 40, palette.white, palette.border, 6)
    + text(label, x + w / 2, y + 25, 13, palette.text, 550, 'text-anchor="middle"');
}
function badge(label, kind, x, y) {
  const colors = kind === "success" ? ["#EAF5F3", "#0F766E"]
    : kind === "warning" ? ["#FFF4DE", "#92400E"]
      : kind === "error" ? ["#FEF2F2", "#B91C1C"] : ["#EEF2F7", "#475569"];
  const w = Math.ceil(label.length * 7 + 24);
  return rect(x, y, w, 28, colors[0], colors[0], 5) + text(label, x + 12, y + 19, 12, colors[1], 600);
}
function shell(content) {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}">`
    + `<defs><style>@font-face{font-family:'Inter Variable';src:url(data:font/ttf;base64,${embeddedFontBase64}) format('truetype');font-weight:100 900;}</style></defs>`
    + rect(0, 0, width, height, palette.background, palette.background, 0)
    + rect(0, 0, 200, height, palette.ink, palette.ink, 0)
    + text("CIS", 24, 49, 27, palette.white, 650) + text("Management", 24, 78, 16, "#CBD5E1")
    + rect(16, 133, 168, 44, "#1E293B", "#1E293B", 7) + text("Applications", 34, 161, 14, palette.white, 600)
    + text("Design preview", 24, height - 40, 11, "#94A3B8")
    + rect(200, 0, width - 200, 88, palette.white, palette.border, 0)
    + text("Management", left, 51, 17, palette.text, 600)
    + text("SYNTHETIC DESIGN PREVIEW", right, 35, 10.5, palette.muted, 600, 'text-anchor="end"')
    + text(`${previewInputs.application} · ${previewInputs.environment}`, right, 59, 12, palette.muted, 400, 'text-anchor="end"')
    + content + "</svg>";
}
function mainTable(selected = false) {
  let result = text("Applications  /  Country Services  /  Configuration  /  Country settings", left, 125, 12, palette.muted)
    + text("Instance status", left, 180, 30, palette.ink, 650)
    + text("Country settings · Development", left, 213, 14, palette.text)
    + button("Refresh", right - 112, 155)
    + text("Last reported Configuration per instance", left, 270, 15, palette.text, 550)
    + text(`Read ${previewInputs.capturedAtLabel}`, right, 270, 11.5, palette.muted, 400, 'text-anchor="end"')
    + text("Reports describe each instance's last observation. Stale reports are shown as Unknown.", left, 299, 12, palette.muted)
    + rect(left, tableTop, right - left, 558)
    + rect(left + 1, tableTop + 1, right - left - 2, 56, "#F1F5F9", "transparent", 6)
    + text("Instance", left + 24, tableTop + 35, 12.5, palette.text, 600)
    + text("Configuration version in use", left + 400, tableTop + 35, 12.5, palette.text, 600)
    + text("Status", left + 941, tableTop + 35, 12.5, palette.text, 600)
    + line(left, tableTop + 57, right);
  previewInputs.rows.forEach((row, index) => {
    const top = tableTop + 58 + index * 100;
    if (selected && index === 1) {
      result += rect(left + 1, top, right - left - 2, 100, palette.selected, "transparent", 0)
        + rect(left + 1, top, 3, 100, palette.blue, palette.blue, 0);
    }
    result += text(row.instance, left + 24, top + 39, 14, palette.blue, 600)
      + text(row.version, left + 400, top + 35, 14, row.version === "Not reported" ? palette.muted : palette.ink, 550)
      + text(row.savedAtLabel, left + 400, top + 61, 11.5, palette.muted)
      + badge(row.status, row.kind, left + 941, top + 19)
      + text(row.observation, left + 941, top + 67, 11, palette.muted)
      + (index < previewInputs.rows.length - 1 ? line(left, top + 100, right) : "");
  });
  return result + text("Select an instance for its report details.", left, 925, 12, palette.muted);
}
function detailsPanel() {
  const x = 1040, panelWidth = width - x;
  return mainTable(true)
    + rect(200, 88, width - 200, height - 88, "#0F172A", "transparent", 0, 'opacity="0.15"')
    + rect(x, 88, panelWidth, height - 88, palette.white, palette.border, 0)
    + text("Instance report", x + 32, 127, 11.5, palette.muted, 550)
    + text("country-api-eu-02", x + 32, 169, 23, palette.ink, 650)
    + text("×", width - 37, 133, 26, palette.muted, 400, 'text-anchor="middle"')
    + text("Country Services · Development", x + 32, 200, 13, palette.muted)
    + line(x, 226, width)
    + text("Status", x + 32, 266, 12, palette.muted, 550)
    + badge("Needs restart", "warning", x + 32, 282)
    + text("At its last report, this instance required a restart", x + 32, 342, 13, palette.text)
    + text("to use the deployed Configuration version.", x + 32, 365, 13, palette.text)
    + text("Configuration version in use", x + 32, 422, 12, palette.muted, 550)
    + text("European defaults", x + 32, 454, 18, palette.ink, 600)
    + text("Saved 25 Nov 2026, 16:30 CET", x + 32, 480, 12, palette.muted)
    + text("Deployed version", x + 32, 535, 12, palette.muted, 550)
    + text(previewInputs.deployed.note, x + 32, 566, 15, palette.text, 550)
    + text(`Saved ${previewInputs.deployed.savedAtLabel}`, x + 32, 592, 12, palette.muted)
    + text("Last reported", x + 32, 648, 12, palette.muted, 550)
    + text("26 Nov 2026, 20:12 CET", x + 32, 678, 14, palette.text)
    + text("This is a reported observation, not a live-value check.", x + 32, 709, 11.5, palette.muted)
    + line(x + 32, 761, width - 32)
    + text("›", x + 32, 797, 19, palette.muted)
    + text("Technical details", x + 57, 795, 13, palette.text, 550);
}

const screens = Object.freeze([
  { file: "instance-status-table-preview.png", content: () => mainTable(false) },
  { file: "instance-status-panel-preview.png", content: detailsPanel }
]);
if (previewInputs.synthetic !== true || previewInputs.rows.length !== 5) {
  throw new Error("Only the explicit synthetic observation examples may be rendered.");
}
const fontRuntimeRoot = path.join(outputDirectory, ".font-runtime-instance-status");
mkdirSync(fontRuntimeRoot, { recursive: true });
const fontRuntimeDirectory = mkdtempSync(path.join(fontRuntimeRoot, "render-"));
const temporaryFontPath = path.join(fontRuntimeDirectory, "InterVariable.ttf");
function checkedFontPath(value) {
  const resolved = path.resolve(value);
  if (!resolved.startsWith(path.resolve(fontRuntimeRoot) + path.sep)) {
    throw new Error("Unexpected owned font cleanup path.");
  }
  return resolved;
}
try {
  writeFileSync(temporaryFontPath, fontBytes);
  await sharp({ text: { text: "font preload", fontfile: temporaryFontPath, font: "Inter Variable", rgba: true } }).png().toBuffer();
  const frames = [];
  for (const screen of screens) {
    const output = path.join(outputDirectory, screen.file);
    await sharp(Buffer.from(shell(screen.content()))).png().toFile(output);
    const metadata = await sharp(output).metadata();
    const stats = await sharp(output).stats();
    if (metadata.width !== width || metadata.height !== height || stats.channels.every(channel => channel.stdev === 0)) {
      throw new Error("Invalid or blank Instance status preview.");
    }
    const bytes = readFileSync(output);
    frames.push({ path: screen.file, width, height, bytes: bytes.length, sha256: sha(bytes) });
    console.log(`Rendered ${screen.file} (${width}x${height})`);
  }
  writeFileSync(path.join(outputDirectory, "instance-status-preview-manifest.json"), JSON.stringify({
    synthetic: true,
    status: "Pending exact screenshot approval; not production/browser evidence",
    renderer: { path: "render-instance-status-preview.mjs", sha256: sha(readFileSync(fileURLToPath(import.meta.url))) },
    fontSource: { path: path.basename(fontSource), embeddedFontSha256 },
    inputSha256: sha(JSON.stringify(previewInputs)),
    reviewNotes,
    frames,
    runtime: { node: process.version, sharp: sharp.versions.sharp, libvips: sharp.versions.vips }
  }, null, 2));
} finally {
  unlinkSync(checkedFontPath(temporaryFontPath));
  rmdirSync(checkedFontPath(fontRuntimeDirectory));
  try {
    rmdirSync(fontRuntimeRoot);
  } catch (error) {
    if (error.code !== "ENOTEMPTY") {
      throw error;
    }
  }
}
console.log(`Runtime node=${process.version} sharp=${sharp.versions.sharp} libvips=${sharp.versions.vips}`);
