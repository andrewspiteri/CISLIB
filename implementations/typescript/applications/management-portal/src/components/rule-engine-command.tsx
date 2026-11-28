"use client";

import { useEffect, useRef, useState } from "react";
import type { CommandReceipt } from "../command-receipts.ts";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../portal-context.ts";
import type { PortalReply } from "../portal-model.ts";
import { ProtectedStateOwner } from "../protected-state.ts";
import type { RuleOperationFamily, RuleOperationObservation, RuleOperationTarget } from "../rule-engine-operations.ts";
import { queryUrl, TechnicalDetails } from "./shared";
import { useProtectedPortal } from "./protected-context";

export function useRuleCommand(target: RuleOperationTarget, base: string, environment: string | undefined) {
  const { owner: portal, snapshot: session } = useProtectedPortal();
  const [owner] = useState(() => new ProtectedStateOwner<RuleOperationObservation>({ context: session.context, receipts: portal.receipts }));
  const [busy, setBusy] = useState(false), [message, setMessage] = useState<string>();
  const pending = useRef(false);
  const [, redraw] = useState(0);
  useEffect(() => portal.register(owner), [portal, owner]);
  const stored = portal.receipts.load();
  const receipts = stored.kind === "ready" ? stored.receipts.filter(receipt =>
    isRuleReceipt(receipt, target)) : [];
  const snapshot = owner.view.snapshot;
  const observation = snapshot === undefined || session.blocked || session.context === undefined
    || portalContextKey(snapshot.context) !== portalContextKey(session.context)
    || snapshot.value.target.applicationId !== target.applicationId || snapshot.value.target.rulesetId !== target.rulesetId ? undefined : snapshot.value;

  function select(path: string, key: string) {
    owner.selectTarget({ requestKey: path + "#" + key, applicationId: target.applicationId, definitionId: target.rulesetId,
      ...(environment === undefined ? {} : { environment }) });
  }
  async function exchange(receipt: CommandReceipt, path: string, options: RequestInit, recoverySignal?: AbortSignal) {
    const before = portal.getSnapshot();
    if (before.context === undefined || before.blocked || !isRuleReceipt(receipt, target)) { return undefined; }
    select(path, receipt.key);
    await owner.request(async ticket => {
      if (ticket.context === undefined) { return { state: "unauthenticated" }; }
      const headers = new Headers(options.headers);
      headers.set(portalContextHeader, portalContextKey(ticket.context));
      const response = await fetch(path, { ...options, headers, credentials: "same-origin", cache: "no-store", redirect: "error",
        signal: recoverySignal === undefined ? ticket.signal : AbortSignal.any([ticket.signal, recoverySignal]) });
      const reply = await response.json() as PortalReply<RuleOperationObservation>;
      if (reply.state === "unauthenticated") {
        if (portal.getSnapshot().epoch === before.epoch) { portal.signOut(); }
        return { state: "unauthenticated" };
      }
      const context = capturePortalContext(reply.context);
      if (context !== undefined && !portal.observe(context, before.epoch, owner).accepted) { return { state: "unavailable" }; }
      if (reply.state !== "ready") {
        if (reply.state === "denied" || reply.state === "not-found") { setMessage(reply.message); }
        return { state: reply.state, ...(context === undefined ? {} : { context }) };
      }
      const value = reply.value;
      if (context === undefined || value.family !== receipt.family || value.key !== receipt.key
        || value.target.applicationId !== target.applicationId || value.target.rulesetId !== target.rulesetId) {
        return { state: "unavailable" };
      }
      return { state: "ready", context, value };
    });
    const result = owner.view.snapshot;
    const actual = portal.getSnapshot();
    return result !== undefined && result.value.key === receipt.key && !actual.blocked && actual.context !== undefined
      && portalContextKey(actual.context) === portalContextKey(result.context) ? result.value : undefined;
  }

  async function start(family: RuleOperationFamily, ifMatch: string | undefined, body: object) {
    portal.checkExpiry();
    const current = portal.getSnapshot();
    if (pending.current || current.blocked || current.context === undefined || current.verification !== undefined) { return undefined; }
    const intent = portal.receipts.prepare({ family, target });
    if (intent.kind !== "prepared") { setMessage("An original operation key could not be retained. Nothing was submitted."); return undefined; }
    pending.current = true; setBusy(true); setMessage(undefined);
    try {
      const route = family === "management.rule-engine.version.save" ? "/versions"
        : family === "management.rule-engine.deployment.request" ? "/deployments" : "/counter-resets";
      const path = queryUrl(base + route, { rulesetId: target.rulesetId });
      const settled = await exchange(intent.receipt, path, { method: "POST", body: JSON.stringify(body),
        headers: { "content-type": "application/json", "idempotency-key": intent.receipt.key, ...(ifMatch === undefined ? {} : { "if-match": ifMatch }) } });
      if (family === "management.rule-engine.version.save" && settled?.disposition === "committed" && settled.cleanupSucceeded) {
        portal.receipts.forgetExact(intent.receipt);
      }
      return settled;
    } catch { setMessage("The original result could not be confirmed. Check this operation before submitting again."); return undefined; }
    finally { pending.current = false; setBusy(false); }
  }
  async function recover(receipt: CommandReceipt) {
    if (pending.current || !isRuleReceipt(receipt, target) || session.blocked || session.verification !== undefined) { return undefined; }
    pending.current = true; setBusy(true); setMessage(undefined);
    try {
      const route = receipt.family === "management.rule-engine.version.save" ? "/version-operations/"
        : receipt.family === "management.rule-engine.deployment.request" ? "/deployment-operations/" : "/counter-reset-operations/";
      const path = queryUrl(base + route + encodeURIComponent(receipt.key), { rulesetId: target.rulesetId });
      // Selecting a new protected target cancels the previous recovery. Select
      // before opening this read so it cannot cancel its own original lookup.
      select(path, receipt.key);
      const recovered = await portal.receipts.recover(receipt, (_prior, signal) => exchange(receipt, path, { method: "GET" }, signal));
      const settled = recovered.kind === "settled" ? recovered.value : undefined;
      if (receipt.family === "management.rule-engine.version.save" && settled?.disposition === "committed" && settled.cleanupSucceeded) {
        portal.receipts.forgetExact(receipt);
      }
      return settled;
    } catch { setMessage("The original result could not be read. The original key is still retained."); return undefined; }
    finally { pending.current = false; setBusy(false); }
  }
  function forget(receipt: CommandReceipt) {
    const value = owner.view.snapshot?.value;
    if (value?.key !== receipt.key || value.disposition === "unresolved" || !value.cleanupSucceeded) { return; }
    portal.receipts.forgetExact(receipt); redraw(value => value + 1);
  }
  return { busy, start, recover, receipts, observation, forget, message };
}
function isRuleReceipt(receipt: CommandReceipt, target: RuleOperationTarget) {
  return (receipt.family === "management.rule-engine.version.save" || receipt.family === "management.rule-engine.deployment.request"
    || receipt.family === "management.rule-engine.counter.reset")
    && receipt.target.applicationId === target.applicationId && receipt.target.rulesetId === target.rulesetId;
}
export function RuleOriginalResults({ operation, family, onResult }: {
  readonly operation: ReturnType<typeof useRuleCommand>; readonly onResult: (result: RuleOperationObservation | undefined) => void;
  readonly family?: RuleOperationFamily;
}) {
  const value = operation.observation;
  return <div className="rm11-originals">
    {operation.message === undefined ? null : <p className="notice" role="status">{operation.message}</p>}
    {operation.receipts.filter(receipt => family === undefined ? receipt.family !== "management.rule-engine.counter.reset" : receipt.family === family).map(receipt => {
      const result = value?.key === receipt.key ? value : undefined;
      const label = receipt.family === "management.rule-engine.version.save" ? "Save"
        : receipt.family === "management.rule-engine.deployment.request" ? "Deployment" : "Counter reset";
      const settled = result !== undefined && result.disposition !== "unresolved" && result.cleanupSucceeded;
      return <div className="notice" key={receipt.key}><strong>{label + " · " + (result === undefined ? "Original result not confirmed" : resultLabel(result))}</strong>
        {result?.code === undefined ? null : <TechnicalDetails fields={[{ label: "Outcome code", value: result.code }]} />}
        <div className="rm11-actions">
          <button type="button" disabled={operation.busy} onClick={() => { void operation.recover(receipt).then(onResult); }}>Check result</button>
          {settled ? <button type="button" disabled={operation.busy} onClick={() => operation.forget(receipt)}>Dismiss original result</button> : null}
        </div>
      </div>;
    })}
  </div>;
}
function resultLabel(result: RuleOperationObservation) {
  if (result.disposition === "committed" && !result.cleanupSucceeded) { return "Original committed · cleanup not confirmed"; }
  switch (result.disposition) {
    case "committed": return "Original committed";
    case "rejected": return "Original rejected";
    case "conflict": return "Current revision changed";
    case "notCommitted": return "Original not committed";
    default: return "Original result not confirmed";
  }
}
