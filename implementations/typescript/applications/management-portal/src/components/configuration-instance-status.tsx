"use client";

import { useEffect, useRef, useState } from "react";
import type { MouseEvent } from "react";
import type { ConfigurationAdoptionEntry, ConfigurationRelease } from "@cislib/management-client";
import { adoptionRowKey, freshnessLabel, reportedStateLabel, updateModeLabel } from "../configuration-adoption-model.ts";
import { instanceConfigurationVersion, instanceStatusPresentation } from "../configuration-instance-status-model.ts";
import { configurationVersionLabel, configurationVersionMatches } from "../configuration-versions.ts";
import { useProtectedPortal } from "./protected-context";
import { Empty, Fact, Facts, Panel, TechnicalDetails, reportedLabel } from "./shared";

/** Both status entry points show the same authorized observations without requiring version metadata. */
export function ConfigurationInstanceStatusTable({ rows, releases, scope, earlierPage = false, configurationLabel, fallback }: {
  rows: readonly ConfigurationAdoptionEntry[]; releases: readonly ConfigurationRelease[] | undefined; scope: string;
  earlierPage?: boolean; configurationLabel: string; fallback: HTMLElement | null
}) {
  const { snapshot: session } = useProtectedPortal();
  const [selection, select] = useState<{ scope: string; key: string }>();
  const origin = useRef<HTMLElement | null>(null);
  const selectionScope = JSON.stringify([scope, session.epoch]);
  const instances = rows.filter(row => row.registrationId !== undefined), targets = rows.filter(row => row.registrationId === undefined);
  const selected = selection?.scope === selectionScope ? instances.find(row => adoptionRowKey(row) === selection.key) : undefined;
  useEffect(() => { select(undefined); }, [selectionScope]);
  function open(row: ConfigurationAdoptionEntry, event: MouseEvent<HTMLButtonElement>) {
    origin.current = event.currentTarget; select({ scope: selectionScope, key: adoptionRowKey(row) });
  }
  return <><div className="table-frame configuration-instance-status"><table><thead><tr>
    <th>Instance</th><th>Configuration version in use</th><th>Status</th>
  </tr></thead><tbody>{instances.map(row => {
    const status = instanceStatusPresentation(row, earlierPage), version = instanceConfigurationVersion(row, releases);
    const freshness = freshnessCaption(row, earlierPage);
    return <tr key={adoptionRowKey(row)}><td><button className="item-link" onClick={event => open(row, event)}>{instanceLabel(row)}</button></td>
      <td><VersionLabel value={version} /></td><td><StatusBadge value={status} />
        <span className="row-subtitle">{row.observedAtUtc === undefined ? "Report time not reported" : <>Reported <StatusTime value={row.observedAtUtc} /></>}</span>
        {freshness === undefined ? null : <span className="row-subtitle">{freshness}</span>}</td></tr>;
  })}</tbody></table>
    {instances.length === 0 ? <Empty title="No registered instances on this page">No registered instances were returned on this authorized page. This does not establish successful deployment or absence of processes.</Empty> : null}
  </div>
    {targets.length === 0 ? null : <div className="instance-status-targets"><TechnicalDetails label="Technical details: unregistered targets"
      fields={targets.flatMap((row, index) => diagnosticFields(row).map(field => ({ label: `Target ${index + 1} · ${field.label}`, value: field.value })))} /></div>}
    <Panel open={selected !== undefined} close={() => select(undefined)} title={selected === undefined ? "Instance report" : instanceLabel(selected)}
      eyebrow="Instance status" subtitle={`${configurationLabel} · ${selected?.environment ?? "Selected target"}`} origin={origin.current} fallback={fallback}>
      {selected === undefined ? null : <InstanceStatusDetails row={selected} releases={releases} earlierPage={earlierPage} />}
    </Panel>
  </>;
}

function instanceLabel(row: ConfigurationAdoptionEntry): string {
  return row.reportedInstanceId === undefined ? row.convergence === "noInstances" ? "No observed instances"
    : row.registrationId === undefined ? "Target observation" : "Reported instance" : reportedLabel(row.reportedInstanceId, "Reported instance");
}
function freshnessCaption(row: ConfigurationAdoptionEntry, earlierPage: boolean): string | undefined {
  if (earlierPage) { return "Earlier observation"; }
  if (row.convergence === "noInstances") { return undefined; }
  if (row.registrationFreshness === "offline") { return "Instance offline"; }
  if (row.adoptionFreshness === "stale" || row.adoptionFreshness === "offline") { return "Stale report"; }
  if (row.registrationFreshness === "stale") { return "Not recently seen"; }
  return row.registrationFreshness === "fresh" && row.adoptionFreshness === "fresh" ? undefined : "Status unknown";
}
function StatusBadge({ value }: { value: ReturnType<typeof instanceStatusPresentation> }) {
  return <span className={`badge ${value.label === "Failed" ? "instance-status-failed" : value.tone}`}>{value.label}</span>;
}
function VersionLabel({ value }: { value: ReturnType<typeof instanceConfigurationVersion> }) {
  return <><span>{value.label}</span>{value.createdAtUtc === undefined ? null : <span className="row-subtitle"><StatusTime value={value.createdAtUtc} /></span>}</>;
}
function StatusTime({ value }: { value: string | undefined }) {
  return value === undefined ? <>Not reported</> : <time dateTime={value}>{new Intl.DateTimeFormat(undefined,
    { year: "numeric", month: "short", day: "2-digit", hour: "2-digit", minute: "2-digit", timeZoneName: "short" }).format(new Date(value))}</time>;
}
function InstanceStatusDetails({ row, releases, earlierPage }: {
  row: ConfigurationAdoptionEntry; releases: readonly ConfigurationRelease[] | undefined; earlierPage: boolean
}) {
  const status = instanceStatusPresentation(row, earlierPage), version = instanceConfigurationVersion(row, releases);
  const desired = releases?.find(item => item.releaseId === row.desired?.releaseId
    && configurationVersionMatches(item, row.applicationId, row.configurationId, row.schemaVersion, row.environment));
  return <div className="instance-status-details">{earlierPage ? <p className="notice" role="status">This is an earlier permitted observation. Refresh to request current state.</p> : null}
    <StatusBadge value={status} /><p>{status.detail}</p>
    <Facts><Fact label="Configuration version in use" wide><VersionLabel value={version} /></Fact>
      <Fact label="Deployed version at this read" wide>{row.desired === undefined ? "Not reported" : desired === undefined ? "Version details unavailable"
        : <><span>{configurationVersionLabel(desired)}</span><span className="row-subtitle"><StatusTime value={desired.createdAtUtc} /></span></>}</Fact>
      <Fact label="Last reported" wide><StatusTime value={row.observedAtUtc} /></Fact></Facts>
    <p className="muted">This is a reported observation, not a live-value check.</p>
    <TechnicalDetails fields={diagnosticFields(row)} />
  </div>;
}
function diagnosticFields(row: ConfigurationAdoptionEntry): readonly { readonly label: string; readonly value: string | undefined }[] {
  return [
      { label: "Reported app version", value: row.applicationVersion }, { label: "Environment", value: row.environment },
      { label: "Registration freshness", value: freshnessLabel(row.registrationFreshness) }, { label: "Report freshness", value: freshnessLabel(row.adoptionFreshness) },
      { label: "Update mode", value: updateModeLabel(row.reported?.updateMode) }, { label: "Reported state", value: reportedStateLabel(row.reported?.state) },
      { label: "Live state observation", value: row.reported === undefined ? "Not reported" : row.reported.liveStateValid ? "Reported valid; not an uninterrupted guarantee" : "Not guaranteed (reported)" },
      { label: "Desired generation", value: row.desired?.generation }, { label: "Reported desired generation", value: row.reported?.desired?.generation },
      { label: "Accepted generation", value: row.reported?.applied?.generation }, { label: "Accepted local revision", value: row.reported?.applied?.localRevision },
      { label: "Historical generation", value: row.reported?.lastSuccessful?.generation }, { label: "Historical local revision", value: row.reported?.lastSuccessful?.localRevision },
      { label: "Historical acceptance meaning", value: "Earlier acceptance does not establish current live values or replace a missing applied version." },
      { label: "Candidate failure code", value: row.reported?.candidateFailureCode }, { label: "Notification failure code", value: row.reported?.notificationFailureCode },
      { label: "Consumer observation time", value: row.observedAtUtc }, { label: "Report receipt time", value: row.receivedAtUtc },
      { label: "Application ID", value: row.applicationId }, { label: "Configuration ID", value: row.configurationId }, { label: "Schema version", value: row.schemaVersion },
      { label: "Registration ID", value: row.registrationId }, { label: "Reported instance ID", value: row.reportedInstanceId }, { label: "Cohort ID", value: row.cohortId },
      { label: "Desired release ID", value: row.desired?.releaseId }, { label: "Reported desired release ID", value: row.reported?.desired?.releaseId },
      { label: "Accepted release ID", value: row.reported?.applied?.releaseId }, { label: "Historical successful release ID", value: row.reported?.lastSuccessful?.releaseId },
      { label: "Native convergence token", value: row.convergence }, { label: "Reported state token", value: row.reported?.state }, { label: "Update mode token", value: row.reported?.updateMode },
      { label: "Registration freshness token", value: row.registrationFreshness }, { label: "Adoption freshness token", value: row.adoptionFreshness }
  ];
}
