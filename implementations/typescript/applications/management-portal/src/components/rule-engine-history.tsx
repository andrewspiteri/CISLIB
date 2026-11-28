"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import { isTimestamp, isUuid } from "@cislib/management-client";
import type { Page, RuleExecutionDetail, RuleExecutionStatus, RuleExecutionSummary, RuleVersionSummary } from "@cislib/management-client";
import { inspectExecution } from "../rule-engine-history-model.ts";
import type { ExecutionInspection, InspectedTrace } from "../rule-engine-history-model.ts";
import { ruleCellLabel, ruleRowLabel } from "../rule-engine-draft.ts";
import type { RuleHistoryReturn } from "../rule-engine-routes.ts";
import { ruleVersionLabel } from "./rule-engine-adoption";
import { Badge, Empty, Fact, Facts, Pager, ReadNotice, TechnicalDetails, Timestamp, queryUrl, ready, usePages, usePortalRead } from "./shared";

interface HistoryProps {
  readonly applicationId: string; readonly rulesetId: string; readonly environment: string;
  readonly base: string; readonly versions: readonly RuleVersionSummary[]; readonly revision: number;
  readonly history: RuleHistoryReturn; readonly path: (options: RuleHistoryReturn, executionId?: string) => string;
}
const outcomes = ["Completed", "Failed", "Cancelled", "Incomplete"] as const;

export function RuleExecutionHistory({ applicationId, rulesetId, environment, base, versions, revision, history, path }: HistoryProps) {
  const router = useRouter();
  const [filter, setFilter] = useState(history.filters);
  const [issue, setIssue] = useState<string>();
  const focused = useRef(false);
  const query = history.filters;
  const pages = usePages(JSON.stringify([applicationId, rulesetId, query]), history.cursor);
  const read = usePortalRead<Page<RuleExecutionSummary>>(queryUrl(base + "/executions", { rulesetId, ...query, cursor: pages.cursor, pageSize: "25" }), revision);
  const response = ready(read);
  const page = response?.items.every(item => item.applicationId === applicationId && item.rulesetId === rulesetId && item.environment === environment) ? response : undefined;
  const instances = [...new Set([filter.instanceId, ...(page?.items ?? []).map(item => item.instanceId)].flatMap(item => item ? [item] : []))];
  useEffect(() => {
    if (focused.current || page === undefined || read.busy || history.focusExecution === undefined) { return; }
    const row = document.getElementById("rm11-execution-" + history.focusExecution);
    if (row !== null) { row.focus({ preventScroll: true }); row.scrollIntoView({ block: "nearest" }); focused.current = true; }
  }, [page, read.busy, history.focusExecution]);
  function submit(event: FormEvent) {
    event.preventDefault();
    const value = Object.fromEntries(Object.entries(filter).filter(([, item]) => item !== undefined && item !== "")) as RuleHistoryReturn["filters"];
    const from = utcFilter(value.startedFromUtc), through = utcFilter(value.startedThroughUtc);
    if (value.executionId !== undefined && !isUuid(value.executionId) || value.version !== undefined && !isUuid(value.version)
      || value.instanceId !== undefined && !isUuid(value.instanceId)
      || value.startedFromUtc !== undefined && from === undefined || value.startedThroughUtc !== undefined && through === undefined
      || from !== undefined && through !== undefined && from > through) {
      setIssue("Enter exact identifiers and a valid UTC time range."); return;
    }
    setIssue(undefined);
    pages.reset();
    router.push(path({ filters: { ...value, ...(from === undefined ? {} : { startedFromUtc: from }),
      ...(through === undefined ? {} : { startedThroughUtc: through }) } }));
  }
  function change(key: keyof RuleHistoryReturn["filters"], value: string) { setFilter(current => ({ ...current, [key]: value })); }
  return <section aria-label="Execution history">
    <form onSubmit={submit} className="rm11-execution-filters">
      <label>Execution ID<input value={filter.executionId ?? ""} onChange={event => change("executionId", event.target.value)} placeholder="Paste an exact ID" /></label>
      <label>Version<input aria-label="Version" value={filter.version ?? ""} list="rm11-execution-versions" onChange={event => change("version", event.target.value)} placeholder="Any version" />
        <datalist id="rm11-execution-versions">{versions.map(version => <option key={version.version} value={version.version}>{ruleVersionLabel(version.version, versions)}</option>)}</datalist></label>
      <label>Instance<input aria-label="Instance" value={filter.instanceId ?? ""} list="rm11-execution-instances" onChange={event => change("instanceId", event.target.value)} placeholder="Any instance" />
        <datalist id="rm11-execution-instances">{instances.map(instance => <option key={instance} value={instance} />)}</datalist></label>
      <label>Started from (UTC)<input value={filter.startedFromUtc ?? ""} onChange={event => change("startedFromUtc", event.target.value)} placeholder="2026-11-26T16:00:00Z" /></label>
      <label>Started through (UTC)<input value={filter.startedThroughUtc ?? ""} onChange={event => change("startedThroughUtc", event.target.value)} placeholder="2026-11-26T17:00:00Z" /></label>
      <label>Outcome<select aria-label="Outcome" value={filter.status ?? ""} onChange={event => change("status", event.target.value)}><option value="">All outcomes</option>
        {outcomes.map(outcome => <option key={outcome} value={outcome}>{outcome}</option>)}</select></label>
      <button className="primary" type="submit">Find executions</button>
    </form>
    {issue === undefined ? null : <p className="notice" role="alert">{issue}</p>}
    <ReadNotice state={read} restart={pages.reset} />
    {page === undefined ? null : <div className="table-frame"><table><thead><tr>
      <th>Started</th><th>Version</th><th>Instance</th><th>Outcome</th><th>Returned matches</th><th>Execution</th>
    </tr></thead><tbody>{page.items.map(item => <tr key={item.executionId}>
      <td><Timestamp value={item.startedAtUtc} compact /></td><td>{ruleVersionLabel(item.version, versions)}</td><td>{item.instanceId}</td>
      <td><ExecutionBadge status={item.status} /></td>
      <td>{item.status === "Completed" ? item.returnedMatchCount ?? "Unavailable" : "—"}</td>
      <td><Link id={"rm11-execution-" + item.executionId} href={path({ filters: query,
        ...(pages.cursor === undefined ? {} : { cursor: pages.cursor }), focusExecution: item.executionId }, item.executionId)}>Open detail</Link></td>
    </tr>)}</tbody></table>
    {page.items.length === 0 ? <Empty title="No matching executions">Change the filters or refresh after more reports arrive.</Empty> : null}
    <Pager pages={pages} nextCursor={page.nextCursor} busy={read.busy} label="Executions" /></div>}
  </section>;
}

export function RuleExecutionInspection({ applicationId, rulesetId, environment, base, versions, revision, history, path, executionId }: HistoryProps & { readonly executionId: string }) {
  const read = usePortalRead<RuleExecutionDetail>(queryUrl(base + "/execution", { rulesetId, executionId }), revision);
  const value = ready(read);
  const detail = value?.execution.executionId === executionId && value.execution.applicationId === applicationId
    && value.execution.environment === environment && value.execution.rulesetId === rulesetId ? value : undefined;
  const facts = useMemo(() => {
    try { return detail === undefined ? undefined : inspectExecution(detail); } catch { return undefined; }
  }, [detail]);
  return <section aria-label="Execution detail">
    <Link className="back-link" href={path(history)}>← Back to executions</Link>
    <ReadNotice state={read} />
    {detail === undefined ? null : <><div className="section-heading"><h2>Rule evaluation</h2><ExecutionBadge status={detail.execution.status} /></div>
      <Facts><Fact label="Version">{ruleVersionLabel(detail.execution.version, versions)}</Fact>
        <Fact label="Instance">{detail.execution.instanceId}</Fact><Fact label="Started"><Timestamp value={detail.execution.startedAtUtc} compact /></Fact>
        <Fact label="Finished"><Timestamp value={detail.execution.finishedAtUtc} compact /></Fact></Facts>
      {facts === undefined ? <p className="notice" role="status">The retained execution evidence could not be interpreted.</p> : <InspectionFacts facts={facts} />}
      <TechnicalDetails fields={[{ label: "Execution ID", value: executionId }, { label: "Ruleset version", value: detail.execution.version },
        { label: "Correlation ID", value: facts?.correlationId }, { label: "Registration ID", value: detail.execution.registrationId },
        { label: "Application build", value: detail.execution.applicationBuild },
        { label: "Engine contract version", value: facts?.engineVersion === undefined ? undefined : String(facts.engineVersion) }]} />
    </>}
  </section>;
}

function InspectionFacts({ facts }: { readonly facts: ExecutionInspection }) {
  return <>
    <div className="rm11-execution-panels">
      <section className="card rm11-panel"><h2>Supplied inputs</h2>
        {facts.inputs === undefined ? <p>The start record has not been received.</p> : <div className="table-frame"><table><thead>
          <tr><th>Field</th><th>Type or presence</th><th>Actual value</th></tr></thead><tbody>
          {facts.inputs.map(input => <tr key={input.columnId}><td>{input.label}{input.unknown ? <small>Undeclared input key</small> : null}</td>
            <td>{input.kind ?? input.presence}</td><td><code>{input.value ?? "—"}</code></td></tr>)}
        </tbody></table></div>}
      </section>
      <section className="card rm11-panel"><h2>Observed rule trace</h2>
        {facts.trace === undefined ? <p>No completion record has been received.</p> : facts.trace.length === 0 ? <p>No rule observations were recorded.</p>
          : <div className="rm11-trace">{facts.trace.map(row => <ObservedRule key={row.row.ruleId} trace={row} facts={facts} />)}</div>}
        <p className="muted" role="status">{facts.stop}</p>
        {facts.errorCode === undefined && facts.priorErrorCode === undefined ? null : <TechnicalDetails label="Recorded stop details"
          fields={[{ label: "Terminal error code", value: facts.errorCode }, { label: "Earlier error code", value: facts.priorErrorCode }]} />}
      </section>
    </div>
    <section className="card rm11-panel rm11-returned"><h2>Returned matches</h2>
      {facts.selected === undefined ? <p>No usable result is reported for this execution.</p> : facts.selected.length === 0 ? <p>Completed with no returned rows.</p>
        : <div className="table-frame"><table><thead><tr><th>Position</th><th>Rule</th><th>Weight</th><th>Metadata</th></tr></thead>
          <tbody>{facts.selected.map((row, index) => <tr key={row.ruleId}><td>{index + 1}</td><td>{ruleRowLabel(row)}</td>
            <td>{row.weight}</td><td><code>{row.metadataText ?? "—"}</code></td></tr>)}</tbody></table></div>}
    </section>
  </>;
}
function ObservedRule({ trace, facts }: { readonly trace: InspectedTrace; readonly facts: ExecutionInspection }) {
  return <details><summary><span>{trace.row.ordinal} · {ruleRowLabel(trace.row)}</span><Badge>{trace.state}</Badge>
    <span>{trace.outcome}</span></summary><div className="table-frame"><table><thead><tr><th>Condition</th><th>Supplied value</th>
      <th>Preset</th><th>Observation</th></tr></thead><tbody>{facts.definition.columns.map((column, index) => {
        const input = facts.inputs?.find(item => item.columnId === column.id);
        const cell = trace.row.conditions.find(entry => entry.columnId === column.id);
        return <tr key={column.id}><td>{column.displayName}</td><td><code>{input?.value ?? input?.presence ?? "Unreported"}</code></td>
          <td>{cell === undefined ? "Unreported" : ruleCellLabel(cell)}</td><td>{trace.cells[index] ?? "Unknown"}</td></tr>;
      })}</tbody></table></div>
    {trace.errorCode === undefined ? null : <p>Recorded diagnostic <code>{trace.errorCode}</code></p>}</details>;
}
function ExecutionBadge({ status }: { readonly status: RuleExecutionStatus }) {
  return <Badge tone={status === "Completed" ? "good" : status === "Failed" || status === "Incomplete" ? "warning" : "neutral"}>{status}</Badge>;
}

function utcFilter(value: string | undefined) {
  if (value === undefined) { return undefined; }
  const match = /^([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(?::[0-9]{2})?)(?:\.([0-9]{1,7}))?Z?$/u.exec(value);
  const date = match?.[1];
  if (date === undefined) { return undefined; }
  const canonical = date + (date.length === 16 ? ":00" : "") + "." + (match?.[2] ?? "").padEnd(7, "0") + "Z";
  return isTimestamp(canonical) ? canonical : undefined;
}
