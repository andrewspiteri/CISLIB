"use client";

import { useEffect, useRef, useState } from "react";
import type { ConfigurationActions, ConfigurationAdoptionEntry, ConfigurationAssignmentMap, ConfigurationPreview, ConfigurationRelease, Page } from "@cislib/management-client";
import type { ConfigurationAssignmentReview } from "../configuration-assignment-model.ts";
import { configurationVersionDeployCommand, reviewConfigurationVersionDeploy } from "../configuration-version-commands.ts";
import { configurationVersionContentMatches, configurationVersionDeployment, configurationVersionLabel, configurationVersionMatches } from "../configuration-versions.ts";
import type { ConfigurationVersionSnapshot } from "../configuration-versions.ts";
import { resolvedConfigurationOriginal } from "../configuration-command-results.ts";
import { isCompletedConfigurationDeployment } from "../configuration-deployment-receipts.ts";
import { compareWithDeployedConfiguration } from "../configuration-version-diff.ts";
import { adoptionPageMatches } from "../configuration-adoption-model.ts";
import { formatConfigurationJson } from "../configuration-draft.ts";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../portal-context.ts";
import type { PortalApplication, PortalReply } from "../portal-model.ts";
import { ProtectedStateOwner } from "../protected-state.ts";
import { applicationRoute, configurationRoute } from "../portal-routes.ts";
import { useProtectedPortal } from "./protected-context";
import type { ListContext } from "./application-detail";
import { Badge, Pager, ReadNotice, Shell, TechnicalDetails, queryUrl, ready, reportedLabel, usePages, usePortalRead } from "./shared";
import { ConfigurationOperationStatus, configurationOperationClean, useConfigurationOperation } from "./configuration-operation";
import { ConfigurationVersionEditor } from "./configuration-version-editor";
import type { ConfigurationSavedCompletion } from "./configuration-version-editor";
import { ConfigurationInstanceStatusTable } from "./configuration-instance-status";

/** One Configuration: immutable saved versions, separately deployed desired version, and local edits. */
export function ConfigurationVersionPage({ applicationId, configurationId, schemaVersion, context, initialCursor, initialReleaseId, initialCompletedSave }: {
  applicationId: string; configurationId: string; schemaVersion: string; context: ListContext; initialCursor?: string; initialReleaseId?: string; initialCompletedSave?: ConfigurationSavedCompletion
}) {
  const { owner: portal, snapshot: session } = useProtectedPortal();
  const [revision, refresh] = useState(0), [selected, select] = useState<{ scope: string; id: string }>();
  const [editing, edit] = useState<{ scope: string; version?: ConfigurationVersionSnapshot; first?: boolean }>();
  const [completed, setCompleted] = useState<ConfigurationSavedCompletion | undefined>(initialCompletedSave);
  const [retiredContentReply, retireContentReply] = useState<unknown>(undefined);
  const [compareOpen, showCompare] = useState(false);
  const [statusOpen, showStatus] = useState(false), [busy, setBusy] = useState(false), [message, setMessage] = useState<string>();
  const [deployment, setDeployment] = useState<{ scope: string; version: ConfigurationRelease; note: string; review?: ConfigurationAssignmentReview }>();
  const [previewOwner] = useState(() => new ProtectedStateOwner<ConfigurationAssignmentReview>({ context: session.context, receipts: portal.receipts }));
  const operation = useConfigurationOperation();
  const alive = useRef(true), pending = useRef(false), dialog = useRef<HTMLDialogElement>(null), mapNow = useRef<ConfigurationAssignmentMap | undefined>(undefined);
  const base = `/api/portal/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}`;
  const sessionKey = session.context === undefined ? undefined : portalContextKey(session.context);
  const scope = JSON.stringify([applicationId, configurationId, schemaVersion, sessionKey]);
  const pages = usePages(scope + "/saved-versions", undefined);
  const versionsUrl = queryUrl(base + "/releases", { schemaVersion, pageSize: "10", cursor: pages.cursor });
  const versionsState = usePortalRead<Page<ConfigurationRelease>>(versionsUrl, revision);
  const mapState = usePortalRead<ConfigurationAssignmentMap>(queryUrl(base + "/assignments", { schemaVersion }), revision);
  const actionsState = usePortalRead<ConfigurationActions>(queryUrl(base + "/binding-actions", { schemaVersion }), revision);
  const appState = usePortalRead<PortalApplication>(`/api/portal/applications/${applicationId}`, revision);
  const application = ready(appState)?.applicationId === applicationId ? ready(appState) : undefined;
  const received = ready(versionsState);
  const versions = received !== undefined && received.items.every(item => configurationVersionMatches(item, applicationId, configurationId, schemaVersion)) ? received : undefined;
  const actualMap = ready(mapState);
  const map = actualMap !== undefined && configurationVersionMatches(actualMap, applicationId, configurationId, schemaVersion) ? actualMap : undefined;
  mapNow.current = map;
  const currentDeployment = !mapState.busy && !mapState.stale && !session.blocked ? map : undefined;
  const deployedOnPage = currentDeployment?.defaultReleaseId === undefined ? undefined : versions?.items.find(item => item.releaseId === currentDeployment.defaultReleaseId);
  const deployedMetadataState = usePortalRead<ConfigurationRelease>(currentDeployment === undefined || currentDeployment.defaultReleaseId === undefined || currentDeployment.overrides.length !== 0 || deployedOnPage !== undefined ? null
    : `/api/portal/configuration/releases/${currentDeployment.defaultReleaseId}`, revision);
  const deployedMetadata = ready(deployedMetadataState);
  const selectedId = selected?.scope === scope ? selected.id : initialReleaseId ?? versions?.items[0]?.releaseId;
  const contentUrl = selectedId === undefined ? null : queryUrl(base + `/versions/${selectedId}`, { schemaVersion });
  const contentState = usePortalRead<ConfigurationVersionSnapshot>(contentUrl, revision);
  const receivedContent = ready(contentState);
  const content = receivedContent !== undefined && contentState.reply !== retiredContentReply && receivedContent.release.releaseId === selectedId && configurationVersionContentMatches(receivedContent)
    && configurationVersionMatches(receivedContent.release, applicationId, configurationId, schemaVersion, map?.environment) ? receivedContent : undefined;
  const compareId = map === undefined || mapState.busy || mapState.stale || map.overrides.length !== 0 ? undefined : map.defaultReleaseId;
  const compareState = usePortalRead<ConfigurationVersionSnapshot>(!compareOpen || compareId === undefined || compareId === selectedId ? null : queryUrl(base + `/versions/${compareId}`, { schemaVersion }), revision);
  const compareValue = ready(compareState);
  const compared = compareId === selectedId && !contentState.busy && !contentState.stale ? content : compareValue !== undefined && compareValue.release.releaseId === compareId && configurationVersionContentMatches(compareValue) && !compareState.busy && !compareState.stale
    && configurationVersionMatches(compareValue.release, applicationId, configurationId, schemaVersion, content?.release.environment) ? compareValue : undefined;
  const action = ready(actionsState);
  const canDeploy = map !== undefined && !mapState.busy && !mapState.stale && !actionsState.busy && !actionsState.stale
    && action?.resourceId === map.bindingId && ["replaceConfigurationAssignments", "previewConfiguration"].every(name => action.actions.some(item => item.operationId === name && item.supported && item.allowed))
    && !session.blocked && sessionKey !== undefined && !busy;
  const selectedEditing = editing?.scope === scope ? editing : undefined;
  const completedSave = completed !== undefined && completed.sessionKey === sessionKey && completed.applicationId === applicationId && completed.configurationId === configurationId
    && completed.schemaVersion === schemaVersion ? completed : undefined;
  const deploy = deployment?.scope === scope ? deployment : undefined;
  const loaded = portal.receipts.load();
  const originals = loaded.kind === "ready" ? loaded.receipts.filter(item => item.family === "management.configuration.assignment.replace"
    && item.target.applicationId === applicationId && item.target.configurationId === configurationId && item.target.schemaVersion === schemaVersion) : [];
  const unresolvedDeployment = originals.some(receipt => !isCompletedConfigurationDeployment(receipt, operation.observation, applicationId, configurationId, schemaVersion));
  const deployed = currentDeployment?.overrides.length !== 0 ? undefined : deployedOnPage ?? (deployedMetadata !== undefined && !deployedMetadataState.busy && !deployedMetadataState.stale
    && deployedMetadata.releaseId === currentDeployment?.defaultReleaseId && configurationVersionMatches(deployedMetadata, applicationId, configurationId, schemaVersion, currentDeployment?.environment) ? deployedMetadata : undefined);
  const appName = application?.metadata.displayName || application?.application;
  const environment = map?.environment ?? content?.release.environment ?? application?.environment;
  useEffect(() => portal.register(previewOwner), [portal, previewOwner]);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => { edit(undefined); retireContentReply(undefined); showCompare(false); setDeployment(undefined); setMessage(undefined); }, [scope]);
  useEffect(() => {
    if (deploy === undefined) { dialog.current?.close(); }
    else if (!dialog.current?.open) { dialog.current?.showModal(); }
  }, [deploy]);
  function discardLocal(prompt: string) {
    if (portal.navigationBlocked && !window.confirm(prompt)) { return false; }
    portal.discardLocalDrafts(); return true;
  }
  function chooseVersion(id: string) {
    if (busy || !discardLocal("Discard unsaved JSON and inspect this saved version?")) { return; }
    showCompare(false); edit(undefined); select({ scope, id });
  }
  function leave() {
    if (!discardLocal("Discard unsaved changes and return to the Configuration list?")) { return; }
    window.location.assign(applicationRoute({ applicationId, applicationName: appName, tab: "configuration", cursor: initialCursor, context }));
  }
  function beginEdit(value: ConfigurationVersionSnapshot) {
    if (busy || contentState.busy || contentState.stale || value.content.state !== "present") { return; }
    if (completedSave !== undefined) {
      const known = [completedSave.publication, completedSave.release, ...(completedSave.creation === undefined ? [] : [completedSave.creation])];
      if (known.every(configurationOperationClean) && loaded.kind === "ready") {
        for (const fact of known) {
          const receipt = loaded.receipts.find(item => item.key === fact.key && item.family === fact.family);
          if (receipt !== undefined && portal.receipts.forgetExact(receipt).kind !== "forgotten") { setMessage("A completed local reference could not be dismissed. New writes remain blocked until storage is available."); break; }
        }
      }
    }
    showCompare(false); edit({ scope, version: value });
  }
  function openDeploy(version: ConfigurationRelease) {
    if (pending.current || !canDeploy || versionsState.busy || versionsState.stale || map === undefined || unresolvedDeployment
      || !configurationVersionMatches(version, applicationId, configurationId, schemaVersion, map.environment)) { return; }
    setMessage(undefined); setDeployment({ scope, version, note: "Deploy " + configurationVersionLabel(version) });
  }
  async function previewDeployment(version: ConfigurationRelease, note: string, capturedMap: ConfigurationAssignmentMap, capturedSession: string) {
    const epoch = portal.getSnapshot().epoch;
    previewOwner.selectTarget({ requestKey: base + "#deploy-preview", applicationId, definitionId: configurationId, schemaVersion });
      const command = configurationVersionDeployCommand(capturedMap, version, note);
      await previewOwner.request(async ticket => {
        if (ticket.context === undefined) { return { state: "unauthenticated" }; }
        const response = await fetch("/api/portal/configuration/preview?pageSize=20", { method: "POST", signal: ticket.signal,
          credentials: "same-origin", cache: "no-store", redirect: "error", headers: { "Content-Type": "application/json", [portalContextHeader]: portalContextKey(ticket.context) },
          body: JSON.stringify({ commandVersion: 1, applicationId, configurationId, schemaVersion, assignment: command }) });
        const body = await response.json() as PortalReply<ConfigurationPreview>;
        if (body.state === "unauthenticated") { if (portal.getSnapshot().epoch === epoch) { portal.signOut(); } return { state: "unauthenticated" }; }
        if (body.context !== undefined && !portal.observe(body.context, epoch, previewOwner).accepted) { return { state: "unavailable" }; }
        const actual = capturePortalContext(body.context);
        if (body.state !== "ready" || actual === undefined) { return { state: body.state === "ready" ? "unavailable" : body.state, ...(actual === undefined ? {} : { context: actual }) }; }
        return { state: "ready", context: actual, value: reviewConfigurationVersionDeploy(capturedMap, version, note, body.value) };
      });
      const result = previewOwner.view.snapshot, current = portal.getSnapshot();
      return alive.current && result !== undefined && current.context !== undefined && portalContextKey(current.context) === capturedSession
        && portalContextKey(result.context) === capturedSession && !current.blocked ? result.value : undefined;
  }
  async function confirmDeploy() {
    portal.checkExpiry();
    const current = portal.getSnapshot();
    if (pending.current || !canDeploy || deploy === undefined || map === undefined || unresolvedDeployment || current.context === undefined
      || portalContextKey(current.context) !== sessionKey) { return; }
    pending.current = true; setBusy(true); setMessage(undefined);
    let issued = false;
    try {
      const reviewed = await previewDeployment(deploy.version, deploy.note, map, portalContextKey(current.context));
      const latest = portal.getSnapshot(), latestMap = mapNow.current;
      if (reviewed === undefined || !alive.current || latest.context === undefined || latest.blocked || portalContextKey(latest.context) !== sessionKey
        || latestMap === undefined || latestMap.eTag !== reviewed.baseline.eTag || latestMap.bindingId !== reviewed.baseline.bindingId || latestMap.environment !== reviewed.baseline.environment) {
        setMessage("Current deployment changed or could not be reviewed. Nothing was deployed."); return;
      }
      const retained = portal.receipts.load();
      if (retained.kind !== "ready") { setMessage("Original reference storage is unavailable. Nothing was deployed."); return; }
      const previous = retained.receipts.filter(receipt => receipt.family === "management.configuration.assignment.replace" && receipt.target.applicationId === applicationId
        && receipt.target.configurationId === configurationId && receipt.target.schemaVersion === schemaVersion);
      for (const receipt of previous) {
        if (!isCompletedConfigurationDeployment(receipt, operation.owner.view.snapshot?.value, applicationId, configurationId, schemaVersion)
          || !operation.dismiss(receipt)) { setMessage("A prior deployment reference could not be safely acknowledged. Nothing new was deployed."); return; }
      }
      const nativeIntent = { family: "management.configuration.assignment.replace" as const, target: { applicationId, configurationId, schemaVersion } };
      const prepared = portal.receipts.prepare(nativeIntent);
      if (prepared.kind !== "prepared") { setMessage("The original deployment reference could not be retained. Nothing was deployed."); return; }
      issued = true;
      const result = await operation.request(queryUrl(base + "/assignments", { schemaVersion }), { method: "PUT", headers: {
        "Content-Type": "application/json", "Idempotency-Key": prepared.receipt.key, "If-Match": reviewed.baseline.eTag }, body: JSON.stringify(reviewed.command) }, nativeIntent, prepared.receipt.key);
      if (!alive.current) { return; }
      if (configurationOperationClean(result)) { setMessage("Deployment selected. Instance status is reported separately."); setDeployment(undefined); refresh(value => value + 1); }
      else { setMessage("Deployment was not fully confirmed. Keep its original reference and recover the same result below."); setDeployment(undefined); }
    } catch { if (alive.current) { setMessage(issued ? "Deployment delivery was not confirmed. Recover its original result; do not submit a replacement." : "Current deployment could not be reviewed. Nothing was deployed."); if (issued) { setDeployment(undefined); } } }
    finally { pending.current = false; if (alive.current) { setBusy(false); } }
  }
  const comparisonResult = map === undefined || mapState.busy || mapState.stale || content === undefined || compared === undefined || contentState.busy || contentState.stale
    ? undefined : compareWithDeployedConfiguration(map, content, compared);
  return <Shell wide signedIn={!session.blocked} footer="Configuration versions">
    <nav className="configuration-breadcrumb" aria-label="Configuration breadcrumb"><button className="item-link" onClick={leave}>Configuration</button><span>/</span><span>{reportedLabel(configurationId, "Configuration")}</span></nav>
    <div className="page-heading"><div><h1>{reportedLabel(configurationId, "Configuration")}</h1><p>{appName ?? "Application"}{environment === undefined ? "" : ` · ${environment}`}</p></div>
      <button disabled={busy} onClick={() => refresh(value => value + 1)}>Refresh</button></div>
    {message === undefined ? null : <p className="notice" role="status">{message}</p>}
    <div className="configuration-deployment-bar"><div>{currentDeployment === undefined ? <Badge>Deployment unavailable</Badge> : currentDeployment.overrides.length > 0 ? <Badge>Varies by target</Badge>
      : currentDeployment.defaultReleaseId === undefined ? <Badge>Not deployed</Badge> : <><Badge tone="good">Deployed</Badge><strong>{deployed === undefined ? "Saved version outside this page" : configurationVersionLabel(deployed)}</strong></>}</div>
      <button className="item-link" aria-expanded={statusOpen} onClick={() => showStatus(value => !value)}>Instance status</button></div>
    {!statusOpen ? null : <ConfigurationInstanceStatus applicationId={applicationId} configurationId={configurationId} schemaVersion={schemaVersion} revision={revision}
      releases={[...(!versionsState.busy && !versionsState.stale ? versions?.items ?? [] : []), ...(!deployedMetadataState.busy && !deployedMetadataState.stale && deployedMetadata !== undefined ? [deployedMetadata] : [])]} />}
    <div className="configuration-version-grid"><section className="card configuration-version-list" aria-label="Saved versions"><h2>Saved versions</h2><ReadNotice state={versionsState} restart={() => pages.reset()} />
      {versions === undefined ? null : <>{versions.items.length === 0 ? <p>No saved versions on this page.</p> : versions.items.map(item => <article key={item.releaseId} data-version-id={item.releaseId}
        className={item.releaseId === selectedId ? "configuration-version-row selected" : "configuration-version-row"}>
        <button className="item-link version-title" disabled={busy} onClick={() => chooseVersion(item.releaseId)}>{configurationVersionLabel(item)}</button><VersionTime value={item.createdAtUtc} />
        <div className="configuration-version-row-actions">{configurationVersionDeployment(currentDeployment, item.releaseId) === "deployed" ? <Badge tone="good">Deployed</Badge>
          : <button disabled={!canDeploy || versionsState.busy || versionsState.stale || unresolvedDeployment || selectedEditing !== undefined} onClick={() => openDeploy(item)}>Deploy</button>}</div></article>)}
        <Pager pages={pages} nextCursor={versions.nextCursor} busy={versionsState.busy || busy} changed={() => select(undefined)} label={`${versions.items.length} saved versions on this page`} /></>}
    </section><section className="configuration-version-content" aria-label="Selected version">
      {selectedEditing !== undefined ? <ConfigurationVersionEditor key={scope + (selectedEditing.version?.release.releaseId ?? "first")} applicationId={applicationId} configurationId={configurationId} schemaVersion={schemaVersion}
        {...(selectedEditing.version === undefined ? {} : { baseVersion: selectedEditing.version })} firstVersion={selectedEditing.first === true} onBusy={setBusy} onCancel={() => { if (discardLocal("Discard unsaved JSON?")) { edit(undefined); } }}
        onContentLost={() => { retireContentReply(contentState.reply); edit(undefined); refresh(value => value + 1); }}
        onSaved={(completion, keepEditing) => { showCompare(false); setCompleted(completion); if (!keepEditing) { edit(undefined); } select({ scope, id: completion.savedReleaseId }); refresh(value => value + 1); }} />
        : <section className="card configuration-selected-version"><ReadNotice state={contentState} />{content === undefined ? <><p>Select a saved version to inspect.</p>
          {versions?.items.length === 0 && !versionsState.busy && !versionsState.stale && map?.defaultReleaseId === undefined
            ? <button className="primary" disabled={busy || map === undefined || mapState.busy || mapState.stale} onClick={() => edit({ scope, first: true })}>Create first version</button> : null}</> : <>
          <div className="configuration-version-heading"><div><h2>{configurationVersionLabel(content.release)}</h2><VersionTime value={content.release.createdAtUtc} /></div>
            <button className="primary" disabled={busy || contentState.busy || contentState.stale || content.content.state !== "present"} onClick={() => beginEdit(content)}>Edit as new version</button></div>
          {content.content.state !== "present" ? <p className="notice">{content.content.message}</p> : <><p className="muted">{content.content.layer === "application" ? "Application JSON" : "Shared JSON"} · exact saved layer</p>
            <pre className="configuration-json-readonly">{formatted(content.content.publication.payloadJson)}</pre></>}
          <p className="muted">Saving a version does not deploy it.</p></>}
        </section>}
    </section></div>
    <details className="configuration-comparison" open={compareOpen} onToggle={event => showCompare(event.currentTarget.open)}><summary>Differences from deployed version</summary>
      {map === undefined || mapState.busy || mapState.stale ? <p>Current deployment is unavailable for comparison.</p> : map.overrides.length !== 0
        ? <p>Deployment varies by target. There is no single deployed version to compare.</p> : map.defaultReleaseId === undefined ? <p>No version is currently deployed.</p>
          : <><p>Selected: {content === undefined ? "Not available" : configurationVersionLabel(content.release)} · Deployed: {compared === undefined ? "Loading" : configurationVersionLabel(compared.release)}</p>
            {compareId === selectedId ? null : <ReadNotice state={compareState} />}{comparisonResult === undefined ? null : <ConfigurationDiff result={comparisonResult} />}</>}
    </details>
    {originals.length === 0 ? null : <details className="configuration-recovery"><summary>Deployment original result</summary>{originals.map(receipt => <div key={receipt.key}>
      {operation.observation?.key === receipt.key ? <ConfigurationOperationStatus value={operation.observation} /> : <p>Original result not recovered in this view.</p>}
      <div className="metadata-actions"><button disabled={busy || !operation.canRecover(receipt)} onClick={() => { void operation.recover(receipt, queryUrl(base + `/assignment-operations/${receipt.key}`, { schemaVersion })).then(() => refresh(value => value + 1)); }}>Recover original deployment</button>
        <button disabled={busy || operation.observation?.key !== receipt.key || !resolvedConfigurationOriginal(operation.observation)} onClick={() => { if (operation.dismiss(receipt)) { refresh(value => value + 1); } }}>Dismiss resolved reference</button></div></div>)}</details>}
    <TechnicalDetails fields={[{ label: "Application ID", value: applicationId }, { label: "Configuration ID", value: configurationId }, { label: "Schema version", value: schemaVersion },
      { label: "Binding ID", value: map?.bindingId }, { label: "Selected saved version", value: content?.release.releaseId }, { label: "Native actor reference", value: content?.release.actor },
      { label: "Shared publication", value: content?.release.commonSource.publicationId }, { label: "Application publication", value: content?.release.applicationSource.publicationId },
      { label: "Assignment precondition", value: map?.eTag }, { label: "Last Save publication original", value: completedSave?.publication.key },
      { label: "Last Save version original", value: completedSave?.release.key }]} />
    <dialog ref={dialog} className="configuration-deploy-dialog" onCancel={event => { if (busy) { event.preventDefault(); } else { setDeployment(undefined); } }}>
      {deploy === undefined ? null : <><h2>Deploy {configurationVersionLabel(deploy.version)}</h2><p>{reportedLabel(configurationId, "Configuration")}</p><p>{appName ?? "Application"} · {deploy.version.environment}</p>
        <h3>All instances of this application in {deploy.version.environment}</h3><dl className="facts"><div className="fact"><dt>Currently deployed</dt><dd>{deployed === undefined ? map?.defaultReleaseId === undefined ? "Not deployed" : "Saved version outside this page" : configurationVersionLabel(deployed)}</dd></div>
          <div className="fact"><dt>Selected saved version</dt><dd>{configurationVersionLabel(deploy.version)}</dd></div></dl>
        {map?.overrides.length ? <p className="notice">{map.overrides.length} existing target-specific assignments will be removed. All instances will use the same desired version.</p> : null}
        <label htmlFor="configuration-deploy-note">Deployment note</label><input id="configuration-deploy-note" value={deploy.note} disabled={busy} onChange={event => setDeployment({ ...deploy, note: event.target.value })} />
        {message === undefined ? null : <p className="notice" role="status">{message}</p>}
        <p className="muted">Instances report adoption separately; deployment does not confirm immediate activation.</p><div className="metadata-actions"><button disabled={busy} onClick={() => setDeployment(undefined)}>Cancel</button>
          <button className="primary" disabled={!canDeploy || unresolvedDeployment} onClick={() => { void confirmDeploy(); }}>Deploy {configurationVersionLabel(deploy.version)}</button></div></>}
    </dialog>
  </Shell>;
}
function formatted(raw: string) { const result = formatConfigurationJson(raw); return result.valid ? result.json : raw; }
export function VersionTime({ value }: { value: string }) {
  return <time dateTime={value}>{new Intl.DateTimeFormat(undefined, { year: "numeric", month: "short", day: "2-digit", hour: "2-digit", minute: "2-digit", timeZoneName: "short" }).format(new Date(value))}</time>;
}
function ConfigurationDiff({ result }: { result: ReturnType<typeof compareWithDeployedConfiguration> }) {
  if (result.state !== "ready") { return <p className="notice">{result.message}</p>; }
  return <div className="configuration-diff"><p>{result.changed ? "These saved versions differ." : "No changes in the compared saved layers."}</p>
    {result.notice === undefined ? null : <p className="notice">{result.notice}</p>}
    <div className="table-frame"><table><thead><tr><th>Deployed version</th><th>Selected saved version</th></tr></thead><tbody>{result.rows.map((row, index) => <tr className={`diff-${row.kind}`} key={index}>
      <td><code>{row.left ?? ""}</code></td><td><code>{row.right ?? ""}</code></td></tr>)}</tbody></table></div></div>;
}
function ConfigurationInstanceStatus({ applicationId, configurationId, schemaVersion, revision, releases }: {
  applicationId: string; configurationId: string; schemaVersion: string; revision: number; releases: readonly ConfigurationRelease[]
}) {
  const heading = useRef<HTMLHeadingElement>(null);
  const pages = usePages(JSON.stringify([applicationId, configurationId, schemaVersion, "inline-status"]), undefined);
  const url = queryUrl(`/api/portal/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}/adoption`, { schemaVersion, pageSize: "10", cursor: pages.cursor });
  const state = usePortalRead<Page<ConfigurationAdoptionEntry>>(url, revision), received = ready(state);
  const page = received !== undefined && adoptionPageMatches(received, applicationId, configurationId, schemaVersion) ? received : undefined;
  return <section className="card" aria-label="Instance status"><h2 ref={heading} tabIndex={-1}>Instance status</h2><ReadNotice state={state} />{page === undefined ? null : <>
    <ConfigurationInstanceStatusTable rows={page.items} releases={releases} scope={JSON.stringify([url, revision])}
      earlierPage={state.stale === true} configurationLabel={reportedLabel(configurationId, "Configuration")} fallback={heading.current} />
    <Pager pages={pages} nextCursor={page.nextCursor} busy={state.busy} label={`${page.items.filter(row => row.registrationId !== undefined).length} instances on this page`} /></>}
  </section>;
}
