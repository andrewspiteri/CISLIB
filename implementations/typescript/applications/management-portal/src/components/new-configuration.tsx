"use client";

import { useEffect, useRef, useState } from "react";
import type { ConfigurationCreationContext } from "@cislib/management-client";
import type { CommandReceipt } from "../command-receipts.ts";
import type { ConfigurationCommandObservation } from "../configuration-command-results.ts";
import { resolvedConfigurationOriginal } from "../configuration-command-results.ts";
import type { ConfigurationCreationReview, ConfigurationInitialDraft } from "../configuration-creation-model.ts";
import { reviewConfigurationCreation } from "../configuration-creation-model.ts";
import { configurationNewVersionDraft } from "../configuration-new-version.ts";
import { configurationDraftBytes, formatConfigurationJson } from "../configuration-draft.ts";
import { capturePortalContext, portalContextKey } from "../portal-context.ts";
import { applicationRoute, configurationRoute } from "../portal-routes.ts";
import { ProtectedStateOwner } from "../protected-state.ts";
import type { ListContext } from "./application-detail";
import { ConfigurationVersionEditor } from "./configuration-version-editor";
import type { ConfigurationSavedCompletion } from "./configuration-version-editor";
import { ConfigurationVersionPage } from "./configuration-version-page";
import { ConfigurationOperationStatus, configurationOperationClean, useConfigurationOperation } from "./configuration-operation";
import { useProtectedPortal } from "./protected-context";
import { ReadNotice, Shell, TechnicalDetails, ready, usePortalRead } from "./shared";

interface NewSaveAttempt { readonly review: ConfigurationCreationReview; readonly key: string; readonly sessionKey: string; }
interface VersionHandoff {
  readonly scope: string; readonly configurationId: string; readonly schemaVersion: string; readonly note: string;
  readonly creation: ConfigurationCommandObservation; readonly seed?: ConfigurationInitialDraft;
}

/** One visible Save intent; native setup, publication and release retain separate originals. */
export function NewConfiguration({ applicationId, context, initialCursor }: { applicationId: string; context: ListContext; initialCursor?: string }) {
  const { snapshot: session } = useProtectedPortal();
  const [completed, setCompleted] = useState<ConfigurationSavedCompletion>();
  const sessionKey = session.context === undefined ? undefined : portalContextKey(session.context);
  const saved = completed?.applicationId === applicationId && completed.sessionKey === sessionKey && !session.blocked ? completed : undefined;
  useEffect(() => setCompleted(undefined), [applicationId, sessionKey]);
  if (saved !== undefined) {
    return <ConfigurationVersionPage applicationId={applicationId} configurationId={saved.configurationId} schemaVersion={saved.schemaVersion}
      context={context} initialReleaseId={saved.savedReleaseId} initialCompletedSave={saved} {...(initialCursor === undefined ? {} : { initialCursor })} />;
  }
  return <NewConfigurationForm applicationId={applicationId} context={context} onCompleted={setCompleted}
    {...(initialCursor === undefined ? {} : { initialCursor })} />;
}

/** Form owners retire before the saved page mounts; the finite document owner bound is unchanged. */
function NewConfigurationForm({ applicationId, context, initialCursor, onCompleted }: {
  applicationId: string; context: ListContext; initialCursor?: string; onCompleted(completion: ConfigurationSavedCompletion): void
}) {
  const { owner: portal, snapshot: session } = useProtectedPortal();
  const [draftOwner] = useState(() => new ProtectedStateOwner<ConfigurationCreationContext>({ context: session.context, receipts: portal.receipts }));
  const creation = useConfigurationOperation();
  const [revision, refresh] = useState(0), [, redraw] = useState(0);
  const [fields, setFields] = useState({ scope: "", name: "", note: "" });
  const [handoff, setHandoff] = useState<VersionHandoff>();
  const [message, setMessage] = useState<string>(), [busy, setBusy] = useState(false);
  const pending = useRef(false), alive = useRef(true), accepted = useRef(false);
  const appliedRead = useRef<{ reply: unknown; revision: number; url: string } | undefined>(undefined);
  const attempt = useRef<NewSaveAttempt | undefined>(undefined);
  const fieldsNow = useRef(fields);
  fieldsNow.current = fields;
  const url = `/api/portal/applications/${applicationId}/configuration-creations/context`;
  const read = usePortalRead<ConfigurationCreationContext>(url, revision, { requestKey: url, applicationId });
  const observed = ready(read), sessionKey = session.context === undefined ? undefined : portalContextKey(session.context);
  const scope = JSON.stringify([applicationId, sessionKey]), snapshot = draftOwner.view.snapshot;
  const incoming = read.reply?.state === "ready" ? capturePortalContext(read.reply.context) : undefined;
  const admissible = read.reply === undefined || read.reply.state === "unavailable" || observed !== undefined && incoming !== undefined
    && portalContextKey(incoming) === sessionKey && observed.applicationId === applicationId;
  const data = snapshot !== undefined && sessionKey !== undefined && !session.blocked && portalContextKey(snapshot.context) === sessionKey
    && admissible && snapshot.target.requestKey === url && snapshot.target.applicationId === applicationId && snapshot.value.applicationId === applicationId
    && (observed === undefined || observed.environment === snapshot.value.environment && observed.schemaVersion === snapshot.value.schemaVersion
      && observed.hostApplicationName === snapshot.value.hostApplicationName && observed.eTag === snapshot.value.eTag) ? snapshot.value : undefined;
  const raw = data === undefined ? undefined : draftOwner.view.draft?.text;
  const name = fields.scope === scope ? fields.name : "", note = fields.scope === scope ? fields.note : "";
  const loaded = portal.receipts.load();
  const originals = loaded.kind === "ready" ? loaded.receipts.filter(item => item.family === "management.configuration.create" && item.target.applicationId === applicationId) : [];
  const observation = creation.observation?.family === "management.configuration.create" && "applicationId" in creation.observation.target
    && creation.observation.target.applicationId === applicationId ? creation.observation : undefined;
  const current = data !== undefined && raw !== undefined && draftOwner.view.canWrite && !read.busy && !read.stale && !session.blocked
    && !busy && originals.length === 0 && loaded.kind === "ready";
  const recoveryReady = read.reply !== undefined && !read.busy && appliedRead.current?.reply === read.reply
    && appliedRead.current.revision === revision && appliedRead.current.url === url;
  const activeHandoff = handoff?.scope === scope ? handoff : undefined;
  const back = applicationRoute({ applicationId, applicationName: data?.application, tab: "configuration", cursor: initialCursor, context });

  useEffect(() => portal.register(draftOwner), [portal, draftOwner]);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => {
    setFields({ scope, name: "", note: "" }); setHandoff(undefined); setMessage(undefined);
    attempt.current = undefined; accepted.current = false;
  }, [scope]);
  useEffect(() => {
    let active = true;
    if (read.busy || read.reply === undefined || activeHandoff !== undefined) { return; }
    if (appliedRead.current?.reply === read.reply && appliedRead.current.revision === revision && appliedRead.current.url === url) { return; }
    const value = ready(read), actual = read.reply?.state === "ready" ? capturePortalContext(read.reply.context) : undefined;
    if (value === undefined || actual === undefined || value.applicationId !== applicationId) {
      if (read.reply?.state === "unavailable") { draftOwner.transientUnavailable(); }
      else { draftOwner.permissionLost(); attempt.current = undefined; setFields({ scope, name: "", note: "" }); }
      appliedRead.current = { reply: read.reply, revision, url }; redraw(count => count + 1); return;
    }
    if (read.stale) { draftOwner.transientUnavailable(); appliedRead.current = { reply: read.reply, revision, url }; redraw(count => count + 1); return; }
    draftOwner.setContext(actual); draftOwner.selectTarget({ requestKey: url, applicationId });
    void draftOwner.request(async () => ({ state: "ready", context: actual, value })).then(() => {
      if (!active) { return; }
      if (draftOwner.view.draft === undefined && !draftOwner.view.intentDirty) { draftOwner.startDraft("{}\n"); }
      appliedRead.current = { reply: read.reply, revision, url }; redraw(count => count + 1);
    });
    return () => { active = false; };
  }, [read.reply, read.busy, read.stale, draftOwner, applicationId, url, revision, scope, activeHandoff]);

  function editField(field: "name" | "note", value: string) {
    if (!current) { return; }
    const next = { ...(fields.scope === scope ? fields : { scope, name: "", note: "" }), [field]: value };
    fieldsNow.current = next; setFields(next); draftOwner.setIntentDirty(true); setMessage(undefined); redraw(count => count + 1);
  }
  function editRaw(value: string) {
    if (!current) { return; }
    if (draftOwner.editDraft(value).kind !== "changed") { setMessage("The previous JSON is retained. Keep the draft within 256 KiB."); return; }
    setMessage(undefined); redraw(count => count + 1);
  }
  function leave() {
    if (portal.navigationBlocked && !window.confirm("Discard unsaved changes and return to the Configuration list?")) { return; }
    portal.discardLocalDrafts(); window.location.assign(back);
  }
  function clearDeniedDraft() {
    draftOwner.permissionLost(); attempt.current = undefined;
    setFields({ scope, name: "", note: "" });
  }
  function beginVersion(captured: NewSaveAttempt, value: ConfigurationCommandObservation) {
    portal.checkExpiry();
    const actual = portal.getSnapshot(), currentFields = fieldsNow.current;
    if (!alive.current || actual.blocked || actual.context === undefined || currentFields.scope !== scope
      || currentFields.name !== captured.review.configurationId || currentFields.note !== captured.review.reason
      || draftOwner.view.draft?.text !== captured.review.raw) { throw new TypeError("This Save's original local intent is no longer available."); }
    const seed = configurationNewVersionDraft(captured.review, value, captured.key, captured.sessionKey, portalContextKey(actual.context));
    window.history.replaceState(null, "", configurationRoute({ applicationId, applicationName: data?.application, configurationId: seed.configurationId,
      schemaVersion: seed.schemaVersion, context }));
    setHandoff({ scope, configurationId: seed.configurationId, schemaVersion: seed.schemaVersion, note: captured.review.reason, creation: value, seed });
  }
  async function save() {
    portal.checkExpiry();
    const actual = portal.getSnapshot();
    if (pending.current || !current || data === undefined || raw === undefined || actual.context === undefined || actual.blocked
      || portalContextKey(actual.context) !== sessionKey) { return; }
    let reviewed: ConfigurationCreationReview;
    try { reviewed = reviewConfigurationCreation(data, name, raw, note, "createGeneric"); }
    catch { setMessage("Provide a name, a version note and a complete JSON object within 256 KiB."); return; }
    const intent = { family: "management.configuration.create" as const, target: { applicationId } };
    const prepared = portal.receipts.prepare(intent);
    if (prepared.kind !== "prepared") { setMessage("The Save reference could not be retained. Nothing was submitted."); return; }
    const captured = { review: reviewed, key: prepared.receipt.key, sessionKey: portalContextKey(actual.context) };
    attempt.current = captured; pending.current = true; setBusy(true); setMessage(undefined);
    try {
      const result = await creation.request(`/api/portal/applications/${applicationId}/configuration-creations`, { method: "POST", headers: {
        "Content-Type": "application/json", "Idempotency-Key": prepared.receipt.key, "If-Match": reviewed.ifMatch },
        body: JSON.stringify({ commandVersion: 1, configurationId: reviewed.configurationId, intent: reviewed.intent, reason: reviewed.reason }) }, intent, prepared.receipt.key);
      if (!alive.current) { return; }
      if (result?.transport.status === 403 || result?.transport.status === 404
        || creation.owner.view.state === "denied" || creation.owner.view.state === "not-found") { clearDeniedDraft(); }
      if (configurationOperationClean(result) && result !== undefined) { beginVersion(captured, result); }
      else { setMessage("Save incomplete. Its setup result is not fully confirmed; the JSON has not been saved as a version. Keep the original reference below."); }
    } catch { if (alive.current) { setMessage("Save incomplete. Recover the exact original result below; no replacement was submitted."); } }
    finally { pending.current = false; if (alive.current) { setBusy(false); redraw(count => count + 1); } }
  }
  async function recover(receipt: CommandReceipt) {
    if (pending.current || !recoveryReady || portal.receipts.recoveryBusy || receipt.family !== "management.configuration.create"
      || receipt.target.applicationId !== applicationId) { return; }
    // Settle all issued editor reads before the independent lookup is admitted.
    draftOwner.selectTarget({ requestKey: url, applicationId });
    pending.current = true; setBusy(true); setMessage(undefined);
    try {
      await creation.recover(receipt, `/api/portal/applications/${applicationId}/configuration-creation-operations/${receipt.key}`);
      const value = creation.owner.view.snapshot?.value;
      if (value?.transport.status === 403 || value?.transport.status === 404
        || creation.owner.view.state === "denied" || creation.owner.view.state === "not-found") { clearDeniedDraft(); }
    }
    catch { if (alive.current) { setMessage("The original result is unavailable. Its reference is retained; nothing was resubmitted."); } }
    finally { pending.current = false; if (alive.current) { setBusy(false); redraw(count => count + 1); } }
  }
  function completeSave(receipt: CommandReceipt) {
    const captured = attempt.current;
    if (pending.current || captured === undefined || captured.key !== receipt.key || observation?.key !== receipt.key || !configurationOperationClean(observation)) { return; }
    try { beginVersion(captured, observation); }
    catch { setMessage("The original JSON or session is no longer available. No later Save stage was submitted."); }
  }
  function admittedDraft() {
    if (accepted.current) { return; }
    accepted.current = true;
    draftOwner.discardDraft(); attempt.current = undefined;
    setFields({ scope, name: "", note: "" });
    setHandoff(value => {
      if (value === undefined || value.scope !== scope) { return value; }
      const { seed: _seed, ...retained } = value;
      return retained;
    });
  }
  function versionSaved(completion: ConfigurationSavedCompletion, keepEditing: boolean) {
    const actual = portal.getSnapshot();
    if (!alive.current || actual.blocked || actual.context === undefined || completion.applicationId !== applicationId
      || completion.sessionKey !== portalContextKey(actual.context)) { return; }
    if (!keepEditing) {
      try {
        window.history.replaceState(null, "", configurationRoute({ applicationId, applicationName: data?.application, configurationId: completion.configurationId,
          schemaVersion: completion.schemaVersion, releaseId: completion.savedReleaseId, context }));
      } catch { setMessage("The version was saved. Its current page address could not be updated."); }
      onCompleted(completion);
    }
  }

  if (activeHandoff !== undefined) {
    return <Shell wide signedIn={!session.blocked} footer="Configuration versions"><button className="item-link back-link" onClick={leave}>← Back to Configuration list</button>
      <div className="page-heading"><div><h1>{activeHandoff.configurationId}</h1><p>Application only · no Shared base</p></div></div>
      <ConfigurationVersionEditor applicationId={applicationId} configurationId={activeHandoff.configurationId} schemaVersion={activeHandoff.schemaVersion}
        {...(activeHandoff.seed === undefined ? {} : { initialDraft: activeHandoff.seed })} initialNote={activeHandoff.note} autoSave creation={activeHandoff.creation}
        onDraftAccepted={admittedDraft} onBusy={setBusy} onCancel={leave} onSaved={versionSaved} />
    </Shell>;
  }
  return <Shell wide signedIn={!session.blocked} footer="New Configuration"><button className="item-link back-link" disabled={busy} onClick={leave}>← Back to Configuration list</button>
    <div className="page-heading"><div><h1>New configuration</h1><p>Application only · no Shared base. Saving does not deploy it.</p></div>
      <button disabled={busy || read.busy} onClick={() => refresh(count => count + 1)}>Refresh</button></div>
    <ReadNotice state={read} />{message === undefined ? null : <p className="notice" role="status">{message}</p>}
    {data === undefined || raw === undefined ? null : <section className="card configuration-version-editor"><label htmlFor="new-configuration-name">Configuration name</label>
      <input id="new-configuration-name" value={name} disabled={!current} maxLength={128} onChange={event => editField("name", event.target.value)} />
      <div className="configuration-json-toolbar"><label htmlFor="new-configuration-json">Application JSON</label><button disabled={!current} onClick={() => {
        const result = formatConfigurationJson(raw); if (result.valid) { editRaw(result.json); } else { setMessage(result.message); }
      }}>Format JSON</button></div>
      <textarea id="new-configuration-json" className="configuration-json" spellCheck={false} value={raw} disabled={!current} onChange={event => editRaw(event.target.value)} />
      <p className="muted">{configurationDraftBytes(raw)} / 262144 UTF-8 bytes</p>
      <label htmlFor="new-configuration-note">Version note</label><input id="new-configuration-note" value={note} maxLength={1024} disabled={!current} onChange={event => editField("note", event.target.value)} />
      <div className="configuration-save-actions"><p>Saving does not deploy this version.</p><button className="primary" disabled={!current} onClick={() => { void save(); }}>{busy ? "Saving…" : "Save version"}</button></div>
    </section>}
    {originals.length === 0 ? null : <section className="card configuration-recovery"><h2>Save progress and original result</h2><p>Setup alone does not confirm a saved version. Recovery reads the original result and never automatically resumes a Save.</p>
      {originals.map(receipt => <section key={receipt.key}>
        {observation?.key === receipt.key ? <ConfigurationOperationStatus value={observation} /> : <p>Original result not recovered in this view.</p>}
        <div className="metadata-actions"><button disabled={busy || !recoveryReady || portal.receipts.recoveryBusy || !creation.canRecover(receipt)} onClick={() => { void recover(receipt); }}>Recover original Save result</button>
          {observation?.key !== receipt.key || !configurationOperationClean(observation) || attempt.current?.key !== receipt.key ? null
            : <button className="primary" disabled={busy} onClick={() => completeSave(receipt)}>Complete incomplete Save</button>}
          <button disabled={busy || observation?.key !== receipt.key || !resolvedConfigurationOriginal(observation)} onClick={() => {
            if (creation.dismiss(receipt)) { attempt.current = undefined; refresh(count => count + 1); }
          }}>Dismiss resolved reference</button>
          {observation?.key !== receipt.key || observation.disposition !== "applied" || observation.decision?.configurationId === undefined || observation.decision.schemaVersion === undefined ? null
            : <a href={configurationRoute({ applicationId, applicationName: data?.application, configurationId: observation.decision.configurationId,
              schemaVersion: observation.decision.schemaVersion, context })}>Open configuration</a>}
        </div>
      </section>)}
    </section>}
    <TechnicalDetails fields={[{ label: "Application ID", value: applicationId }, { label: "Schema version", value: data?.schemaVersion },
      { label: "Configuration host", value: data?.hostApplicationName }, { label: "Setup precondition", value: data?.eTag }]} />
    {portal.receiptNotice === undefined ? null : <p className="notice">Earlier original references could not be restored ({portal.receiptNotice}). A missing reference does not prove that a Save failed.</p>}
    {busy ? <p className="notice" role="status">Waiting for the current Save request to settle…</p> : null}
  </Shell>;
}
