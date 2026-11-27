"use client";

import { useEffect, useRef, useState } from "react";
import type { ConfigurationCandidatePreview, ConfigurationPreview } from "@cislib/management-client";
import type { ConfigurationInitialDraft } from "../configuration-creation-model.ts";
import { initialConfigurationDraftMatches } from "../configuration-creation-model.ts";
import type { ConfigurationEditorSnapshot } from "../configuration-editor-model.ts";
import type { ConfigurationCommandObservation } from "../configuration-command-results.ts";
import { resolvedConfigurationOriginal } from "../configuration-command-results.ts";
import { configurationDraftBytes, formatConfigurationJson } from "../configuration-draft.ts";
import { captureConfigurationVersionSave, ConfigurationVersionSaveInterrupted, configurationVersionSaveMatches,
  configurationVersionSavedReleaseId, executeConfigurationVersionSave, rebindConfigurationVersionRelease,
  configurationVersionReleasePreview, acceptConfigurationVersionReleasePreview } from "../configuration-version-commands.ts";
import type { ConfigurationVersionSaveIntent, ConfigurationVersionSaveProgress } from "../configuration-version-commands.ts";
import type { ConfigurationVersionSnapshot } from "../configuration-versions.ts";
import { configurationVersionMatches } from "../configuration-versions.ts";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../portal-context.ts";
import type { PortalReply } from "../portal-model.ts";
import { ProtectedStateOwner } from "../protected-state.ts";
import { useProtectedPortal } from "./protected-context";
import { ReadNotice, TechnicalDetails, queryUrl, ready, usePortalRead } from "./shared";
import { ConfigurationOperationStatus, configurationOperationClean, useConfigurationOperation } from "./configuration-operation";

export interface ConfigurationSavedCompletion {
  readonly applicationId: string; readonly configurationId: string; readonly schemaVersion: string; readonly sessionKey: string;
  readonly savedReleaseId: string; readonly publication: ConfigurationCommandObservation; readonly release: ConfigurationCommandObservation;
  readonly creation?: ConfigurationCommandObservation;
}
interface SaveAttempt { readonly intent: ConfigurationVersionSaveIntent; publicationKey?: string; releaseKey?: string;
  readonly previousRelease?: ConfigurationCommandObservation; readonly dismissedReleaseKey?: string; }

/** The one local JSON owner and the fixed two-original Save sequence, reused after a known native creation. */
export function ConfigurationVersionEditor({ applicationId, configurationId, schemaVersion, baseVersion, initialDraft, autoSave = false, firstVersion = false,
  initialNote = "", creation, onDraftAccepted, onContentLost, onBusy, onCancel, onSaved }: {
  applicationId: string; configurationId: string; schemaVersion: string; baseVersion?: ConfigurationVersionSnapshot;
  initialDraft?: ConfigurationInitialDraft; initialNote?: string; autoSave?: boolean; firstVersion?: boolean; creation?: ConfigurationCommandObservation;
  onDraftAccepted?(): void; onContentLost?(): void; onBusy?(busy: boolean): void; onCancel(): void; onSaved(completion: ConfigurationSavedCompletion, keepEditing: boolean): void
}) {
  const { owner: portal, snapshot: session } = useProtectedPortal();
  const [draftOwner] = useState(() => new ProtectedStateOwner<ConfigurationEditorSnapshot>({ context: session.context, receipts: portal.receipts }));
  const [previewOwner] = useState(() => new ProtectedStateOwner<ConfigurationCandidatePreview | ConfigurationPreview>({ context: session.context, receipts: portal.receipts }));
  const publication = useConfigurationOperation(), release = useConfigurationOperation();
  const [revision, refresh] = useState(0), [, redraw] = useState(0), [note, setNote] = useState(initialNote);
  const [progress, setProgress] = useState<ConfigurationVersionSaveProgress>(), [message, setMessage] = useState<string>(), [busy, setBusy] = useState(false);
  const pending = useRef(false), alive = useRef(true), admitted = useRef(false), automatic = useRef(false);
  const appliedRead = useRef<{ reply: unknown; revision: number; url: string } | undefined>(undefined);
  const attempt = useRef<SaveAttempt | undefined>(undefined), rawNow = useRef<string | undefined>(undefined), noteNow = useRef(note), currentData = useRef<ConfigurationEditorSnapshot | undefined>(undefined);
  const sessionKey = session.context === undefined ? undefined : portalContextKey(session.context);
  const base = `/api/portal/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}`;
  const url = queryUrl(base + "/editor", { schemaVersion, source: "application" });
  const read = usePortalRead<ConfigurationEditorSnapshot>(url, revision);
  const incoming = ready(read), snapshot = draftOwner.view.snapshot;
  const incomingContext = read.reply?.state === "ready" ? capturePortalContext(read.reply.context) : undefined;
  const admissible = read.reply === undefined || read.reply.state === "unavailable" || incoming !== undefined && incomingContext !== undefined
    && portalContextKey(incomingContext) === sessionKey && matches(incoming, applicationId, configurationId, schemaVersion);
  const data = snapshot !== undefined && sessionKey !== undefined && !session.blocked && admissible
    && portalContextKey(snapshot.context) === sessionKey && snapshot.target.requestKey === url
    && snapshot.target.applicationId === applicationId && snapshot.target.definitionId === configurationId && snapshot.target.schemaVersion === schemaVersion
    && matches(snapshot.value, applicationId, configurationId, schemaVersion)
    && (incoming === undefined || sameBinding(incoming, snapshot.value) && incoming.context.defaults.state === snapshot.value.context.defaults.state)
    ? snapshot.value : undefined;
  const raw = data === undefined ? undefined : draftOwner.view.draft?.text;
  currentData.current = data; rawNow.current = raw; noteNow.current = note;
  const sourceId = data?.source?.sourceId ?? initialDraft?.sourceId;
  const loaded = portal.receipts.load();
  const originals = loaded.kind === "ready" ? loaded.receipts.filter(item => item.family === "management.configuration.source.publish"
    ? sourceId === undefined || item.target.sourceId === sourceId : item.family === "management.configuration.release.create"
      && item.target.applicationId === applicationId && item.target.configurationId === configurationId && item.target.schemaVersion === schemaVersion) : [];
  const editable = data !== undefined && data.context.defaults.state === "available" && !session.blocked && !busy
    && ["ready", "stale", "loading"].includes(draftOwner.view.state) && allowed(data, "source", "publishConfigurationSource");
  const savable = editable && draftOwner.view.canWrite && !read.busy && !read.stale && loaded.kind === "ready" && originals.length === 0;
  const recoveryReady = !read.busy && read.reply !== undefined && appliedRead.current !== undefined
    && appliedRead.current.reply === read.reply && appliedRead.current.revision === revision && appliedRead.current.url === url;
  useEffect(() => { const a = portal.register(draftOwner), b = portal.register(previewOwner); return () => { a(); b(); }; }, [portal, draftOwner, previewOwner]);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => {
    let active = true;
    if (read.busy || read.reply === undefined) { return; }
    if (appliedRead.current?.reply === read.reply && appliedRead.current.revision === revision && appliedRead.current.url === url) { return; }
    if (read.reply.state !== "ready") {
      if (read.reply.state === "unavailable") { draftOwner.transientUnavailable(); }
      else { draftOwner.permissionLost(); attempt.current = undefined; setProgress(undefined); onContentLost?.(); }
      appliedRead.current = { reply: read.reply, revision, url }; redraw(value => value + 1); return;
    }
    if (read.stale) { draftOwner.transientUnavailable(); appliedRead.current = { reply: read.reply, revision, url }; redraw(value => value + 1); return; }
    const value = read.reply.value, context = capturePortalContext(read.reply.context);
    if (context === undefined || !matches(value, applicationId, configurationId, schemaVersion)) {
      draftOwner.permissionLost(); attempt.current = undefined; setProgress(undefined); onContentLost?.(); appliedRead.current = { reply: read.reply, revision, url }; redraw(value => value + 1); return;
    }
    if (draftOwner.view.snapshot !== undefined && !sameBinding(value, draftOwner.view.snapshot.value)) {
      draftOwner.discardDraft(); attempt.current = undefined; setProgress(undefined);
    }
    if (value.context.defaults.state !== "available") { draftOwner.permissionLost(); attempt.current = undefined; setProgress(undefined); onContentLost?.(); appliedRead.current = { reply: read.reply, revision, url }; redraw(value => value + 1); return; }
    draftOwner.setContext(context); draftOwner.selectTarget({ requestKey: url, applicationId, definitionId: configurationId, schemaVersion });
    void draftOwner.request(async () => ({ state: "ready", context, value })).then(() => {
      if (!active) { return; }
      appliedRead.current = { reply: read.reply, revision, url };
      if (draftOwner.view.draft !== undefined) { redraw(value => value + 1); return; }
      let seed: string | undefined;
      if (initialDraft !== undefined) {
        if (initialConfigurationDraftMatches(initialDraft, value, portalContextKey(context))) { seed = initialDraft.raw; }
      } else if (baseVersion?.content.state === "present" && configurationVersionMatches(baseVersion.release, applicationId, configurationId, schemaVersion, value.context.environment)) {
        const content = baseVersion.content.publication, slot = baseVersion.content.layer === "application" ? value.context.applicationSource : value.context.commonSource;
        const pinned = baseVersion.content.layer === "application" ? baseVersion.release.applicationSource : baseVersion.release.commonSource;
        if (slot.state === "available" && slot.sourceId === content.sourceId && content.kind === baseVersion.content.layer && pinned.state === "present" && pinned.publicationId === content.publicationId
          && content.configurationId === configurationId && content.schemaVersion === schemaVersion && content.environment === value.context.environment
          && (content.kind === "common" ? content.applicationId === undefined : content.applicationId === applicationId)) {
          const formatted = formatConfigurationJson(content.payloadJson);
          seed = formatted.valid ? formatted.json : content.payloadJson;
        }
      } else if (firstVersion && value.assignment !== undefined && value.assignment.bindingId === value.context.bindingId
        && configurationVersionMatches(value.assignment, applicationId, configurationId, schemaVersion, value.context.environment)
        && value.assignment.defaultReleaseId === undefined && value.assignment.overrides.length === 0) { seed = "{}\n"; }
      if (seed === undefined || draftOwner.startDraft(seed).kind !== "changed") { setMessage("The saved JSON could not be admitted for this exact source and session. Nothing was copied."); }
      else {
        draftOwner.setIntentDirty(initialDraft !== undefined);
        if (!admitted.current) { admitted.current = true; onDraftAccepted?.(); }
      }
      redraw(value => value + 1);
    });
    return () => { active = false; };
  }, [read.reply, read.busy, read.stale, draftOwner, applicationId, configurationId, schemaVersion, url, revision, initialDraft, baseVersion, firstVersion, onDraftAccepted]);
  useEffect(() => {
    if (autoSave && !automatic.current && admitted.current && savable && raw !== undefined && note.length > 0) {
      automatic.current = true; void save();
    }
  }, [autoSave, savable, raw, note]);
  function change(value: string) {
    if (!editable) { return; }
    if (draftOwner.editDraft(value).kind !== "changed") { setMessage("The previous JSON is retained. Keep the draft within 256 KiB."); return; }
    rawNow.current = value; redraw(count => count + 1);
  }
  function isCurrent(intent: ConfigurationVersionSaveIntent, phase: "publication" | "release") {
    portal.checkExpiry();
    const actual = portal.getSnapshot();
    return alive.current && !actual.blocked && actual.context !== undefined && draftOwner.view.canWrite
      && configurationVersionSaveMatches(intent, currentData.current, portalContextKey(actual.context), phase)
      && (phase === "release" || rawNow.current === intent.raw && noteNow.current === intent.note);
  }
  async function perform(captured: SaveAttempt, knownPublication?: ConfigurationCommandObservation) {
    const intent = captured.intent;
    return executeConfigurationVersionSave(intent, {
      isCurrent: phase => isCurrent(intent, phase),
      preview: async command => {
        const epoch = portal.getSnapshot().epoch;
        previewOwner.selectTarget({ requestKey: url + "#save-preview", applicationId, definitionId: configurationId, schemaVersion });
        await previewOwner.request(async ticket => {
          if (ticket.context === undefined) { return { state: "unauthenticated" }; }
          const response = await fetch("/api/portal/configuration/candidate-preview?pageSize=20", { method: "POST", signal: ticket.signal,
            credentials: "same-origin", cache: "no-store", redirect: "error", headers: { "Content-Type": "application/json", [portalContextHeader]: portalContextKey(ticket.context) }, body: JSON.stringify(command) });
          const body = await response.json() as PortalReply<ConfigurationCandidatePreview>;
          if (body.state === "unauthenticated") { if (portal.getSnapshot().epoch === epoch) { portal.signOut(); } return { state: "unauthenticated" }; }
          if (body.context !== undefined && !portal.observe(body.context, epoch, previewOwner).accepted) { return { state: "unavailable" }; }
          const context = capturePortalContext(body.context);
          return body.state === "ready" && context !== undefined ? { state: "ready", context, value: body.value }
            : { state: body.state === "ready" ? "unavailable" : body.state, ...(context === undefined ? {} : { context }) };
        });
        const result = previewOwner.view.snapshot;
        if (result === undefined || !("draftSha256" in result.value) || !isCurrent(intent, "publication") || portalContextKey(result.context) !== intent.sessionKey) { throw new Error("Save preview is unavailable."); }
        return result.value;
      },
      publish: async (command, ifMatch) => {
        const nativeIntent = { family: "management.configuration.source.publish" as const, target: { sourceId: intent.sourceId } };
        const prepared = portal.receipts.prepare(nativeIntent);
        if (prepared.kind !== "prepared") { throw new Error("The publication original could not be retained."); }
        captured.publicationKey = prepared.receipt.key;
        if (alive.current) { setProgress({ stage: "publication" }); }
        const value = await publication.request(`/api/portal/configuration/sources/${intent.sourceId}/publications`, { method: "POST",
          headers: { "Content-Type": "application/json", "Idempotency-Key": prepared.receipt.key, "If-Match": ifMatch }, body: JSON.stringify(command) }, nativeIntent, prepared.receipt.key);
        if (value === undefined) { throw new Error("The publication original result is unavailable."); }
        return value;
      },
      release: async (command, ifMatch) => {
        const nativeIntent = { family: "management.configuration.release.create" as const, target: { applicationId, configurationId, schemaVersion } };
        const prepared = portal.receipts.prepare(nativeIntent);
        if (prepared.kind !== "prepared") { throw new Error("The saved-version original could not be retained."); }
        captured.releaseKey = prepared.receipt.key;
        if (alive.current) { setProgress({ stage: "release", ...(knownPublication === undefined ? {} : { publication: knownPublication }) }); }
        const value = await release.request(queryUrl(base + "/releases", { schemaVersion }), { method: "POST", headers: {
          "Content-Type": "application/json", "Idempotency-Key": prepared.receipt.key, "If-Match": ifMatch }, body: JSON.stringify(command) }, nativeIntent, prepared.receipt.key);
        if (value === undefined) { throw new Error("The saved-version original result is unavailable."); }
        return value;
      }
    }, knownPublication);
  }
  function acceptProgress(value: ConfigurationVersionSaveProgress, captured: SaveAttempt) {
    const actual = portal.getSnapshot();
    if (!alive.current || actual.context === undefined || actual.blocked || portalContextKey(actual.context) !== captured.intent.sessionKey) { return; }
    setProgress(value);
    if (value.savedReleaseId !== undefined && value.publication !== undefined && value.release !== undefined
      && value.publication.key === captured.publicationKey && value.release.key === captured.releaseKey) {
      const completion: ConfigurationSavedCompletion = { applicationId, configurationId, schemaVersion, sessionKey: captured.intent.sessionKey,
        savedReleaseId: value.savedReleaseId, publication: value.publication, release: value.release, ...(creation === undefined ? {} : { creation }) };
      const keepEditing = rawNow.current !== captured.intent.raw || noteNow.current !== captured.intent.note
        || !configurationOperationClean(value.publication) || !configurationOperationClean(value.release);
      if (!keepEditing) { draftOwner.discardDraft(); }
      onSaved(completion, keepEditing);
    } else { setMessage(value.publication?.disposition === "applied" ? "JSON was saved, but the complete version is not confirmed. It has not been deployed. Recover or continue this exact Save below."
      : "Save is not confirmed. Keep the original reference; nothing was deployed."); }
  }
  async function save() {
    portal.checkExpiry();
    const context = portal.getSnapshot().context, value = currentData.current;
    if (pending.current || !savable || context === undefined || value === undefined || rawNow.current === undefined) { return; }
    pending.current = true; setBusy(true); onBusy?.(true); setMessage(undefined);
    try {
      const intent = await captureConfigurationVersionSave(value, rawNow.current, noteNow.current, portalContextKey(context), baseVersion?.release ?? "application-only");
      const captured: SaveAttempt = { intent }; attempt.current = captured;
      acceptProgress(await perform(captured), captured);
    } catch (error) {
      if (alive.current) {
        if (error instanceof ConfigurationVersionSaveInterrupted && attempt.current !== undefined) { acceptProgress(error.progress, attempt.current); }
        setMessage("Save did not confirm every stage. Keep its exact references and recover the original result; no automatic replacement was submitted.");
      }
    } finally { pending.current = false; onBusy?.(false); if (alive.current) { setBusy(false); redraw(count => count + 1); } }
  }
  async function continueSave() {
    const captured = attempt.current, known = publication.owner.view.snapshot?.value;
    if (pending.current || captured === undefined || known === undefined || known.key !== captured.publicationKey || captured.releaseKey !== undefined || !configurationOperationClean(known)
      || !recoveryReady || !draftOwner.view.canWrite) { return; }
    pending.current = true; setBusy(true); onBusy?.(true);
    try {
      let selected = captured;
      if (captured.previousRelease !== undefined && captured.dismissedReleaseKey !== undefined) {
        const actual = portal.getSnapshot(), editor = currentData.current;
        if (actual.context === undefined || actual.blocked || editor === undefined) { return; }
        const intent = rebindConfigurationVersionRelease(captured.intent, editor, portalContextKey(actual.context), known, captured.previousRelease, captured.dismissedReleaseKey);
        const epoch = actual.epoch;
        previewOwner.selectTarget({ requestKey: url + "#release-preview", applicationId, definitionId: configurationId, schemaVersion });
        await previewOwner.request(async ticket => {
          if (ticket.context === undefined) { return { state: "unauthenticated" }; }
          const response = await fetch("/api/portal/configuration/preview?pageSize=20", { method: "POST", signal: ticket.signal, credentials: "same-origin", cache: "no-store", redirect: "error",
            headers: { "Content-Type": "application/json", [portalContextHeader]: portalContextKey(ticket.context) }, body: JSON.stringify(configurationVersionReleasePreview(intent, known)) });
          const body = await response.json() as PortalReply<ConfigurationPreview>;
          if (body.state === "unauthenticated") { if (portal.getSnapshot().epoch === epoch) { portal.signOut(); } return { state: "unauthenticated" }; }
          if (body.context !== undefined && !portal.observe(body.context, epoch, previewOwner).accepted) { return { state: "unavailable" }; }
          const context = capturePortalContext(body.context);
          return body.state === "ready" && context !== undefined ? { state: "ready", context, value: body.value }
            : { state: body.state === "ready" ? "unavailable" : body.state, ...(context === undefined ? {} : { context }) };
        });
        const preview = previewOwner.view.snapshot;
        if (preview === undefined || "draftSha256" in preview.value || portalContextKey(preview.context) !== intent.sessionKey || !isCurrent(intent, "release")) { return; }
        acceptConfigurationVersionReleasePreview(intent, known, preview.value);
        selected = { ...captured, intent }; attempt.current = selected;
      }
      if (!isCurrent(selected.intent, "release")) { return; }
      acceptProgress(await perform(selected, known), selected);
    }
    catch (error) { if (alive.current) { if (error instanceof ConfigurationVersionSaveInterrupted) { setProgress(error.progress); } setMessage("The remaining Save stage is not confirmed. Recover its exact original result."); } }
    finally { pending.current = false; onBusy?.(false); if (alive.current) { setBusy(false); redraw(count => count + 1); } }
  }
  async function recoverOriginal(receipt: (typeof originals)[number]) {
    const owner = receipt.family === "management.configuration.source.publish" ? publication : release;
    const path = receipt.family === "management.configuration.source.publish" ? `/api/portal/configuration/sources/${receipt.target.sourceId}/publication-operations/${receipt.key}`
      : queryUrl(base + `/release-operations/${receipt.key}`, { schemaVersion });
    if (pending.current || !recoveryReady) { return; }
    draftOwner.selectTarget({ requestKey: url, applicationId, definitionId: configurationId, schemaVersion });
    pending.current = true; setBusy(true); onBusy?.(true);
    try {
      await owner.recover(receipt, path);
      const currentObservation = owner.owner.view.snapshot?.value;
      if (currentObservation?.transport.status === 403 || currentObservation?.transport.status === 404) { draftOwner.permissionLost(); }
      const captured = attempt.current, pub = publication.owner.view.snapshot?.value, rel = release.owner.view.snapshot?.value;
      if (captured !== undefined && pub !== undefined && rel !== undefined && pub.key === captured.publicationKey && rel.key === captured.releaseKey) {
        const id = configurationVersionSavedReleaseId(captured.intent, pub, rel);
        if (id !== undefined) { acceptProgress({ stage: "saved", publication: pub, release: rel, savedReleaseId: id }, captured); }
      }
    } catch { if (alive.current) { setMessage("The original result is unavailable. Its exact reference is retained."); } }
    finally { pending.current = false; onBusy?.(false); if (alive.current) { setBusy(false); redraw(count => count + 1); } }
  }
  return <section className="card configuration-version-editor" aria-label="Edit new version"><div className="configuration-version-heading"><h2>Edit a new version</h2><span className="badge warning">Unsaved changes</span></div>
    <ReadNotice state={read} />{message === undefined ? null : <p className="notice" role="status">{message}</p>}
    {raw === undefined ? <p>Current JSON editing context is unavailable. Original-result recovery remains separate below.</p> : <>
      <div className="configuration-json-toolbar"><label htmlFor="configuration-version-json">Application JSON</label><button disabled={!editable} onClick={() => { const result = formatConfigurationJson(raw); if (result.valid) { change(result.json); } else { setMessage(result.message); } }}>Format JSON</button></div>
      <textarea id="configuration-version-json" className="configuration-json" spellCheck={false} value={raw} disabled={!editable} onChange={event => change(event.target.value)} />
      <p className="muted">{configurationDraftBytes(raw)} / 262144 UTF-8 bytes</p><label htmlFor="configuration-version-note">Version note</label><input id="configuration-version-note" value={note} maxLength={1024} disabled={!editable}
        onChange={event => { noteNow.current = event.target.value; setNote(event.target.value); draftOwner.setIntentDirty(true); }} />
      <div className="configuration-save-actions"><p>Saving does not deploy this version.</p><button disabled={busy} onClick={onCancel}>Cancel edits</button><button className="primary" disabled={!savable || note.length === 0} onClick={() => { void save(); }}>{busy ? "Saving…" : "Save version"}</button></div>
    </>}
    {progress?.stage !== "saved" ? null : <p className="notice" role="status">Version saved. Current local edits and request cleanup remain separate facts below.</p>}
    {originals.length === 0 ? null : <details className="configuration-recovery" open={progress?.stage !== "saved"}><summary>Save progress and original results</summary><p>Incomplete Save is not a deployable version. Recovery reads the same original keys and never republishes.</p>
      {originals.map(receipt => { const owner = receipt.family === "management.configuration.source.publish" ? publication : release; return <section key={receipt.key}>
        {owner.observation?.key === receipt.key ? <ConfigurationOperationStatus value={owner.observation} /> : <p>Original result has not been recovered here.</p>}
        <div className="metadata-actions"><button disabled={busy || !recoveryReady || !owner.canRecover(receipt)} onClick={() => { void recoverOriginal(receipt); }}>Recover original Save result</button>
          <button disabled={busy || owner.observation?.key !== receipt.key || !resolvedConfigurationOriginal(owner.observation)} onClick={() => {
            const refused = owner.observation, captured = attempt.current;
            if (owner.dismiss(receipt)) {
              if (receipt.family === "management.configuration.release.create" && captured?.releaseKey === receipt.key && refused !== undefined
                && ["rejected", "conflict", "notCommitted"].includes(refused.disposition)) {
                attempt.current = { intent: captured.intent, ...(captured.publicationKey === undefined ? {} : { publicationKey: captured.publicationKey }), previousRelease: refused, dismissedReleaseKey: receipt.key };
                setMessage("The refused Save stage was acknowledged. Refresh current state, then continue the original version without republishing its JSON.");
              }
              refresh(count => count + 1);
            }
          }}>Dismiss resolved reference</button></div></section>; })}
      {attempt.current?.previousRelease === undefined ? null : <ConfigurationOperationStatus value={attempt.current.previousRelease} />}
      {attempt.current?.publicationKey === undefined || attempt.current.releaseKey !== undefined ? null : <button disabled={busy || !configurationOperationClean(publication.observation)} onClick={() => { void continueSave(); }}>Continue incomplete Save</button>}
    </details>}
    <TechnicalDetails fields={[{ label: "Application ID", value: applicationId }, { label: "Configuration ID", value: configurationId }, { label: "Schema", value: schemaVersion },
      { label: "Source ID", value: sourceId }, { label: "Binding ID", value: data?.context.bindingId }, { label: "Saved Shared base", value: baseVersion?.release.commonSource.publicationId }]} />
  </section>;
}
function matches(value: ConfigurationEditorSnapshot, app: string, configuration: string, schema: string) {
  return value.context.applicationId === app && value.context.configurationId === configuration && value.context.schemaVersion === schema && value.context.selectedSource === "application";
}
function sameBinding(left: ConfigurationEditorSnapshot, right: ConfigurationEditorSnapshot) {
  return left.context.bindingId === right.context.bindingId && left.context.environment === right.context.environment && left.context.hostApplicationName === right.context.hostApplicationName
    && left.context.commonSource.state === right.context.commonSource.state && left.context.commonSource.sourceId === right.context.commonSource.sourceId
    && left.context.applicationSource.state === right.context.applicationSource.state && left.context.applicationSource.sourceId === right.context.applicationSource.sourceId;
}
function allowed(value: ConfigurationEditorSnapshot, resource: "source" | "binding", name: string) {
  return value.actions.state === "available" && value.actions[resource].resourceId === (resource === "source" ? value.source?.sourceId : value.context.bindingId)
    && value.actions[resource].actions.some(item => item.operationId === name && item.supported && item.allowed);
}
