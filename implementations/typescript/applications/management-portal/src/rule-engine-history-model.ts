import type { RuleExecutionDetail } from "@cislib/management-client";
import { jsonArrayMemberValues } from "./configuration-draft.ts";
import { parseRuleDefinition } from "./rule-engine-draft.ts";
import type { RuleDraft, RuleRow } from "./rule-engine-draft.ts";

interface CapturedInput { readonly columnId: string; readonly kind: string; readonly value?: unknown; }
interface AuditFacts { readonly correlationId?: string; readonly engineContractVersion: number; }
interface StartFacts extends AuditFacts { readonly inputs: readonly CapturedInput[]; }
interface CompletionFacts extends AuditFacts {
  readonly status: "Completed" | "Failed" | "Cancelled"; readonly rows: readonly ObservedRow[]; readonly selectedRowIndices: readonly number[];
  readonly errorCode?: string; readonly errorRow?: number; readonly errorColumn?: number; readonly priorErrorCode?: string;
}
interface ObservedRow { readonly rowIndex: number; readonly state: string; readonly outcome: string; readonly cellOutcomes: string;
  readonly errorCode?: string; readonly errorRow?: number; readonly errorColumn?: number; }
export interface InspectedInput {
  readonly columnId: string; readonly label: string; readonly presence: "Omitted" | "Supplied null" | "Supplied"; readonly kind?: string;
  readonly value?: string; readonly unknown: boolean;
}
export interface InspectedTrace { readonly row: RuleRow; readonly state: string; readonly outcome: string; readonly cells: readonly string[];
  readonly errorCode?: string; readonly errorColumn?: number; }
export interface ExecutionInspection {
  readonly definition: RuleDraft; readonly inputs: readonly InspectedInput[] | undefined; readonly trace: readonly InspectedTrace[] | undefined;
  readonly selected: readonly RuleRow[] | undefined; readonly correlationId: string | undefined; readonly engineVersion: number | undefined;
  readonly stop: string; readonly errorCode: string | undefined; readonly priorErrorCode: string | undefined;
}
const cellStates: Readonly<Record<string, string>> = { u: "Not evaluated", n: "Not matched", m: "Matched", e: "Error" };
const types: Readonly<Record<string, string>> = { string: "String", decimal: "Decimal", boolean: "Boolean", stringList: "String list", json: "JSON", null: "Null" };

export function inspectExecution(detail: RuleExecutionDetail): ExecutionInspection {
  const { execution } = detail;
  const definition = parseRuleDefinition(detail.definitionJson, execution.rulesetId, execution.schemaRevision, execution.version);
  const start = auditFacts<StartFacts>(detail.startAuditJson);
  const completion = auditFacts<CompletionFacts>(detail.completionAuditJson);
  const inputs = start === undefined ? undefined : inspectInputs(definition, start.inputs,
    jsonArrayMemberValues(detail.startAuditJson!, ["details", "inputs"], "value"));
  const trace = completion?.rows.map(item => {
    const row = definition.rows[item.rowIndex];
    if (row === undefined || item.cellOutcomes.length !== definition.columns.length) { throw new TypeError("The observed trace does not match its definition."); }
    return { row, outcome: traceLabel(item), state: item.state, cells: [...item.cellOutcomes].map(letter => cellStates[letter] ?? "Unknown"),
      ...(item.errorCode === undefined ? {} : { errorCode: item.errorCode }),
      ...(item.errorColumn === undefined ? {} : { errorColumn: item.errorColumn }) };
  });
  const selected = execution.status !== "Completed" || completion?.status !== "Completed" ? undefined : completion.selectedRowIndices.map(index => {
    const row = definition.rows[index];
    if (row === undefined) { throw new TypeError("A returned match does not exist in its retained definition."); }
    return row;
  });
  const stop = execution.status === "Incomplete" ? "An audit phase has not been received. The terminal result is unconfirmed."
    : execution.status === "Failed" ? "Evaluation failed." : execution.status === "Cancelled" ? "Evaluation was cancelled."
      : definition.mode === "firstMatch" && selected?.length === 1 ? "First match stopped at the first matching enabled rule."
        : "Evaluation reached the end of the eligible rules.";
  return { definition, inputs, trace, selected, stop, correlationId: start?.correlationId ?? completion?.correlationId,
    engineVersion: start?.engineContractVersion ?? completion?.engineContractVersion, errorCode: completion?.errorCode, priorErrorCode: completion?.priorErrorCode };
}

function auditFacts<T extends AuditFacts>(raw: string | undefined): T | undefined {
  if (raw === undefined) { return undefined; }
  const record = JSON.parse(raw) as { readonly details: T };
  return record.details;
}
function inspectInputs(draft: RuleDraft, captured: readonly CapturedInput[], raw: readonly (string | undefined)[]): readonly InspectedInput[] {
  const inputs = new Map(captured.map((input, index) => [input.columnId, { input, raw: raw[index] }]));
  const declared = new Set(draft.columns.map(column => column.id));
  const capturedFields = draft.columns.map(column => ({ columnId: column.id, label: column.displayName }));
  for (const value of captured) {
    if (!declared.has(value.columnId)) { capturedFields.push({ columnId: value.columnId, label: value.columnId }); }
  }
  return capturedFields.map(field => {
    const value = inputs.get(field.columnId), unknown = !declared.has(field.columnId);
    if (value === undefined) { return { ...field, presence: "Omitted", unknown }; }
    if (value.input.kind === "null") { return { ...field, presence: "Supplied null", value: "null", unknown }; }
    const text = value.input.kind === "string" || value.input.kind === "decimal" ? String(value.input.value) : value.raw;
    return { ...field, presence: "Supplied", kind: types[value.input.kind] ?? value.input.kind, unknown,
      ...(text === undefined ? {} : { value: text }) };
  });
}
function traceLabel(observed: ObservedRow) {
  switch (observed.outcome) {
    case "Matched": return observed.state === "Test" ? "Matched · test hit" : "Matched";
    case "Disabled": return "Skipped · disabled";
    case "NotReached": return "Not reached";
    case "NotMatched": return "Not matched";
    case "Error": return observed.state === "Test" ? "Test diagnostic" : "Error";
    default: return "Unknown";
  }
}
