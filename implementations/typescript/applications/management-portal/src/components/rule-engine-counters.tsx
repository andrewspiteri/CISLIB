"use client";

import { useEffect, useRef, useState } from "react";
import type { RuleCounterPage, RuleCounterResetOutcomesPage } from "@cislib/management-client";
import { ruleNote, ruleRowLabel, stateLabels } from "../rule-engine-draft.ts";
import type { RuleDraft } from "../rule-engine-draft.ts";
import type { RuleOperationObservation } from "../rule-engine-operations.ts";
import { RuleOriginalResults } from "./rule-engine-command";
import type { useRuleCommand } from "./rule-engine-command";
import { useProtectedPortal } from "./protected-context";
import { Badge, Empty, Fact, Facts, Pager, ReadNotice, TechnicalDetails, Timestamp, queryUrl, ready, usePages, usePortalRead } from "./shared";

const resetFamily = "management.rule-engine.counter.reset" as const;
type SelectionMode = "one" | "selected" | "all";
interface PendingReset { readonly selection: SelectionMode; readonly ruleIds: readonly string[]; }
interface CounterProps {
  readonly applicationId: string; readonly applicationName: string; readonly rulesetId: string; readonly rulesetName: string;
  readonly environment: string; readonly base: string; readonly deployedDefinition: RuleDraft | undefined; readonly revision: number;
  readonly refresh: () => void; readonly canReset: boolean; readonly operation: ReturnType<typeof useRuleCommand>;
}
export function RuleCounters(props: CounterProps) {
  const { snapshot } = useProtectedPortal();
  const { base, rulesetId, applicationId, environment, revision, deployedDefinition, operation } = props;
  const pages = usePages(JSON.stringify([applicationId, rulesetId, "counters"]), undefined);
  const read = usePortalRead<RuleCounterPage>(queryUrl(base + "/counters", { rulesetId, pageSize: "25", cursor: pages.cursor }), revision);
  const received = ready(read);
  const page = received?.applicationId === applicationId && received.environment === environment && received.rulesetId === rulesetId ? received : undefined;
  const [selected, setSelected] = useState<readonly string[]>([]);
  const [pending, setPending] = useState<PendingReset>();
  const [result, setResult] = useState<RuleOperationObservation>();
  useEffect(() => {
    if (snapshot.blocked || read.reply?.state === "denied" || read.reply?.state === "not-found" || read.reply?.state === "unauthenticated") {
      setSelected([]); setPending(undefined); setResult(undefined);
    }
  }, [snapshot.epoch, snapshot.blocked, read.reply?.state]);
  const outstanding = operation.receipts.some(receipt => receipt.family === resetFamily);
  const writable = props.canReset && page !== undefined && !read.busy && !read.stale && !operation.busy && !outstanding
    && !snapshot.blocked && snapshot.context !== undefined && snapshot.verification === undefined;
  const lookup = result?.family === resetFamily && result.disposition === "committed" && result.counterReset !== undefined ? result : undefined;
  const outcomePages = usePages(JSON.stringify([applicationId, rulesetId, "counter-reset-outcomes", lookup?.key]), undefined);
  const outcomesRead = usePortalRead<RuleCounterResetOutcomesPage>(lookup === undefined ? null : queryUrl(base + "/counter-reset-outcomes/" + encodeURIComponent(lookup.key),
    { rulesetId, pageSize: "25", cursor: outcomePages.cursor }), revision);
  const facts = ready(outcomesRead);
  const outcomes = facts?.applicationId === applicationId && facts.environment === environment && facts.rulesetId === rulesetId
    && facts.operation.key === lookup?.key ? facts : undefined;

  function label(ruleId: string) {
    const current = deployedDefinition?.rows.find(item => item.ruleId === ruleId);
    return current === undefined ? "Retained rule" : ruleRowLabel(current);
  }
  function toggle(ruleId: string, checked: boolean) {
    setSelected(current => checked && current.length < 48 && !current.includes(ruleId) ? [...current, ruleId]
      : checked ? current : current.filter(id => id !== ruleId));
  }
  function receive(value: RuleOperationObservation | undefined) {
    if (value === undefined || value.family !== resetFamily) { return; }
    setResult(value);
    if (value.disposition === "committed" && value.cleanupSucceeded) { setPending(undefined); setSelected([]); props.refresh(); }
  }
  async function reset(reason: string) {
    if (!writable || pending === undefined || !ruleNote(reason)) { return; }
    receive(await operation.start(resetFamily, undefined, { selection: pending.selection, ruleIds: pending.ruleIds, reason }));
  }
  return <section className="rm11-counters" aria-label="Hit counters">
    <div className="section-heading"><div><h2>Hit counters</h2><p>{selected.length} selected · {props.rulesetName} · {environment}</p></div>
      <div className="rm11-actions"><button type="button" disabled={read.busy} onClick={props.refresh}>Refresh counters</button>
        {props.canReset ? <><button type="button" disabled={!writable} onClick={() => setPending({ selection: "all", ruleIds: [] })}>Reset all</button>
          <button type="button" disabled={!writable || selected.length === 0} onClick={() => setPending({ selection: "selected", ruleIds: [...selected] })}>
            Reset selected</button></> : null}
      </div></div>
    <ReadNotice state={read} restart={pages.reset} />
    {page?.availability === "unavailable" ? <p className="notice">Counter counts are unavailable.</p> : null}
    <RuleOriginalResults operation={operation} family={resetFamily} onResult={receive} />
    {page === undefined ? null : <div className="table-frame"><table><thead><tr>
      {props.canReset ? <th>Select</th> : null}<th>Rule</th><th>State</th><th>Hits</th><th>Last reset</th><th>Freshness</th>{props.canReset ? <th>Actions</th> : null}
    </tr></thead><tbody>{page.items.map(item => {
      const definition = deployedDefinition?.rows.find(row => row.ruleId === item.ruleId);
      const selectedNow = selected.includes(item.ruleId);
      return <tr key={item.ruleId}>
        {props.canReset ? <td><input type="checkbox" aria-label={"Select " + label(item.ruleId) + " " + item.ruleId} checked={selectedNow}
          disabled={!writable || !selectedNow && selected.length >= 48} onChange={event => toggle(item.ruleId, event.target.checked)} /></td> : null}
        <td>{label(item.ruleId)}<TechnicalDetails label="Rule details" fields={[{ label: "Rule ID", value: item.ruleId }]} /></td>
        <td><Badge>{definition === undefined ? deployedDefinition === undefined ? "Unavailable" : "Retained" : stateLabels[definition.state]}</Badge></td>
        <td>{item.count ?? "Unavailable"}</td><td><Timestamp value={item.lastResetAtUtc} compact /></td>
        <td>{item.count === undefined ? "Read unavailable" : <><Timestamp value={page.observedAtUtc} compact />{read.stale || page.availability === "stale" ? " · stale" : ""}</>}</td>
        {props.canReset ? <td><button type="button" disabled={!writable}
          aria-label={"Reset " + label(item.ruleId) + " " + item.ruleId} onClick={() => setPending({ selection: "one", ruleIds: [item.ruleId] })}>Reset</button></td> : null}
      </tr>;
    })}</tbody></table>
      {page.items.length === 0 ? <Empty title="No counters reported">No retained rule counters appear on this page.</Empty> : null}
      <Pager pages={pages} nextCursor={page.nextCursor} busy={read.busy} label="Hit counters" />
    </div>}
    <p className="muted">Reset all includes retained rule identities in this ruleset.</p>
    {lookup?.counterReset === undefined ? null : <section className="card rm11-panel" aria-label="Counter reset outcomes">
      <h2>Counter reset outcomes</h2>
      <Facts><Fact label="Reset time"><Timestamp value={lookup.counterReset.resetAtUtc} compact /></Fact>
        <Fact label="Reset">{lookup.counterReset.appliedCount}</Fact><Fact label="Unavailable">{lookup.counterReset.rejectedCount}</Fact></Facts>
      <ReadNotice state={outcomesRead} restart={outcomePages.reset} />
      {outcomes === undefined ? null : <><div className="table-frame"><table><thead><tr><th>Rule</th><th>Outcome</th></tr></thead>
        <tbody>{outcomes.items.map(item => <tr key={item.ruleId}><td>{label(item.ruleId)}
          <TechnicalDetails label="Rule details" fields={[{ label: "Rule ID", value: item.ruleId }]} /></td>
          <td><Badge tone={item.disposition === "reset" ? "good" : "warning"}>{item.disposition === "reset" ? "Reset" : "Not found"}</Badge></td></tr>)}</tbody>
      </table></div><Pager pages={outcomePages} nextCursor={outcomes.nextCursor} busy={outcomesRead.busy} label="Reset outcomes" /></>}
    </section>}
    {pending === undefined ? null : <CounterResetDialog reset={pending} eligible={writable}
      active={!snapshot.blocked && snapshot.verification === undefined} scope={props.rulesetName + " · " + props.applicationName + " · " + environment}
      getLabel={label} count={id => page?.items.find(item => item.ruleId === id)?.count} onCancel={() => setPending(undefined)} onReset={reason => { void reset(reason); }} />}
  </section>;
}
function CounterResetDialog({ active, reset, eligible, scope, getLabel, count, onCancel, onReset }: {
  readonly active: boolean; readonly reset: PendingReset; readonly eligible: boolean; readonly scope: string;
  readonly getLabel: (id: string) => string; readonly count: (id: string) => string | undefined; readonly onCancel: () => void;
  readonly onReset: (reason: string) => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [reason, setReason] = useState("");
  useEffect(() => {
    const current = dialog.current;
    if (current !== null && !current.open && active) { current.showModal(); }
    if (!active && current?.open) { current.close(); }
    return () => { if (current?.open) { current.close(); } };
  }, [active]);
  const button = reset.selection === "one" ? "Reset counter" : reset.selection === "selected" ? "Reset selected" : "Reset all";
  return <dialog ref={dialog} className="configuration-deploy-dialog" aria-labelledby="rm11-counter-reset-title"
    onCancel={event => { event.preventDefault(); onCancel(); }}><h2 id="rm11-counter-reset-title">
      {reset.selection === "all" ? "Reset all counters" : reset.ruleIds.length === 1 ? "Reset 1 counter" : "Reset " + reset.ruleIds.length + " counters"}</h2>
    <p>{scope}</p>
    {reset.selection === "all" ? <p>Every retained rule identity in this ruleset is included.</p>
      : <ul>{reset.ruleIds.map(id => <li key={id}>{getLabel(id)} · {count(id) === undefined ? "Count unavailable" : count(id) + " hits"}</li>)}</ul>}
    <label>Reset reason<input value={reason} maxLength={1024} onChange={event => setReason(event.target.value)} /></label>
    <p>Each counter reports its own reset outcome.</p>
    <div className="rm11-actions"><button type="button" onClick={onCancel}>Cancel</button>
      <button className="rm11-reset-button" type="button" disabled={!eligible || !ruleNote(reason)} onClick={() => onReset(reason)}>{button}</button></div>
  </dialog>;
}
