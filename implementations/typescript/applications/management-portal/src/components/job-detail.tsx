"use client";

import Link from "next/link";
import * as Tabs from "@radix-ui/react-tabs";
import { useState } from "react";
import type { PortalApplication, PortalInventory, PortalPage } from "../portal-model";
import { JobDefinitionFacts, JobObservationFacts, ReportFacts, categoryName, jobLink, jobRows } from "./application-detail";
import type { ListContext } from "./application-detail";
import { Badge, Empty, Fact, Facts, ReadNotice, Shell, TechnicalDetails, isUnauthenticated, queryUrl, ready, reportedLabel, usePortalRead } from "./shared";

export interface JobTarget {
  readonly registration?: string; readonly report?: string; readonly scope?: string; readonly job?: string;
  readonly resource?: string; readonly mode?: string; readonly cursor?: string; readonly view?: string;
}
export function JobDetail({ applicationId, target, context }: { applicationId: string; target: JobTarget; context: ListContext }) {
  const [revision, refresh] = useState(0);
  const [tab, setTab] = useState("overview");
  const [taskTab, setTaskTab] = useState("trigger");
  const [taskHistory, showTaskHistory] = useState(false);
  const base = `/api/portal/applications/${applicationId}`;
  const applicationState = usePortalRead<PortalApplication>(base, revision);
  const reportState = usePortalRead<PortalPage<PortalInventory>>(ready(applicationState) === undefined ? null : queryUrl(`${base}/inventory`, { cursor: target.cursor }), revision);
  const blocked = isUnauthenticated(applicationState, reportState);
  const application = blocked ? undefined : ready(applicationState);
  const report = application === undefined ? undefined : ready(reportState)?.items.find(item => item.registrationId === target.registration && item.inventory.sequence === target.report);
  const selected = report === undefined ? undefined : jobRows(report).find(item => item.job.executionScope === target.scope && item.job.jobId === target.job
    && item.observation?.resourceId === target.resource && item.observation?.mode === target.mode);
  const back = queryUrl(`/applications/${applicationId}`, { tab: "jobs", cursor: target.cursor, ...context });
  const view = target.view === "task" || target.view === "event" ? target.view : "job";
  return <Shell signedIn={!blocked}><Link className="back-link job-back-link" href={back}>← Back to scheduled jobs</Link>
    <div className="page-heading job-heading"><div><div className="eyebrow">{application !== undefined ? `${application.metadata.displayName || application.application} / ${application.environment}` : "Application details"}</div>
      <h1>{view === "job" ? selected === undefined ? "Job details" : reportedLabel(selected.job.displayName, "Reported job") : view === "task" ? "Scheduled task details" : "Event details"}</h1>
      {selected !== undefined ? <p>{categoryName(selected.report, selected.job)} <Badge>{view === "job" ? "Reported job" : "Unavailable"}</Badge></p> : null}
      {application === undefined ? null : <TechnicalDetails fields={[{ label: "Application ID", value: application.applicationId }, { label: "Application identity", value: application.application }]} />}</div><button onClick={() => refresh(value => value + 1)} disabled={applicationState.busy || reportState.busy}>Refresh</button></div>
    <ReadNotice state={blocked && reportState.reply?.state === "unauthenticated" ? reportState : applicationState} />{application !== undefined ? <ReadNotice state={reportState} /> : null}
    {application !== undefined && ready(reportState) !== undefined && selected === undefined ? <Empty title="Selected report or job unavailable">The exact registration, report, job and resource observation are no longer present on this inventory page. Return to Scheduled jobs and select a current item.</Empty> : null}
    {selected !== undefined ? view === "job" ? <Tabs.Root value={tab} onValueChange={setTab}><Tabs.List className="tabs job-tabs" aria-label="Job details"><Tabs.Trigger value="overview">Overview</Tabs.Trigger><Tabs.Trigger value="history">Run history</Tabs.Trigger><Tabs.Trigger value="configuration">Configuration</Tabs.Trigger></Tabs.List>
      <Tabs.Content className="tab-content job-overview" value="overview"><div className="detail-grid"><section className="card"><h2>Job definition</h2><JobDefinitionFacts {...selected} /></section><section className="card"><h2>Selected resource observation</h2><JobObservationFacts {...selected} />
        {selected.observation?.observedOwner !== undefined ? <><h3>Observed owner</h3><Facts><Fact label="Application">{reportedLabel(selected.observation.observedOwner.ownerApplication, "Reported owner application")}</Fact><Fact label="Generation">{selected.observation.observedOwner.generation}</Fact></Facts>
          <TechnicalDetails fields={[{ label: "Observed owner instance ID", value: selected.observation.observedOwner.instanceId }]} /><p className="muted">An observed owner is historical report context, not a current lease or permission.</p></> : null}</section></div>
        <section className="card selected-report"><ReportFacts report={selected.report} compact /></section>
        <Related applicationId={applicationId} selected={selected} cursor={target.cursor} context={context} /></Tabs.Content>
      <Tabs.Content className="tab-content" value="history"><Unavailable kind="Run history">This service has no run-history API. No execution records or occurrence outcomes are inferred from the latest inventory.</Unavailable><Related applicationId={applicationId} selected={selected} cursor={target.cursor} context={context} /></Tabs.Content>
      <Tabs.Content className="tab-content" value="configuration"><section className="card"><h2>Reported local configuration</h2><Facts><Fact label="Enabled">{selected.job.effectiveConfiguration === undefined ? undefined : selected.job.effectiveConfiguration.enabled ? "Yes" : "No"}</Fact><Fact label="Local revision">{selected.job.effectiveConfiguration?.revision}</Fact></Facts><p className="notice">These are reported local facts. Schedule expressions, configuration bindings and values are not reported by this contract.</p></section></Tabs.Content>
    </Tabs.Root> : view === "task" ? <><Link className="back-link" href={jobLink(applicationId, selected, target.cursor, context)}>← Job details</Link>
      <div className="section-heading task-actions"><h2>{taskHistory ? "Task run history" : "Task configuration"}</h2><button onClick={() => showTaskHistory(value => !value)}>{taskHistory ? "Back to task configuration" : "View run history"}</button></div>
      {taskHistory ? <Unavailable kind="Task run history">The service has no task-history API. Job observations do not establish task executions.</Unavailable> : <Tabs.Root value={taskTab} onValueChange={setTaskTab}><Tabs.List className="tabs" aria-label="Scheduled task configuration"><Tabs.Trigger value="trigger">Trigger</Tabs.Trigger><Tabs.Trigger value="job">Job</Tabs.Trigger></Tabs.List>
        <Tabs.Content className="tab-content" value="trigger"><Unavailable kind="Trigger configuration">No scheduled-task definition or trigger binding is available. A supported Scheduled mode does not define a trigger, cron expression or recurrence.</Unavailable></Tabs.Content>
        <Tabs.Content className="tab-content" value="job"><Unavailable kind="Task job configuration">No task-to-job configuration binding is reported. The selected job inventory is context only.</Unavailable></Tabs.Content>
      </Tabs.Root>}</> : <><Link className="back-link" href={jobLink(applicationId, selected, target.cursor, context)}>← Job details</Link><Unavailable kind="Event details">No immutable event record or captured event configuration is available. Current job configuration cannot be presented as historical event context.</Unavailable></> : null}
  </Shell>;
}
function Unavailable({ kind, children }: { kind: string; children: string }) { return <section className="card unavailable"><Badge tone="warning">Unavailable</Badge><h2>{kind}</h2><p>{children}</p><p className="muted">These details are available when the connected service supports scheduling and history.</p></section>; }
function Related({ applicationId, selected, cursor, context }: { applicationId: string; selected: Parameters<typeof jobLink>[1]; cursor: string | undefined; context: ListContext }) {
  return <section className="card related"><h2>Related details</h2><p>No task or event association is supplied by this inventory. These views explain the unavailable data; they do not identify a related record.</p>
    <div className="inline-links"><Link href={jobLink(applicationId, selected, cursor, context, "task")}>Scheduled task details →</Link><Link href={jobLink(applicationId, selected, cursor, context, "event")}>Event details →</Link></div></section>;
}
