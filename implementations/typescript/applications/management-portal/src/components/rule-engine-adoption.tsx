"use client";

import type { Page, RuleDeployment, RuleDeploymentInstance, RuleVersionSummary } from "@cislib/management-client";
import { Badge, Empty, Pager, ReadNotice, TechnicalDetails, Timestamp } from "./shared";
import type { ReadState, usePages } from "./shared";

export function ruleVersionLabel(id: string | undefined, versions: readonly RuleVersionSummary[]): string {
  if (id === undefined) { return "Unavailable"; }
  const note = versions.find(item => item.version === id)?.versionNote;
  return note === undefined || note === "" ? "Version " + id.slice(0, 8) : note;
}

export function RuleAdoption({ deployment, state, page, pages, versions, refresh }: {
  readonly deployment: RuleDeployment | undefined; readonly state: ReadState<Page<RuleDeploymentInstance>>;
  readonly page: Page<RuleDeploymentInstance> | undefined; readonly pages: ReturnType<typeof usePages>;
  readonly versions: readonly RuleVersionSummary[]; readonly refresh: () => void;
}) {
  const observed = page?.items.filter(item => item.configuration.registrationId !== undefined) ?? [];
  const noRecipients = page !== undefined && observed.length === 0 && page.items.some(item => item.configuration.convergence === "noInstances");
  const applied = observed.filter(item => item.configuration.registrationFreshness === "fresh"
    && item.configuration.adoptionFreshness === "fresh" && item.configuration.convergence === "applied");
  return <section className="rm11-adoption">
    <div className="notice rm11-deployment-summary" role="status">
      <strong>{deployment === undefined ? "Requested deployment unavailable" : deployment.desired === undefined ? "No requested version"
        : "Requested deployment · " + ruleVersionLabel(deployment.desired.version, versions)}</strong>
      {deployment?.desired === undefined ? null : <p>{deployment.requestedAtUtc === undefined ? "Request time unavailable" : <>
        Requested <Timestamp value={deployment.requestedAtUtc} compact /></>}</p>}
    </div>
    <div className="section-heading"><div><h2>Instance adoption</h2>
      {observed.length === 0 ? null : <p>{applied.length} of {observed.length} observed instances on this page report current acceptance.</p>}</div>
      <button type="button" disabled={state.busy} onClick={refresh}>Refresh status</button></div>
    <ReadNotice state={state} restart={pages.reset} />
    {page === undefined ? null : <div className="table-frame"><table><thead><tr>
      <th>Instance</th><th>Requested version</th><th>Last accepted version</th><th>Adoption</th><th>Latest report</th>
    </tr></thead><tbody>{observed.map(item => {
      const actual = item.configuration, state = adoptionLabel(item);
      return <tr key={actual.registrationId}><td>{actual.reportedInstanceId || "Reported instance"}
        <TechnicalDetails label="Instance details" fields={[{ label: "Registration ID", value: actual.registrationId },
          { label: "Release generation", value: actual.desired?.generation }, { label: "Observed at", value: actual.observedAtUtc }]} /></td>
        <td>{ruleVersionLabel(item.requestedVersion, versions)}</td><td>{ruleVersionLabel(item.appliedVersion ?? item.lastSuccessfulVersion, versions)}</td>
        <td><Badge tone={state === "Applied" ? "good" : state === "Rejected" || state === "Pending" ? "warning" : "neutral"}>{state}</Badge>
          {actual.adoptionFreshness === "stale" ? <small>Stale report</small> : null}</td>
        <td><Timestamp value={actual.observedAtUtc} compact />
          {actual.reported?.candidateFailureCode === undefined ? null : <details className="technical-details"><summary>Reported rejection details</summary>
            <code>{actual.reported.candidateFailureCode}</code></details>}</td>
      </tr>;
    })}</tbody></table>
      {observed.length === 0 ? <Empty title={noRecipients ? "No recipients" : "No instance reports"}>There are no current recipients reported on this page.</Empty> : null}
      <Pager pages={pages} nextCursor={page.nextCursor} busy={state.busy} label="Instance adoption" />
    </div>}
    <TechnicalDetails label="Deployment details" fields={[{ label: "Requested version", value: deployment?.desired?.version }, { label: "Requested at", value: deployment?.requestedAtUtc },
      { label: "Assignment generation", value: deployment?.desired?.generation }, { label: "Assignment revision", value: deployment?.assignmentETag }]} />
  </section>;
}

function adoptionLabel(item: RuleDeploymentInstance): string {
  const observed = item.configuration;
  if (observed.registrationFreshness === "offline") { return "Offline"; }
  switch (observed.convergence) {
    case "applied": return "Applied";
    case "pending": return "Pending";
    case "rejected": return "Rejected";
    case "pendingRestart": return "Restart required";
    case "faulted": return "Faulted";
    case "noInstances": return "No recipients";
    default: return "Unknown";
  }
}
