"use client";

import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import type { CommandReceipt } from "../command-receipts.ts";
import type { MetadataCommandObservation } from "../command-results.ts";
import { metadataConfirmationAction, metadataDraftText, metadataTargetMatches, readMetadataDraft, resolvedMetadataOriginal, reviewMetadata } from "../metadata-draft.ts";
import type { MetadataEditorSnapshot, MetadataReview } from "../metadata-draft.ts";
import { recoverMetadataOriginal } from "../metadata-recovery.ts";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../portal-context.ts";
import { ProtectedStateOwner } from "../protected-state.ts";
import type { PortalReply } from "../portal-model.ts";
import { applicationRoute } from "../portal-routes.ts";
import type { ListContext } from "./application-detail";
import { useProtectedPortal } from "./protected-context";
import { Badge, Fact, Facts, ReadNotice, Shell, TechnicalDetails, usePortalRead } from "./shared";

/** A finite metadata flow; commands are submitted once and original keys are only looked up afterwards. */
export function MetadataEditor({ applicationId, context }: { applicationId: string; context: ListContext }) {
  const { owner: portal, snapshot: session } = useProtectedPortal();
  const [draftOwner] = useState(() => new ProtectedStateOwner<MetadataEditorSnapshot>({ context: session.context, receipts: portal.receipts }));
  const [commandOwner] = useState(() => new ProtectedStateOwner<MetadataCommandObservation>({ context: session.context, receipts: portal.receipts }));
  const [revision, refresh] = useState(0);
  const [, redraw] = useState(0);
  const [review, setReview] = useState<{ key: string; requestKey: string; value: MetadataReview }>();
  const [message, setMessage] = useState<string>();
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);
  const alive = useRef(true);
  const heading = useRef<HTMLHeadingElement>(null);
  const reviewHeading = useRef<HTMLHeadingElement>(null);
  const url = `/api/portal/applications/${applicationId}/metadata`;
  const read = usePortalRead<MetadataEditorSnapshot>(url, revision);
  const sessionKey = session.context === undefined ? undefined : portalContextKey(session.context);
  const view = draftOwner.view;
  const matchesTarget = !session.blocked && sessionKey !== undefined && view.snapshot !== undefined
    && portalContextKey(view.snapshot.context) === sessionKey
    && metadataTargetMatches(applicationId, url, view.snapshot.value, view.target)
    && metadataTargetMatches(applicationId, url, view.snapshot.value, view.snapshot.target);
  const editor = matchesTarget ? view.snapshot?.value : undefined;
  const back = applicationRoute({ applicationId, applicationName: editor?.metadata.displayName || editor?.application, tab: "instances", context });
  const draft = !matchesTarget || view.draft === undefined ? undefined : readMetadataDraft(view.draft.text);
  const currentReview = matchesTarget && review?.key === sessionKey && review.requestKey === url
    && review.value.applicationId === applicationId && review.value.ifMatch === editor?.metadataETag ? review.value : undefined;
  const receipts = portal.receipts.load();
  const receiptNotice = portal.receiptNotice;
  const originals = receipts.kind === "ready" ? receipts.receipts.filter(item => item.family === "management.metadata.replace" && item.target.applicationId === applicationId) : [];
  const commandSnapshot = commandOwner.view.snapshot;
  const observation = sessionKey !== undefined && commandSnapshot !== undefined
    && portalContextKey(commandSnapshot.context) === sessionKey && commandSnapshot.target.applicationId === applicationId
    && commandSnapshot.value.target.applicationId === applicationId
    && (commandSnapshot.target.requestKey === url + "#submit" || commandSnapshot.target.requestKey === `${url}-operations/${encodeURIComponent(commandSnapshot.value.key)}#lookup`)
    ? commandSnapshot.value : undefined;
  const hasResolvedOriginal = resolvedMetadataOriginal(observation);
  const editable = view.canWrite && editor?.canReplace === true && !read.busy && !read.stale && !busy && originals.length === 0;

  useEffect(() => { const first = portal.register(draftOwner), second = portal.register(commandOwner); return () => { first(); second(); }; }, [portal, draftOwner, commandOwner]);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => { setReview(undefined); setMessage(undefined); }, [sessionKey]);
  useEffect(() => {
    let current = true;
    const reply = read.reply;
    if (reply === undefined || read.busy) { return; }
    if (reply.state !== "ready") {
      if (reply.state === "unavailable") { draftOwner.transientUnavailable(); }
      else { draftOwner.permissionLost(); setReview(undefined); }
      redraw(value => value + 1); return;
    }
    if (read.stale) { draftOwner.transientUnavailable(); redraw(value => value + 1); return; }
    const actual = capturePortalContext(reply.context);
    if (actual === undefined) { draftOwner.signOut(); return; }
    const previous = draftOwner.view.snapshot?.value;
    if (previous !== undefined && previous.metadataETag === reply.value.metadataETag
      && previous.canReplace === reply.value.canReplace && draftOwner.view.draft !== undefined
      && draftOwner.view.state === "ready") { return; }
    if (previous?.canReplace && !reply.value.canReplace) { draftOwner.discardDraft(); setReview(undefined); }
    draftOwner.setContext(actual);
    draftOwner.selectTarget({ requestKey: url, applicationId, environment: reply.value.environment });
    // Consume the already validated protected read; no second authentication or native read is performed.
    void draftOwner.request(async () => ({ state: "ready", context: actual, value: reply.value })).then(() => {
      if (!current) { return; }
      if (draftOwner.view.draft === undefined) { draftOwner.startDraft(metadataDraftText(reply.value.metadata)); }
      setReview(undefined); redraw(value => value + 1);
    });
    return () => { current = false; };
  }, [read.reply, read.busy, read.stale, draftOwner, applicationId, url]);
  useEffect(() => { if (currentReview !== undefined) { reviewHeading.current?.focus(); } }, [currentReview]);

  function edit(field: "displayName" | "description", value: string) {
    if (!editable || draft === undefined) { return; }
    draftOwner.editDraft(JSON.stringify({ ...draft, [field]: value }));
    setReview(undefined); setMessage(undefined); redraw(current => current + 1);
  }
  function prepareReview(event: FormEvent) {
    event.preventDefault();
    if (!editable || editor === undefined || view.draft === undefined || sessionKey === undefined) { return; }
    try { setReview({ key: sessionKey, requestKey: url, value: reviewMetadata(editor, view.draft.text, applicationId) }); setMessage(undefined); }
    catch { setMessage("Enter a display name of 1–256 characters and a description of at most 2,048 characters. Control characters are not supported."); }
  }
  function leave() {
    if (draftOwner.view.navigationBlocked && !window.confirm("Discard unsaved changes and return to the application?")) { return; }
    draftOwner.discardDraft(); window.location.assign(back);
  }
  async function commandRequest(target: string, init: RequestInit, recoverySignal?: AbortSignal) {
    const epoch = portal.getSnapshot().epoch;
    commandOwner.selectTarget({ requestKey: target + (init.method === "PUT" ? "#submit" : "#lookup"), applicationId });
    return commandOwner.request(async ticket => {
      const headers = new Headers(init.headers);
      if (ticket.context !== undefined) { headers.set(portalContextHeader, portalContextKey(ticket.context)); }
      const signal = recoverySignal === undefined ? ticket.signal : AbortSignal.any([ticket.signal, recoverySignal]);
      const response = await fetch(target, { ...init, headers, signal, credentials: "same-origin", cache: "no-store", redirect: "error" });
      const body = await response.json() as PortalReply<MetadataCommandObservation>;
      if (body.state === "unauthenticated" || body.state === "ready" && body.value.transport.status === 401) {
        if (portal.getSnapshot().epoch === epoch) { portal.signOut(); }
        return { state: "unauthenticated" };
      }
      if (body.context !== undefined && !portal.observe(body.context, epoch, commandOwner).accepted) { return { state: "unavailable" }; }
      if (body.state === "ready") {
        const actual = capturePortalContext(body.context);
        if (actual === undefined) { return { state: "unauthenticated" }; }
        if (body.value.target.applicationId !== applicationId || body.value.family !== "management.metadata.replace") { throw new TypeError("Unrelated original result."); }
        if (body.value.transport.status === 403 || body.value.transport.status === 404) { draftOwner.permissionLost(); }
        return { state: "ready", context: actual, value: body.value };
      }
      return { state: body.state, ...(body.context === undefined ? {} : { context: body.context }) };
    });
  }
  async function confirm() {
    portal.checkExpiry();
    const actualSession = portal.getSnapshot();
    const actualView = draftOwner.view;
    if (pending.current || !editable || currentReview === undefined || !actualView.canWrite || actualSession.blocked
      || actualSession.context === undefined || portalContextKey(actualSession.context) !== review?.key
      || currentReview.applicationId !== applicationId || currentReview.ifMatch !== actualView.snapshot?.value.metadataETag
      || !metadataTargetMatches(applicationId, url, actualView.snapshot?.value, actualView.target)
      || !metadataTargetMatches(applicationId, url, actualView.snapshot?.value, actualView.snapshot?.target)) { return; }
    const selected = currentReview;
    const prepared = portal.receipts.prepare({ family: "management.metadata.replace", target: { applicationId } });
    if (prepared.kind !== "prepared") { setMessage("The original operation reference could not be retained. Nothing was submitted."); return; }
    pending.current = true; setBusy(true); setMessage(undefined);
    try {
      await commandRequest(url, { method: "PUT", headers: { "Content-Type": "application/json", "Idempotency-Key": prepared.receipt.key, "If-Match": selected.ifMatch }, body: JSON.stringify(selected.command) });
      const settled = commandOwner.view.snapshot;
      const actual = portal.getSnapshot();
      const currentTarget = draftOwner.view;
      if (alive.current && currentTarget.canWrite && settled !== undefined && actual.context !== undefined && !actual.blocked && portalContextKey(actual.context) === review?.key
        && portalContextKey(settled.context) === review?.key && settled.target.requestKey === url + "#submit" && settled.target.applicationId === applicationId
        && metadataTargetMatches(applicationId, url, currentTarget.snapshot?.value, currentTarget.target)
        && metadataTargetMatches(applicationId, url, currentTarget.snapshot?.value, currentTarget.snapshot?.target)) {
        const action = metadataConfirmationAction(selected, settled.value, prepared.receipt.key, currentTarget.draft?.text);
        if (action !== "stay") {
          draftOwner.discardDraft();
          if (action === "return") {
            window.location.assign(applicationRoute({ applicationId, applicationName: selected.command.displayName, tab: "instances", context }));
            return;
          }
          setMessage("Metadata was updated. Current request cleanup was not confirmed; the original result reference is retained below.");
          return;
        }
      }
      draftOwner.transientUnavailable();
    } catch { draftOwner.transientUnavailable(); if (alive.current) { setMessage("The response was not confirmed. Recover the original result using the retained reference."); } }
    finally { pending.current = false; if (alive.current) { setBusy(false); setReview(undefined); redraw(value => value + 1); } }
  }
  async function recover(receipt: CommandReceipt) {
    if (pending.current || portal.receipts.recoveryBusy) { return; }
    const actual = commandOwner.view.snapshot;
    const actualContext = portal.getSnapshot().context;
    if (actual !== undefined && actual.value.target.applicationId === applicationId && actualContext !== undefined
      && portalContextKey(actual.context) === portalContextKey(actualContext) && resolvedMetadataOriginal(actual.value)) {
      setMessage("Dismiss the resolved original reference and refresh before looking up another operation."); return;
    }
    pending.current = true; setBusy(true); setMessage(undefined);
    try {
      const recovered = await recoverMetadataOriginal(commandOwner, portal.receipts, receipt, applicationId, async (target, _original, signal) => {
        await commandRequest(target, { method: "GET" }, signal);
      });
      if (recovered.kind === "blocked" && alive.current) { setMessage("This original reference cannot be recovered in the current session. Nothing was resubmitted."); }
    } catch { if (alive.current) { setMessage("The original result is unavailable. Keep this reference and try recovery later; nothing was resubmitted."); } }
    finally { pending.current = false; if (alive.current) { setBusy(false); redraw(value => value + 1); } }
  }
  function finish(receipt: CommandReceipt) {
    if (busy || observation?.key !== receipt.key || !resolvedMetadataOriginal(observation)) { return; }
    if (portal.receipts.forgetExact(receipt).kind !== "forgotten") { setMessage("The local reference could not be dismissed."); return; }
    const applied = observation.disposition === "applied";
    commandOwner.selectTarget(undefined);
    if (applied) { draftOwner.discardDraft(); }
    setReview(undefined); refresh(value => value + 1); redraw(value => value + 1);
  }

  return <Shell signedIn={!session.blocked} footer="Display metadata"><button className="item-link back-link" onClick={leave}>← Back to application</button>
    <div className="page-heading"><div><div className="eyebrow">Application display metadata</div><h1 ref={heading} tabIndex={-1}>Edit display metadata</h1>
      <p>{editor === undefined ? "Display name and description only." : `${editor.application} / ${editor.environment}`}</p></div>
      <button disabled={busy || read.busy} onClick={() => { setReview(undefined); refresh(value => value + 1); }}>Refresh current metadata</button></div>
    <ReadNotice state={read} />
    {message === undefined ? null : <p className="notice" role="alert">{message}</p>}
    {editor === undefined || draft === undefined || session.blocked ? null : <>
      <TechnicalDetails fields={[{ label: "Application ID", value: editor.applicationId }, { label: "Application identity", value: editor.application },
        { label: "Metadata revision", value: editor.metadataRevision }, { label: "Metadata precondition", value: editor.metadataETag }]} />
      {!editor.canReplace ? <p className="notice">Editing requires permission to change this application and recover its original result.</p> : null}
      {currentReview === undefined ? <form className="card metadata-editor" onSubmit={prepareReview}><h2>Display metadata</h2>
        <p>The application identity and environment remain fixed. A blank description explicitly clears the description.</p>
        <label htmlFor="metadata-display-name">Display name</label><input id="metadata-display-name" value={draft.displayName} maxLength={512} disabled={!editable} onChange={event => edit("displayName", event.target.value)} />
        <label htmlFor="metadata-description">Description</label><textarea id="metadata-description" value={draft.description} maxLength={4096} disabled={!editable} onChange={event => edit("description", event.target.value)} />
        {editor.canReplace ? <button type="button" disabled={!editable || draft.description === ""} onClick={() => edit("description", "")}>Clear description</button> : null}
        <div className="metadata-actions">{editor.canReplace ? <button type="submit" className="primary" disabled={!editable || !view.draft?.dirty}>Review changes</button> : null}<button type="button" onClick={leave} disabled={busy}>Cancel</button></div>
      </form> : <section className="card metadata-review"><h2 ref={reviewHeading} tabIndex={-1}>Review display metadata</h2>
        <p>Confirm this application’s exact display metadata. This does not change its canonical identity.</p>
        <div className="table-frame"><table><thead><tr><th>Field</th><th>Before</th><th>After</th></tr></thead><tbody>
          <tr><th scope="row">Display name</th><td>{currentReview.before.displayName}</td><td>{currentReview.command.displayName}</td></tr>
          <tr><th scope="row">Description</th><td>{currentReview.before.description || "No description"}</td><td>{currentReview.command.description === undefined ? "Clear description — no description will remain" : currentReview.command.description}</td></tr>
        </tbody></table></div><div className="metadata-actions"><button className="primary" disabled={!editable} onClick={() => { void confirm(); }}>Confirm metadata change</button>
          <button disabled={busy} onClick={() => { setReview(undefined); heading.current?.focus(); }}>Back to draft</button><button disabled={busy} onClick={leave}>Cancel</button></div>
      </section>}
    </>}
    {originals.length === 0 ? null : <section className="card" aria-label="Original metadata operations"><h2>Original operations</h2>
      <p>These references contain no draft. Recovery only looks up the same original operation and never resubmits it.</p>
      {originals.map(receipt => <section className="metadata-operation" key={receipt.key}><h3>Metadata change</h3>
        <TechnicalDetails fields={[{ label: "Original operation key", value: receipt.key }, { label: "Application ID", value: applicationId }]} />
        {observation?.key === receipt.key ? <MetadataOutcome observation={observation} /> : <p>Original outcome has not been recovered in this view.</p>}
        <div className="metadata-actions"><button disabled={busy || session.blocked || hasResolvedOriginal} onClick={() => { void recover(receipt); }}>Recover original result</button>
          {observation?.key === receipt.key && resolvedMetadataOriginal(observation)
            ? <button disabled={busy} onClick={() => finish(receipt)}>Dismiss resolved reference and refresh</button> : null}</div>
      </section>)}
    </section>}
    {receipts.kind === "blocked" && receipts.reason !== "context-unavailable" ? <p className="notice" role="status">Original references could not be loaded ({receipts.reason}). No new change can be submitted until reference storage is available.</p> : null}
    {receiptNotice === undefined ? null : <p className="notice" role="status">Earlier original references could not be restored ({receiptNotice}). A missing local reference does not prove that a previous change failed.</p>}
    {busy ? <p className="notice" role="status">Waiting for the current request to settle…</p> : null}
  </Shell>;
}

function MetadataOutcome({ observation }: { observation: MetadataCommandObservation }) {
  const label = { applied: "Metadata change committed", rejected: "Original change rejected", conflict: "Metadata conflict — refresh and review again", notCommitted: "Original change was not committed", unresolved: "Original outcome unresolved" }[observation.disposition];
  return <div role="status"><h3>{label}</h3>{observation.disposition === "unresolved" || observation.disposition === "notCommitted"
    ? <p>{observation.disposition === "unresolved" ? "The same original reference is retained. This is not permission to submit a replacement operation."
      : "This observation is retained until you explicitly dismiss it and refresh. Nothing is resubmitted."}</p> : null}
    {observation.disposition === "conflict" ? <p>Your permitted draft remains in memory. Dismiss the resolved reference, refresh current metadata and deliberately review the change again.</p> : null}
    {observation.disposition !== "applied" || observation.decision === undefined ? null : <Facts><Fact label="Committed display name">{observation.decision.metadata.displayName}</Fact><Fact label="Committed description">{observation.decision.metadata.description || "No description"}</Fact><Fact label="Metadata revision">{observation.decision.metadataRevision}</Fact></Facts>}
    <TechnicalDetails fields={[{ label: "Original completion", value: observation.originalCompletion }, { label: "Result source", value: observation.resultSource },
      { label: "Native decision code", value: observation.rejection?.code ?? observation.code }, { label: "Request outcome", value: observation.transport.kind },
      { label: "Request cleanup succeeded", value: observation.transport.cleanupSucceeded ? "Yes" : "No" },
      { label: "Native attempt outcome", value: observation.attemptCompletion?.outcome },
      { label: "Native attempt cleanup succeeded", value: observation.attemptCompletion === undefined ? undefined : observation.attemptCompletion.cleanupSucceeded ? "Yes" : "No" }]} />
  </div>;
}
