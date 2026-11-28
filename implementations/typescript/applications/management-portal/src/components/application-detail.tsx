"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { isCursor } from "@cislib/management-client";
import * as Tabs from "@radix-ui/react-tabs";
import { useEffect, useRef, useState } from "react";
import type { MouseEvent, ReactNode } from "react";
import { ConfigurationRows } from "./configuration-list";
import { ConfigurationDetails } from "./configuration-details";
import type { ConfigurationRow } from "./configuration-list";
import { InstanceGroups } from "./instance-list";
import { instancePresence } from "../instance-groups.ts";
import { applicationRoute, configurationRoute, jobDetailsRoute, readableSlug } from "../portal-routes.ts";
import { ruleEngineRoute } from "../rule-engine-routes.ts";
import type { PortalApplication, PortalCapabilities, PortalCapability, PortalInstance, PortalInventory, PortalJob, PortalJobObservation, PortalPage } from "../portal-model";
import { applicationSelection, configurationSelection as captureConfigurationSelection, parameter, validRouteParameters } from "../page-input.ts";
import type { PageInput } from "../page-input.ts";
import { Badge, Empty, Fact, Facts, Pager, Panel, ReadNotice, Shell, TechnicalDetails, Timestamp, isUnauthenticated, queryUrl, ready, reportedLabel, usePages, usePortalRead } from "./shared";

export type ApplicationTab = "instances" | "configuration" | "capabilities" | "jobs";
export interface ListContext { readonly fromQuery?: string; readonly fromEnvironment?: string; readonly fromCursor?: string }
interface JobSelection { readonly report: PortalInventory; readonly job: PortalJob; readonly observation?: PortalJobObservation }
interface ConfigurationSelection { readonly configurationId: string; readonly schemaVersion: string; readonly source?: "common" | "application" }
type Selected = { readonly kind: "instance"; readonly item: PortalInstance } | { readonly kind: "configuration"; readonly target: ConfigurationSelection; readonly item?: ConfigurationRow | undefined }
  | { readonly kind: "capability"; readonly item: PortalCapability } | { readonly kind: "job"; readonly item: JobSelection };

export function ApplicationDetail({ applicationId, initialTab = "instances", initialCursor, initialConfiguration, context }: {
  applicationId: string; initialTab?: ApplicationTab; initialCursor?: string; initialConfiguration?: ConfigurationSelection; context: ListContext
}) {
  const router = useRouter();
  const [tab, setTab] = useState<ApplicationTab>(initialTab);
  const selectedTab = useRef<ApplicationTab>(initialTab);
  const [revision, refresh] = useState(0);
  const [selection, select] = useState<{ tab: ApplicationTab; key: string }>();
  const [configurationSelection, selectConfiguration] = useState<ConfigurationSelection | undefined>(initialConfiguration);
  const panelOrigin = useRef<HTMLElement | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const base = `/api/portal/applications/${applicationId}`;
  const appState = usePortalRead<PortalApplication>(base, revision);
  const pages = usePages(JSON.stringify([applicationId, tab]), initialCursor);
  const currentPages = useRef(pages);
  currentPages.current = pages;
  const contentUrl = tab === "jobs" ? "inventory" : tab === "configuration" ? "configuration-summaries" : tab;
  const listState = usePortalRead<PortalPage<PortalInstance | ConfigurationRow | PortalInventory> | PortalCapabilities>(
    ready(appState) === undefined ? null : queryUrl(`${base}/${contentUrl}`, { cursor: tab === "capabilities" ? undefined : pages.cursor }), revision);
  const blocked = isUnauthenticated(appState, listState);
  const application = blocked ? undefined : ready(appState);
  const value = blocked || application === undefined ? undefined : ready(listState);
  const selected: Selected | undefined = tab === "configuration" && configurationSelection !== undefined && application !== undefined
    ? { kind: "configuration", target: configurationSelection, item: value !== undefined && "items" in value
      ? (value.items as readonly ConfigurationRow[]).find(item => item.configurationId === configurationSelection.configurationId && item.schemaVersion === configurationSelection.schemaVersion) : undefined }
    : selection?.tab === tab && value !== undefined ? findSelection(tab, value, selection.key) : undefined;
  useEffect(() => {
    selectedTab.current = initialTab; setTab(initialTab); select(undefined); selectConfiguration(initialConfiguration);
  }, [applicationId, initialTab, initialConfiguration?.configurationId, initialConfiguration?.schemaVersion, initialConfiguration?.source]);
  useEffect(() => {
    const historySelection = () => {
      const url = new URL(window.location.href), parts = url.pathname.split("/").filter(Boolean);
      const input: PageInput = {};
      for (const name of ["applicationId", "configurationId", "schemaVersion", "source", "cursor"]) {
        const values = url.searchParams.getAll(name); if (values.length !== 0) { input[name] = values.length === 1 ? values[0] : values; }
      }
      try {
        if (parts[0] !== "applications" || parts[2] !== "configuration" || parts[1] === undefined
          || applicationSelection(decodeURIComponent(parts[1]), input) !== applicationId) { selectConfiguration(undefined); return; }
        const cursor = parameter(input, "cursor");
        if (!validRouteParameters(input, ["cursor"]) || cursor !== undefined && !isCursor(cursor)) { selectConfiguration(undefined); return; }
        if (selectedTab.current === "configuration" && currentPages.current.cursor !== cursor) { currentPages.current.restore(cursor); }
        if (parts.length !== 4) { selectConfiguration(undefined); return; }
        const target = captureConfigurationSelection(decodeURIComponent(parts[1]!), decodeURIComponent(parts[3]!), input);
        const source = parameter(input, "source");
        selectConfiguration(target?.applicationId === applicationId && (input.source === undefined || source === "common" || source === "application")
          ? { configurationId: target.configurationId, schemaVersion: target.schemaVersion, ...(source === undefined ? {} : { source: source as "common" | "application" }) } : undefined);
      } catch { selectConfiguration(undefined); }
    };
    window.addEventListener("popstate", historySelection);
    return () => window.removeEventListener("popstate", historySelection);
  }, [applicationId]);
  useEffect(() => {
    if (appState.reply !== undefined && appState.reply.state !== "ready" || listState.reply !== undefined && listState.reply.state !== "ready") { select(undefined); }
  }, [appState.reply, listState.reply]);
  function open(key: string, event: MouseEvent<HTMLButtonElement>) {
    panelOrigin.current = event.currentTarget;
    select({ tab, key });
  }
  const applicationName = application?.metadata.displayName || application?.application;
  function navigateTab(next: ApplicationTab) {
    if (selectedTab.current === next) { return; }
    selectedTab.current = next;
    select(undefined); selectConfiguration(undefined); setTab(next);
    router.push(applicationRoute({ applicationId, applicationName, tab: next, context }), { scroll: false });
  }
  function openConfiguration(definition: ConfigurationRow, event: MouseEvent<HTMLButtonElement>) {
    event.preventDefault();
    router.push(configurationRoute({ applicationId, applicationName, configurationId: definition.configurationId,
      schemaVersion: definition.schemaVersion, context, cursor: pages.cursor }));
  }
  function closePanel() {
    select(undefined); selectConfiguration(undefined);
    if (selected?.kind === "configuration") {
      window.history.pushState(null, "", applicationRoute({ applicationId, applicationName, tab: "configuration", cursor: pages.cursor, context }));
    }
  }
  const back = queryUrl("/applications", { query: context.fromQuery, environment: context.fromEnvironment, cursor: context.fromCursor });
  return <Shell signedIn={!blocked}><Link className="back-link" href={back}>← Back to applications</Link>
    <div className="page-heading"><div><div className="eyebrow">Application</div><h1 ref={heading} tabIndex={-1}>{application?.metadata.displayName || application?.application || "Application details"}</h1>
      {application?.metadata.description === undefined || application.metadata.description === "" ? null : <p className="application-description">{application.metadata.description}</p>}
      {application !== undefined ? <p>{application.application}<span className="heading-separator">/</span><Badge>{application.environment}</Badge></p> : null}</div>
      <div className="heading-actions">{application === undefined ? null : <a className="button" href={queryUrl(`/applications/${applicationId}/metadata`, { ...context })}>Display metadata</a>}
        <button disabled={appState.busy || listState.busy} onClick={() => refresh(value => value + 1)}>Refresh</button></div></div>
    <ReadNotice state={blocked && listState.reply?.state === "unauthenticated" ? listState : appState} />
    {application !== undefined ? <>
      <Tabs.Root value={tab} onValueChange={value => navigateTab(value as ApplicationTab)}>
        <Tabs.List className="tabs" aria-label="Application details">{([
          ["instances", "Instances"], ["configuration", "Configuration"], ["capabilities", "Capabilities"], ["jobs", "Scheduled jobs"]
        ] as const).map(([name, label]) => <Tabs.Trigger key={name} value={name} asChild onMouseDown={event => {
          if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) { event.preventDefault(); }
        }}>
          <Link href={applicationRoute({ applicationId, applicationName, tab: name, context })}
            onClick={event => { if (!event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey && event.button === 0) { event.preventDefault(); } }}>{label}</Link>
        </Tabs.Trigger>)}</Tabs.List>
        <Tabs.Content className="tab-content" value={tab}>
          {tab !== "instances" ? <div className="section-heading"><div><h2>{({ configuration: "Configurations", capabilities: "Capabilities", jobs: "Scheduled jobs" })[tab]}</h2>
            <p>{({ configuration: "Inspect the assigned JSON or prepare changes for this application.", capabilities: "Registered application features. Service API details are separate diagnostics.", jobs: "One selected instance report at a time. Reported modes do not establish configured tasks or schedules." })[tab]}</p></div>
            {tab === "configuration" ? <Link className="button primary" href={queryUrl(`/applications/${readableSlug(applicationName, "application")}/configuration/new`, { applicationId, cursor: pages.cursor, ...context })}>New configuration</Link> : null}
          </div> : null}
          <ReadNotice state={listState} restart={() => { select(undefined); pages.reset(); }} />
          {value !== undefined ? tab === "configuration" && "items" in value
            ? <ConfigurationRows applicationId={applicationId} applicationName={applicationName} items={value.items as readonly ConfigurationRow[]} context={context} onOpen={openConfiguration} />
            : <DetailList tab={tab} value={value} open={open}
              rulesets={tab === "capabilities" ? { applicationId, href: ruleEngineRoute({ applicationId, applicationName, context }) } : undefined} /> : null}
          {value !== undefined && "items" in value ? <Pager pages={pages} nextCursor={value.nextCursor} busy={listState.busy} changed={() => select(undefined)} label={tab === "jobs" ? "Registration inventories" : `${value.items.length} items on this page`} /> : null}
        </Tabs.Content>
      </Tabs.Root>
      <TechnicalDetails fields={[{ label: "Application ID", value: application.applicationId }, { label: "Application identity", value: application.application }, { label: "Metadata revision", value: application.metadataRevision }]} />
      <Panel open={selected !== undefined} wide={selected?.kind === "configuration"} close={closePanel} title={selectedTitle(selected)} eyebrow={selected?.kind === "job" ? "Reported job" : selected?.kind === "capability" ? "Service API" : selected?.kind === "configuration" ? "Configuration details" : "Details"}
        subtitle={`${application.metadata.displayName || application.application} · ${application.environment}`} origin={panelOrigin.current} fallback={heading.current}
        footer={selected?.kind === "job" ? <Link className="button primary" href={jobLink(applicationId, selected.item, pages.cursor, context, undefined, applicationName)}>View full details</Link> : undefined}>
        {selected?.kind === "configuration" ? <ConfigurationDetails applicationId={applicationId} configurationId={selected.target.configurationId} schemaVersion={selected.target.schemaVersion}
          {...(selected.target.source === undefined ? {} : { source: selected.target.source })} {...(selected.item === undefined ? {} : { definition: selected.item })} context={context} /> : selected !== undefined ? <SelectedDetails selected={selected} /> : null}
      </Panel>
    </> : null}
  </Shell>;
}

function DetailList({ tab, value, open, rulesets }: { tab: ApplicationTab; value: PortalPage<PortalInstance | ConfigurationRow | PortalInventory> | PortalCapabilities;
  open: (key: string, event: MouseEvent<HTMLButtonElement>) => void; rulesets: { applicationId: string; href: string } | undefined }) {
  if (tab === "capabilities" && "serviceApis" in value) {
    return <><div className="feature-groups">
      <section className="card feature-group"><div className="feature-heading"><h3>Schedulable job types</h3><Badge tone="warning">Unavailable</Badge></div>
        <p>Trusted job definitions and their supported versions are unavailable. Reported jobs can still be inspected under Scheduled jobs.</p></section>
      {rulesets === undefined ? null : <RuleEngineCapability {...rulesets} />}
      <section className="card feature-group"><div className="feature-heading"><h3>Trigger providers</h3><Badge tone="warning">Unavailable</Badge></div>
        <p>Trusted trigger-provider definitions and supported configuration operations are unavailable in this view.</p></section>
    </div><details className="service-api-details"><summary>Service API details</summary>
      <p>{value.catalog.message} The descriptors below describe Management HTTP APIs; permission hints are advisory and grant no authority.</p>
      <div className="table-frame"><table><thead><tr><th>Operation</th><th>API version</th><th>Method / path</th><th>Caller hint</th></tr></thead><tbody>
        {value.serviceApis.map(item => <tr key={capabilityKey(item)}><td><Item onClick={event => open(capabilityKey(item), event)}>{item.operationId}</Item></td><td>{item.version}</td><td><code>{item.method} {item.path}</code></td><td><Badge>{hint(item.callerHint)}</Badge></td></tr>)}
      </tbody></table>{value.serviceApis.length === 0 ? <Empty title="No service API descriptors">No HTTP API descriptors are reported.</Empty> : null}</div>
    </details></>;
  }
  if (!("items" in value)) { return null; }
  if (tab === "instances") { return <InstanceGroups items={value.items as readonly PortalInstance[]} open={open} />; }
  if (tab === "configuration") { return null; }
  const reports = value.items as readonly PortalInventory[];
  return <>{reports.length === 0 ? <div className="table-frame"><Empty title="No job inventory reported">No complete inventory is available for this application.</Empty></div> : null}
    {reports.map(report => <section key={report.registrationId} aria-label="Registration inventory"><div className="inventory-summary"><strong>Reported inventory</strong>
      <span>Reported app version {report.applicationVersion}</span><span>Observed <Timestamp value={report.inventory.observedAtUtc} /></span><span>Received <Timestamp value={report.receivedAtUtc} /></span>
      <TechnicalDetails fields={[{ label: "Registration ID", value: report.registrationId }, { label: "Report sequence", value: report.inventory.sequence }]} /></div>
      <div className="table-frame"><table><thead><tr><th>Job</th><th>Resource / mode</th><th>Reported state</th><th>Last outcome</th></tr></thead><tbody>{jobRows(report).map(item => <tr key={jobKey(item)}>
        <td><Item onClick={event => open(jobKey(item), event)}>{reportedLabel(item.job.displayName, "Reported job")}</Item><span className="row-description">{categoryName(report, item.job)}</span></td>
        <td>{item.observation === undefined ? "Not reported" : <>{reportedLabel(item.observation.resourceId, "Reported resource")}<span className="row-subtitle">{item.observation.mode}</span></>}</td><td><Badge>{item.observation?.state ?? "Not reported"}</Badge></td><td>{item.observation?.lastOutcome ?? "Not reported"}</td>
      </tr>)}</tbody></table>{report.inventory.jobs.length === 0 ? <Empty title="No jobs in this report">The selected registration reported an empty job inventory.</Empty> : null}</div>
    </section>)}</>;
}

function RuleEngineCapability({ applicationId, href }: { readonly applicationId: string; readonly href: string }) {
  const read = usePortalRead<readonly { operationId: string; allowed: boolean }[]>("/api/portal/applications/" + applicationId + "/rule-engine/capabilities");
  const listing = ready(read)?.find(item => item.operationId === "listRuleEngineRulesets");
  const visible = listing?.allowed === true && !read.stale && !read.busy;
  return <section className="card feature-group"><div className="feature-heading"><h3>Registered RuleEngine instances</h3>
    <Badge tone={visible ? "good" : "warning"}>{visible ? "Available" : read.busy ? "Checking" : "Unavailable"}</Badge></div>
    {visible ? <><p>Open registered rulesets for this application and environment.</p><Link className="button" href={href}>Rule engines</Link></>
      : <p>Trusted registered engine definitions and versioned participation are unavailable in this view.</p>}
  </section>;
}
function Item({ onClick, children }: { onClick: (event: MouseEvent<HTMLButtonElement>) => void; children: ReactNode }) { return <button className="item-link" onClick={onClick}>{children}</button>; }
function hint(value: PortalCapability["callerHint"]) { return ({ allowed: "Allowed hint", denied: "Denied hint", notEvaluated: "Not evaluated" })[value]; }
function capabilityKey(item: PortalCapability) { return JSON.stringify([item.operationId, item.version, item.method, item.path]); }
export function jobRows(report: PortalInventory): readonly JobSelection[] {
  return report.inventory.jobs.flatMap<JobSelection>(job => job.observations?.length ? job.observations.map(observation => ({ report, job, observation })) : [{ report, job }]);
}
function jobKey(item: JobSelection) { return JSON.stringify([item.report.registrationId, item.report.inventory.sequence, item.job.executionScope, item.job.jobId, item.observation?.resourceId, item.observation?.mode]); }
export function categoryName(report: PortalInventory, job: PortalJob) { return reportedLabel(report.inventory.categories.find(category => category.executionScope === job.executionScope && category.categoryId === job.categoryId)?.displayName ?? job.categoryId, "Reported category"); }
function findSelection(tab: ApplicationTab, value: PortalPage<PortalInstance | ConfigurationRow | PortalInventory> | PortalCapabilities, key: string): Selected | undefined {
  if (tab === "capabilities" && "serviceApis" in value) { const item = value.serviceApis.find(item => capabilityKey(item) === key); return item === undefined ? undefined : { kind: "capability", item }; }
  if (!("items" in value)) { return undefined; }
  if (tab === "instances") { const item = (value.items as readonly PortalInstance[]).find(item => item.registrationId === key); return item === undefined ? undefined : { kind: "instance", item }; }
  if (tab === "configuration") { return undefined; }
  const item = (value.items as readonly PortalInventory[]).flatMap(jobRows).find(item => jobKey(item) === key);
  return item === undefined ? undefined : { kind: "job", item };
}
function selectedTitle(value: Selected | undefined) { if (value === undefined) { return "Item details"; } return value.kind === "instance" ? reportedLabel(value.item.reportedInstanceId, "Reported instance") : value.kind === "configuration" ? reportedLabel(value.target.configurationId, "Configuration") : value.kind === "capability" ? value.item.operationId : reportedLabel(value.item.job.displayName, "Reported job"); }
function SelectedDetails({ selected }: { selected: Exclude<Selected, { readonly kind: "configuration" }> }) {
  switch (selected.kind) {
    case "instance": { const item = selected.item; return <><Facts><Fact label="Reported app version">{item.applicationVersion}</Fact><Fact label="Presence">{instancePresence(item.presence).label}</Fact>
      <Fact label="Instance evidence">{item.instanceEvidence.kind}</Fact><Fact label="Process started"><Timestamp value={item.processStartedAtUtc} /></Fact><Fact label="Registered"><Timestamp value={item.registeredAtUtc} /></Fact>
      <Fact label="Last seen"><Timestamp value={item.lastSeenAtUtc} /></Fact><Fact label="Closed"><Timestamp value={item.closedAtUtc} /></Fact><Fact label="Lifecycle">{item.observation?.lifecycle}</Fact><Fact label="Health">{item.observation?.health}</Fact>
      <Fact label="Observed"><Timestamp value={item.observation?.observedAtUtc} /></Fact><Fact label="Observation received"><Timestamp value={item.observationReceivedAtUtc} /></Fact></Facts>
      <TechnicalDetails fields={[{ label: "Registration ID", value: item.registrationId }, { label: "Reported instance ID", value: item.reportedInstanceId },
        { label: "Reported identifier", value: item.instanceEvidence.reportedIdentifier }, { label: "Verified identifier", value: item.instanceEvidence.verifiedIdentifier },
        { label: "Observation sequence", value: item.observation?.sequence }, { label: "Inventory sequence", value: item.inventorySequence }]} />
      <p className="notice">Instance labels and presence are reported observations. They do not establish distinct processes or an application-wide current version.</p></>; }
    case "capability": { const item = selected.item; return <><Facts><Fact label="API version">{item.version}</Fact><Fact label="Method">{item.method}</Fact><Fact label="Caller hint">{hint(item.callerHint)}</Fact></Facts>
      <TechnicalDetails fields={[{ label: "Operation ID", value: item.operationId }, { label: "Path", value: item.path },
        { label: "Required capabilities", value: item.requiredCapabilities.length === 0 ? "None reported" : item.requiredCapabilities.join(", ") }]} />
      <p className="notice">Descriptors describe the Management service APIs. Permission hints are advisory and grant no authority.</p></>; }
    case "job": return <><JobFacts {...selected.item} /><ReportFacts report={selected.item.report} /></>;
  }
}
export function JobFacts(selection: JobSelection) { return <><JobDefinitionFacts {...selection} /><JobObservationFacts {...selection} /></>; }
export function JobDefinitionFacts({ report, job }: JobSelection) { return <><Facts><Fact label="Category">{categoryName(report, job)}</Fact><Fact label="Supported modes">{job.supportedModes.join(", ")}</Fact>
  {job.description === undefined ? null : <Fact label="Description" wide>{job.description}</Fact>}{job.resourceScopeDescription === undefined ? null : <Fact label="Resource scope" wide>{job.resourceScopeDescription}</Fact>}</Facts>
  <TechnicalDetails fields={[{ label: "Execution scope", value: job.executionScope }, { label: "Job ID", value: job.jobId }, { label: "Category ID", value: job.categoryId }]} /></>; }
export function JobObservationFacts({ observation }: JobSelection) { return <><Facts><Fact label="Resource">{observation === undefined ? undefined : reportedLabel(observation.resourceId, "Reported resource")}</Fact><Fact label="Observed mode">{observation?.mode}</Fact><Fact label="State">{observation?.state}</Fact><Fact label="Last outcome">{observation?.lastOutcome}</Fact><Fact label="Last occurrence"><Timestamp value={observation?.lastOccurrenceUtc} /></Fact><Fact label="Next occurrence"><Timestamp value={observation?.nextOccurrenceUtc} /></Fact></Facts>
  {observation === undefined ? null : <TechnicalDetails fields={[{ label: "Resource ID", value: observation.resourceId }]} />}</>; }
export function ReportFacts({ report, compact = false }: { report: PortalInventory; compact?: boolean }) { return <section className="report-facts">{compact ? <h2>Selected report</h2> : <h3>Report provenance</h3>}<Facts><Fact label="Reported app version">{report.applicationVersion}</Fact><Fact label="Observed"><Timestamp value={report.inventory.observedAtUtc} /></Fact><Fact label="Received"><Timestamp value={report.receivedAtUtc} /></Fact></Facts>
  <TechnicalDetails fields={[{ label: "Registration ID", value: report.registrationId }, { label: "Report sequence", value: report.inventory.sequence }]} /></section>; }
export function jobLink(applicationId: string, item: JobSelection, cursor: string | undefined, context: ListContext, view?: string, applicationName?: string) {
  return jobDetailsRoute({ applicationId, applicationName, target: { registration: item.report.registrationId, report: item.report.inventory.sequence,
    scope: item.job.executionScope, job: item.job.jobId, resource: item.observation?.resourceId, mode: item.observation?.mode, cursor, view }, context });
}
