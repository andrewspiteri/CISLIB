import { formatConfigurationJson } from "./configuration-draft.ts";
import type { ConfigurationAssignmentMap } from "@cislib/management-client";
import type { ConfigurationVersionSnapshot } from "./configuration-versions.ts";
import { configurationVersionContentMatches, configurationVersionMatches, configurationVersionSharedBaseChanged } from "./configuration-versions.ts";

export interface ConfigurationComparisonRow {
  readonly kind: "same" | "changed" | "added" | "removed";
  readonly left?: string;
  readonly right?: string;
}
export type ConfigurationVersionComparison = { readonly state: "ready"; readonly changed: boolean; readonly rows: readonly ConfigurationComparisonRow[] }
  | { readonly state: "unavailable"; readonly message: string };
export const configurationComparisonLimits = Object.freeze({ maximumRows: 512 });

export type ConfigurationSavedVersionComparison = { readonly state: "ready"; readonly changed: boolean; readonly sharedBaseChanged: boolean;
  readonly referencesChanged: boolean; readonly rows: readonly ConfigurationComparisonRow[]; readonly notice: string }
  | { readonly state: "unavailable"; readonly message: string };

/** The UI supplies its fresh actual map; a historical/default-looking row is never inferred as the deployed baseline. */
export function compareWithDeployedConfiguration(map: ConfigurationAssignmentMap, selected: ConfigurationVersionSnapshot,
  deployed: ConfigurationVersionSnapshot): ConfigurationSavedVersionComparison {
  if (map.defaultReleaseId === undefined) { return Object.freeze({ state: "unavailable", message: "No version is deployed for this Configuration. There is no deployed baseline to compare." }); }
  if (map.overrides.length > 0) { return Object.freeze({ state: "unavailable", message: "Existing targeted assignments do not provide one uniform deployed baseline. Comparison is unavailable." }); }
  if (deployed.release.releaseId !== map.defaultReleaseId
    || !configurationVersionMatches(deployed.release, map.applicationId, map.configurationId, map.schemaVersion, map.environment)
    || !configurationVersionMatches(selected.release, map.applicationId, map.configurationId, map.schemaVersion, map.environment)) {
    return Object.freeze({ state: "unavailable", message: "The current deployed version changed or does not match this Configuration. Refresh before comparing." });
  }
  return compareConfigurationSavedVersions(deployed, selected);
}

/** This compares actual selected JSON layers and identifies un-compared Shared/reference differences; it never composes effective JSON. */
export function compareConfigurationSavedVersions(left: ConfigurationVersionSnapshot, right: ConfigurationVersionSnapshot): ConfigurationSavedVersionComparison {
  const a = left.release, b = right.release;
  if (!configurationVersionMatches(b, a.applicationId, a.configurationId, a.schemaVersion, a.environment)
    || !configurationVersionContentMatches(left) || !configurationVersionContentMatches(right)) {
    return Object.freeze({ state: "unavailable", message: "Select versions from the same exact Configuration and schema." });
  }
  if (left.content.state !== "present" || right.content.state !== "present") {
    return Object.freeze({ state: "unavailable", message: "Both selected JSON layers must be currently readable. Protected or missing content is not an empty version." });
  }
  const compared = compareConfigurationVersionJson(left.content.publication.payloadJson, right.content.publication.payloadJson);
  if (compared.state !== "ready") { return compared; }
  const sharedBaseChanged = configurationVersionSharedBaseChanged(a, b);
  const referencesChanged = sharedBaseChanged || a.applicationSource.state !== b.applicationSource.state
    || a.applicationSource.publicationId !== b.applicationSource.publicationId;
  return Object.freeze({ state: "ready", changed: compared.changed || referencesChanged, sharedBaseChanged, referencesChanged, rows: compared.rows,
    notice: sharedBaseChanged ? "Shared bases differ. This compares the displayed JSON layers only; it does not compare or merge their Shared content."
      : referencesChanged ? "Saved publication references differ. The displayed JSON text is compared without composing runtime values."
        : "Exact saved JSON text comparison; whitespace outside strings is ignored. Shared references are unchanged. This is not runtime acceptance." });
}

/** Read-only exact JSON text comparison. Only whitespace outside strings is normalized; numbers are never parsed or re-encoded. */
export function compareConfigurationVersionJson(left: string, right: string): ConfigurationVersionComparison {
  const a = formatConfigurationJson(left), b = formatConfigurationJson(right);
  if (!a.valid || !b.valid) { return Object.freeze({ state: "unavailable", message: "These versions cannot be safely formatted within the bounded comparison limit. Inspect their exact JSON separately." }); }
  const before = a.json.split("\n"), after = b.json.split("\n");
  if (Math.max(before.length, after.length) > configurationComparisonLimits.maximumRows) {
    return Object.freeze({ state: "unavailable", message: "This comparison exceeds 512 lines. Inspect the versions separately; no partial diff is presented as complete." });
  }
  let prefix = 0, suffix = 0;
  while (prefix < Math.min(before.length, after.length) && before[prefix] === after[prefix]) { prefix++; }
  while (suffix < Math.min(before.length, after.length) - prefix && before[before.length - suffix - 1] === after[after.length - suffix - 1]) { suffix++; }
  const rows: ConfigurationComparisonRow[] = [];
  for (let index = 0; index < prefix; index++) { rows.push(Object.freeze({ kind: "same", left: before[index]!, right: after[index]! })); }
  const length = Math.max(before.length - prefix - suffix, after.length - prefix - suffix);
  for (let index = 0; index < length; index++) {
    const l = index < before.length - prefix - suffix ? before[prefix + index] : undefined;
    const r = index < after.length - prefix - suffix ? after[prefix + index] : undefined;
    rows.push(Object.freeze({ kind: l === undefined ? "added" : r === undefined ? "removed" : l === r ? "same" : "changed",
      ...(l === undefined ? {} : { left: l }), ...(r === undefined ? {} : { right: r }) }));
  }
  for (let index = suffix; index > 0; index--) { rows.push(Object.freeze({ kind: "same", left: before[before.length - index]!, right: after[after.length - index]! })); }
  return Object.freeze({ state: "ready", changed: a.json !== b.json, rows: Object.freeze(rows) });
}
