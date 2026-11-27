import type { ConfigurationAdoptionEntry, ConfigurationRelease, ConfigurationVersion } from "@cislib/management-client";
import { isOrderedInteger, isUuid, knownConfigurationConvergence, knownConfigurationState, knownConfigurationUpdateMode } from "@cislib/management-client";
import type { AdoptionPresentation } from "./configuration-adoption-model.ts";
import { configurationVersionLabel, configurationVersionMatches } from "./configuration-versions.ts";

export interface InstanceConfigurationVersion {
  readonly state: "available" | "unavailable" | "notReported" | "unknown";
  readonly label: string;
  readonly createdAtUtc?: string;
  /** Actual reported accepted identity for diagnostics only; never a main-table label fallback. */
  readonly releaseId?: string;
}

/** Compact current-state wording; the original native observations remain available in diagnostics. */
export function instanceStatusPresentation(row: ConfigurationAdoptionEntry, earlierPage = false): AdoptionPresentation {
  if (earlierPage) { return status("Unknown", "Refresh to see the latest reported instance status."); }
  if (!knownConfigurationConvergence(row.convergence).isKnown) {
    return status("Unknown", "The service reported a status this portal does not recognize.");
  }
  if (row.convergence === "noInstances") {
    return status("No observed instances", "No instance has been reported for this Configuration.");
  }
  if (row.registrationFreshness !== "fresh" || row.adoptionFreshness !== "fresh") {
    return status("Unknown", "The instance or its Configuration report is no longer current.");
  }
  const report = row.reported;
  if (report === undefined || !knownConfigurationState(report.state).isKnown || !knownConfigurationUpdateMode(report.updateMode).isKnown) {
    return status("Unknown", "The latest report does not establish this instance's Configuration status.");
  }
  switch (row.convergence) {
    case "pendingRestart": return report.state === "PendingRestart"
      ? status("Needs restart", "At its last report, this instance needed a restart to use the deployed Configuration.", "warning")
      : status("Unknown", "The latest report does not confirm whether a restart is needed.");
    case "rejected": return report.state === "Rejected"
      ? status("Failed", "The instance reported that it could not accept the deployed Configuration.", "warning")
      : status("Unknown", "The latest report does not confirm the Configuration failure.");
    case "faulted": return report.state === "Faulted"
      ? status("Failed", "The instance reported that its Configuration could not be used.", "warning")
      : status("Unknown", "The latest report does not confirm the Configuration failure.");
    case "pending": return status("Pending", "Waiting for the instance to report the deployed Configuration.");
    case "applied": {
      if ((report.state === "Applied" || report.state === "AppliedUnchanged") && report.liveStateValid
        && sameVersion(row.desired, report.desired) && sameVersion(row.desired, report.applied)) {
        return status("Up to date", "The latest report matches the deployed Configuration.", "good");
      }
      return status("Unknown", "The latest report does not confirm that the deployed Configuration is in use.");
    }
    default: return status("Unknown", "The latest report does not confirm that the deployed Configuration is in use.");
  }
}

/** Metadata is optional: no extra read authority is required to keep the instance row visible. */
export function instanceConfigurationVersion(row: ConfigurationAdoptionEntry, releases: readonly ConfigurationRelease[] | undefined): InstanceConfigurationVersion {
  const report = row.reported;
  if (!knownConfigurationConvergence(row.convergence).isKnown) { return version("unknown", "Current version unknown"); }
  if (row.convergence === "noInstances") { return version("notReported", "Not reported"); }
  if (report === undefined) { return version("notReported", "Not reported"); }
  if (!knownConfigurationState(report.state).isKnown || !knownConfigurationUpdateMode(report.updateMode).isKnown) {
    return version("unknown", "Current version unknown");
  }
  const accepted = report.applied;
  if (accepted === undefined) { return version("notReported", "Not reported"); }
  if (!report.liveStateValid || !validVersion(accepted)) { return version("unknown", "Current version unknown"); }
  const unavailable = () => version("unavailable", "Version details unavailable", accepted.releaseId);
  // One optional history page plus the parent's already loaded exact deployed metadata.
  if (releases === undefined || releases.length > 101) { return unavailable(); }
  const matching = releases.filter(item => item.releaseId === accepted.releaseId);
  const release = matching[0];
  if (release === undefined || !matching.every(item => configurationVersionMatches(item, row.applicationId, row.configurationId, row.schemaVersion, row.environment)
    && item.reason === release.reason && item.createdAtUtc === release.createdAtUtc)) { return unavailable(); }
  return Object.freeze({ state: "available", label: configurationVersionLabel(release), createdAtUtc: release.createdAtUtc, releaseId: accepted.releaseId });
}

function validVersion(value: ConfigurationVersion): boolean { return isUuid(value.releaseId) && isOrderedInteger(value.generation); }
function sameVersion(left: ConfigurationVersion | undefined, right: ConfigurationVersion | undefined): boolean {
  return left !== undefined && right !== undefined && validVersion(left) && validVersion(right)
    && left.releaseId === right.releaseId && left.generation === right.generation;
}
function status(label: string, detail: string, tone: AdoptionPresentation["tone"] = "neutral"): AdoptionPresentation {
  return Object.freeze({ label, detail, tone });
}
function version(state: InstanceConfigurationVersion["state"], label: string, releaseId?: string): InstanceConfigurationVersion {
  return Object.freeze({ state, label, ...(releaseId === undefined ? {} : { releaseId }) });
}
