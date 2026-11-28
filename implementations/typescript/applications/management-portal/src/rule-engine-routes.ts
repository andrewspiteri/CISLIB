import { isCursor, isTimestamp, isUuid } from "@cislib/management-client";
import type { RuleExecutionQuery, RuleExecutionStatus } from "@cislib/management-client";
import { readableSlug } from "./portal-routes.ts";
import type { PortalListContext } from "./portal-routes.ts";
import { isRuleSelector } from "./rule-engine-draft.ts";

export type RulePortalView = "grid" | "new-version" | "versions" | "deploy" | "deployment" | "reconcile" | "counters" | "executions" | "execution";
export interface RuleHistoryReturn { readonly filters: RuleExecutionQuery; readonly cursor?: string; readonly focusExecution?: string; }
export const ruleExecutionFilterKeys = ["executionId", "version", "instanceId", "status", "startedFromUtc", "startedThroughUtc"] as const;

export function readRuleExecutionFilters(read: (key: typeof ruleExecutionFilterKeys[number]) => string | undefined): RuleExecutionQuery {
  const executionId = read("executionId"), version = read("version"), instanceId = read("instanceId"), status = read("status"),
    startedFromUtc = read("startedFromUtc"), startedThroughUtc = read("startedThroughUtc");
  if ([executionId, version, instanceId].some(value => value !== undefined && !isUuid(value))
    || status !== undefined && !["Completed", "Failed", "Cancelled", "Incomplete"].includes(status)
    || startedFromUtc !== undefined && !isTimestamp(startedFromUtc) || startedThroughUtc !== undefined && !isTimestamp(startedThroughUtc)
    || startedFromUtc !== undefined && startedThroughUtc !== undefined && startedFromUtc > startedThroughUtc) {
    throw new TypeError("Use exact execution filters and an ordered UTC interval.");
  }
  return { ...(executionId === undefined ? {} : { executionId }), ...(version === undefined ? {} : { version }),
    ...(instanceId === undefined ? {} : { instanceId }), ...(status === undefined ? {} : { status: status as RuleExecutionStatus }),
    ...(startedFromUtc === undefined ? {} : { startedFromUtc }), ...(startedThroughUtc === undefined ? {} : { startedThroughUtc }) };
}

export function ruleEngineRoute(input: {
  readonly applicationId: string; readonly applicationName?: string | undefined; readonly rulesetId?: string | undefined;
  readonly rulesetName?: string | undefined; readonly view?: RulePortalView | undefined; readonly version?: string | undefined;
  readonly schemaRevision?: string | undefined;
  readonly executionId?: string | undefined; readonly history?: RuleHistoryReturn | undefined; readonly context?: PortalListContext | undefined;
}) {
  if (!isUuid(input.applicationId) || input.rulesetId !== undefined && !isRuleSelector(input.rulesetId)
    || input.version !== undefined && !isUuid(input.version) || input.view === "deploy" && input.version === undefined
    || (input.view === "new-version") !== (input.schemaRevision !== undefined)
    || input.schemaRevision !== undefined && (!isRuleSelector(input.schemaRevision) || input.version !== undefined || input.rulesetId === undefined)
    || input.view === "execution" && !isUuid(input.executionId)
    || input.history?.cursor !== undefined && !isCursor(input.history.cursor)
    || input.history?.focusExecution !== undefined && !isUuid(input.history.focusExecution)) {
    throw new TypeError("Select one exact Rule Engine view.");
  }
  const query = new URLSearchParams({ applicationId: input.applicationId });
  let path = "/applications/" + encodeURIComponent(readableSlug(input.applicationName, "application")) + "/rule-engines";
  if (input.rulesetId !== undefined) {
    path += "/" + encodeURIComponent(readableSlug(input.rulesetName ?? input.rulesetId, "configuration"));
    query.set("rulesetId", input.rulesetId);
  }
  if (input.view !== undefined && input.view !== "grid") {
    path += "/" + (input.view === "execution" ? "executions" : input.view);
    if (input.view === "deploy") { path += "/" + input.version; }
    if (input.view === "execution") { path += "/" + input.executionId; }
  }
  if (input.version !== undefined && input.view !== "deploy") { query.set("version", input.version); }
  if (input.schemaRevision !== undefined) { query.set("schemaRevision", input.schemaRevision); }
  if (input.history !== undefined) {
    const filters = readRuleExecutionFilters(key => input.history?.filters[key]);
    for (const [key, value] of Object.entries(filters)) { query.set(key, value); }
    if (input.history.cursor !== undefined) { query.set("historyCursor", input.history.cursor); }
    if (input.history.focusExecution !== undefined) { query.set("focusExecution", input.history.focusExecution); }
  }
  for (const [key, value] of Object.entries(input.context ?? {})) {
    if (typeof value === "string" && value !== "") { query.set(key, value); }
  }
  return path + "?" + query.toString();
}
