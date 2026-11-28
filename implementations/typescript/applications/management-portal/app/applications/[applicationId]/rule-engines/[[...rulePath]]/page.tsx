import { notFound } from "next/navigation";
import { isCursor, isUuid } from "@cislib/management-client";
import { RuleEnginePage } from "../../../../../src/components/rule-engine-page";
import { applicationSelection, listContext, parameter, validRouteParameters } from "../../../../../src/page-input";
import type { PageInput } from "../../../../../src/page-input";
import { isRuleSelector } from "../../../../../src/rule-engine-draft";
import { readRuleExecutionFilters, ruleExecutionFilterKeys } from "../../../../../src/rule-engine-routes";
import type { RuleHistoryReturn, RulePortalView } from "../../../../../src/rule-engine-routes";

export default async function RuleEnginesRoute({ params, searchParams }: {
  params: Promise<{ applicationId: string; rulePath?: string[] }>; searchParams: Promise<PageInput>
}) {
  const path = await params, input = await searchParams, segments = path.rulePath ?? [];
  const applicationId = applicationSelection(path.applicationId, input);
  const rulesetId = parameter(input, "rulesetId") ?? segments[0], action = segments[1];
  const historyView = action === "executions";
  const version = historyView ? undefined : parameter(input, "version");
  const schemaRevision = parameter(input, "schemaRevision");
  if (applicationId === undefined || !validRouteParameters(input, ["rulesetId", "version", "schemaRevision"])
    || segments.length > 3 || rulesetId !== undefined && !isRuleSelector(rulesetId)
    || segments.length === 0 && (rulesetId !== undefined || version !== undefined)
    || version !== undefined && !isUuid(version)
    || action !== undefined && !["new-version", "versions", "deploy", "deployment", "reconcile", "counters", "executions"].includes(action)
    || (action === "new-version") !== (schemaRevision !== undefined)
    || schemaRevision !== undefined && (!isRuleSelector(schemaRevision) || version !== undefined)
    || action === "deploy" && (version !== undefined || !isUuid(segments[2]))
    || action !== "deploy" && !historyView && segments.length > 2
    || historyView && segments.length === 3 && !isUuid(segments[2])) { notFound(); }
  const selectedVersion = action === "deploy" ? segments[2] : version;
  const executionId = historyView ? segments[2] : undefined;
  const view = (executionId === undefined ? action ?? "grid" : "execution") as RulePortalView;
  let history: RuleHistoryReturn = { filters: {} };
  if (historyView) {
    if (!validRouteParameters(input, [...ruleExecutionFilterKeys, "historyCursor", "focusExecution"])) { notFound(); }
    const cursor = parameter(input, "historyCursor"), focusExecution = parameter(input, "focusExecution");
    if (cursor !== undefined && !isCursor(cursor) || focusExecution !== undefined && !isUuid(focusExecution)) { notFound(); }
    try {
      history = { filters: readRuleExecutionFilters(key => parameter(input, key)),
        ...(cursor === undefined ? {} : { cursor }), ...(focusExecution === undefined ? {} : { focusExecution }) };
    } catch { notFound(); }
  }
  return <RuleEnginePage key={JSON.stringify([applicationId, rulesetId, view, selectedVersion, schemaRevision, executionId, history])}
    applicationId={applicationId} rulesetId={rulesetId} view={view} initialVersion={selectedVersion}
    newSchemaRevision={schemaRevision} executionId={executionId} history={history} context={listContext(input)} />;
}
