"use client";

import { useEffect, useState } from "react";
import type { CommandReceipt } from "../command-receipts.ts";
import type { ConfigurationCommandIntent, ConfigurationCommandObservation } from "../configuration-command-results.ts";
import { resolvedConfigurationOriginal } from "../configuration-command-results.ts";
import { configurationOperationCanAdvance, mergeConfigurationOperationFact } from "../configuration-operation-facts.ts";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../portal-context.ts";
import type { PortalReply } from "../portal-model.ts";
import { ProtectedStateOwner } from "../protected-state.ts";
import { useProtectedPortal } from "./protected-context";
import { TechnicalDetails } from "./shared";

/** One existing native command family. Receipts and the caller's explicit Save/Deploy intent remain separate. */
export function useConfigurationOperation() {
  const { owner: portal, snapshot: session } = useProtectedPortal();
  const [owner] = useState(() => new ProtectedStateOwner<ConfigurationCommandObservation>({ context: session.context, receipts: portal.receipts }));
  const [, redraw] = useState(0);
  useEffect(() => portal.register(owner), [portal, owner]);
  const snapshot = owner.view.snapshot;
  const observation = snapshot !== undefined && session.context !== undefined && !session.blocked
    && portalContextKey(snapshot.context) === portalContextKey(session.context) ? snapshot.value : undefined;
  function target(path: string, method: string) { return { requestKey: path + (method === "GET" ? "#lookup" : "#submit") }; }
  async function request(path: string, init: RequestInit, intent: ConfigurationCommandIntent, key: string, recoverySignal?: AbortSignal, retainedFact?: ConfigurationCommandObservation) {
    const epoch = portal.getSnapshot().epoch;
    const previousSnapshot = owner.view.snapshot, currentContext = portal.getSnapshot().context;
    const previous = retainedFact ?? (previousSnapshot !== undefined && currentContext !== undefined && portalContextKey(previousSnapshot.context) === portalContextKey(currentContext)
      && previousSnapshot.value.key === key && previousSnapshot.value.family === intent.family && sameTarget(previousSnapshot.value.target, intent.target) ? previousSnapshot.value : undefined);
    const selected = target(path, init.method ?? "GET");
    owner.selectTarget(selected);
    const result = await owner.request(async ticket => {
      if (ticket.context === undefined) { return { state: "unauthenticated" }; }
      try {
      const headers = new Headers(init.headers);
      headers.set(portalContextHeader, portalContextKey(ticket.context));
      const response = await fetch(path, { ...init, headers, credentials: "same-origin", cache: "no-store", redirect: "error",
        signal: recoverySignal === undefined ? ticket.signal : AbortSignal.any([ticket.signal, recoverySignal]) });
      const reply = await response.json() as PortalReply<ConfigurationCommandObservation>;
      if (reply.state === "unauthenticated" || reply.state === "ready" && reply.value.transport.status === 401) {
        if (portal.getSnapshot().epoch === epoch) { portal.signOut(); }
        return { state: "unauthenticated" };
      }
      if (reply.context !== undefined && !portal.observe(reply.context, epoch, owner).accepted) { return { state: "unavailable" }; }
      if (reply.state !== "ready") {
        if (previous !== undefined) {
          const context = capturePortalContext(reply.context) ?? ticket.context;
          return { state: "ready", context, value: mergeConfigurationOperationFact(previous, { family: intent.family, target: intent.target, key, disposition: "unresolved",
            transport: { kind: "http", status: response.status, cleanupSucceeded: true, mutationUncertain: false } }) };
        }
        return { state: reply.state, ...(reply.context === undefined ? {} : { context: reply.context }) };
      }
      const context = capturePortalContext(reply.context);
      if (context === undefined) { return { state: "unauthenticated" }; }
      if (reply.value.family !== intent.family || reply.value.key !== key || !sameTarget(reply.value.target, intent.target)) {
        if (previous === undefined) { throw new TypeError("The original response does not match this exact Configuration operation."); }
        return { state: "ready", context, value: mergeConfigurationOperationFact(previous, { family: intent.family, target: intent.target, key, disposition: "unresolved",
          transport: { kind: "invalidResponse", cleanupSucceeded: false, mutationUncertain: false, code: "portal.operation.target_mismatch" } }) };
      }
      return { state: "ready", context, value: mergeConfigurationOperationFact(previous, reply.value) };
      } catch (error) {
        if (previous === undefined || ticket.signal.aborted || recoverySignal?.aborted) { throw error; }
        return { state: "ready", context: ticket.context, value: mergeConfigurationOperationFact(previous, { family: intent.family, target: intent.target, key, disposition: "unresolved",
          transport: { kind: "transportFailure", cleanupSucceeded: false, mutationUncertain: false, code: "portal.lookup.unavailable" } }) };
      }
    });
    redraw(value => value + 1);
    const current = portal.getSnapshot(), settled = owner.view.snapshot;
    return result.kind === "applied" && settled !== undefined && current.context !== undefined && !current.blocked
      && portalContextKey(settled.context) === portalContextKey(current.context) && settled.target.requestKey === selected.requestKey
      && settled.value.key === key ? settled.value : undefined;
  }
  function canRecover(receipt: CommandReceipt) {
    const intent = receiptIntent(receipt);
    if (intent === undefined) { return false; }
    const prior = owner.view.snapshot, actual = portal.getSnapshot();
    const previous = prior !== undefined && actual.context !== undefined && !actual.blocked && portalContextKey(prior.context) === portalContextKey(actual.context) ? prior.value : undefined;
    const cleanupRetry = previous?.disposition === "applied" && previous.key === receipt.key && previous.family === intent.family
      && sameTarget(previous.target, intent.target) && !configurationOperationCanAdvance(previous);
    return !actual.blocked && actual.context !== undefined && !portal.receipts.recoveryBusy && owner.view.pendingRequests === 0
      && (!resolvedConfigurationOriginal(previous) || cleanupRetry);
  }
  async function recover(receipt: CommandReceipt, path: string) {
    if (!canRecover(receipt)) { return; }
    const intent = receiptIntent(receipt);
    if (intent === undefined) { return; }
    const prior = owner.view.snapshot, actual = portal.getSnapshot();
    const previous = prior !== undefined && actual.context !== undefined && portalContextKey(prior.context) === portalContextKey(actual.context)
      && prior.value.key === receipt.key && prior.value.family === intent.family && sameTarget(prior.value.target, intent.target) ? prior.value : undefined;
    owner.selectTarget(target(path, "GET"));
    return portal.receipts.recover(receipt, async (_original, signal) => {
      await request(path, { method: "GET" }, intent, receipt.key, signal, previous);
    });
  }
  function dismiss(receipt: CommandReceipt) {
    const value = owner.view.snapshot?.value;
    if (value?.key !== receipt.key || !resolvedConfigurationOriginal(value) || portal.receipts.forgetExact(receipt).kind !== "forgotten") { return false; }
    owner.selectTarget(undefined); redraw(value => value + 1); return true;
  }
  return { owner, observation, request, canRecover, recover, dismiss };
}
function receiptIntent(receipt: CommandReceipt): ConfigurationCommandIntent | undefined {
  switch (receipt.family) {
    case "management.configuration.create": return { family: receipt.family, target: { applicationId: receipt.target.applicationId } };
    case "management.configuration.source.publish": return { family: receipt.family, target: { sourceId: receipt.target.sourceId } };
    case "management.configuration.release.create":
    case "management.configuration.assignment.replace": return { family: receipt.family, target: { applicationId: receipt.target.applicationId, configurationId: receipt.target.configurationId, schemaVersion: receipt.target.schemaVersion } };
    default: return undefined;
  }
}
function sameTarget(left: ConfigurationCommandObservation["target"], right: ConfigurationCommandIntent["target"]): boolean {
  const keys = Object.keys(right);
  return Object.keys(left).length === keys.length && keys.every(key => Reflect.get(left, key) === Reflect.get(right, key));
}
export function configurationOperationClean(value: ConfigurationCommandObservation | undefined): boolean {
  return configurationOperationCanAdvance(value);
}
export function ConfigurationOperationStatus({ value }: { value: ConfigurationCommandObservation }) {
  const label = value.disposition === "applied" ? "Original change committed" : value.disposition === "conflict" ? "Current state changed — review again"
    : value.disposition === "rejected" ? "Original change rejected" : value.disposition === "notCommitted" ? "Original change not committed" : "Original result not confirmed";
  return <div role="status"><strong>{label}</strong><TechnicalDetails fields={[
    { label: "Original key", value: value.key }, { label: "Original completion", value: value.originalCompletion },
    { label: "Result source", value: value.resultSource }, { label: "Decision code", value: value.decision?.code ?? value.rejection?.code ?? value.code },
    { label: "Current request outcome", value: value.transport.kind }, { label: "Current request cleanup", value: value.transport.cleanupSucceeded ? "Confirmed" : "Unconfirmed" },
    { label: "Native cleanup", value: value.attemptCompletion === undefined ? undefined : value.attemptCompletion.cleanupSucceeded ? "Confirmed" : "Unconfirmed" },
    { label: "Recovery observation", value: value.recovery?.state }, { label: "Earlier request cleanup", value: value.recovery === undefined ? undefined : value.recovery.previousTransport.cleanupSucceeded ? "Confirmed" : "Unconfirmed" },
    { label: "Earlier native cleanup", value: value.recovery?.previousAttemptCompletion === undefined ? undefined : value.recovery.previousAttemptCompletion.cleanupSucceeded ? "Confirmed" : "Unconfirmed" }
  ]} /></div>;
}
