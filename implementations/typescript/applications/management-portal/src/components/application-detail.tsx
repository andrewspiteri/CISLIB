"use client";

import Link from "next/link";
import * as Tabs from "@radix-ui/react-tabs";
import { useEffect, useRef, useState } from "react";
import type { MouseEvent, ReactNode } from "react";
import type { PortalApplication, PortalCapabilities, PortalCapability, PortalDefinition, PortalInstance, PortalInventory, PortalJob, PortalJobObservation, PortalPage } from "../portal-model";
import { Badge, Empty, Fact, Facts, Pager, Panel, ReadNotice, Shell, Timestamp, isUnauthenticated, queryUrl, ready, usePages, usePortalRead } from "./shared";

export type ApplicationTab = "instances" | "configuration" | "capabilities" | "jobs";
export interface ListContext { readonly fromQuery?: string; readonly fromEnvironment?: string; readonly fromCursor?: string }
interface JobSelection { readonly report: PortalInventory; readonly job: PortalJob; readonly observation?: PortalJobObservation }
type Selected = { readonly kind: "instance"; readonly item: PortalInstance } | { readonly kind: "configuration"; readonly item: PortalDefinition }
  | { readonly kind: "capability"; readonly item: PortalCapability } | { readonly kind: "job"; readonly item: JobSelection };

export function ApplicationDetail({ applicationId, initialTab = "instances", initialCursor, context }: {
  applicationId: string; initialTab?: ApplicationTab; initialCursor?: string; context: ListContext
}) {
  const [tab, setTab] = useState<ApplicationTab>(initialTab);
  const [revision, refresh] = useState(0);
  const [selection, select] = useState<{ tab: ApplicationTab; key: string }>();
  const panelOrigin = useRef<HTMLElement | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const base = `/api/portal/applications/${applicationId}`;
  const appState = usePortalRead<PortalApplication>(base, revision);
  const pages = usePages(JSON.stringify([applicationId, tab]), initialCursor);
  const contentUrl = tab === "jobs" ? "inventory" : tab;
  const listState = usePortalRead<PortalPage<PortalInstance | PortalDefinition | PortalInventory> | PortalCapabilities>(
    ready(appState) === undefined ? null : queryUrl(`${base}/${contentUrl}`, { cursor: tab === "capabilities" ? undefined : pages.cursor }), revision);
  const blocked = isUnauthenticated(appState, listState);
  const application = blocked ? undefined : ready(appState);
  const value = blocked || application === undefined ? undefined : ready(listState);
  const selected = selection?.tab === tab && value !== undefined ? findSelection(tab, value, selection.key) : undefined;
  useEffect(() => {
    if (appState.reply !== undefined && appState.reply.state !== "ready" || listState.reply !== undefined && listState.reply.state !== "ready") { select(undefined); }
  }, [appState.reply, listState.reply]);
  function open(key: string, event: MouseEvent<HTMLButtonElement>) {
    panelOrigin.current = event.currentTarget;
    select({ tab, key });
  }
  const back = queryUrl("/applications", { query: context.fromQuery, environment: context.fromEnvironment, cursor: context.fromCursor });
  return <Shell signedIn={!blocked}><Link className="back-link" href={back}>← Back to applications</Link>
    <div className="page-heading"><div><div className="eyebrow">Application</div><h1 ref={heading} tabIndex={-1}>{application?.metadata.displayName || application?.application || "Application details"}</h1>
      {application !== undefined ? <p>{application.application}<span className="heading-separator">/</span><Badge>{application.environment}</Badge></p> : null}</div>
      <button disabled={appState.busy || listState.busy} onClick={() => refresh(value => value + 1)}>Refresh</button></div>
    <ReadNotice state={blocked && listState.reply?.state === "unauthenticated" ? listState : appState} />
    {application !== undefined ? <><p className="identifier app-identity">Application ID {application.applicationId}</p>
      <Tabs.Root value={tab} onValueChange={value => { select(undefined); setTab(value as ApplicationTab); }}>
        <Tabs.List className="tabs" aria-label="Application details"><Tabs.Trigger value="instances">Instances</Tabs.Trigger><Tabs.Trigger value="configuration">Configuration</Tabs.Trigger>
          <Tabs.Trigger value="capabilities">Capabilities</Tabs.Trigger><Tabs.Trigger value="jobs">Scheduled jobs</Tabs.Trigger></Tabs.List>
        <Tabs.Content className="tab-content" value={tab}>
          <div className="section-heading"><div><h2>{({ instances: "Instances", configuration: "Configuration list", capabilities: "Capabilities", jobs: "Scheduled jobs" })[tab]}</h2>
            <p>{({ instances: "Reported application versions belong to individual registrations.", configuration: "Definitions registered for this application and environment. Metadata only.",
              capabilities: "Management API descriptors and advisory permission hints. Each request is authorized independently.", jobs: "One complete reported inventory at a time. Supported modes do not establish a schedule." })[tab]}</p></div></div>
          <ReadNotice state={listState} restart={() => { select(undefined); pages.reset(); }} />
          {value !== undefined ? <DetailList tab={tab} value={value} open={open} /> : null}
          {value !== undefined && "items" in value ? <Pager pages={pages} nextCursor={value.nextCursor} busy={listState.busy} changed={() => select(undefined)} label={tab === "jobs" ? "Registration inventories" : `${value.items.length} items on this page`} /> : null}
        </Tabs.Content>
      </Tabs.Root>
      <Panel open={selected !== undefined} close={() => select(undefined)} title={selectedTitle(selected)} eyebrow={selected?.kind === "job" ? "Scheduled job" : "Details"}
        subtitle={`${application.application} · ${application.environment}`} origin={panelOrigin.current} fallback={heading.current}
        footer={selected?.kind === "job" ? <Link className="button primary" href={jobLink(applicationId, selected.item, pages.cursor, context)}>View full details →</Link> : undefined}>
        {selected !== undefined ? <SelectedDetails selected={selected} /> : null}
      </Panel>
    </> : null}
  </Shell>;
}

function DetailList({ tab, value, open }: { tab: ApplicationTab; value: PortalPage<PortalInstance | PortalDefinition | PortalInventory> | PortalCapabilities; open: (key: string, event: MouseEvent<HTMLButtonElement>) => void }) {
  if (tab === "capabilities" && "implementedApis" in value) {
    return <div className="table-frame"><table><thead><tr><th>Operation</th><th>API version</th><th>Method / path</th><th>Caller hint</th></tr></thead><tbody>
      {value.implementedApis.map(item => <tr key={capabilityKey(item)}><td><Item onClick={event => open(capabilityKey(item), event)}>{item.operationId}</Item></td><td>{item.version}</td><td><code>{item.method} {item.path}</code></td><td><Badge>{hint(item.callerHint)}</Badge></td></tr>)}
    </tbody></table>{value.implementedApis.length === 0 ? <Empty title="No capabilities reported">No API descriptors are available.</Empty> : null}</div>;
  }
  if (!("items" in value)) { return null; }
  if (tab === "instances") {
    const items = value.items as readonly PortalInstance[];
    return <div className="table-frame"><table><thead><tr><th>Instance</th><th>Reported app version</th><th>Presence</th><th>Last seen</th></tr></thead><tbody>{items.map(item => <tr key={item.registrationId}>
      <td><Item onClick={event => open(item.registrationId, event)}>{item.reportedInstanceId}</Item><span className="row-subtitle identifier">{item.registrationId}</span></td><td>{item.applicationVersion}</td><td><Badge>{item.presence}</Badge></td><td><Timestamp value={item.lastSeenAtUtc} /></td>
    </tr>)}</tbody></table>{items.length === 0 ? <Empty title="No instances reported">This application has no visible registrations on this page.</Empty> : null}</div>;
  }
  if (tab === "configuration") {
    const items = value.items as readonly PortalDefinition[];
    return <div className="table-frame"><table><thead><tr><th>Configuration ID</th><th>Schema version</th><th>Source ID</th><th>Environment</th></tr></thead><tbody>{items.map(item => <tr key={definitionKey(item)}>
      <td><Item onClick={event => open(definitionKey(item), event)}>{item.configurationId}</Item>{item.description !== undefined ? <span className="row-description">{item.description}</span> : null}</td><td>{item.schemaVersion}</td><td className="identifier">{item.sourceId}</td><td><Badge>{item.environment}</Badge></td>
    </tr>)}</tbody></table>{items.length === 0 ? <Empty title="No configuration definitions">No application definitions are visible on this page.</Empty> : null}</div>;
  }
  const reports = value.items as readonly PortalInventory[];
  return <>{reports.length === 0 ? <div className="table-frame"><Empty title="No job inventory reported">No complete inventory is available for this application.</Empty></div> : null}
    {reports.map(report => <section key={report.registrationId} aria-label="Registration inventory"><div className="inventory-summary"><strong>Reported inventory</strong><span className="identifier">Registration {report.registrationId}</span>
      <span>App version {report.applicationVersion} · Report {report.inventory.sequence}</span><span>Observed <Timestamp value={report.inventory.observedAtUtc} /></span><span>Received <Timestamp value={report.receivedAtUtc} /></span></div>
      <div className="table-frame"><table><thead><tr><th>Job</th><th>Resource / mode</th><th>Reported state</th><th>Last outcome</th></tr></thead><tbody>{jobRows(report).map(item => <tr key={jobKey(item)}>
        <td><Item onClick={event => open(jobKey(item), event)}>{item.job.displayName}</Item><span className="row-subtitle">{item.job.executionScope} / {item.job.jobId}</span><span className="row-description">{categoryName(report, item.job)}</span></td>
        <td>{item.observation === undefined ? "Not reported" : <>{item.observation.resourceId}<span className="row-subtitle">{item.observation.mode}</span></>}</td><td><Badge>{item.observation?.state ?? "Not reported"}</Badge></td><td>{item.observation?.lastOutcome ?? "Not reported"}</td>
      </tr>)}</tbody></table>{report.inventory.jobs.length === 0 ? <Empty title="No jobs in this report">The selected registration reported an empty job inventory.</Empty> : null}</div>
    </section>)}</>;
}
function Item({ onClick, children }: { onClick: (event: MouseEvent<HTMLButtonElement>) => void; children: ReactNode }) { return <button className="item-link" onClick={onClick}>{children}</button>; }
function hint(value: PortalCapability["callerHint"]) { return ({ allowed: "Allowed hint", denied: "Denied hint", notEvaluated: "Not evaluated" })[value]; }
function definitionKey(item: PortalDefinition) { return JSON.stringify([item.applicationId, item.environment, item.configurationId, item.schemaVersion, item.sourceId, item.bindingId]); }
function capabilityKey(item: PortalCapability) { return JSON.stringify([item.operationId, item.version, item.method, item.path]); }
export function jobRows(report: PortalInventory): readonly JobSelection[] {
  return report.inventory.jobs.flatMap<JobSelection>(job => job.observations?.length ? job.observations.map(observation => ({ report, job, observation })) : [{ report, job }]);
}
function jobKey(item: JobSelection) { return JSON.stringify([item.report.registrationId, item.report.inventory.sequence, item.job.executionScope, item.job.jobId, item.observation?.resourceId, item.observation?.mode]); }
export function categoryName(report: PortalInventory, job: PortalJob) { return report.inventory.categories.find(category => category.executionScope === job.executionScope && category.categoryId === job.categoryId)?.displayName ?? job.categoryId; }
function findSelection(tab: ApplicationTab, value: PortalPage<PortalInstance | PortalDefinition | PortalInventory> | PortalCapabilities, key: string): Selected | undefined {
  if (tab === "capabilities" && "implementedApis" in value) { const item = value.implementedApis.find(item => capabilityKey(item) === key); return item === undefined ? undefined : { kind: "capability", item }; }
  if (!("items" in value)) { return undefined; }
  if (tab === "instances") { const item = (value.items as readonly PortalInstance[]).find(item => item.registrationId === key); return item === undefined ? undefined : { kind: "instance", item }; }
  if (tab === "configuration") { const item = (value.items as readonly PortalDefinition[]).find(item => definitionKey(item) === key); return item === undefined ? undefined : { kind: "configuration", item }; }
  const item = (value.items as readonly PortalInventory[]).flatMap(jobRows).find(item => jobKey(item) === key);
  return item === undefined ? undefined : { kind: "job", item };
}
function selectedTitle(value: Selected | undefined) { if (value === undefined) { return "Item details"; } return value.kind === "instance" ? value.item.reportedInstanceId : value.kind === "configuration" ? value.item.configurationId : value.kind === "capability" ? value.item.operationId : value.item.job.displayName; }
function SelectedDetails({ selected }: { selected: Selected }) {
  switch (selected.kind) {
    case "instance": { const item = selected.item; return <><Facts><Fact label="Reported app version">{item.applicationVersion}</Fact><Fact label="Presence">{item.presence}</Fact><Fact label="Registration ID" wide>{item.registrationId}</Fact><Fact label="Reported instance ID" wide>{item.reportedInstanceId}</Fact>
      <Fact label="Instance evidence">{item.instanceEvidence.kind}</Fact><Fact label="Verified identifier">{item.instanceEvidence.verifiedIdentifier}</Fact><Fact label="Process started"><Timestamp value={item.processStartedAtUtc} /></Fact><Fact label="Registered"><Timestamp value={item.registeredAtUtc} /></Fact>
      <Fact label="Last seen"><Timestamp value={item.lastSeenAtUtc} /></Fact><Fact label="Closed"><Timestamp value={item.closedAtUtc} /></Fact><Fact label="Lifecycle">{item.observation?.lifecycle}</Fact><Fact label="Health">{item.observation?.health}</Fact>
      <Fact label="Observation sequence">{item.observation?.sequence}</Fact><Fact label="Observed"><Timestamp value={item.observation?.observedAtUtc} /></Fact><Fact label="Observation received"><Timestamp value={item.observationReceivedAtUtc} /></Fact></Facts><p className="notice">Instance labels and presence are reported observations. They do not establish distinct processes or an application-wide current version.</p></>; }
    case "configuration": { const item = selected.item; return <><Facts><Fact label="Configuration ID">{item.configurationId}</Fact><Fact label="Schema version">{item.schemaVersion}</Fact><Fact label="Kind">{item.kind}</Fact><Fact label="Environment">{item.environment}</Fact><Fact label="Source ID" wide>{item.sourceId}</Fact><Fact label="Binding ID" wide>{item.bindingId}</Fact><Fact label="Application ID" wide>{item.applicationId}</Fact><Fact label="Host application" wide>{item.hostApplicationName}</Fact><Fact label="Description" wide>{item.description}</Fact></Facts><p className="notice">Metadata only. Values, defaults and publication content are not included in this view.</p></>; }
    case "capability": { const item = selected.item; return <><Facts><Fact label="Operation ID" wide>{item.operationId}</Fact><Fact label="API version">{item.version}</Fact><Fact label="Method">{item.method}</Fact><Fact label="Path" wide>{item.path}</Fact><Fact label="Required capabilities" wide>{item.requiredCapabilities.length === 0 ? "None reported" : item.requiredCapabilities.join(", ")}</Fact><Fact label="Caller hint">{hint(item.callerHint)}</Fact></Facts><p className="notice">Descriptors describe the Management service APIs. Permission hints are advisory and grant no authority.</p></>; }
    case "job": return <><JobFacts {...selected.item} /><ReportFacts report={selected.item.report} /></>;
  }
}
export function JobFacts({ report, job, observation }: JobSelection) { return <Facts><Fact label="Execution scope">{job.executionScope}</Fact><Fact label="Job ID">{job.jobId}</Fact><Fact label="Category">{categoryName(report, job)}</Fact><Fact label="Supported modes">{job.supportedModes.join(", ")}</Fact><Fact label="Resource">{observation?.resourceId}</Fact><Fact label="Observed mode">{observation?.mode}</Fact><Fact label="State">{observation?.state}</Fact><Fact label="Last outcome">{observation?.lastOutcome}</Fact><Fact label="Last occurrence"><Timestamp value={observation?.lastOccurrenceUtc} /></Fact><Fact label="Next occurrence"><Timestamp value={observation?.nextOccurrenceUtc} /></Fact><Fact label="Description" wide>{job.description}</Fact><Fact label="Resource scope" wide>{job.resourceScopeDescription}</Fact></Facts>; }
export function JobDefinitionFacts({ report, job }: JobSelection) { return <Facts><Fact label="Execution scope">{job.executionScope}</Fact><Fact label="Job ID">{job.jobId}</Fact><Fact label="Category">{categoryName(report, job)}</Fact><Fact label="Supported modes">{job.supportedModes.join(", ")}</Fact>
  {job.description === undefined ? null : <Fact label="Description" wide>{job.description}</Fact>}{job.resourceScopeDescription === undefined ? null : <Fact label="Resource scope" wide>{job.resourceScopeDescription}</Fact>}</Facts>; }
export function JobObservationFacts({ observation }: JobSelection) { return <Facts><Fact label="Resource">{observation?.resourceId}</Fact><Fact label="Observed mode">{observation?.mode}</Fact><Fact label="State">{observation?.state}</Fact><Fact label="Last outcome">{observation?.lastOutcome}</Fact><Fact label="Last occurrence"><Timestamp value={observation?.lastOccurrenceUtc} /></Fact><Fact label="Next occurrence"><Timestamp value={observation?.nextOccurrenceUtc} /></Fact></Facts>; }
export function ReportFacts({ report, compact = false }: { report: PortalInventory; compact?: boolean }) { return <section className="report-facts">{compact ? <h2>Selected report</h2> : <h3>Report provenance</h3>}<Facts><Fact label="Registration ID" wide={!compact}>{report.registrationId}</Fact><Fact label="Reported app version">{report.applicationVersion}</Fact><Fact label="Report sequence">{report.inventory.sequence}</Fact><Fact label="Observed"><Timestamp value={report.inventory.observedAtUtc} /></Fact><Fact label="Received"><Timestamp value={report.receivedAtUtc} /></Fact></Facts></section>; }
export function jobLink(applicationId: string, item: JobSelection, cursor: string | undefined, context: ListContext, view?: string) {
  return queryUrl(`/applications/${applicationId}/jobs`, { registration: item.report.registrationId, report: item.report.inventory.sequence, scope: item.job.executionScope, job: item.job.jobId,
    resource: item.observation?.resourceId, mode: item.observation?.mode, cursor, ...context, view });
}
