import type { ConfigurationAdoptionEntry, ConfigurationAppliedVersion, ConfigurationStatusEntry, ConfigurationVersion, Page } from "@cislib/management-client";
import { knownConfigurationConvergence, knownConfigurationFreshness, knownConfigurationState, knownConfigurationUpdateMode } from "@cislib/management-client";

export interface AdoptionPresentation {
  readonly label: string;
  readonly detail: string;
  readonly tone: "neutral" | "good" | "warning";
}

/** Copy only the authorized portable observations. Compatible additions are not browser disclosure. */
export function projectConfigurationAdoptionPage(value: Page<ConfigurationAdoptionEntry>): Page<ConfigurationAdoptionEntry> {
  return Object.freeze({ items: Object.freeze(value.items.map(row => Object.freeze({
    applicationId: row.applicationId, environment: row.environment, configurationId: row.configurationId, schemaVersion: row.schemaVersion,
    registrationFreshness: row.registrationFreshness, adoptionFreshness: row.adoptionFreshness, convergence: row.convergence,
    ...(row.cohortId === undefined ? {} : { cohortId: row.cohortId }),
    ...(row.registrationId === undefined ? {} : { registrationId: row.registrationId }),
    ...(row.reportedInstanceId === undefined ? {} : { reportedInstanceId: row.reportedInstanceId }),
    ...(row.applicationVersion === undefined ? {} : { applicationVersion: row.applicationVersion }),
    ...(row.desired === undefined ? {} : { desired: version(row.desired) }),
    ...(row.reported === undefined ? {} : { reported: report(row.reported) }),
    ...(row.observedAtUtc === undefined ? {} : { observedAtUtc: row.observedAtUtc }),
    ...(row.receivedAtUtc === undefined ? {} : { receivedAtUtc: row.receivedAtUtc })
  }))), ...(value.nextCursor === undefined ? {} : { nextCursor: value.nextCursor }) });
}
function version(value: ConfigurationVersion): ConfigurationVersion {
  return Object.freeze({ releaseId: value.releaseId, generation: value.generation });
}
function applied(value: ConfigurationAppliedVersion): ConfigurationAppliedVersion {
  return Object.freeze({ ...version(value), localRevision: value.localRevision });
}
function report(value: ConfigurationStatusEntry): ConfigurationStatusEntry {
  return Object.freeze({ configurationId: value.configurationId, schemaVersion: value.schemaVersion, updateMode: value.updateMode,
    state: value.state, liveStateValid: value.liveStateValid,
    ...(value.desired === undefined ? {} : { desired: version(value.desired) }),
    ...(value.applied === undefined ? {} : { applied: applied(value.applied) }),
    ...(value.lastSuccessful === undefined ? {} : { lastSuccessful: applied(value.lastSuccessful) }),
    ...(value.candidateFailureCode === undefined ? {} : { candidateFailureCode: value.candidateFailureCode }),
    ...(value.notificationFailureCode === undefined ? {} : { notificationFailureCode: value.notificationFailureCode }) });
}

/** Reported instance names do not establish distinct processes or stable selection identity. */
export function adoptionRowKey(row: ConfigurationAdoptionEntry): string {
  return JSON.stringify([row.cohortId ?? null, row.registrationId ?? null]);
}
export function adoptionPageMatches(value: Page<ConfigurationAdoptionEntry>, applicationId: string, configurationId: string, schemaVersion: string): boolean {
  const environment = value.items[0]?.environment;
  return value.items.every(row => row.applicationId === applicationId && row.configurationId === configurationId
    && row.schemaVersion === schemaVersion && row.environment === environment
    && (row.reported === undefined || row.reported.configurationId === configurationId && row.reported.schemaVersion === schemaVersion))
    && new Set(value.items.map(adoptionRowKey)).size === value.items.length;
}
export function freshnessLabel(value: string): string {
  if (!knownConfigurationFreshness(value).isKnown) { return "Unknown freshness"; }
  const labels: Readonly<Record<string, string>> = { fresh: "Fresh", stale: "Stale", offline: "Offline", unknown: "Unknown", noInstances: "No observed recipients" };
  return labels[value] ?? "Unknown freshness";
}
export function updateModeLabel(value: string | undefined): string {
  if (value === undefined) { return "Not reported"; }
  return knownConfigurationUpdateMode(value).isKnown ? value === "HotReload" ? "Hot reload" : "Restart required" : "Unknown update mode";
}
export function reportedStateLabel(value: string | undefined): string {
  if (value === undefined) { return "Not reported"; }
  if (!knownConfigurationState(value).isKnown) { return "Unknown reported state"; }
  const labels: Readonly<Record<string, string>> = { Pending: "Pending", Applied: "Applied", AppliedUnchanged: "Applied unchanged", Rejected: "Rejected",
    PendingRestart: "Pending restart", Faulted: "Faulted" };
  return labels[value] ?? "Unknown reported state";
}

/** Native convergence and the two native freshness facets stay separate; no fleet/live guarantee is derived. */
export function adoptionPresentation(row: ConfigurationAdoptionEntry, earlierPage = false): AdoptionPresentation {
  const observation = nativePresentation(row);
  if (!earlierPage) { return observation; }
  const labels: Readonly<Record<string, string>> = {
    "Applied (reported)": "Applied (last observed)", "Applied (last reported)": "Applied (last observed)",
    "Applied unchanged (reported)": "Applied unchanged (last observed)", "Applied unchanged (last reported)": "Applied unchanged (last observed)",
    "Pending current assignment": "Pending assignment (last observed)", "Pending restart": "Pending restart (last observed)",
    "Rejected candidate": "Rejected candidate (last observed)", "Configuration faulted": "Configuration faulted (last observed)",
    "Current adoption unknown": "Adoption unknown (earlier page)", "No observed recipients": "No observed recipients (earlier page)"
  };
  return Object.freeze({ label: labels[observation.label] ?? "Adoption unknown (earlier page)",
    detail: "This is an earlier permitted page observation. Current adoption could not be refreshed. Reported facts and native freshness describe that read.", tone: "neutral" });
}
function nativePresentation(row: ConfigurationAdoptionEntry): AdoptionPresentation {
  const current = row.convergence;
  if (!knownConfigurationConvergence(current).isKnown) {
    return Object.freeze({ label: "Current adoption unknown", detail: "The service reported an adoption state this portal does not recognize.", tone: "neutral" });
  }
  if (row.reported !== undefined && (!knownConfigurationState(row.reported.state).isKnown
    || !knownConfigurationUpdateMode(row.reported.updateMode).isKnown)) {
    return Object.freeze({ label: "Current adoption unknown", detail: "The report uses an unrecognized state or update mode. Current acceptance is unknown.", tone: "neutral" });
  }
  if (current === "noInstances") {
    return Object.freeze({ label: "No observed recipients", detail: "No registration is observed for this target. This is not successful adoption or a process count.", tone: "neutral" });
  }
  if (current === "faulted") {
    return Object.freeze({ label: "Configuration faulted", detail: "Live state is not guaranteed. Last successful acceptance is historical.", tone: "warning" });
  }
  if (current === "unknown") {
    return Object.freeze({ label: "Current adoption unknown", detail: "No matching adoption report establishes the current assignment's acceptance.", tone: "neutral" });
  }
  if (current === "pending") {
    return Object.freeze({ label: "Pending current assignment", detail: "A previous accepted version does not establish acceptance of the current desired assignment.", tone: "neutral" });
  }
  if (current === "pendingRestart") {
    return Object.freeze({ label: "Pending restart", detail: "The last report identified a valid candidate pending restart; accepted live values were unchanged then. Current live state is not guaranteed.", tone: "warning" });
  }
  if (current === "rejected") {
    return Object.freeze({ label: "Rejected candidate", detail: "The candidate was rejected. Any prior acceptance is shown separately.", tone: "warning" });
  }
  const accepted = row.reported?.state === "Applied" || row.reported?.state === "AppliedUnchanged";
  if (!accepted || row.reported?.applied === undefined || !row.reported.liveStateValid || !knownConfigurationUpdateMode(row.reported.updateMode).isKnown) {
    return Object.freeze({ label: "Current adoption unknown", detail: "The current report does not establish a recognized accepted live state.", tone: "neutral" });
  }
  const fresh = row.registrationFreshness === "fresh" && row.adoptionFreshness === "fresh";
  return Object.freeze({ label: row.reported.state === "AppliedUnchanged"
    ? fresh ? "Applied unchanged (reported)" : "Applied unchanged (last reported)"
    : fresh ? "Applied (reported)" : "Applied (last reported)",
    detail: fresh ? "Accepted in the reported observation. Freshness is not a guarantee of uninterrupted live execution."
      : "Registration or adoption freshness is not current. This remains an earlier acceptance observation.",
    tone: fresh ? "good" : "neutral" });
}
