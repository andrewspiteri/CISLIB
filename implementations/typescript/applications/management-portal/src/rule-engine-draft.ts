import { OpaqueJsonObject } from "@cislib/management-client";
import { jsonArrayMemberValues } from "./configuration-draft.ts";

type TypedPreset = "string" | "decimal" | "boolean" | "stringList";
export type PresetKind = TypedPreset | "any" | "missing" | "";
export type RuleState = "enabled" | "test" | "disabled";
export type RuleMode = "firstMatch" | "allMatching";
export interface RuleColumn { readonly id: string; readonly displayName: string; readonly inputKind: Exclude<TypedPreset, "stringList">;
  readonly allowedPresetKinds: readonly PresetKind[]; }
export interface RuleCell { readonly columnId: string; readonly presetKind: PresetKind; readonly operator?: string;
  readonly preset?: { readonly kind: TypedPreset; readonly value?: string | boolean | readonly string[] }; }
export interface RuleRow { readonly ruleId: string; readonly ordinal: number; readonly state: RuleState; readonly weight: string;
  readonly comment?: string; readonly metadataText?: string; readonly conditions: readonly RuleCell[]; }
interface MetadataShape { readonly kind: "object" | "array" | "number" | "boolean" | "string"; readonly nullable: boolean;
  readonly properties?: readonly { readonly name: string; readonly required: boolean; readonly shape: MetadataShape }[];
  readonly element?: MetadataShape; }
export interface RuleDraft { readonly rulesetId: string; readonly schemaRevision: string; readonly version: string; readonly mode: RuleMode;
  readonly displayName?: string; readonly columns: readonly RuleColumn[]; readonly rows: readonly RuleRow[]; readonly metadataShape?: MetadataShape; }
export interface DraftIssue { readonly message: string; readonly ruleId?: string; readonly columnId?: string; readonly field?: "weight" | "metadata" | "comment"; }

export const ruleUiLimits = { definitionBytes: 128 * 1024, rows: 48, columns: 24, listItems: 32, stringBytes: 512, metadataBytes: 4096 } as const;
const utf8 = new TextEncoder();
const operators = {
  string: ["equals", "notEquals", "wildcardMatch"], decimal: ["equals", "notEquals", "greaterThan", "greaterThanOrEqual", "lessThan", "lessThanOrEqual"],
  boolean: ["equals", "notEquals"], stringList: ["isIn", "isNotIn"]
} as const;
export const operatorLabels: Readonly<Record<string, string>> = { equals: "Equals", notEquals: "Does not equal", wildcardMatch: "Wildcard matches",
  greaterThan: "Greater than", greaterThanOrEqual: "At least", lessThan: "Less than", lessThanOrEqual: "At most", isIn: "Is in", isNotIn: "Is not in" };
export const stateLabels: Readonly<Record<RuleState, string>> = { enabled: "Enabled", test: "Test", disabled: "Disabled" };
export const modeLabels: Readonly<Record<RuleMode, string>> = { firstMatch: "First match", allMatching: "All matching" };

export function isRuleSelector(value: unknown): value is string {
  return typeof value === "string" && value.length > 0 && value.length <= 128 && !/[\p{Cc}\uD800-\uDFFF]/u.test(value) && utf8.encode(value).length <= 128;
}
function boundedText(value: string, bytes: number) {
  return value.length <= bytes && !/[\uD800-\uDFFF]/u.test(value) && utf8.encode(value).length <= bytes;
}
export function ruleNote(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0 && !/\p{Cc}/u.test(value) && boundedText(value, 1024);
}
export function parseRuleDefinition(raw: string, rulesetId: string, schemaRevision: string, version?: string): RuleDraft {
  if (!boundedText(raw, ruleUiLimits.definitionBytes)) { throw new TypeError("The definition is too large."); }
  new OpaqueJsonObject(raw);
  const definition = JSON.parse(raw) as Omit<RuleDraft, "rows"> & { readonly rows: readonly (Omit<RuleRow, "metadataText"> & { readonly metadata?: unknown })[] };
  if (definition.rulesetId !== rulesetId || definition.schemaRevision !== schemaRevision || !isRuleSelector(definition.version)
    || version !== undefined && definition.version !== version || definition.mode !== "firstMatch" && definition.mode !== "allMatching"
    || !Array.isArray(definition.columns) || definition.columns.length > ruleUiLimits.columns
    || !Array.isArray(definition.rows) || definition.rows.length > ruleUiLimits.rows) {
    throw new TypeError("The definition does not match this ruleset.");
  }
  const metadata = jsonArrayMemberValues(raw, "rows", "metadata");
  return { ...definition, rows: definition.rows.map(({ metadata: _metadata, ...row }, index) => {
    const metadataText = metadata[index];
    return { ...row, ...(metadataText === undefined ? {} : { metadataText }) };
  }) };
}

export function ruleOperators(kind: PresetKind): readonly string[] {
  return kind === "" || kind === "any" || kind === "missing" ? [] : operators[kind];
}
export function rulePresetKinds(column: RuleColumn): readonly PresetKind[] {
  return [...column.allowedPresetKinds.filter(kind => kind === "string" || kind === "decimal" || kind === "boolean" || kind === "stringList"), "any", "missing"];
}
export function newRuleCell(columnId: string, kind: PresetKind): RuleCell {
  const operator = ruleOperators(kind)[0];
  if (operator === undefined || kind === "" || kind === "any" || kind === "missing") { return { columnId, presetKind: kind }; }
  return { columnId, presetKind: kind, operator, preset: { kind, ...(kind === "stringList" ? { value: [] } : {}) } };
}
export function updateRule(draft: RuleDraft, id: string, edit: (row: RuleRow) => RuleRow): RuleDraft {
  return { ...draft, rows: draft.rows.map(row => row.ruleId === id ? edit(row) : row) };
}
export function updateRuleCell(draft: RuleDraft, id: string, next: RuleCell): RuleDraft {
  return updateRule(draft, id, row => ({ ...row, conditions: row.conditions.map(cell => cell.columnId === next.columnId ? next : cell) }));
}
export function addRule(draft: RuleDraft, id: string, basedOn?: string): RuleDraft {
  if (draft.rows.length >= ruleUiLimits.rows || !isRuleSelector(id) || draft.rows.some(row => row.ruleId === id)) { return draft; }
  const rows = [...draft.rows], index = rows.findIndex(row => row.ruleId === basedOn), original = rows[index];
  const insertion = index < 0 ? rows.length : index + 1;
  const previous = rows[insertion - 1]?.ordinal;
  const next = rows[insertion]?.ordinal;
  const ordinal = Math.min((previous ?? 0) + 10, next === undefined ? 2147483647 : next - 1);
  const row: RuleRow = original === undefined ? { ruleId: id, ordinal, state: "enabled", weight: "0",
    conditions: draft.columns.map(column => newRuleCell(column.id, "")) } : { ...original, ordinal, ruleId: id };
  rows.splice(insertion, 0, row);
  return { ...draft, rows: ordinal > (previous ?? -1) ? rows : rows.map((item, position) => ({ ...item, ordinal: (position + 1) * 10 })) };
}
export function moveRule(draft: RuleDraft, id: string, step: -1 | 1): RuleDraft {
  const rows = [...draft.rows], current = rows.findIndex(row => row.ruleId === id), next = current + step;
  const moved = rows[current], neighbor = rows[next];
  if (moved === undefined || neighbor === undefined) { return draft; }
  rows[current] = { ...neighbor, ordinal: moved.ordinal };
  rows[next] = { ...moved, ordinal: neighbor.ordinal };
  return { ...draft, rows };
}
export function removeRule(draft: RuleDraft, id: string): RuleDraft {
  return { ...draft, rows: draft.rows.filter(row => row.ruleId !== id) };
}

export function validRuleDecimal(value: string): boolean {
  if (!boundedText(value, ruleUiLimits.definitionBytes) || !/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/u.test(value)) { return false; }
  const [integer = "", fullFraction = ""] = value.replace(/^-/, "").split(".");
  const fraction = fullFraction.replace(/0+$/u, "");
  return fraction.length <= 28 && integer.length + fraction.length <= 29
    && BigInt(integer + fraction) <= 79228162514264337593543950335n;
}
function validMetadata(raw: string | undefined, shape: MetadataShape | undefined): boolean {
  if (raw === undefined || raw.trim() === "") { return shape === undefined || shape.nullable; }
  if (!boundedText(raw, ruleUiLimits.metadataBytes)) { return false; }
  try {
    const value: unknown = JSON.parse(raw);
    new OpaqueJsonObject('{"value":' + raw + "}");
    return (shape === undefined || metadataMatches(value, shape)) && metadataDepth(value) <= 13;
  } catch { return false; }
}
function metadataDepth(value: unknown): number {
  if (typeof value !== "object" || value === null) { return 0; }
  return 1 + Math.max(0, ...Object.values(value).map(metadataDepth));
}
function metadataMatches(value: unknown, shape: MetadataShape): boolean {
  if (value === null) { return shape.nullable; }
  if (shape.kind === "array") {
    const element = shape.element;
    return Array.isArray(value) && element !== undefined && value.every(item => metadataMatches(item, element));
  }
  if (shape.kind === "object") {
    if (typeof value !== "object" || Array.isArray(value)) { return false; }
    return (shape.properties ?? []).every(field => Object.hasOwn(value, field.name)
      ? metadataMatches(Reflect.get(value, field.name), field.shape) : !field.required);
  }
  return typeof value === shape.kind;
}
export function ruleCellIssue(cell: RuleCell, column: RuleColumn): string | undefined {
  if (cell.presetKind === "any" || cell.presetKind === "missing") { return undefined; }
  if (cell.presetKind === "") { return "Choose a typed condition, Any or Missing."; }
  if (!column.allowedPresetKinds.includes(cell.presetKind) || cell.operator === undefined || !ruleOperators(cell.presetKind).includes(cell.operator)
    || cell.preset?.kind !== cell.presetKind) { return "Choose a condition and operator allowed by the schema."; }
  const value = cell.preset.value;
  if (cell.presetKind === "decimal") { return typeof value === "string" && validRuleDecimal(value) ? undefined : "Enter an exact decimal without grouping or an exponent."; }
  if (cell.presetKind === "boolean") { return typeof value === "boolean" ? undefined : "Choose true or false."; }
  if (cell.presetKind === "stringList") { return Array.isArray(value) && value.length <= ruleUiLimits.listItems
    && value.every(item => typeof item === "string" && boundedText(item, ruleUiLimits.stringBytes)) ? undefined : "Enter no more than 32 bounded literal strings."; }
  if (typeof value !== "string" || !boundedText(value, ruleUiLimits.stringBytes)) { return "Enter text within 512 UTF-8 bytes."; }
  if (cell.operator === "wildcardMatch" && /\\(?![\\*?])/u.test(value.replace(/\\\\/gu, ""))) { return "Escape only an asterisk, question mark or backslash."; }
  return undefined;
}

export function prepareRuleDefinition(draft: RuleDraft): { readonly json: string } | { readonly issue: DraftIssue } {
  for (const row of draft.rows) {
    for (const column of draft.columns) {
      const cell = row.conditions.find(item => item.columnId === column.id) ?? newRuleCell(column.id, "");
      const message = ruleCellIssue(cell, column);
      if (message !== undefined) { return { issue: { ruleId: row.ruleId, columnId: column.id, message } }; }
    }
    if (!validRuleDecimal(row.weight)) { return { issue: { ruleId: row.ruleId, field: "weight", message: "Enter an exact decimal weight." } }; }
    if (row.comment !== undefined && !boundedText(row.comment, ruleUiLimits.definitionBytes)) {
      return { issue: { ruleId: row.ruleId, field: "comment", message: "This comment exceeds the definition limit." } };
    }
    if (!validMetadata(row.metadataText, draft.metadataShape)) {
      return { issue: { ruleId: row.ruleId, field: "metadata", message: "Enter complete bounded JSON matching the declared metadata shape." } };
    }
  }
  const { rows, ...definition } = draft;
  const parts = rows.map(({ metadataText, ...row }) => {
    const json = JSON.stringify(row);
    return metadataText === undefined || metadataText.trim() === "" ? json : json.slice(0, -1) + ',"metadata":' + metadataText.trim() + "}";
  });
  const json = JSON.stringify(definition).slice(0, -1) + ',"rows":[' + parts.join(",") + "]}";
  return boundedText(json, ruleUiLimits.definitionBytes) ? { json } : { issue: { message: "Keep the complete version within 128 KiB." } };
}

export function ruleCellLabel(cell: RuleCell): string {
  if (cell.presetKind === "any" || cell.presetKind === "missing") { return cell.presetKind === "any" ? "Any" : "Missing"; }
  if (cell.presetKind === "" || cell.preset?.value === undefined) { return "Set condition"; }
  const value = cell.preset.value;
  return (operatorLabels[cell.operator ?? ""] ?? "Compare") + " " + (Array.isArray(value) ? value.join(", ") : String(value));
}
export function ruleRowLabel(row: RuleRow): string { return row.comment?.trim().split(/\r?\n/u, 1)[0]?.slice(0, 100) || "Rule " + row.ordinal; }
