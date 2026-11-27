"use client";

import { useEffect, useRef, useState } from "react";
import type { ConfigurationActions, ConfigurationAssignmentMap, ConfigurationCohortChoice, ConfigurationPreview, ConfigurationRelease, Page } from "@cislib/management-client";
import { isUuid } from "@cislib/management-client";
import { beginConfigurationAssignment, buildConfigurationAssignmentReview, chooseConfigurationAssignmentDefault,
  chooseConfigurationAssignmentOverride, configurationAssignmentCommand, configurationAssignmentRows, nextConfigurationAssignmentGeneration,
  removeConfigurationAssignmentOverride, setConfigurationAssignmentIntent, setConfigurationAssignmentReason } from "../configuration-assignment-model.ts";
import type { ConfigurationAssignmentDraft, ConfigurationAssignmentReview } from "../configuration-assignment-model.ts";
import { resolvedConfigurationOriginal } from "../configuration-command-results.ts";
import { configurationReadTarget, recoverConfigurationOriginal } from "../configuration-recovery.ts";
import type { ConfigurationCommandObservation } from "../configuration-command-results.ts";
import type { CommandReceipt } from "../command-receipts.ts";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../portal-context.ts";
import type { PortalReply } from "../portal-model.ts";
import { ProtectedStateOwner } from "../protected-state.ts";
import type { ProtectedTarget } from "../protected-state.ts";
import type { ListContext } from "./application-detail";
import { useProtectedPortal } from "./protected-context";
import { Badge, Fact, Facts, Pager, ReadNotice, Shell, TechnicalDetails, Timestamp, queryUrl, ready, usePages, usePortalRead } from "./shared";

interface ScopedDraft { readonly scope: string; readonly value: ConfigurationAssignmentDraft; }

/** Complete native map decisions only; source content and recipient acceptance are separate boundaries. */
export function ConfigurationAssignment({ applicationId, configurationId, schemaVersion, context, initialReleaseId }: {
  applicationId: string; configurationId: string; schemaVersion: string; context: ListContext; initialReleaseId?: string
}) {
  const { owner: portal, snapshot: session } = useProtectedPortal();
  const [draftOwner] = useState(() => new ProtectedStateOwner<ConfigurationAssignmentMap>({ context: session.context, receipts: portal.receipts }));
  const [reviewOwner] = useState(() => new ProtectedStateOwner<ConfigurationAssignmentReview>({ context: session.context, receipts: portal.receipts }));
  const [commandOwner] = useState(() => new ProtectedStateOwner<ConfigurationCommandObservation>({ context: session.context, receipts: portal.receipts }));
  const [revision, refresh] = useState(0), [, redraw] = useState(0);
  const [storedDraft, setDraft] = useState<ScopedDraft>();
  const [mode, setMode] = useState<"canary" | "promote" | "rollback">("canary");
  const [cohortId, selectCohort] = useState("");
  const [releaseId, setReleaseId] = useState(initialReleaseId ?? "");
  const [requestedRelease, inspectRelease] = useState<string | undefined>(initialReleaseId);
  const [message, setMessage] = useState<string>();
  const [busy, setBusy] = useState(false);
  const pending = useRef(false), alive = useRef(true), serial = useRef(0), heading = useRef<HTMLHeadingElement>(null);
  const appliedRead = useRef<{ reply: unknown; revision: number; url: string } | undefined>(undefined);
  const sessionKey = session.context === undefined ? undefined : portalContextKey(session.context);
  const scope = JSON.stringify([applicationId, configurationId, schemaVersion, sessionKey]);
  const base = `/api/portal/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}`;
  const mapUrl = queryUrl(base + "/assignments", { schemaVersion });
  const read = usePortalRead<ConfigurationAssignmentMap>(mapUrl, revision);
  const actionsRead = usePortalRead<ConfigurationActions>(queryUrl(base + "/binding-actions", { schemaVersion }), revision);
  const releasePages = usePages(scope + "/releases", undefined), cohortPages = usePages(scope + "/cohorts", undefined);
  const releasesRead = usePortalRead<Page<ConfigurationRelease>>(queryUrl(base + "/releases", { schemaVersion, pageSize: "10", cursor: releasePages.cursor }), revision);
  const cohortsRead = usePortalRead<Page<ConfigurationCohortChoice>>(queryUrl(base + "/cohorts", { schemaVersion, pageSize: "10", cursor: cohortPages.cursor }), revision);
  const selectedRead = usePortalRead<ConfigurationRelease>(requestedRelease === undefined ? null : `/api/portal/configuration/releases/${requestedRelease}`, revision);
  const incoming = ready(read), snapshot = draftOwner.view.snapshot;
  const incomingContext = read.reply?.state === "ready" ? capturePortalContext(read.reply.context) : undefined;
  const admissible = read.reply === undefined || read.reply.state === "unavailable" || incoming !== undefined
    && incomingContext !== undefined && portalContextKey(incomingContext) === sessionKey && sameTuple(incoming, applicationId, configurationId, schemaVersion);
  const currentMap = incoming !== undefined && sameTuple(incoming, applicationId, configurationId, schemaVersion) ? incoming : undefined;
  const data = snapshot !== undefined && sessionKey !== undefined && portalContextKey(snapshot.context) === sessionKey && admissible
    && snapshot.target.requestKey === mapUrl && sameTuple(snapshot.value, applicationId, configurationId, schemaVersion)
    && (incoming === undefined || incoming.bindingId === snapshot.value.bindingId && incoming.environment === snapshot.value.environment) ? snapshot.value : undefined;
  const draft = data !== undefined && storedDraft?.scope === scope ? storedDraft.value : undefined;
  const actionValue = ready(actionsRead);
  const action = data !== undefined && actionValue?.resourceId === data.bindingId && !actionsRead.busy && !actionsRead.stale
    ? actionValue.actions.find(item => item.operationId === "replaceConfigurationAssignments") : undefined;
  const originalsState = portal.receipts.load();
  const originals = originalsState.kind === "ready" ? originalsState.receipts.filter(receipt => receipt.family === "management.configuration.assignment.replace"
    && receipt.target.applicationId === applicationId && receipt.target.configurationId === configurationId && receipt.target.schemaVersion === schemaVersion) : [];
  const observationSnapshot = commandOwner.view.snapshot;
  const observation = observationSnapshot !== undefined && sessionKey !== undefined && portalContextKey(observationSnapshot.context) === sessionKey
    && sameOriginal(observationSnapshot.value, applicationId, configurationId, schemaVersion) ? observationSnapshot.value : undefined;
  const reviewSnapshot = reviewOwner.view.snapshot;
  const review = data !== undefined && draft !== undefined && reviewSnapshot !== undefined && sessionKey !== undefined
    && portalContextKey(reviewSnapshot.context) === sessionKey && reviewSnapshot.target.applicationId === applicationId
    && reviewSnapshot.target.definitionId === configurationId && reviewSnapshot.target.schemaVersion === schemaVersion
    && reviewSnapshot.value.baseline.bindingId === data.bindingId && reviewSnapshot.value.baseline.environment === data.environment ? reviewSnapshot.value : undefined;
  const changedMap = draft !== undefined && currentMap !== undefined && draft.baseline.eTag !== currentMap.eTag;
  const editable = data !== undefined && draft !== undefined && draftOwner.view.canWrite && !read.busy && !read.stale && !busy && !session.blocked && originals.length === 0;
  const canWrite = editable && action?.supported === true && action.allowed;
  const releases = ready(releasesRead)?.items.filter(item => data !== undefined && sameRelease(item, data));
  const cohorts = ready(cohortsRead)?.items;
  const selected = ready(selectedRead);
  const exactSelected = selected !== undefined && selected.releaseId === requestedRelease && data !== undefined && sameRelease(selected, data)
    && !selectedRead.busy && !selectedRead.stale ? selected : undefined;
  const releaseLabels = new Map([...(releases ?? []), ...(exactSelected === undefined ? [] : [exactSelected])].map(item => [item.releaseId, item.reason]));
  const back = queryUrl(`/applications/${applicationId}`, { tab: "configuration", ...context });
  const recoveryReady = !read.busy && !actionsRead.busy && !releasesRead.busy && !cohortsRead.busy && !selectedRead.busy
    && appliedRead.current !== undefined && appliedRead.current.reply === read.reply && appliedRead.current.revision === revision && appliedRead.current.url === mapUrl;

  useEffect(() => { const owners = [draftOwner, reviewOwner, commandOwner].map(owner => portal.register(owner)); return () => owners.forEach(dispose => dispose()); }, [portal, draftOwner, reviewOwner, commandOwner]);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => { setDraft(undefined); selectCohort(""); setMode("canary"); setReleaseId(initialReleaseId ?? ""); inspectRelease(initialReleaseId); setMessage(undefined); }, [scope, initialReleaseId]);
  useEffect(() => {
    if (read.busy) { return; }
    if (read.reply === undefined) { appliedRead.current = { reply: read.reply, revision, url: mapUrl }; redraw(count => count + 1); return; }
    if (read.reply.state !== "ready") {
      if (read.reply.state === "unavailable") { draftOwner.transientUnavailable(); }
      else { loseContent(); }
      appliedRead.current = { reply: read.reply, revision, url: mapUrl }; redraw(value => value + 1); return;
    }
    if (read.stale) { draftOwner.transientUnavailable(); appliedRead.current = { reply: read.reply, revision, url: mapUrl }; redraw(value => value + 1); return; }
    const value = read.reply.value, actual = capturePortalContext(read.reply.context);
    if (actual === undefined || !sameTuple(value, applicationId, configurationId, schemaVersion)) { loseContent(); appliedRead.current = { reply: read.reply, revision, url: mapUrl }; redraw(count => count + 1); return; }
    const previous = draftOwner.view.snapshot?.value;
    if (previous !== undefined && (previous.bindingId !== value.bindingId || previous.environment !== value.environment)) { loseContent(); }
    draftOwner.setContext(actual); draftOwner.selectTarget(configurationReadTarget({ applicationId, configurationId, schemaVersion, view: "assignment" }));
    let active = true;
    void draftOwner.request(async () => ({ state: "ready", context: actual, value })).then(() => {
      if (!active) { return; }
      setDraft(existing => existing?.scope === scope ? existing : { scope, value: beginConfigurationAssignment(value) });
      appliedRead.current = { reply: read.reply, revision, url: mapUrl }; redraw(count => count + 1);
    });
    return () => { active = false; };
  }, [read.reply, read.busy, read.stale, scope, mapUrl, applicationId, configurationId, schemaVersion, draftOwner]);
  useEffect(() => { if (review !== undefined) { heading.current?.focus(); } }, [review]);

  function loseContent() {
    draftOwner.permissionLost(); reviewOwner.selectTarget(undefined); setDraft(undefined); inspectRelease(undefined); setReleaseId(""); selectCohort("");
  }
  function invalidateReview() { reviewOwner.selectTarget(undefined); setMessage(undefined); }
  function change(next: ConfigurationAssignmentDraft) {
    if (!editable) { return; }
    setDraft({ scope, value: next }); draftOwner.setIntentDirty(true); invalidateReview(); redraw(value => value + 1);
  }
  function chooseDefault(release: ConfigurationRelease, fresh: boolean) {
    if (!fresh || !canWrite || draft === undefined || mode === "canary" && draft.baseline.defaultReleaseId !== undefined) { return; }
    try { change(chooseConfigurationAssignmentDefault(draft, release)); } catch { setMessage("This release does not match the exact assignment binding."); }
  }
  function singleCanaryTarget(cohort: string): boolean {
    return draft !== undefined && configurationAssignmentRows(draft).every(row => row.kind !== "cohort" || row.change === "retained" || row.cohortId === cohort);
  }
  function chooseOverride(release: ConfigurationRelease, fresh: boolean) {
    const cohort = cohorts?.find(item => item.cohortId === cohortId);
    if (!fresh || !canWrite || draft === undefined || cohort === undefined || cohortsRead.busy || cohortsRead.stale) { return; }
    if (mode === "canary" && !singleCanaryTarget(cohort.cohortId)) { setMessage("Canary changes one explicitly selected cohort. Discard the earlier target change before choosing another cohort."); return; }
    try { change(chooseConfigurationAssignmentOverride(draft, cohort, release)); } catch { setMessage("Choose a current trusted cohort and exact retained release; at most sixteen pins are supported."); }
  }
  function removeOverride(cohort: string) {
    if (!canWrite || draft === undefined || mode === "canary" && (cohort !== cohortId || !singleCanaryTarget(cohort))) { return; }
    change(removeConfigurationAssignmentOverride(draft, cohort));
  }
  function reset() {
    if (currentMap === undefined || !sameTuple(currentMap, applicationId, configurationId, schemaVersion) || busy || originals.length > 0) { return; }
    if (draftOwner.view.navigationBlocked && !window.confirm("Discard these intended map changes and use the current complete map?")) { return; }
    draftOwner.discardDraft(); setDraft({ scope, value: beginConfigurationAssignment(currentMap) }); setMode("canary"); invalidateReview(); redraw(value => value + 1);
  }
  function leave() {
    if (draftOwner.view.navigationBlocked && !window.confirm("Discard these intended Configuration assignments and return to the list?")) { return; }
    draftOwner.discardDraft(); window.location.assign(back);
  }
  async function call<T>(owner: ProtectedStateOwner<T>, path: string, init: RequestInit, target: ProtectedTarget, recoverySignal?: AbortSignal) {
    const epoch = portal.getSnapshot().epoch;
    owner.selectTarget(target);
    return owner.request(async ticket => {
      const headers = new Headers(init.headers);
      if (ticket.context !== undefined) { headers.set(portalContextHeader, portalContextKey(ticket.context)); }
      const response = await fetch(path, { ...init, headers, credentials: "same-origin", cache: "no-store", redirect: "error",
        signal: recoverySignal === undefined ? ticket.signal : AbortSignal.any([ticket.signal, recoverySignal]) });
      const body = await response.json() as PortalReply<T>;
      if (body.state === "unauthenticated") { if (portal.getSnapshot().epoch === epoch) { portal.signOut(); } return { state: "unauthenticated" }; }
      if (body.context !== undefined && !portal.observe(body.context, epoch, owner).accepted) { return { state: "unavailable" }; }
      if (body.state === "ready") {
        const actual = capturePortalContext(body.context);
        if (actual === undefined) { return { state: "unauthenticated" }; }
        return { state: "ready", context: actual, value: body.value };
      }
      if (body.state === "denied" || body.state === "not-found") { loseContent(); }
      return { state: body.state, ...(body.context === undefined ? {} : { context: body.context }) };
    });
  }
  async function inspect() {
    if (pending.current || !canWrite || draft === undefined || data === undefined || changedMap) { return; }
    let command;
    try { command = configurationAssignmentCommand(draft); } catch { setMessage("Select an explicit default, complete every pin decision and provide a reason before review."); return; }
    pending.current = true; setBusy(true); setMessage(undefined);
    const captured = draft, epoch = portal.getSnapshot().epoch;
    reviewOwner.selectTarget(mapTarget(mapUrl + "#review-" + (++serial.current), data));
    try {
      await reviewOwner.request(async ticket => {
        if (ticket.context === undefined) { return { state: "unauthenticated" }; }
        const response = await fetch("/api/portal/configuration/preview?pageSize=20", { method: "POST", credentials: "same-origin", cache: "no-store", redirect: "error", signal: ticket.signal,
          headers: { "Content-Type": "application/json", [portalContextHeader]: portalContextKey(ticket.context) },
          body: JSON.stringify({ commandVersion: 1, applicationId, configurationId, schemaVersion, assignment: command }) });
        const body = await response.json() as PortalReply<ConfigurationPreview>;
        if (body.state === "unauthenticated") { if (portal.getSnapshot().epoch === epoch) { portal.signOut(); } return { state: "unauthenticated" }; }
        if (body.context !== undefined && !portal.observe(body.context, epoch, reviewOwner).accepted) { return { state: "unavailable" }; }
        const actual = capturePortalContext(body.context);
        if (body.state !== "ready" || actual === undefined) {
          if (body.state === "denied" || body.state === "not-found") { loseContent(); }
          return { state: body.state === "ready" ? "unavailable" : body.state, ...(actual === undefined ? {} : { context: actual }) };
        }
        return { state: "ready", context: actual, value: buildConfigurationAssignmentReview(captured, body.value) };
      });
    } catch { setMessage("The native review does not establish this complete map. Refresh current state and deliberately reconcile any changed precondition."); }
    finally { pending.current = false; if (alive.current) { setBusy(false); redraw(value => value + 1); } }
  }
  async function submit() {
    portal.checkExpiry();
    const actualSession = portal.getSnapshot().context;
    if (pending.current || !canWrite || review === undefined || draft === undefined || changedMap || actualSession === undefined
      || portalContextKey(actualSession) !== sessionKey || !draftOwner.view.canWrite) { return; }
    let currentCommand;
    try { currentCommand = configurationAssignmentCommand(draft); } catch { return; }
    if (JSON.stringify(currentCommand) !== JSON.stringify(review.command)) { return; }
    const prepared = portal.receipts.prepare({ family: "management.configuration.assignment.replace", target: { applicationId, configurationId, schemaVersion } });
    if (prepared.kind !== "prepared") { setMessage("The original reference could not be retained. Nothing was submitted."); return; }
    pending.current = true; setBusy(true); setMessage(undefined);
    try {
      await call(commandOwner, mapUrl, { method: "PUT", headers: { "Content-Type": "application/json", "Idempotency-Key": prepared.receipt.key, "If-Match": review.baseline.eTag },
        body: JSON.stringify(review.command) }, { requestKey: mapUrl + "#submit", applicationId, definitionId: configurationId, schemaVersion });
      const seen = commandOwner.view.snapshot?.value;
      if (seen?.transport.status === 403 || seen?.transport.status === 404) { loseContent(); }
      else { draftOwner.transientUnavailable(); }
    } catch { draftOwner.transientUnavailable(); setMessage("The original outcome is not confirmed. Recover the same reference; no replacement will be submitted automatically."); }
    finally { pending.current = false; if (alive.current) { setBusy(false); redraw(value => value + 1); } }
  }
  async function recover(receipt: CommandReceipt) {
    const live = commandOwner.view.snapshot?.value;
    if (pending.current || !recoveryReady || portal.receipts.recoveryBusy || resolvedConfigurationOriginal(live)) { return; }
    const path = queryUrl(base + `/assignment-operations/${encodeURIComponent(receipt.key)}`, { schemaVersion });
    const target: ProtectedTarget = { requestKey: path + "#lookup", applicationId, definitionId: configurationId, schemaVersion };
    pending.current = true; setBusy(true); setMessage(undefined);
    try {
      await recoverConfigurationOriginal(draftOwner, commandOwner, portal.receipts, receipt, { applicationId, configurationId, schemaVersion, view: "assignment" }, recoveryReady,
        async (lookupPath, _original, signal) => { await call(commandOwner, lookupPath, { method: "GET" }, target, signal); });
      const seen = commandOwner.view.snapshot?.value;
      if (seen?.transport.status === 403 || seen?.transport.status === 404) { loseContent(); }
    } catch { setMessage("The original assignment is unresolved. Keep this reference and recover later; no replacement was submitted."); }
    finally { pending.current = false; if (alive.current) { setBusy(false); redraw(value => value + 1); } }
  }
  function dismiss(receipt: CommandReceipt) {
    if (busy || observation?.key !== receipt.key || !resolvedConfigurationOriginal(observation) || portal.receipts.forgetExact(receipt).kind !== "forgotten") { return; }
    commandOwner.selectTarget(undefined); invalidateReview();
    if (observation.disposition === "applied") { draftOwner.discardDraft(); setDraft(undefined); }
    refresh(value => value + 1); redraw(value => value + 1);
  }
  function useMode(next: "canary" | "promote" | "rollback") {
    if (!editable || draft === undefined) { return; }
    if (next === "canary" && draft.baseline.defaultReleaseId !== undefined && draft.defaultReleaseId !== draft.baseline.defaultReleaseId) {
      setMessage("Canary retains the existing default. Deliberately discard the intended default change before switching to Canary."); return;
    }
    if (next === "canary" && configurationAssignmentRows(draft).filter(row => row.kind === "cohort" && row.change !== "retained").length > 1) {
      setMessage("Canary changes one cohort. Deliberately discard the multiple intended cohort changes before switching to Canary."); return;
    }
    setMode(next); change(setConfigurationAssignmentIntent(draft, next === "canary" ? "replace" : next));
  }
  function releaseButtons(release: ConfigurationRelease, fresh: boolean) {
    return <div className="metadata-actions"><button disabled={!fresh || !canWrite || mode === "canary" && draft?.baseline.defaultReleaseId !== undefined}
      onClick={() => chooseDefault(release, fresh)}>Use for application default</button><button disabled={!fresh || !canWrite || cohortId === "" || cohortsRead.busy || cohortsRead.stale}
        onClick={() => chooseOverride(release, fresh)}>Use for selected cohort</button></div>;
  }
  const rows = draft === undefined ? [] : configurationAssignmentRows(draft);

  return <Shell signedIn={!session.blocked} footer="Configuration assignments"><button className="item-link back-link" onClick={leave}>← Back to Configuration list</button>
    <a className="item-link" href={queryUrl(`/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}/adoption`, { schemaVersion, ...context })}>View Instance status</a>
    <div className="page-heading"><div><div className="eyebrow">Configuration / {data?.environment ?? "Exact binding"}</div><h1>Assignment map</h1>
      <p>{configurationId} / schema {schemaVersion}. Publication, release, assignment and local adoption are separate facts.</p></div>
      <button disabled={busy || read.busy} onClick={() => { invalidateReview(); refresh(value => value + 1); }}>Refresh current state</button></div>
    <ReadNotice state={read} />{message === undefined ? null : <p className="notice" role="alert">{message}</p>}
    {data === undefined || draft === undefined ? null : <>
      <Facts><Fact label="Current assignment generation">{currentMap?.generation ?? data.generation ?? "Not assigned"}</Fact><Fact label="Next committed generation">{nextConfigurationAssignmentGeneration(currentMap ?? data) ?? "Exhausted"}</Fact></Facts>
      <TechnicalDetails fields={[{ label: "Application ID", value: applicationId }, { label: "Binding ID", value: data.bindingId }, { label: "Frozen map precondition", value: draft.baseline.eTag }, { label: "Current map precondition", value: currentMap?.eTag }]} />
      <ReadNotice state={actionsRead} />{action?.supported && action.allowed ? null : <p className="notice">Current assignment write and original-result permissions are unavailable. Existing map metadata can still be inspected.</p>}
      <nav aria-label="Assignment intent" className="detail-tabs">{(["canary", "promote", "rollback"] as const).map(item => <button key={item} aria-pressed={mode === item} disabled={!editable} onClick={() => useMode(item)}>{item === "canary" ? "Canary" : item === "promote" ? "Promote" : "Rollback"}</button>)}</nav>
      <p>{mode === "canary" ? "Canary retains an existing default and every unrelated override. Select one trusted cohort to add or change its pin. An unassigned map needs an explicit default."
        : mode === "promote" ? "Choose the new default and explicitly remove or retain each existing pin. Removing a pin makes that cohort inherit the default."
          : "Choose an explicitly retained earlier release. Rollback advances generation; it never decrements it or invents a release version."}</p>
      {changedMap ? <section className="notice" role="alert"><h2>Current assignment changed</h2><p>The intended map retains its original precondition. No automatic rebase or submission is allowed.</p>
        <MapTable rows={configurationAssignmentRows(beginConfigurationAssignment(currentMap!))} title="Fresh current map" />
        <button disabled={busy || originals.length > 0} onClick={reset}>Use current map and discard intended changes</button></section> : null}
      <section className="card"><h2>Complete intended map</h2><MapTable rows={rows} labels={releaseLabels} title="Current and proposed entries" remove={removeOverride}
        canRemove={cohort => mode !== "canary" || cohort === cohortId && singleCanaryTarget(cohort)} disabled={!canWrite} />
        <p>Every current pin is copied before editing. A protected or unloaded release is retained by its exact identity unless you explicitly remove or replace it.</p>
        <label htmlFor="assignment-reason">Assignment reason</label><input id="assignment-reason" value={draft.reason} disabled={!canWrite} maxLength={1024}
          onChange={event => { try { change(setConfigurationAssignmentReason(draft, event.target.value)); } catch { setMessage("The reason must remain within the native 1024-byte limit."); } }} />
      </section>
      <section className="card"><h2>Trusted cohort choices</h2><ReadNotice state={cohortsRead} restart={() => cohortPages.reset()} />
        <label htmlFor="assignment-cohort">Cohort for an override</label><select id="assignment-cohort" value={cohortId} disabled={!canWrite || cohortsRead.busy || cohortsRead.stale === true}
          onChange={event => selectCohort(event.target.value)}><option value="">Choose an authorized cohort on this page</option>{cohorts?.map(item => <option key={item.cohortId} value={item.cohortId}>{item.displayName}</option>)}</select>
        {cohorts === undefined ? null : <Pager pages={cohortPages} nextCursor={ready(cohortsRead)?.nextCursor} busy={cohortsRead.busy} changed={() => selectCohort("")} label="cohort choices" />}
      </section>
      <section className="card"><h2>Retained immutable releases</h2><p>Dates, reasons and actors describe actual retained records. UUID order and assignment generation are not release versions.</p>
        <ReadNotice state={releasesRead} restart={() => releasePages.reset()} />
        {releases === undefined ? null : <><div className="table-frame"><table><thead><tr><th>Created</th><th>Reason and actor</th><th>Explicit selection</th></tr></thead><tbody>{releases.map(item => <tr key={item.releaseId}>
          <td><Timestamp value={item.createdAtUtc} /></td><td><strong>{item.reason}</strong><p>{item.actor}</p><TechnicalDetails fields={[{ label: "Release ID", value: item.releaseId }]} /></td><td>{releaseButtons(item, !releasesRead.busy && releasesRead.stale !== true)}</td></tr>)}</tbody></table></div>
          <Pager pages={releasePages} nextCursor={ready(releasesRead)?.nextCursor} busy={releasesRead.busy} label="retained releases" /></>}
        <label htmlFor="assignment-release-id">Exact retained release ID</label><div className="metadata-actions"><input id="assignment-release-id" value={releaseId} maxLength={36} onChange={event => setReleaseId(event.target.value)} />
          <button disabled={busy || !isUuid(releaseId)} onClick={() => inspectRelease(releaseId)}>Inspect exact release</button></div>
        {requestedRelease === undefined ? null : <><ReadNotice state={selectedRead} />{exactSelected === undefined ? null : <section aria-label="Selected exact release"><h3>{exactSelected.reason}</h3>
          <Timestamp value={exactSelected.createdAtUtc} /><p>{exactSelected.actor}</p><TechnicalDetails fields={[{ label: "Release ID", value: exactSelected.releaseId }]} />{releaseButtons(exactSelected, !selectedRead.busy && selectedRead.stale !== true)}</section>}</>}
      </section>
      <div className="metadata-actions"><button className="primary" disabled={!canWrite || changedMap} onClick={() => { void inspect(); }}>Review complete assignment</button>
        <button disabled={!editable} onClick={reset}>Discard intended map changes</button></div>
      {review === undefined ? null : <section className="card assignment-review"><h2 ref={heading} tabIndex={-1}>Review assignment map</h2>
        <Badge>{review.command.intent}</Badge><MapTable rows={review.rows} labels={releaseLabels} title="Every affected map entry" />
        <p>{review.command.reason}</p><p>{review.preview.redacted ? "Effective source values are protected. This is a structurally eligible metadata map review." : "Effective impact is available for this bounded preview."} Consumer acceptance, restart and deployment remain unverified.</p>
        <p>{review.preview.recipients.items.length} recipient observations on this page{review.preview.recipients.nextCursor === undefined ? "." : "; further pages are not included in this observation."}</p>
        <TechnicalDetails fields={[{ label: "Original map precondition", value: review.baseline.eTag }, { label: "Selected default release ID", value: review.command.defaultReleaseId }]} />
        <div className="metadata-actions"><button className="primary" disabled={!canWrite || changedMap || busy} onClick={() => { void submit(); }}>Confirm assignment</button><button disabled={busy} onClick={invalidateReview}>Back to map</button></div>
      </section>}
    </>}
    {originals.length === 0 ? null : <section className="card"><h2>Original assignment operations</h2><p>Recovery reads the same original key directly. It does not need an application/map prefetch and never reconstructs or resubmits a map.</p>
      {originals.map(receipt => <section key={receipt.key} aria-label="Assignment original"><TechnicalDetails fields={[{ label: "Original assignment key", value: receipt.key }]} />
        {observation?.key !== receipt.key ? <p>Original outcome is not established in this view.</p> : <AssignmentOutcome value={observation} />}
        <div className="metadata-actions"><button disabled={busy || session.blocked || !recoveryReady || resolvedConfigurationOriginal(observation)} onClick={() => { void recover(receipt); }}>Recover original assignment</button>
          {observation?.key !== receipt.key || !resolvedConfigurationOriginal(observation) ? null : <button disabled={busy} onClick={() => dismiss(receipt)}>Dismiss resolved assignment and refresh</button>}</div>
        {observation?.key !== receipt.key || observation.disposition !== "applied" ? null : <a className="item-link" href={queryUrl(`/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}/adoption`, { schemaVersion, ...context })}>Inspect committed assignment adoption</a>}
      </section>)}
    </section>}
    {portal.receiptNotice === undefined ? null : <p className="notice">Earlier original references could not be restored ({portal.receiptNotice}); this does not establish an earlier assignment failed.</p>}
    {busy ? <p role="status" className="notice">Waiting for the current assignment request to settle…</p> : null}
  </Shell>;
}

function sameTuple(value: ConfigurationAssignmentMap, app: string, configuration: string, schema: string): boolean { return value.applicationId === app && value.configurationId === configuration && value.schemaVersion === schema; }
function sameRelease(value: ConfigurationRelease, map: ConfigurationAssignmentMap): boolean { return value.applicationId === map.applicationId && value.configurationId === map.configurationId && value.schemaVersion === map.schemaVersion && value.environment === map.environment; }
function mapTarget(requestKey: string, map: ConfigurationAssignmentMap): ProtectedTarget { return { requestKey, applicationId: map.applicationId, environment: map.environment, definitionId: map.configurationId, schemaVersion: map.schemaVersion }; }
function sameOriginal(value: ConfigurationCommandObservation, app: string, configuration: string, schema: string): boolean {
  return value.family === "management.configuration.assignment.replace" && "applicationId" in value.target && "configurationId" in value.target && "schemaVersion" in value.target
    && value.target.applicationId === app && value.target.configurationId === configuration && value.target.schemaVersion === schema;
}
function MapTable({ rows, labels, title, remove, canRemove, disabled = false }: { rows: ReturnType<typeof configurationAssignmentRows>; labels?: ReadonlyMap<string, string>; title: string; remove?(cohort: string): void; canRemove?(cohort: string): boolean; disabled?: boolean }) {
  return <div className="table-frame"><table aria-label={title}><thead><tr><th>Target</th><th>Current release</th><th>Proposed release</th><th>Decision</th></tr></thead><tbody>{rows.map(row => <tr key={row.kind === "default" ? "default" : row.cohortId}>
    <th scope="row">{row.kind === "default" ? "Application default" : row.cohortId}</th><td><ReleaseIdentity value={row.beforeReleaseId} label={row.beforeReleaseId === undefined ? undefined : labels?.get(row.beforeReleaseId)} absent="Not assigned" /></td>
    <td><ReleaseIdentity value={row.afterReleaseId} label={row.afterReleaseId === undefined ? undefined : labels?.get(row.afterReleaseId)} absent={row.kind === "cohort" ? "Inherit application default" : "Not selected"} /></td><td>{row.change}
      {remove === undefined || row.kind !== "cohort" || row.afterReleaseId === undefined ? null : <button className="item-link" disabled={disabled || canRemove?.(row.cohortId!) === false} onClick={() => remove(row.cohortId!)}>Remove {row.cohortId} override</button>}</td>
  </tr>)}</tbody></table></div>;
}
function ReleaseIdentity({ value, label, absent }: { value?: string | undefined; label?: string | undefined; absent: string }) {
  return value === undefined ? <span>{absent}</span> : <><span>{label ?? "Retained release — details not loaded"}</span><TechnicalDetails fields={[{ label: "Release ID", value }]} /></>;
}
function AssignmentOutcome({ value }: { value: ConfigurationCommandObservation }) {
  return <div role="status"><h3>{value.disposition === "applied" ? "Assignment committed" : value.disposition === "conflict" ? "Original assignment precondition conflict"
    : value.disposition === "rejected" ? "Original assignment rejected" : value.disposition === "notCommitted" ? "Original assignment not committed" : "Original assignment unresolved"}</h3>
    <p>Commitment is separate from recipient acceptance or restart. No second assignment is queued or inferred.</p>
    {value.disposition === "applied" && value.decision?.assignment !== undefined ? <Facts><Fact label="Committed generation">{value.decision.assignment.generation}</Fact><Fact label="Cohort pins">{value.decision.assignment.overrides.length}</Fact></Facts> : null}
    <TechnicalDetails fields={[{ label: "Original completion", value: value.originalCompletion }, { label: "Result source", value: value.resultSource }, { label: "Decision code", value: value.rejection?.code ?? value.decision?.code ?? value.code },
      { label: "Current request outcome", value: value.transport.kind }, { label: "Current cleanup succeeded", value: value.transport.cleanupSucceeded ? "Yes" : "No" }]} />
  </div>;
}
