"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import type { Page, RuleCounterPage, RuleDeployment, RuleDeploymentInstance, RuleSchema, RuleSetSummary, RuleVersion, RuleVersionSummary } from "@cislib/management-client";
import type { PortalApplication, PortalPage } from "../portal-model.ts";
import { applicationRoute } from "../portal-routes.ts";
import type { PortalListContext } from "../portal-routes.ts";
import { portalContextKey } from "../portal-context.ts";
import { ProtectedStateOwner } from "../protected-state.ts";
import { modeLabels, parseRuleDefinition, prepareRuleDefinition, ruleNote, ruleRowLabel, stateLabels } from "../rule-engine-draft.ts";
import type { DraftIssue, RuleDraft } from "../rule-engine-draft.ts";
import type { RuleOperationObservation } from "../rule-engine-operations.ts";
import { ruleEngineRoute } from "../rule-engine-routes.ts";
import type { RuleHistoryReturn, RulePortalView } from "../rule-engine-routes.ts";
import { Badge, Empty, Fact, Facts, Pager, ReadNotice, Shell, TechnicalDetails, Timestamp, queryUrl, ready, usePages, usePortalRead } from "./shared";
import type { ReadState } from "./shared";
import { RuleAdoption, ruleVersionLabel } from "./rule-engine-adoption";
import { RuleGrid } from "./rule-engine-grid";
import { RuleOriginalResults, useRuleCommand } from "./rule-engine-command";
import { RuleCounters } from "./rule-engine-counters";
import { RuleExecutionHistory, RuleExecutionInspection } from "./rule-engine-history";
import { useProtectedPortal } from "./protected-context";

type ViewProps = { readonly applicationId: string; readonly context: PortalListContext; readonly rulesetId: string | undefined;
  readonly view: RulePortalView; readonly initialVersion: string | undefined; readonly newSchemaRevision: string | undefined;
  readonly executionId: string | undefined; readonly history: RuleHistoryReturn; };
type RuleGrant = { readonly operationId: string; readonly allowed: boolean };
type RuleEditingBasis = { readonly version: string; readonly schemaRevision?: never }
  | { readonly version?: never; readonly schemaRevision: string };
const saveOperation = "management.rule-engine.version.save" as const;
const deployOperation = "management.rule-engine.deployment.request" as const;

export function RuleEnginePage(props: ViewProps) {
  const { snapshot } = useProtectedPortal();
  const [revision, refresh] = useState(0);
  // A workspace refresh must leave its mounted draft owner in place. The protected
  // application read still invalidates on a change to the current session or app.
  const appRead = usePortalRead<PortalApplication>("/api/portal/applications/" + props.applicationId);
  const app = ready(appRead);
  const application = app?.applicationId === props.applicationId ? app : undefined;
  const applicationName = application?.metadata.displayName || application?.application;
  return <Shell wide signedIn={!snapshot.blocked}>
    <nav className="configuration-breadcrumb" aria-label="Breadcrumb">
      <Link href={applicationRoute({ applicationId: props.applicationId, applicationName, tab: "capabilities", context: props.context })}>
        Application capabilities</Link>
      {props.rulesetId === undefined ? null : <><span aria-hidden="true">/</span>
        <Link href={ruleEngineRoute({ applicationId: props.applicationId, applicationName, context: props.context })}>Rule engines</Link></>}
    </nav>
    <ReadNotice state={appRead} />
    {application === undefined ? null : props.rulesetId === undefined
      ? <RuleSets application={application} context={props.context} revision={revision} refresh={() => refresh(value => value + 1)} />
      : <RuleWorkspace application={application} rulesetId={props.rulesetId} view={props.view} initialVersion={props.initialVersion}
        newSchemaRevision={props.newSchemaRevision}
        executionId={props.executionId} history={props.history} context={props.context} appFresh={fresh(appRead)}
        revision={revision} refresh={() => refresh(value => value + 1)} />}
  </Shell>;
}

function RuleSets({ application, context, revision, refresh }: {
  readonly application: PortalApplication; readonly context: PortalListContext; readonly revision: number; readonly refresh: () => void;
}) {
  const router = useRouter();
  const applicationName = application.metadata.displayName || application.application;
  const base = apiBase(application.applicationId);
  const pages = usePages(application.applicationId + "/rule-engines", undefined);
  const listing = usePortalRead<Page<RuleSetSummary>>(queryUrl(base, { pageSize: "3", cursor: pages.cursor }), revision);
  const values = ready(listing);
  const environments = ready(usePortalRead<PortalPage<PortalApplication>>(queryUrl("/api/portal/applications", { query: application.application }), revision));
  const alternatives = environments?.items.filter(item => item.application === application.application && item.applicationId !== application.applicationId) ?? [];
  return <>
    <div className="page-heading"><div><h1>Rule engines</h1><p>{applicationName} <Badge>{application.environment}</Badge></p></div>
      <button type="button" disabled={listing.busy} onClick={refresh}>Refresh</button></div>
    <section className="card rm11-panel"><div className="section-heading"><h2>Registered rulesets</h2>
      <label>Environment<select aria-label="Environment" value={application.applicationId} onChange={event => {
        const next = alternatives.find(item => item.applicationId === event.target.value);
        if (next !== undefined) { router.push(ruleEngineRoute({ applicationId: next.applicationId,
          applicationName: next.metadata.displayName || next.application, context })); }
      }}><option value={application.applicationId}>{application.environment}</option>
        {alternatives.map(item => <option key={item.applicationId} value={item.applicationId}>{item.environment}</option>)}</select></label>
    </div><ReadNotice state={listing} restart={pages.reset} />
    {values === undefined ? null : <>
      <div className="table-frame"><table><thead><tr><th>Ruleset</th><th>Schema</th><th>Requested deployment</th><th>Status</th></tr></thead>
        <tbody>{values.items.filter(item => item.applicationId === application.applicationId && item.environment === application.environment).map(item =>
          <RuleSetEntry key={item.rulesetId} item={item} name={applicationName} context={context} base={base} revision={revision} />)}</tbody></table>
        {values.items.length === 0 ? <Empty title="No registered rulesets">No Rule Engine rulesets are available in this environment.</Empty> : null}</div>
      <Pager pages={pages} nextCursor={values.nextCursor} busy={listing.busy} label="Registered rulesets" />
    </>}</section>
  </>;
}
function RuleSetEntry({ item, name, context, base, revision }: {
  readonly item: RuleSetSummary; readonly name: string; readonly context: PortalListContext; readonly base: string; readonly revision: number;
}) {
  const schemaRead = usePortalRead<RuleSchema>(queryUrl(base + "/schema", { rulesetId: item.rulesetId, schemaRevision: item.schemaRevision }), revision);
  const deploymentRead = usePortalRead<RuleDeployment>(queryUrl(base + "/deployment", { rulesetId: item.rulesetId }), revision);
  const schema = scoped(ready(schemaRead), item.applicationId, item.rulesetId, item.environment);
  const desired = scoped(ready(deploymentRead), item.applicationId, item.rulesetId, item.environment);
  const columns = parseVersion(schema?.schemaJson, item.rulesetId, item.schemaRevision)?.columns.length;
  return <tr><td><Link href={ruleEngineRoute({ applicationId: item.applicationId, applicationName: name, rulesetId: item.rulesetId,
    rulesetName: item.displayName, context })}>{item.displayName}</Link></td>
    <td>{columns === undefined ? "Schema unavailable" : columns + (columns === 1 ? " column" : " columns")}</td>
    <td>{desired === undefined ? "Unavailable" : desired.desired === undefined ? "Unassigned" : ruleVersionLabel(desired.desired.version, [])}</td>
    <td><Badge>{deploymentRead.stale ? "Stale" : desired === undefined ? "Unavailable" : desired.desired === undefined ? "No request" : "Requested"}</Badge></td></tr>;
}

function RuleWorkspace({ application, rulesetId, view, initialVersion, newSchemaRevision, executionId, history, context, appFresh, revision, refresh }: {
  readonly application: PortalApplication; readonly rulesetId: string; readonly view: RulePortalView; readonly initialVersion: string | undefined;
  readonly newSchemaRevision: string | undefined; readonly executionId: string | undefined; readonly history: RuleHistoryReturn;
  readonly context: PortalListContext; readonly appFresh: boolean; readonly revision: number; readonly refresh: () => void;
}) {
  const router = useRouter();
  const { owner: portal, snapshot: session } = useProtectedPortal();
  const { applicationId, environment } = application;
  const name = application.metadata.displayName || application.application;
  const base = apiBase(applicationId);
  const target = { applicationId, rulesetId };
  const operation = useRuleCommand(target, base, environment);
  const ruleRead = usePortalRead<RuleSetSummary>(queryUrl(base + "/context", { rulesetId }), revision);
  const rule = scoped(ready(ruleRead), applicationId, rulesetId, environment);
  const grantsRead = usePortalRead<readonly RuleGrant[]>(base + "/capabilities", revision);
  const grants = ready(grantsRead);
  const granted = (id: string) => grants?.some(grant => grant.operationId === id && grant.allowed === true) === true;
  const versionPages = usePages(JSON.stringify([applicationId, rulesetId, "versions"]), undefined);
  const instancePages = usePages(JSON.stringify([applicationId, rulesetId, "adoption"]), undefined);
  const versionsRead = usePortalRead<Page<RuleVersionSummary>>(queryUrl(base + "/versions", { rulesetId, pageSize: "15",
    cursor: view === "versions" ? versionPages.cursor : undefined }), revision);
  const versions = scopedPage(ready(versionsRead), applicationId, rulesetId, environment);
  const [picked, select] = useState<string>();
  const [basis, setBasis] = useState<RuleEditingBasis>();
  const editing = view === "grid" || view === "new-version" || view === "reconcile";
  const activeBasis: RuleEditingBasis | undefined = newSchemaRevision === undefined ? basis : { schemaRevision: newSchemaRevision };
  useEffect(() => {
    if (editing && newSchemaRevision === undefined && basis === undefined && versions !== undefined && fresh(versionsRead)) {
      const saved = initialVersion ?? versions.items[0]?.version;
      if (saved !== undefined) { setBasis({ version: saved }); }
      else if (rule !== undefined && fresh(ruleRead)) { setBasis({ schemaRevision: rule.schemaRevision }); }
    }
  }, [editing, newSchemaRevision, basis, versions, initialVersion, versionsRead, rule, ruleRead]);
  const versionId = editing && activeBasis !== undefined ? activeBasis.version : picked ?? initialVersion ?? versions?.items[0]?.version;
  const draftSchemaRevision = editing ? activeBasis?.schemaRevision : undefined;
  const versionRead = usePortalRead<RuleVersion>(versionId === undefined || !["grid", "versions", "deploy", "reconcile"].includes(view) ? null
    : queryUrl(base + "/version", { rulesetId, version: versionId }), revision);
  const selectedVersion = scoped(ready(versionRead), applicationId, rulesetId, environment);
  const version = selectedVersion?.version === versionId ? selectedVersion : undefined;
  const deploymentRead = usePortalRead<RuleDeployment>(queryUrl(base + "/deployment", { rulesetId }), revision);
  const deployment = scoped(ready(deploymentRead), applicationId, rulesetId, environment);
  const deployedRead = usePortalRead<RuleVersion>(view !== "counters" || deployment?.desired === undefined ? null
    : queryUrl(base + "/version", { rulesetId, version: deployment.desired.version }), revision);
  const deployed = scoped(ready(deployedRead), applicationId, rulesetId, environment);
  const gridCountersRead = usePortalRead<RuleCounterPage>(editing && granted("listRuleEngineCounters")
    ? queryUrl(base + "/counters", { rulesetId, pageSize: "100" }) : null, revision);
  const gridCounters = scoped(ready(gridCountersRead), applicationId, rulesetId, environment);
  const statusRead = usePortalRead<Page<RuleDeploymentInstance>>(view === "deployment" || view === "versions"
    ? queryUrl(base + "/deployment-instances", { rulesetId, cursor: view === "deployment" ? instancePages.cursor : undefined, pageSize: "25" }) : null, revision);
  const status = ready(statusRead);
  const instances = status?.items.every(item => item.rulesetId === rulesetId && item.configuration.applicationId === applicationId
    && item.configuration.environment === environment) ? status : undefined;
  const activeSchemaRead = usePortalRead<RuleSchema>(draftSchemaRevision !== undefined && rule?.schemaRevision === draftSchemaRevision
    ? queryUrl(base + "/schema", { rulesetId, schemaRevision: draftSchemaRevision }) : null, revision);
  const schema = scoped(ready(activeSchemaRead), applicationId, rulesetId, environment);
  const initial = useMemo(() => {
    if (version !== undefined) { return parseVersion(version.definitionJson, rulesetId, version.schemaRevision, version.version); }
    if (schema === undefined || schema.schemaRevision !== draftSchemaRevision || rule?.schemaRevision !== draftSchemaRevision) { return undefined; }
    const empty = parseVersion(schema.schemaJson, rulesetId, schema.schemaRevision);
    return empty?.rows.length === 0 ? empty : undefined;
  }, [version, schema, draftSchemaRevision, rule?.schemaRevision, rulesetId]);
  const saveAllowed = granted("saveRuleEngineVersion") && granted("getRuleEngineVersionOperation");
  const held = useRuleDraft({ applicationId, rulesetId, environment, initial, versionId, draftSchemaRevision, rule, ruleRead,
    dataRead: versionId === undefined ? activeSchemaRead : versionRead, allowed: saveAllowed, grantsRead,
    active: editing && activeBasis !== undefined });
  const draft = held.draft ?? initial;
  const [note, setNote] = useState(""), [issue, setIssue] = useState<DraftIssue>(), [message, setMessage] = useState<string>();
  const [conflict, setConflict] = useState(view === "reconcile");
  const compareCurrent = usePortalRead<RuleVersion>(conflict && versions?.items[0] !== undefined
    ? queryUrl(base + "/version", { rulesetId, version: versions.items[0].version }) : null, revision);
  const canEdit = appFresh && held.editable && fresh(versionsRead) && !session.blocked && session.verification === undefined;
  const canDeploy = appFresh && deployment !== undefined && fresh(deploymentRead) && fresh(grantsRead)
    && granted("requestRuleEngineDeployment") && granted("getRuleEngineDeploymentOperation") && granted("getRuleEngineDeployment")
    && !session.blocked && session.context !== undefined && session.verification === undefined;
  const saveOutstanding = operation.receipts.some(receipt => receipt.family === saveOperation);
  const deployOutstanding = operation.receipts.some(receipt => receipt.family === deployOperation);
  const displayVersions = version === undefined ? versions?.items ?? [] : [version, ...(versions?.items ?? []).filter(item => item.version !== version.version)];
  const heading = rule?.displayName || "Rule engine";
  const selectedSchemaRevision = version?.schemaRevision ?? draftSchemaRevision;
  const earlierSchema = rule !== undefined && selectedSchemaRevision !== undefined && selectedSchemaRevision !== rule.schemaRevision;
  const newVersionPath = earlierSchema && appFresh && fresh(ruleRead) && fresh(versionsRead) && fresh(grantsRead) && saveAllowed
    && granted("getRuleEngineSchema") && !session.blocked && session.context !== undefined && session.verification === undefined
    ? ruleEngineRoute({ applicationId, applicationName: name, rulesetId, rulesetName: rule.displayName,
      view: "new-version", schemaRevision: rule.schemaRevision, context }) : undefined;
  const path = (next: RulePortalView, selection?: string) => ruleEngineRoute({ applicationId, applicationName: name, rulesetId, rulesetName: rule?.displayName,
    view: next, version: selection, context });
  const historyPath = (selection: RuleHistoryReturn, id?: string) => ruleEngineRoute({ applicationId, applicationName: name, rulesetId,
    rulesetName: rule?.displayName, view: id === undefined ? "executions" : "execution", executionId: id, history: selection, context });
  const historyProps = { applicationId, rulesetId, environment, base, versions: displayVersions, revision, history, path: historyPath };

  useEffect(() => {
    if (session.blocked || grantsRead.reply?.state === "denied" || grantsRead.reply?.state === "not-found" || ruleRead.reply?.state === "denied"
      || ruleRead.reply?.state === "not-found") { setNote(""); setIssue(undefined); setMessage(undefined); setConflict(false); }
  }, [session.blocked, session.epoch, grantsRead.reply?.state, ruleRead.reply?.state]);

  function receive(result: RuleOperationObservation | undefined) {
    if (result === undefined) { return; }
    if (result.disposition === "committed" && result.cleanupSucceeded && result.version !== undefined) {
      held.discard(); setNote(""); setIssue(undefined); setConflict(false); refresh();
      router.push(result.family === saveOperation ? path("versions", result.version) : path("deployment"));
      return;
    }
    if (result.disposition === "conflict" && result.family === saveOperation) {
      setConflict(true); refresh();
    } else if (result.disposition === "conflict") {
      setMessage("The requested deployment changed. Review the current target before trying again.");
      refresh();
    }
  }
  async function save() {
    if (!canEdit || held.editETag === undefined || draft === undefined || saveOutstanding || conflict || operation.busy) { return; }
    setMessage(undefined);
    const selected = prepareRuleDefinition(draft);
    if ("issue" in selected) { setIssue(selected.issue); return; }
    if (!ruleNote(note)) { setIssue({ message: "Enter a version note within 1,024 UTF-8 bytes." }); return; }
    setIssue(undefined);
    receive(await operation.start(saveOperation, held.editETag, { schemaRevision: draft.schemaRevision, definitionJson: selected.json, reason: note,
      ...(versionId === undefined ? {} : { baseVersion: versionId }) }));
  }
  async function deploy(reason: string, intent: "deploy" | "rollback") {
    if (!canDeploy || deployOutstanding || operation.busy || deployment === undefined || version === undefined || !fresh(versionRead)) { return; }
    receive(await operation.start(deployOperation, deployment.assignmentETag, { version: version.version, intent, reason }));
  }
  function reviewLatest() {
    const current = scoped(ready(compareCurrent), applicationId, rulesetId, environment);
    if (!canEdit || rule === undefined || draft === undefined || draft.schemaRevision !== rule.schemaRevision || current === undefined
      || current.version !== versions?.items[0]?.version || !fresh(compareCurrent) || !fresh(ruleRead)) { return; }
    held.acceptEditRevision(rule.editETag); setConflict(false); setMessage("Draft kept against the reviewed edit revision.");
  }

  return <>
    <div className="page-heading"><div><h1>{heading}</h1><p>{name} <Badge>{environment}</Badge></p></div>
      {deployment?.desired === undefined ? null : <div className="rm11-requested"><span>Requested deployment</span>
        <strong>{ruleVersionLabel(deployment.desired.version, displayVersions)}</strong></div>}</div>
    <nav className="tabs rm11-tabs" aria-label="Rule engine views">
      <Link href={path("grid")} aria-current={editing ? "page" : undefined}>Grid</Link>
      <Link href={path("versions")} aria-current={view === "versions" || view === "deploy" ? "page" : undefined}>Versions</Link>
      <Link href={path("deployment")} aria-current={view === "deployment" ? "page" : undefined}>Deployment</Link>
      {granted("listRuleEngineCounters") ? <Link href={path("counters")} aria-current={view === "counters" ? "page" : undefined}>Counters</Link>
        : <span aria-disabled="true">Counters</span>}
      {granted("listRuleEngineExecutions") ? <Link href={historyPath({ filters: {} })}
        aria-current={view === "executions" || view === "execution" ? "page" : undefined}>Executions</Link> : <span aria-disabled="true">Executions</span>}
    </nav>
    <ReadNotice state={ruleRead} />
    {message === undefined ? null : <p className="notice" role="status">{message}</p>}
    {["counters", "executions", "execution"].includes(view) ? null : <RuleOriginalResults operation={operation} onResult={receive} />}
    {view === "counters" ? <RuleCounters applicationId={applicationId} applicationName={name} rulesetId={rulesetId} rulesetName={heading}
      environment={environment} base={base} revision={revision} refresh={refresh} operation={operation}
      canReset={appFresh && fresh(grantsRead) && granted("resetRuleEngineCounters") && granted("getRuleEngineCounterResetOperation")
        && granted("listRuleEngineCounterResetOutcomes")} deployedDefinition={deployed?.version !== deployment?.desired?.version ? undefined
        : parseVersion(deployed?.definitionJson, rulesetId, deployed?.schemaRevision, deployed?.version)} /> : null}
    {view === "executions" ? <RuleExecutionHistory {...historyProps} /> : null}
    {view === "execution" && executionId !== undefined ? <RuleExecutionInspection {...historyProps} executionId={executionId} /> : null}
    {view === "deployment" ? <><ReadNotice state={deploymentRead} />
      <RuleAdoption deployment={deployment} state={statusRead} page={instances} pages={instancePages} versions={displayVersions} refresh={refresh} /></> : null}
    {view === "versions" || view === "deploy" ? <RuleVersions base={base} application={application} rulesetId={rulesetId} versions={versions}
      versionsRead={versionsRead} versionRead={versionRead} selected={version} definition={initial} deployment={deployment}
      deploymentFresh={fresh(deploymentRead)} instances={fresh(statusRead) ? instances : undefined}
      pages={versionPages} choose={select} path={path} newVersionPath={newVersionPath}
      enableEdit={fresh(grantsRead) && saveAllowed && !session.blocked
        && version?.schemaRevision === rule?.schemaRevision} enableDeploy={canDeploy && !deployOutstanding} revision={revision} /> : null}
    {view === "deploy" && version !== undefined && deployment !== undefined ? <RuleDeployDialog
      active={!session.blocked && session.verification === undefined} selected={version} versions={displayVersions}
      applicationName={name} environment={environment} rulesetName={heading} deployment={deployment}
      canSubmit={canDeploy && fresh(versionRead) && !deployOutstanding && !operation.busy} onCancel={() => router.push(path("versions", version.version))}
      onDeploy={(reason, intent) => { void deploy(reason, intent); }} /> : null}
    {editing ? <>
      <ReadNotice state={versionsRead} /><ReadNotice state={versionId === undefined ? activeSchemaRead : versionRead} />
      {earlierSchema ? <div className="notice">
        <p role="status">{version === undefined ? "This draft’s schema differs from the current registered schema."
          : "This saved version uses an earlier schema. Its definition remains read-only."}</p>
        {newVersionPath === undefined ? null : <Link className="button" href={newVersionPath}>Start version with current schema</Link>}
      </div> : null}
      {initial === undefined && (versionRead.reply?.state === "ready" || activeSchemaRead.reply?.state === "ready")
        ? <Empty title="Definition unavailable">The definition could not be read for this ruleset.</Empty> : null}
      {draft === undefined ? null : <>
        {conflict ? <section className="card rm11-panel"><h2>Resolve save conflict</h2><p>Your draft is kept. Compare the current saved version before reusing its edit revision.</p>
          <ReadNotice state={compareCurrent} />
          <Facts><Fact label="Draft base">{ruleVersionLabel(versionId, displayVersions)}</Fact><Fact label="Latest saved">{ruleVersionLabel(ready(compareCurrent)?.version, displayVersions)}</Fact></Facts>
          {ready(compareCurrent) === undefined ? null : <ChangedRules previous={draft}
            current={parseVersion(ready(compareCurrent)?.definitionJson, rulesetId, ready(compareCurrent)?.schemaRevision, ready(compareCurrent)?.version)} />}
          <button type="button" disabled={!canEdit || !fresh(compareCurrent) || !fresh(ruleRead) || rule?.schemaRevision !== draft.schemaRevision}
            onClick={reviewLatest}>Keep draft and use latest revision</button>
        </section> : null}
        <RuleGrid draft={draft} editable={canEdit && !operation.busy} change={next => { held.change(next); setIssue(undefined); }} issue={issue}
          hits={gridCounters?.items} hitsStale={gridCountersRead.stale === true || gridCounters?.availability === "stale"}
          actions={held.editable ? <button className="primary" type="button" disabled={!canEdit || !held.dirty || conflict || saveOutstanding || operation.busy}
            onClick={() => { void save(); }}>Save version</button> : undefined} />
        {held.editable ? <div className="rm11-save-footer"><label>Version note<input value={note} readOnly={!canEdit || operation.busy}
          onChange={event => setNote(event.target.value)} maxLength={1024} /></label><p role="status">{held.dirty ? "Unsaved changes" : "No unsaved changes"}</p></div> : null}
      </>}
    </> : null}
    <TechnicalDetails fields={[{ label: "Application ID", value: applicationId }, { label: "Ruleset ID", value: rulesetId },
      { label: "Schema revision", value: rule?.schemaRevision }, { label: "Saved version", value: versionId }, { label: "Edit revision", value: rule?.editETag }]} />
  </>;
}

function useRuleDraft(input: { readonly applicationId: string; readonly rulesetId: string; readonly environment: string;
  readonly initial: RuleDraft | undefined; readonly versionId: string | undefined; readonly draftSchemaRevision: string | undefined;
  readonly rule: RuleSetSummary | undefined;
  readonly ruleRead: ReadState<unknown>; readonly dataRead: ReadState<unknown>; readonly grantsRead: ReadState<unknown>;
  readonly allowed: boolean; readonly active: boolean }) {
  const { owner: portal, snapshot: session } = useProtectedPortal();
  const [owner] = useState(() => new ProtectedStateOwner<boolean>({ context: session.context, receipts: portal.receipts }));
  const [, redraw] = useState(0);
  const [editRevision, setEditRevision] = useState<{ key: string; eTag: string }>();
  const raw = input.initial === undefined ? undefined : JSON.stringify(input.initial);
  const key = JSON.stringify([input.applicationId, input.rulesetId, input.environment, input.versionId ?? null, input.draftSchemaRevision ?? null]);
  const denied = unsafe(input.ruleRead) || unsafe(input.dataRead) || unsafe(input.grantsRead)
    || fresh(input.grantsRead) && !input.allowed;
  const compatible = input.initial !== undefined && input.rule !== undefined && input.initial.schemaRevision === input.rule.schemaRevision;
  const readyToEdit = input.active && !denied && input.allowed && compatible && fresh(input.ruleRead) && fresh(input.dataRead) && fresh(input.grantsRead);
  useEffect(() => portal.register(owner), [portal, owner]);
  useEffect(() => {
    if (!input.active || denied || session.blocked || session.context === undefined) {
      owner.selectTarget(undefined); setEditRevision(undefined); redraw(value => value + 1);
      return;
    }
    if (!readyToEdit || raw === undefined || input.rule === undefined || session.verification !== undefined) { return; }
    let current = true;
    const eTag = input.rule.editETag;
    owner.selectTarget({ requestKey: key, applicationId: input.applicationId, definitionId: input.rulesetId, environment: input.environment });
    void owner.request(async ticket => ticket.context === undefined ? { state: "unauthenticated" }
      : { state: "ready", context: ticket.context, value: true }).then(() => {
      if (!current || owner.view.target?.requestKey !== key || !owner.view.canWrite) { return; }
      if (owner.view.draft === undefined) { owner.startDraft(raw); }
      setEditRevision(previous => previous?.key === key ? previous : { key, eTag });
      redraw(value => value + 1);
    });
    return () => { current = false; };
  }, [denied, readyToEdit, owner, input.active, input.applicationId, input.rulesetId, input.environment, input.rule?.editETag,
    key, raw, session.blocked, session.context, session.verification]);
  const state = owner.view;
  const supportedDraft = !denied && !session.blocked && input.active && state.target?.requestKey === key ? state.draft : undefined;
  const draft = supportedDraft === undefined ? undefined : JSON.parse(supportedDraft.text) as RuleDraft;
  return { draft, dirty: supportedDraft?.dirty === true, editable: readyToEdit && supportedDraft !== undefined && state.canWrite,
    editETag: editRevision?.key === key ? editRevision.eTag : undefined,
    change(next: RuleDraft) { if (owner.editDraft(JSON.stringify(next)).kind === "changed") { redraw(value => value + 1); } },
    discard() { owner.discardDraft(); redraw(value => value + 1); },
    acceptEditRevision(eTag: string) { setEditRevision({ key, eTag }); }
  };
}

function RuleVersions({ base, application, rulesetId, versions, versionsRead, versionRead, selected, definition, deployment,
  deploymentFresh, instances, pages, choose, path, newVersionPath, enableEdit, enableDeploy, revision }: {
  readonly base: string; readonly application: PortalApplication; readonly rulesetId: string;
  readonly versions: Page<RuleVersionSummary> | undefined; readonly versionsRead: ReadState<Page<RuleVersionSummary>>;
  readonly versionRead: ReadState<RuleVersion>; readonly selected: RuleVersion | undefined; readonly definition: RuleDraft | undefined;
  readonly deployment: RuleDeployment | undefined; readonly deploymentFresh: boolean; readonly instances: Page<RuleDeploymentInstance> | undefined;
  readonly pages: ReturnType<typeof usePages>; readonly choose: (version: string | undefined) => void;
  readonly newVersionPath: string | undefined;
  readonly path: (view: RulePortalView, version?: string) => string; readonly enableEdit: boolean; readonly enableDeploy: boolean; readonly revision: number;
}) {
  const [compare, setCompare] = useState(false);
  const [showRules, setShowRules] = useState(false);
  const desired = deployment?.desired;
  const uniform = deploymentFresh && instances !== undefined && instances.nextCursor === undefined && instances.items.length > 0
    && desired !== undefined && instances.items.every(instance => instance.configuration.registrationId !== undefined
      && instance.configuration.registrationFreshness === "fresh" && instance.configuration.adoptionFreshness === "fresh"
      && instance.configuration.convergence === "applied" && instance.appliedVersion === desired.version
      && instance.configuration.desired?.generation === desired.generation) ? desired.version : undefined;
  const comparedRead = usePortalRead<RuleVersion>(compare && uniform !== undefined && uniform !== selected?.version
    ? queryUrl(base + "/version", { rulesetId, version: uniform }) : null, revision);
  const compared = scoped(ready(comparedRead), application.applicationId, rulesetId, application.environment);
  const comparedDefinition = compared?.version === uniform ? parseVersion(compared?.definitionJson, rulesetId, compared?.schemaRevision, uniform) : undefined;
  function selectVersion(id: string) { choose(id); setCompare(false); setShowRules(false); }
  return <>
    <div className="notice"><strong>{deployment === undefined ? "Requested deployment unavailable" : desired === undefined ? "No requested deployment"
      : "Requested deployment · " + ruleVersionLabel(desired.version, versions?.items ?? [])}</strong>
      <p>Saved versions remain immutable. Instance reports are available in Deployment.</p></div>
    <div className="rm11-versions">
      <section className="card rm11-panel"><h2>Saved versions</h2><ReadNotice state={versionsRead} restart={pages.reset} />
        {versions?.items.length === 0 ? <Empty title="No saved versions">Save a version from the Grid.</Empty> : null}
        {versions === undefined ? null : <><ul className="rm11-version-list">{versions.items.map(item => <li key={item.version}>
          <button type="button" aria-current={selected?.version === item.version ? "true" : undefined} onClick={() => selectVersion(item.version)}>
            {ruleVersionLabel(item.version, versions.items)}</button>
          <Timestamp value={item.savedAtUtc} compact />
          {item.version === desired?.version ? <Badge>Requested</Badge> : null}
        </li>)}</ul><Pager pages={pages} nextCursor={versions.nextCursor} busy={versionsRead.busy} label="Saved versions" changed={() => choose(undefined)} /></>}
      </section>
      <section className="card rm11-panel"><ReadNotice state={versionRead} />
        {selected === undefined || definition === undefined ? null : <>
          <div className="section-heading"><div><h2>{ruleVersionLabel(selected.version, [selected])}</h2>
            <p>Saved <Timestamp value={selected.savedAtUtc} /> · Read only</p></div>
            <div className="rm11-actions">{enableEdit && fresh(versionRead) ? <Link className="button" href={path("grid", selected.version)}>Edit as new version</Link> : null}
              {newVersionPath === undefined || !fresh(versionRead) ? null : <Link className="button" href={newVersionPath}>Start version with current schema</Link>}
              {enableDeploy && fresh(versionRead) ? <Link className="button primary" href={path("deploy", selected.version)}>Deploy</Link> : null}</div>
          </div>
          <Facts><Fact label="Execution mode">{modeLabels[definition.mode]}</Fact><Fact label="Schema">{definition.displayName ?? definition.schemaRevision}</Fact></Facts>
          {showRules ? <RuleGrid draft={definition} editable={false} change={() => {}} /> : <div className="table-frame">
            <table><thead><tr><th>Order</th><th>Rule</th><th>State</th><th>Weight</th></tr></thead>
              <tbody>{definition.rows.map(row => <tr key={row.ruleId}><td>{row.ordinal}</td><td>{ruleRowLabel(row)}</td>
                <td><Badge>{stateLabels[row.state]}</Badge></td><td>{row.weight}</td></tr>)}</tbody></table></div>}
          <div className="rm11-actions">
            <button type="button" onClick={() => setShowRules(value => !value)}>{showRules ? "Hide saved rules" : "View saved rules"}</button>
            {uniform === undefined ? <span className="muted">Comparison unavailable for the current rollout.</span>
              : <button type="button" onClick={() => setCompare(value => !value)}>{compare ? "Hide comparison" : "Compare with deployed"}</button>}
          </div>
          {compare && uniform !== undefined ? uniform === selected.version ? <p role="status">The selected version matches the deployed version.</p>
            : <><ReadNotice state={comparedRead} />{fresh(comparedRead) ? <ChangedRules previous={definition} current={comparedDefinition} /> : null}</> : null}
          <TechnicalDetails fields={[{ label: "Saved version ID", value: selected.version }, { label: "Schema revision", value: selected.schemaRevision }]} />
        </>}
      </section>
    </div>
  </>;
}

function RuleDeployDialog({ active, selected, versions, applicationName, environment, rulesetName, deployment, canSubmit, onCancel, onDeploy }: {
  readonly active: boolean; readonly selected: RuleVersion; readonly versions: readonly RuleVersionSummary[];
  readonly applicationName: string; readonly environment: string; readonly rulesetName: string; readonly deployment: RuleDeployment;
  readonly canSubmit: boolean; readonly onCancel: () => void; readonly onDeploy: (reason: string, intent: "deploy" | "rollback") => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [reason, setReason] = useState("");
  const requested = deployment.desired?.version;
  const ordered = [...versions].sort((left, right) => right.savedAtUtc.localeCompare(left.savedAtUtc));
  const earlier = requested !== undefined && ordered.findIndex(item => item.version === requested) >= 0
    && ordered.findIndex(item => item.version === selected.version) > ordered.findIndex(item => item.version === requested);
  const [intent, setIntent] = useState<"deploy" | "rollback">(earlier ? "rollback" : "deploy");
  useEffect(() => {
    const current = dialog.current;
    if (active && current !== null && !current.open) { current.showModal(); }
    if (!active && current?.open) { current.close(); }
    return () => { if (current?.open) { current.close(); } };
  }, [active]);
  return <dialog className="configuration-deploy-dialog" ref={dialog} aria-labelledby="rm11-confirm-deployment"
    onCancel={event => { event.preventDefault(); onCancel(); }}>
    <h2 id="rm11-confirm-deployment">Deploy {ruleVersionLabel(selected.version, [selected])}</h2>
    <p>{rulesetName} · {applicationName} · {environment}</p>
    <Facts><Fact label="Deployment scope" wide>All instances in {environment}</Fact>
      <Fact label="Currently requested">{requested === undefined ? "None" : ruleVersionLabel(requested, versions)}</Fact>
      <Fact label="Selected saved version">{ruleVersionLabel(selected.version, [selected])}</Fact></Facts>
    {requested === undefined ? null : <label>Intent<select aria-label="Intent" value={intent} onChange={event => setIntent(event.target.value as "deploy" | "rollback")}>
      <option value="deploy">Deploy selected version</option><option value="rollback">Roll back to an earlier version</option>
    </select></label>}
    <label>Deployment reason<input value={reason} maxLength={1024} onChange={event => setReason(event.target.value)} /></label>
    <p>Each instance reports its accepted version separately.</p>
    <div className="rm11-actions"><button type="button" onClick={onCancel}>Cancel</button>
      <button className="primary" type="button" disabled={!canSubmit || !ruleNote(reason) || requested === selected.version}
        onClick={() => onDeploy(reason, intent)}>Deploy version</button></div>
  </dialog>;
}
function ChangedRules({ previous, current }: { readonly previous: RuleDraft; readonly current: RuleDraft | undefined }) {
  if (current === undefined) { return <p>Comparison unavailable.</p>; }
  const yours = new Map(previous.rows.map(row => [row.ruleId, row]));
  const theirs = new Map(current.rows.map(row => [row.ruleId, row]));
  const changes = [...new Set([...yours.keys(), ...theirs.keys()])].flatMap(id => {
    const left = yours.get(id), right = theirs.get(id), selected = left ?? right;
    return selected === undefined || JSON.stringify(left) === JSON.stringify(right) ? []
      : [{ id, label: ruleRowLabel(selected), change: left === undefined ? "Only in compared version" : right === undefined ? "Only in selected version" : "Changed" }];
  });
  return <section aria-label="Rule changes"><h3>Rule changes</h3>{changes.length === 0 ? <p>No rule differences.</p>
    : <ul>{changes.map(item => <li key={item.id}>{item.label} · {item.change}</li>)}</ul>}</section>;
}
function fresh(state: ReadState<unknown>) { return !state.busy && !state.stale && state.reply?.state === "ready"; }
function unsafe(state: ReadState<unknown>) { return state.reply?.state === "denied" || state.reply?.state === "not-found" || state.reply?.state === "unauthenticated"; }
function scoped<T extends { readonly applicationId: string; readonly rulesetId: string; readonly environment: string }>(value: T | undefined,
  application: string, ruleset: string, environment: string): T | undefined {
  return value?.applicationId === application && value.rulesetId === ruleset && value.environment === environment ? value : undefined;
}
function scopedPage<T extends { readonly applicationId: string; readonly rulesetId: string; readonly environment: string }>(page: Page<T> | undefined,
  application: string, ruleset: string, environment: string) {
  return page?.items.every(item => scoped(item, application, ruleset, environment) !== undefined) ? page : undefined;
}
function parseVersion(raw: string | undefined, ruleset: string, revision: string | undefined, version?: string): RuleDraft | undefined {
  if (raw === undefined || revision === undefined) { return undefined; }
  try { return parseRuleDefinition(raw, ruleset, revision, version); } catch { return undefined; }
}
function apiBase(applicationId: string) { return "/api/portal/applications/" + applicationId + "/rule-engine"; }
