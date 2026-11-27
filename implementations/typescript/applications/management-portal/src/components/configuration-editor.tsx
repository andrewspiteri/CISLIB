"use client";

import { useEffect, useRef, useState } from "react";
import type { ConfigurationCandidatePreview, ConfigurationPreview, ConfigurationPublication, ConfigurationSourceReference,
  ConfigurationValidationResult } from "@cislib/management-client";
import { computeConfigurationPayloadSha256, isUuid } from "@cislib/management-client";
import type { ConfigurationEditorSnapshot } from "../configuration-editor-model.ts";
import type { ConfigurationInitialDraft } from "../configuration-creation-model.ts";
import { configurationEditingAccess, initializeConfigurationEditorDraft } from "../configuration-editing.ts";
import { resolvedConfigurationOriginal } from "../configuration-command-results.ts";
import type { ConfigurationCommandIntent, ConfigurationCommandObservation } from "../configuration-command-results.ts";
import type { CommandReceipt } from "../command-receipts.ts";
import { captureConfigurationDraft, captureConfigurationReason, configurationDraftBytes, formatConfigurationJson } from "../configuration-draft.ts";
import { configurationReadTarget, recoverConfigurationOriginal } from "../configuration-recovery.ts";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../portal-context.ts";
import type { PortalReply } from "../portal-model.ts";
import { ProtectedStateOwner } from "../protected-state.ts";
import type { ProtectedTarget } from "../protected-state.ts";
import { applicationRoute, configurationRoute } from "../portal-routes.ts";
import type { ListContext } from "./application-detail";
import { ConfigurationHistory } from "./configuration-history";
import { useProtectedPortal } from "./protected-context";
import { Badge, ReadNotice, Shell, TechnicalDetails, queryUrl, ready, reportedLabel, usePortalRead } from "./shared";

type SourceKind = "common" | "application";
interface Pair { readonly common?: ConfigurationSourceReference; readonly application?: ConfigurationSourceReference; }
interface Review {
  readonly scope: string; readonly mode: "candidate" | "references"; readonly preview: ConfigurationCandidatePreview | ConfigurationPreview;
  readonly bindingId: string; readonly environment: string; readonly hostApplicationName: string; readonly sourceId?: string;
  readonly common: ConfigurationSourceReference; readonly application: ConfigurationSourceReference;
  readonly assignmentETag: string; readonly reason: string; readonly raw?: string; readonly sourceETag?: string; readonly sha256?: string;
}
type Analysis = { readonly kind: "validation"; readonly result: ConfigurationValidationResult } | { readonly kind: "review"; readonly review: Review };

export function ConfigurationEditor({ applicationId, configurationId, schemaVersion, source, context, initialPublicationId, initialDraft, onPublicationCommitted }: {
  applicationId: string; configurationId: string; schemaVersion: string; source: SourceKind; context: ListContext; initialPublicationId?: string;
  initialDraft?: ConfigurationInitialDraft; onPublicationCommitted?(): void
}) {
  const { owner: portal, snapshot: session } = useProtectedPortal();
  const [draftOwner] = useState(() => new ProtectedStateOwner<ConfigurationEditorSnapshot>({ context: session.context, receipts: portal.receipts }));
  const [analysisOwner] = useState(() => new ProtectedStateOwner<Analysis>({ context: session.context, receipts: portal.receipts }));
  const [publicationOwner] = useState(() => new ProtectedStateOwner<ConfigurationCommandObservation>({ context: session.context, receipts: portal.receipts }));
  const [releaseOwner] = useState(() => new ProtectedStateOwner<ConfigurationCommandObservation>({ context: session.context, receipts: portal.receipts }));
  const [revision, refresh] = useState(0), [, redraw] = useState(0);
  const [selection, setSelection] = useState<{ scope: string; pair: Pair }>({ scope: "", pair: {} });
  const [reasonState, setReason] = useState({ scope: "", text: "" });
  const [history, openHistory] = useState<SourceKind>();
  const [applicationHistoryOpen, setApplicationHistoryOpen] = useState(false);
  const [reviewSetup, setReviewSetup] = useState<{ scope: string; mode: "candidate" | "references" }>();
  const [existingReference, setExistingReference] = useState<{ scope: string; publicationId: string }>();
  const [advanced, advance] = useState<string>();
  const [message, setMessage] = useState<string>();
  const [busy, setBusy] = useState(false);
  const pending = useRef(false), alive = useRef(true), serial = useRef(0);
  const initialSelection = useRef<string | undefined>(undefined);
  const appliedRead = useRef<{ reply: unknown; revision: number; url: string } | undefined>(undefined);
  const reviewHeading = useRef<HTMLHeadingElement>(null);
  const announcedPublication = useRef<string | undefined>(undefined);
  const sessionKey = session.context === undefined ? undefined : portalContextKey(session.context);
  const scope = JSON.stringify([applicationId, configurationId, schemaVersion, source, sessionKey]);
  const base = `/api/portal/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}`;
  const url = queryUrl(base + "/editor", { schemaVersion, source });
  const read = usePortalRead<ConfigurationEditorSnapshot>(url, revision);
  const initialPublication = usePortalRead<ConfigurationPublication>(initialPublicationId === undefined ? null
    : queryUrl(`${base}/publication/${initialPublicationId}`, { schemaVersion, source }));
  const snapshot = draftOwner.view.snapshot;
  const incoming = ready(read);
  const incomingContext = read.reply?.state === "ready" ? capturePortalContext(read.reply.context) : undefined;
  const incomingAdmissible = read.reply === undefined || read.reply.state === "unavailable" || incoming !== undefined
    && incomingContext !== undefined && portalContextKey(incomingContext) === sessionKey
    && tuple(incoming, applicationId, configurationId, schemaVersion, source);
  const data = snapshot !== undefined && sessionKey !== undefined && portalContextKey(snapshot.context) === sessionKey
    && incomingAdmissible
    && snapshot.target.requestKey === url && snapshot.target.applicationId === applicationId && snapshot.target.definitionId === configurationId
    && snapshot.target.schemaVersion === schemaVersion && tuple(snapshot.value, applicationId, configurationId, schemaVersion, source)
    && (incoming === undefined || sameBinding(incoming, snapshot.value)
      && incoming.context.defaults.state === snapshot.value.context.defaults.state
      && (incoming.sharedOverrideSeed?.state !== "redacted" || snapshot.value.sharedOverrideSeed?.state === "redacted")
      && (incoming.content.state !== "unavailable" || snapshot.value.content.state === "unavailable")) ? snapshot.value : undefined;
  const raw = data === undefined ? undefined : draftOwner.view.draft?.text;
  const pair = selection.scope === scope ? selection.pair : {};
  const reason = reasonState.scope === scope ? reasonState.text : "";
  const sourceId = data === undefined ? undefined : selectedSource(data);
  const receipts = portal.receipts.load();
  const originals = receipts.kind !== "ready" ? [] : receipts.receipts.filter(item => item.family === "management.configuration.release.create"
    ? item.target.applicationId === applicationId && item.target.configurationId === configurationId && item.target.schemaVersion === schemaVersion
    : item.family === "management.configuration.source.publish" && (data === undefined || item.target.sourceId === data.context.commonSource.sourceId || item.target.sourceId === data.context.applicationSource.sourceId));
  const publication = currentOriginal(publicationOwner, sessionKey, "management.configuration.source.publish");
  const released = currentOriginal(releaseOwner, sessionKey, "management.configuration.release.create");
  const publicationForSource = publication !== undefined && "sourceId" in publication.target && publication.target.sourceId === sourceId ? publication : undefined;
  const published = publicationForSource?.disposition === "applied" && publicationForSource.decision?.publicationId !== undefined ? publicationForSource : undefined;
  const isAdvanced = advanced === scope && published !== undefined;
  const selectedExisting = existingReference?.scope === scope && pair.application?.state === "present"
    && pair.application.publicationId === existingReference.publicationId;
  const releaseForBinding = released !== undefined && "applicationId" in released.target && "configurationId" in released.target && "schemaVersion" in released.target && released.target.applicationId === applicationId
    && released.target.configurationId === configurationId && released.target.schemaVersion === schemaVersion ? released : undefined;
  const liveAnalysis = analysisOwner.view.snapshot;
  const analysis = data !== undefined && liveAnalysis !== undefined && sessionKey !== undefined && portalContextKey(liveAnalysis.context) === sessionKey
    && liveAnalysis.target.applicationId === applicationId && liveAnalysis.target.definitionId === configurationId && liveAnalysis.target.schemaVersion === schemaVersion
    ? liveAnalysis.value : undefined;
  const review = analysis?.kind === "review" && analysis.review.scope === scope && data !== undefined
    && analysis.review.bindingId === data.context.bindingId && analysis.review.environment === data.context.environment
    && analysis.review.hostApplicationName === data.context.hostApplicationName
    && (analysis.review.mode === "references" || analysis.review.sourceId === sourceId) ? analysis.review : undefined;
  const current = data !== undefined && draftOwner.view.canWrite && !read.busy && !read.stale && !busy && !session.blocked;
  const { canEdit, canPublish } = configurationEditingAccess(data, { ownerState: draftOwner.view.state, blocked: session.blocked,
    busy, fresh: current, originalsRetained: originals.length > 0, receiptStorageReady: receipts.kind === "ready" });
  const canRelease = source === "application" && current && allowed(data, "binding", "createConfigurationRelease") && allowed(data, "binding", "previewConfiguration")
    && originals.every(item => isAdvanced && item.family === "management.configuration.source.publish" && item.key === published?.key);
  const canSelectReferences = current && (originals.length === 0 || isAdvanced && originals.every(item => item.family === "management.configuration.source.publish" && item.key === published?.key));
  const back = applicationRoute({ applicationId, applicationName: data?.context.application, tab: "configuration", context });
  const recoveryReady = !read.busy && !initialPublication.busy && appliedRead.current !== undefined && appliedRead.current.reply === read.reply
    && appliedRead.current.revision === revision && appliedRead.current.url === url;

  useEffect(() => { const owners = [draftOwner, analysisOwner, publicationOwner, releaseOwner].map(owner => portal.register(owner)); return () => owners.forEach(dispose => dispose()); }, [portal, draftOwner, analysisOwner, publicationOwner, releaseOwner]);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => { setSelection({ scope, pair: {} }); setReason({ scope, text: "" }); advance(undefined); openHistory(undefined); setApplicationHistoryOpen(false); setReviewSetup(undefined); setExistingReference(undefined); setMessage(undefined); }, [scope]);
  useEffect(() => {
    let active = true;
    if (read.busy) { return; }
    if (read.reply === undefined) { appliedRead.current = { reply: read.reply, revision, url }; redraw(count => count + 1); return; }
    if (read.reply.state !== "ready") {
      if (read.reply.state === "unavailable") { draftOwner.transientUnavailable(); }
      else {
        draftOwner.permissionLost(); analysisOwner.selectTarget(undefined); setSelection({ scope, pair: {} });
        setReason({ scope, text: "" }); openHistory(undefined); setExistingReference(undefined); setReviewSetup(undefined);
      }
      appliedRead.current = { reply: read.reply, revision, url }; redraw(value => value + 1); return;
    }
    if (read.stale) { draftOwner.transientUnavailable(); appliedRead.current = { reply: read.reply, revision, url }; redraw(value => value + 1); return; }
    const value = read.reply.value, actual = capturePortalContext(read.reply.context);
    if (actual === undefined || !tuple(value, applicationId, configurationId, schemaVersion, source)) {
      draftOwner.permissionLost(); analysisOwner.selectTarget(undefined); setSelection({ scope, pair: {} });
      setReason({ scope, text: "" }); openHistory(undefined); setExistingReference(undefined); setReviewSetup(undefined); appliedRead.current = { reply: read.reply, revision, url }; redraw(count => count + 1); return;
    }
    const previous = draftOwner.view.snapshot?.value;
    if (previous !== undefined && !sameBinding(previous, value)) {
      draftOwner.discardDraft(); setSelection({ scope, pair: {} }); analysisOwner.selectTarget(undefined); openHistory(undefined); setExistingReference(undefined); setReviewSetup(undefined);
    }
    if (value.context.defaults.state !== "available" || value.content.state === "unavailable" || value.sharedOverrideSeed?.state === "redacted") {
      draftOwner.discardDraft(); analysisOwner.selectTarget(undefined); openHistory(undefined);
    }
    draftOwner.setContext(actual);
    draftOwner.selectTarget(configurationReadTarget({ applicationId, configurationId, schemaVersion, view: source }));
    void draftOwner.request(async () => ({ state: "ready", context: actual, value })).then(() => {
      if (!active) { return; }
      if (source === "application") {
        const initialized = initializeConfigurationEditorDraft(draftOwner, initialDraft, portalContextKey(actual));
        if (initialized.kind === "blocked") { setMessage(initialized.message); }
      }
      setSelection(previousSelection => previousSelection.scope === scope && (previousSelection.pair.common !== undefined || previousSelection.pair.application !== undefined)
        ? previousSelection : { scope, pair: value.baselineRelease === undefined ? {} : { common: value.baselineRelease.commonSource, application: value.baselineRelease.applicationSource } });
      appliedRead.current = { reply: read.reply, revision, url }; redraw(count => count + 1);
    });
    return () => { active = false; };
  }, [read.reply, read.busy, read.stale, draftOwner, analysisOwner, applicationId, configurationId, schemaVersion, source, url, scope, initialDraft]);
  useEffect(() => { if (review !== undefined) { reviewHeading.current?.focus(); } }, [review]);
  useEffect(() => {
    if (published?.decision?.publicationId !== undefined && review?.mode === "candidate" && review.sourceId === sourceId
      && review.raw === raw && review.sha256 !== undefined && published.decision.sha256 === review.sha256 && announcedPublication.current !== published.key) {
      announcedPublication.current = published.key; onPublicationCommitted?.();
    }
  }, [published, review, sourceId, raw, onPublicationCommitted]);
  useEffect(() => {
    const value = ready(initialPublication);
    const key = scope + "/" + initialPublicationId;
    if (value === undefined || data === undefined || !canSelectReferences || initialPublication.busy || initialPublication.stale || initialSelection.current === key) { return; }
    initialSelection.current = key;
    chooseReference(source, { state: "present", publicationId: value.publicationId }, value);
  }, [initialPublication.reply, initialPublication.busy, initialPublication.stale, data, canSelectReferences, scope, initialPublicationId]);

  function invalidateReview() { analysisOwner.selectTarget(undefined); setMessage(undefined); }
  function markIntent() { draftOwner.setIntentDirty(true); redraw(value => value + 1); }
  function changeRaw(value: string) {
    if (!canEdit) { return; }
    const changed = draftOwner.editDraft(value);
    if (changed.kind !== "changed") { throw new TypeError("The exact draft must remain within 256 KiB."); }
    setExistingReference(undefined); setReviewSetup(undefined); invalidateReview(); redraw(count => count + 1);
  }
  function formatDraft() {
    if (!canEdit || raw === undefined) { return; }
    const formatted = formatConfigurationJson(raw);
    if (!formatted.valid) { setMessage(formatted.message); return; }
    changeRaw(formatted.json);
  }
  function requestReview(mode: "candidate" | "references") {
    if (mode === "candidate" ? !canPublish || raw === undefined : !canRelease) { return; }
    if (pair.common === undefined || pair.application === undefined) {
      setReviewSetup({ scope, mode }); setMessage(undefined); return;
    }
    void inspect(mode);
  }
  function firstSharedReference(reference: ConfigurationSourceReference, publicationValue?: ConfigurationPublication) {
    if (!canSelectReferences || data === undefined || data.baselineRelease !== undefined
      || publicationValue !== undefined && !publicationMatches(publicationValue, data, "common")) { return; }
    chooseReference("common", reference, publicationValue);
    setSelection(previous => ({ scope, pair: { ...(previous.scope === scope ? previous.pair : {}), common: reference,
      application: previous.scope === scope && previous.pair.application !== undefined ? previous.pair.application : { state: "absent" } } }));
  }
  function chooseFirstSharedPublication() {
    if (!canSelectReferences || data?.baselineRelease !== undefined) { return; }
    setSelection(previous => ({ scope, pair: previous.scope === scope && previous.pair.application !== undefined ? { application: previous.pair.application } : {} }));
    markIntent(); invalidateReview(); openHistory("common");
  }
  function chooseReference(kind: SourceKind, reference: ConfigurationSourceReference, publicationValue?: ConfigurationPublication) {
    if (!canSelectReferences || data === undefined) { return; }
    if (publicationValue !== undefined && !publicationMatches(publicationValue, data, kind)) { setMessage("This publication does not match the exact selected binding and source."); return; }
    if (kind === source && canPublish) {
      if (draftOwner.view.draft !== undefined && draftOwner.view.navigationBlocked && !window.confirm("Replace the current source draft with this explicit selection?")) { return; }
      draftOwner.discardDraft();
      const text = publicationValue?.payloadJson ?? "{}";
      const started = draftOwner.startDraft(text);
      if (started.kind !== "changed") { setMessage("The selected source cannot become an editable draft."); return; }
    }
    setSelection(previous => ({ scope, pair: { ...(previous.scope === scope ? previous.pair : {}), [kind]: reference } }));
    if (kind === "application") { setExistingReference(publicationValue === undefined ? undefined : { scope, publicationId: publicationValue.publicationId }); setReviewSetup(undefined); }
    markIntent(); invalidateReview();
  }
  function leave() {
    if (draftOwner.view.navigationBlocked && !window.confirm("Discard this Configuration draft and return to the list?")) { return; }
    draftOwner.discardDraft(); window.location.assign(back);
  }
  async function request<T>(owner: ProtectedStateOwner<T>, path: string, init: RequestInit, requestTarget: ProtectedTarget, recoverySignal?: AbortSignal) {
    const epoch = portal.getSnapshot().epoch;
    owner.selectTarget(requestTarget);
    return owner.request(async ticket => {
      const headers = new Headers(init.headers);
      if (ticket.context !== undefined) { headers.set(portalContextHeader, portalContextKey(ticket.context)); }
      const response = await fetch(path, { ...init, headers, signal: recoverySignal === undefined ? ticket.signal : AbortSignal.any([ticket.signal, recoverySignal]),
        credentials: "same-origin", cache: "no-store", redirect: "error" });
      const body = await response.json() as PortalReply<T>;
      if (body.state === "unauthenticated") { if (portal.getSnapshot().epoch === epoch) { portal.signOut(); } return { state: "unauthenticated" }; }
      if (body.context !== undefined && !portal.observe(body.context, epoch, owner).accepted) { return { state: "unavailable" }; }
      if (body.state === "ready") {
        const actual = capturePortalContext(body.context); if (actual === undefined) { return { state: "unauthenticated" }; }
        return { state: "ready", context: actual, value: body.value };
      }
      if (body.state === "denied" || body.state === "not-found") { draftOwner.permissionLost(); openHistory(undefined); }
      return { state: body.state, ...(body.context === undefined ? {} : { context: body.context }) };
    });
  }
  async function inspect(mode: "validate" | "candidate" | "references") {
    if (pending.current || data === undefined || !current) { return; }
    if (mode !== "validate" && (pair.common === undefined || pair.application === undefined || data.assignment === undefined || reason.trim() === "")) {
      setMessage("Choose both exact source references or deliberate absence, and provide a reason before review."); return;
    }
    if (mode !== "references" && (raw === undefined || sourceId === undefined || !canPublish)) { return; }
    if (mode !== "validate") { setReviewSetup(undefined); }
    pending.current = true; setBusy(true); setMessage(undefined);
    const requestTarget = target(url + "#analysis-" + (++serial.current), data);
    analysisOwner.selectTarget(requestTarget);
    const epoch = portal.getSnapshot().epoch;
    try {
      const capturedReason = mode === "validate" ? reason : captureConfigurationReason(reason);
      const common = pair.common, application = pair.application, assignmentETag = data.assignment?.eTag;
      const command = mode === "validate" ? { commandVersion: 1, sourceId, payloadJson: captureConfigurationDraft(raw!) }
        : mode === "candidate" ? { commandVersion: 1, applicationId, configurationId, schemaVersion, selectedSource: source,
          sourceId, sourceETag: data.source!.eTag, assignmentETag, commonSource: common, applicationSource: application, payloadJson: captureConfigurationDraft(raw!) }
          : { commandVersion: 1, applicationId, configurationId, schemaVersion, commonSource: common, applicationSource: application };
      const path = "/api/portal/configuration/" + (mode === "validate" ? "validate" : mode === "candidate" ? "candidate-preview" : "preview");
      let incomplete = false;
      await analysisOwner.request(async ticket => {
          if (ticket.context === undefined) { return { state: "unauthenticated" }; }
          const sha256 = mode === "candidate" ? await computeConfigurationPayloadSha256(raw!, {}, ticket.signal) : undefined;
          const response = await fetch(path + (mode === "validate" ? "" : "?pageSize=20"), { ...json(command), headers: { "Content-Type": "application/json", [portalContextHeader]: portalContextKey(ticket.context!) },
            signal: ticket.signal, credentials: "same-origin", cache: "no-store", redirect: "error" });
          const body = await response.json() as PortalReply<ConfigurationCandidatePreview | ConfigurationPreview | ConfigurationValidationResult>;
          if (body.state === "unauthenticated") { if (portal.getSnapshot().epoch === epoch) { portal.signOut(); } return { state: "unauthenticated" }; }
          if (body.context !== undefined && !portal.observe(body.context, epoch, analysisOwner).accepted) { return { state: "unavailable" }; }
          const actual = capturePortalContext(body.context);
          if (body.state !== "ready" || actual === undefined) {
            if (body.state === "denied" || body.state === "not-found") { draftOwner.permissionLost(); openHistory(undefined); }
            return { state: body.state === "ready" ? "unavailable" : body.state, ...(actual === undefined ? {} : { context: actual }) };
          }
          if (mode === "validate") { return { state: "ready", context: actual, value: { kind: "validation", result: body.value as ConfigurationValidationResult } }; }
          const result = body.value as ConfigurationCandidatePreview | ConfigurationPreview;
          if (result.applicationId !== applicationId || result.configurationId !== configurationId || result.schemaVersion !== schemaVersion
            || result.currentAssignment.bindingId !== data.context.bindingId || result.currentAssignment.environment !== data.context.environment
            || result.currentAssignment.eTag !== assignmentETag || result.commonSource?.state !== common!.state
            || result.applicationSource?.state !== application!.state || result.commonSource?.publicationId !== common!.publicationId
            || result.applicationSource?.publicationId !== application!.publicationId) { throw new TypeError("Unrelated preview."); }
          if (mode === "candidate" && (!("bindingId" in result) || result.bindingId !== data.context.bindingId || result.environment !== data.context.environment
            || result.hostApplicationName !== data.context.hostApplicationName || result.sourceId !== sourceId || result.sourceETag !== data.source!.eTag
            || result.assignmentETag !== assignmentETag || result.draftSha256 !== sha256)) { throw new TypeError("Unrelated candidate preview."); }
          incomplete = !result.structurallyValid || result.redacted;
          const captured: Review = { scope, mode: mode === "candidate" ? "candidate" : "references", preview: result, common: common!, application: application!,
            bindingId: data.context.bindingId, environment: data.context.environment, hostApplicationName: data.context.hostApplicationName,
            assignmentETag: assignmentETag!, reason: capturedReason, ...(mode === "candidate" ? { sourceId: sourceId!, raw: raw!, sourceETag: data.source!.eTag, sha256: sha256! } : {}) };
          return { state: "ready", context: actual, value: { kind: "review", review: captured } };
        });
      if (incomplete) { setMessage("Review is incomplete: structural issues or protected effective impact prevent confirmation. Consumer acceptance is not established."); }
    } catch { setMessage("The draft or its native validation/preview is unavailable. Keep the draft, refresh current state and review again."); }
    finally { pending.current = false; if (alive.current) { setBusy(false); redraw(value => value + 1); } }
  }

  async function submit(family: "publication" | "release") {
    portal.checkExpiry();
    if (pending.current || review === undefined || data === undefined || sessionKey === undefined || portal.getSnapshot().context === undefined
      || portalContextKey(portal.getSnapshot().context!) !== sessionKey || !draftOwner.view.canWrite
      || !review.preview.structurallyValid || review.preview.redacted || review.scope !== scope) { return; }
    if (family === "publication" && (review.mode !== "candidate" || !canPublish || review.raw !== raw || review.sourceId !== sourceId || review.sourceETag !== data.source?.eTag)) { return; }
    if (family === "release" && (review.mode !== "references" || !canRelease || review.assignmentETag !== data.assignment?.eTag)) { return; }
    const intent: ConfigurationCommandIntent = family === "publication" ? { family: "management.configuration.source.publish", target: { sourceId: sourceId! } }
      : { family: "management.configuration.release.create", target: { applicationId, configurationId, schemaVersion } };
    const prepared = portal.receipts.prepare(intent);
    if (prepared.kind !== "prepared") { setMessage("The original reference could not be retained. Nothing was submitted."); return; }
    const path = family === "publication" ? `/api/portal/configuration/sources/${sourceId}/publications` : queryUrl(base + "/releases", { schemaVersion });
    const command = family === "publication" ? { commandVersion: 1, payloadJson: review.raw!, reason: review.reason }
      : { commandVersion: 1, commonSource: review.common, applicationSource: review.application, reason: review.reason };
    pending.current = true; setBusy(true); setMessage(undefined);
    try {
      await request(family === "publication" ? publicationOwner : releaseOwner, path,
        { ...json(command), headers: { "Content-Type": "application/json", "Idempotency-Key": prepared.receipt.key,
          "If-Match": family === "publication" ? review.sourceETag! : review.assignmentETag } }, { ...target(path + "#submit", data) });
      const observed = (family === "publication" ? publicationOwner : releaseOwner).view.snapshot?.value;
      if (observed?.transport.status === 403 || observed?.transport.status === 404) { draftOwner.permissionLost(); analysisOwner.selectTarget(undefined); openHistory(undefined); }
      else { draftOwner.transientUnavailable(); }
    } catch { draftOwner.transientUnavailable(); setMessage("The response is not confirmed. Recover the same original reference; nothing will be resubmitted automatically."); }
    finally { pending.current = false; if (alive.current) { setBusy(false); redraw(value => value + 1); } }
  }
  async function recover(receipt: CommandReceipt) {
    const actualSession = portal.getSnapshot().context;
    const actualKey = actualSession === undefined ? undefined : portalContextKey(actualSession);
    const knownRelease = currentOriginal(releaseOwner, actualKey, "management.configuration.release.create");
    const knownPublication = currentOriginal(publicationOwner, actualKey, "management.configuration.source.publish");
    if (pending.current || !recoveryReady || portal.receipts.recoveryBusy || resolvedConfigurationOriginal(knownRelease)
      || resolvedConfigurationOriginal(knownPublication) && !isAdvanced) { return; }
    const isPublication = receipt.family === "management.configuration.source.publish";
    if (!isPublication && receipt.family !== "management.configuration.release.create") { return; }
    const path = isPublication ? `/api/portal/configuration/sources/${receipt.target.sourceId}/publication-operations/${encodeURIComponent(receipt.key)}`
      : queryUrl(base + `/release-operations/${encodeURIComponent(receipt.key)}`, { schemaVersion });
    const owner = isPublication ? publicationOwner : releaseOwner;
    const requestTarget: ProtectedTarget = { requestKey: path + "#lookup", ...(isPublication ? {} : { applicationId, definitionId: configurationId, schemaVersion }) };
    pending.current = true; setBusy(true); setMessage(undefined);
    try {
      await recoverConfigurationOriginal(draftOwner, owner, portal.receipts, receipt, { applicationId, configurationId, schemaVersion, view: source }, recoveryReady,
        async (lookupPath, _original, signal) => { await request(owner, lookupPath, { method: "GET" }, requestTarget, signal); });
      const observed = owner.view.snapshot?.value;
      if (observed?.transport.status === 403 || observed?.transport.status === 404) { draftOwner.permissionLost(); analysisOwner.selectTarget(undefined); openHistory(undefined); }
    }
    catch { setMessage("The original outcome is unavailable. Retain this reference and recover later; no replacement was submitted."); }
    finally { pending.current = false; if (alive.current) { setBusy(false); redraw(value => value + 1); } }
  }
  function usePublished() {
    if (published?.decision?.publicationId === undefined || !isUuid(published.decision.publicationId) || data === undefined || busy) { return; }
    if (review?.mode === "candidate" && published.decision.sha256 !== review.sha256) { setMessage("The committed publication digest does not confirm this reviewed payload. Keep the original reference and inspect the immutable publication before a fresh release review."); return; }
    setSelection({ scope, pair: { ...pair, [source]: { state: "present", publicationId: published.decision.publicationId } } });
    advance(scope); analysisOwner.selectTarget(undefined); draftOwner.setIntentDirty(true); refresh(value => value + 1);
  }
  function dismiss(receipt: CommandReceipt, observation: ConfigurationCommandObservation) {
    if (busy || !resolvedConfigurationOriginal(observation) || portal.receipts.forgetExact(receipt).kind !== "forgotten") { return; }
    if (receipt.family === "management.configuration.source.publish") { publicationOwner.selectTarget(undefined); advance(undefined); }
    else { releaseOwner.selectTarget(undefined); }
    analysisOwner.selectTarget(undefined);
    if (observation.disposition === "applied") {
      draftOwner.discardDraft(); setSelection({ scope, pair: {} }); setExistingReference(undefined); setReviewSetup(undefined);
    }
    refresh(value => value + 1); redraw(value => value + 1);
  }

  return <Shell signedIn={!session.blocked} footer="Configuration authoring"><div className="metadata-actions"><button className="item-link back-link" onClick={leave}>← Back to Configuration list</button>
    <a className="item-link" href={configurationRoute({ applicationId, applicationName: data?.context.application, configurationId, schemaVersion, view: "assignment", context })}>Manage assignment</a></div>
    <div className="page-heading"><div><div className="eyebrow">{data === undefined ? "Configuration" : `${data.context.application} / ${data.context.environment}`}</div>
      <h1>{reportedLabel(configurationId, "Configuration")}</h1><p>{source === "common" ? "Assigned shared JSON · read only" : "Application override JSON"}</p></div>
      <button disabled={busy || read.busy} onClick={() => { invalidateReview(); refresh(value => value + 1); }}>Refresh current state</button></div>
    <ReadNotice state={read} />{message === undefined ? null : <p className="notice" role="alert">{message}</p>}
    {initialPublicationId === undefined ? null : <ReadNotice state={initialPublication} />}
    {data === undefined ? null : <>
      <p className="muted">{source === "common" ? "Shared content is read only here. Create an Application override to make changes for this application."
        : data.baselineRelease?.commonSource.state === "present" ? "This override is a separate layer over the assigned shared configuration. Shared JSON is unchanged."
          : data.baselineRelease === undefined ? "This application has no default release assigned. Publishing JSON does not apply it."
            : "The assigned default release has no shared publication. This editor changes only this application's JSON."}</p>
      {source !== "application" || data.baselineRelease?.commonSource.state !== "present" ? null : <a className="item-link" href={configurationRoute({ applicationId,
        applicationName: data.context.application, configurationId, schemaVersion, source: "common", context })}>View assigned shared JSON</a>}
      {source !== "common" ? null : <a className="item-link" href={configurationRoute({ applicationId, applicationName: data.context.application,
        configurationId, schemaVersion, view: "edit", source: "application", context })}>Create an Application override</a>}
      {data.content.state === "unselected" || data.content.state === "unavailable" ? <p className="notice">{data.content.message}</p> : null}
      {data.context.defaults.state === "available" ? null : <p className="notice">Configuration defaults are {data.context.defaults.state}. Editing is unavailable.</p>}
      {source !== "common" || data.content.state !== "present" ? null : <pre className="configuration-json-readonly">{data.content.publication.payloadJson}</pre>}
      {raw === undefined || source !== "application" ? null : <section className="card configuration-editor"><div className="configuration-json-toolbar"><h2>Application JSON</h2>
        <button type="button" disabled={!canEdit} onClick={formatDraft}>Format JSON</button></div>
        <label htmlFor="configuration-raw-json">Application override JSON</label><textarea id="configuration-raw-json" className="configuration-json" spellCheck={false} value={raw} disabled={!canEdit}
          onChange={event => { try { changeRaw(event.target.value); } catch { setMessage("The raw draft exceeds 256 KiB; the previous draft is retained."); } }} />
        <p className="muted">{configurationDraftBytes(raw)} / 262144 UTF-8 bytes</p>
        {canEdit || busy ? null : <p className="notice">Local editing is unavailable for the current session or source permissions. The retained text has not been submitted.</p>}
        {originals.length === 0 ? null : <p className="notice">You can keep editing this local draft. Resolve the retained original operation before publishing another version; editing does not change its saved outcome.</p>}
        {published === undefined ? null : <p className="muted">The published JSON version is fixed. A release can use that version; later local edits are not included in it.</p>}
        {data.sharedOverrideSeed?.state !== "present" ? null : <p className="muted">This new override starts as an exact copy of the assigned Shared JSON. Shared configuration is unchanged.</p>}
        <label htmlFor="configuration-reason">Change reason</label><input id="configuration-reason" value={reason} maxLength={1024} disabled={!canEdit}
        onChange={event => { setReason({ scope, text: event.target.value }); markIntent(); invalidateReview(); }} />
        <div className="metadata-actions"><button disabled={!canPublish || !allowed(data, "source", "validateConfiguration")} onClick={() => { void inspect("validate"); }}>Validate JSON</button>
          {review !== undefined || selectedExisting || reviewSetup?.scope === scope && data.baselineRelease === undefined ? null : <button className="primary"
            disabled={!canPublish || !allowed(data, "source", "validateConfiguration") || !allowed(data, "binding", "previewConfiguration")} onClick={() => requestReview("candidate")}>Review Application JSON version</button>}</div>
      </section>}
      {reviewSetup?.scope !== scope || data.baselineRelease !== undefined || review !== undefined ? null : <section className="card" aria-label="First release source choice"><h2>Shared configuration for this release</h2>
        <p>No default release is assigned. Choose whether {reviewSetup.mode === "candidate" ? "the new Application JSON" : "this immutable Application publication"} will accompany a shared publication.</p>
        <label><input type="radio" name="configuration-first-shared" checked={pair.common?.state === "absent" && history !== "common"} disabled={!canSelectReferences}
          onChange={() => { firstSharedReference({ state: "absent" }); openHistory(undefined); }} /> Use no shared publication · Application only</label>
        <label><input type="radio" name="configuration-first-shared" checked={history === "common" || pair.common?.state === "present"}
          disabled={!canSelectReferences || data.context.commonSource.state !== "available"} onChange={chooseFirstSharedPublication} /> Choose a shared publication</label>
        {history !== "common" || data.context.commonSource.state !== "available" ? null : <ConfigurationHistory applicationId={applicationId} configurationId={configurationId}
          schemaVersion={schemaVersion} source="common" sourceId={data.context.commonSource.sourceId} environment={data.context.environment}
          disabled={!canSelectReferences} choose={value => firstSharedReference({ state: "present", publicationId: value.publicationId }, value)} />}
        <button className="primary" disabled={(reviewSetup.mode === "candidate" ? !canPublish : !canRelease) || pair.common === undefined || pair.application === undefined}
          onClick={() => { void inspect(reviewSetup.mode); }}>{reviewSetup.mode === "candidate" ? "Continue to JSON version review" : "Continue to release review"}</button>
      </section>}
      {(!isAdvanced && !selectedExisting) || review !== undefined ? null : <section className="card"><h2>Review the release</h2><p>The selected immutable Application publication and shared layer form a separate release. It will not be assigned automatically.</p>
        {!isAdvanced ? null : <><label htmlFor="configuration-release-reason">Change reason</label><input id="configuration-release-reason" value={reason} maxLength={1024} disabled={busy}
          onChange={event => { setReason({ scope, text: event.target.value }); markIntent(); invalidateReview(); }} /></>}
        {reviewSetup?.scope === scope && data.baselineRelease === undefined ? null : <button className="primary" disabled={!canRelease} onClick={() => requestReview("references")}>Review release</button>}</section>}
      {analysis?.kind !== "validation" ? null : <section className="notice" role="status"><strong>{analysis.result.valid ? "Declared structure is valid" : "Declared structure has issues"}</strong><p>Consumer semantic acceptance remains unverified.</p>
        {analysis.result.issues.map((issue, index) => <p key={index}>{issue.path ?? "Source"}: {issue.code}</p>)}</section>}
      {review === undefined ? null : <section className="card configuration-review"><h2 ref={reviewHeading} tabIndex={-1}>{review.mode === "candidate" ? "Review Application JSON version" : "Review release: Shared + Application"}</h2>
        <p>{review.mode === "candidate" ? "Publish stores one immutable version of this Application JSON. Then create a release to pin it together with the exact Shared version." : "Create release pins the exact Shared and Application versions as one pair. Assigning that release is a separate action."} Neither step changes running applications.</p>
        <Badge>{review.preview.structurallyValid ? "Declared structure eligible" : "Structural issues"}</Badge><Badge>{review.preview.redacted ? "Effective impact protected" : "Effective impact available"}</Badge>
        <p>Changes compare with the current default assignment. Consumer acceptance remains unknown. Only the displayed recipient page is observed.</p>
        <ul>{review.preview.changes.map((change, index) => <li key={index}>{change.path || "/"}: {change.kind}</li>)}</ul>
        {review.preview.issues.map((issue, index) => <p key={index}>{issue.path ?? "Source"}: {issue.code}</p>)}
        <p>{review.preview.recipients.items.length} recipient observations on this page{review.preview.recipients.nextCursor === undefined ? "." : "; more recipients are available, not included here."} Compatibility is not typed acceptance.</p>
        <TechnicalDetails fields={[{ label: "Common publication ID", value: review.common.publicationId }, { label: "Application publication ID", value: review.application.publicationId },
          { label: "Reviewed payload SHA-256", value: review.sha256 }, { label: "Reviewed assignment precondition", value: review.assignmentETag }]} />
        {review.raw === undefined ? null : <details><summary>Exact reviewed payload</summary><pre>{review.raw}</pre></details>}
        <div className="metadata-actions"><button className="primary" disabled={busy || !review.preview.structurallyValid || review.preview.redacted || (review.mode === "candidate" ? !canPublish : !canRelease)}
          onClick={() => { void submit(review.mode === "candidate" ? "publication" : "release"); }}>{review.mode === "candidate" ? "Publish Application JSON version" : "Create release — not assigned"}</button>
          <button disabled={busy} onClick={invalidateReview}>Back to draft</button></div>
      </section>}
      <TechnicalDetails fields={[{ label: "Application ID", value: applicationId }, { label: "Configuration ID", value: configurationId }, { label: "Schema version", value: schemaVersion },
        { label: "Binding ID", value: data.context.bindingId }, { label: "Configuration host", value: data.context.hostApplicationName },
        { label: "Application source ID", value: data.context.applicationSource.sourceId }, { label: "Shared source ID", value: data.context.commonSource.sourceId },
        { label: "Source precondition", value: data.source?.eTag }, { label: "Assignment precondition", value: data.assignment?.eTag },
        { label: "Assignment generation", value: data.assignment?.generation }, { label: "Assigned default release ID", value: data.assignment?.defaultReleaseId },
        { label: "Selected shared publication ID", value: pair.common?.publicationId }, { label: "Selected Application publication ID", value: pair.application?.publicationId }]} />
      {data.context.defaults.state !== "available" ? null : <details className="configuration-defaults"><summary>Declared defaults</summary>
        <p>Declared defaults are separate from assigned shared JSON and accepted runtime values.</p><pre>{data.context.defaults.defaultsJson}</pre></details>}
      {data.context.applicationSource.state !== "available" || originals.length > 0 ? null : <details className="configuration-defaults" onToggle={event => setApplicationHistoryOpen(event.currentTarget.open)}><summary>Application publication history</summary>
        {!applicationHistoryOpen ? null : <ConfigurationHistory applicationId={applicationId} configurationId={configurationId} schemaVersion={schemaVersion} source="application"
          sourceId={data.context.applicationSource.sourceId} environment={data.context.environment} disabled={!canSelectReferences || source !== "application"}
          {...(source !== "application" ? {} : { choose: (value: ConfigurationPublication) => chooseReference("application", { state: "present", publicationId: value.publicationId }, value) })} />}</details>}
    </>}
    {originals.length === 0 ? null : <section className="card"><h2>Original Configuration operations</h2><p>Only minimal references survive reload. Recovery never reconstructs or resubmits a draft.</p>
      {originals.map(receipt => {
        const observation = receipt.family === "management.configuration.source.publish" ? publication?.key === receipt.key ? publication : undefined
          : releaseForBinding?.key === receipt.key ? releaseForBinding : undefined;
        const locked = resolvedConfigurationOriginal(releaseForBinding) || resolvedConfigurationOriginal(publication) && !isAdvanced;
        return <section className="metadata-operation" key={receipt.key} aria-label={receipt.family === "management.configuration.source.publish" ? "Source publication original" : "Coherent release original"}><h3>{receipt.family === "management.configuration.source.publish" ? "Application JSON version" : "Release: Shared + Application"}</h3>
          <TechnicalDetails fields={[{ label: "Original operation key", value: receipt.key }, { label: "Source ID", value: "sourceId" in receipt.target ? receipt.target.sourceId : undefined }]} />
          {observation === undefined ? <p>Original outcome has not been recovered in this view.</p> : <ConfigurationOutcome value={observation} />}
          <div className="metadata-actions"><button disabled={busy || session.blocked || !recoveryReady || locked} onClick={() => { void recover(receipt); }}>Recover original Configuration result</button>
            {observation === undefined || !resolvedConfigurationOriginal(observation) ? null : <button disabled={busy || receipt.family === "management.configuration.source.publish" && isAdvanced && originals.some(item => item.family === "management.configuration.release.create") && !resolvedConfigurationOriginal(releaseForBinding)}
              onClick={() => dismiss(receipt, observation)}>Dismiss resolved reference and refresh</button>}
            {published?.key !== receipt.key || isAdvanced ? null : <button className="primary" disabled={busy || data === undefined} onClick={usePublished}>Review release with this publication</button>}
            {receipt.family !== "management.configuration.release.create" || observation?.disposition !== "applied" || !isUuid(observation.decision?.releaseId) ? null
              : <button disabled={busy} onClick={() => {
                if (draftOwner.view.navigationBlocked && !window.confirm("Leave this source draft and review assignment for the committed release?")) { return; }
                draftOwner.discardDraft(); window.location.assign(configurationRoute({ applicationId, applicationName: data?.context.application,
                  configurationId, schemaVersion, view: "assignment", releaseId: observation.decision!.releaseId!, context }));
              }}>Review assignment for this release</button>}
          </div>
        </section>;
      })}
    </section>}
    {portal.receiptNotice === undefined ? null : <p className="notice">Earlier original references could not be restored ({portal.receiptNotice}); this does not prove a previous change failed.</p>}
    {busy ? <p className="notice" role="status">Waiting for the current request to settle…</p> : null}
  </Shell>;
}

function tuple(value: ConfigurationEditorSnapshot, app: string, configuration: string, schema: string, source: SourceKind): boolean {
  return value.context.applicationId === app && value.context.configurationId === configuration && value.context.schemaVersion === schema && value.context.selectedSource === source;
}
function selectedSource(value: ConfigurationEditorSnapshot): string | undefined { return value.context[value.context.selectedSource === "common" ? "commonSource" : "applicationSource"].sourceId; }
function sameBinding(left: ConfigurationEditorSnapshot, right: ConfigurationEditorSnapshot): boolean {
  return left.context.bindingId === right.context.bindingId && left.context.environment === right.context.environment
    && left.context.hostApplicationName === right.context.hostApplicationName && selectedSource(left) === selectedSource(right)
    && left.context.commonSource.sourceId === right.context.commonSource.sourceId && left.context.commonSource.state === right.context.commonSource.state
    && left.context.applicationSource.sourceId === right.context.applicationSource.sourceId && left.context.applicationSource.state === right.context.applicationSource.state;
}
function target(requestKey: string, value: ConfigurationEditorSnapshot): ProtectedTarget { return { requestKey, applicationId: value.context.applicationId,
  environment: value.context.environment, definitionId: value.context.configurationId, schemaVersion: value.context.schemaVersion }; }
function allowed(value: ConfigurationEditorSnapshot | undefined, resource: "source" | "binding", operation: string): boolean {
  return value?.actions.state === "available" && value.actions[resource].actions.some(action => action.operationId === operation && action.supported && action.allowed);
}
function publicationMatches(value: ConfigurationPublication, snapshot: ConfigurationEditorSnapshot, kind: SourceKind): boolean {
  return value.sourceId === snapshot.context[kind === "common" ? "commonSource" : "applicationSource"].sourceId && value.kind === kind
    && value.configurationId === snapshot.context.configurationId && value.schemaVersion === snapshot.context.schemaVersion && value.environment === snapshot.context.environment
    && (kind === "common" ? value.applicationId === undefined : value.applicationId === snapshot.context.applicationId);
}
function currentOriginal(owner: ProtectedStateOwner<ConfigurationCommandObservation>, sessionKey: string | undefined, family: ConfigurationCommandObservation["family"]): ConfigurationCommandObservation | undefined {
  const value = owner.view.snapshot;
  return value !== undefined && sessionKey !== undefined && portalContextKey(value.context) === sessionKey && value.value.family === family ? value.value : undefined;
}
function json(value: unknown): RequestInit { return { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(value) }; }
function ConfigurationOutcome({ value }: { value: ConfigurationCommandObservation }) {
  const label = value.disposition === "applied" ? value.family === "management.configuration.source.publish" ? "Publication committed — not assigned" : "Release committed — not assigned"
    : value.disposition === "conflict" ? "Original precondition conflict" : value.disposition === "rejected" ? "Original change rejected"
      : value.disposition === "notCommitted" ? "Original change not committed" : "Original outcome unresolved";
  return <div role="status"><h3>{label}</h3><p>Publication, release, assignment and local adoption are separate facts. No assignment was submitted by this editor.</p>
    {value.disposition === "conflict" ? <p>Still-authorized draft and reference choices remain in memory. Dismiss this resolved reference, refresh current state and deliberately review again. Nothing is resubmitted automatically.</p> : null}
    <TechnicalDetails fields={[{ label: "Original completion", value: value.originalCompletion }, { label: "Result source", value: value.resultSource },
      { label: "Publication ID", value: value.disposition === "applied" ? value.decision?.publicationId : undefined },
      { label: "Release ID", value: value.disposition === "applied" ? value.decision?.releaseId : undefined }, { label: "Decision code", value: value.rejection?.code ?? value.decision?.code ?? value.code },
      { label: "Current request outcome", value: value.transport.kind }, { label: "Current request cleanup succeeded", value: value.transport.cleanupSucceeded ? "Yes" : "No" }]} />
  </div>;
}
