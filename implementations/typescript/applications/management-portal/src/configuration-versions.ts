import type { ConfigurationAssignmentMap, ConfigurationPublication, ConfigurationRelease } from "@cislib/management-client";
import { projectConfigurationPublication, projectConfigurationRelease } from "./configuration-editor-model.ts";

export type ConfigurationVersionContent = { readonly state: "present"; readonly layer: "application" | "common"; readonly publication: ConfigurationPublication }
  | { readonly state: "absent" | "redacted" | "unavailable"; readonly message: string };
export interface ConfigurationVersionSnapshot {
  readonly release: ConfigurationRelease;
  readonly content: ConfigurationVersionContent;
}

export function configurationVersionContentMatches(value: ConfigurationVersionSnapshot): boolean {
  if (value.content.state !== "present") { return true; }
  const release = value.release, layer = release.applicationSource.state === "present" ? "application" : "common";
  const reference = layer === "application" ? release.applicationSource : release.commonSource, publication = value.content.publication;
  return value.content.layer === layer && reference.state === "present" && publication.publicationId === reference.publicationId
    && publication.kind === layer && publication.configurationId === release.configurationId && publication.schemaVersion === release.schemaVersion
    && publication.environment === release.environment && (layer === "common" ? publication.applicationId === undefined : publication.applicationId === release.applicationId);
}

export function projectConfigurationVersion(value: ConfigurationVersionSnapshot): ConfigurationVersionSnapshot {
  if (!configurationVersionContentMatches(value)) { throw new TypeError("The saved JSON does not belong to this exact version."); }
  return Object.freeze({ release: projectConfigurationRelease(value.release), content: value.content.state === "present"
    ? Object.freeze({ state: "present" as const, layer: value.content.layer, publication: projectConfigurationPublication(value.content.publication) })
    : Object.freeze({ state: value.content.state, message: value.content.message }) });
}
export function configurationVersionMatches(value: Pick<ConfigurationRelease, "applicationId" | "configurationId" | "schemaVersion" | "environment">, applicationId: string, configurationId: string, schemaVersion: string,
  environment?: string): boolean {
  return value.applicationId === applicationId && value.configurationId === configurationId && value.schemaVersion === schemaVersion
    && (environment === undefined || value.environment === environment);
}
export function configurationVersionLabel(value: ConfigurationRelease): string {
  return value.reason.length === 0 ? "Saved " + value.createdAtUtc : value.reason;
}
export function configurationVersionDeployment(map: ConfigurationAssignmentMap | undefined, releaseId: string): "deployed" | "targeted" | "saved" | "unavailable" {
  if (map === undefined) { return "unavailable"; }
  if (map.overrides.length > 0 && (map.defaultReleaseId === releaseId || map.overrides.some(row => row.releaseId === releaseId))) { return "targeted"; }
  return map.defaultReleaseId === releaseId ? "deployed" : "saved";
}
export function configurationVersionSharedBaseChanged(left: ConfigurationRelease, right: ConfigurationRelease): boolean {
  return left.commonSource.state !== right.commonSource.state || left.commonSource.publicationId !== right.commonSource.publicationId;
}
