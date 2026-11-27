import type { ConfigurationActions, ConfigurationEditorContext, ConfigurationPublication, ConfigurationRelease, ConfigurationSummary, Page } from "@cislib/management-client";
import { projectConfigurationActions } from "./configuration-editor-model.ts";
import type { PortalDefinition } from "./portal-model.ts";

/** A metadata row is never enough to infer whether a source is assigned. */
export type ConfigurationRow = PortalDefinition | ConfigurationSummary;
export interface ConfigurationRowActionsSnapshot {
  readonly applicationId: string;
  readonly configurationId: string;
  readonly schemaVersion: string;
  readonly bindingId: string;
  readonly actions: ConfigurationDetailSnapshot["actions"];
}

export type ConfigurationDetailContent = { readonly state: "present"; readonly layer: "common" | "application";
  readonly publication: Pick<ConfigurationPublication, "publicationId" | "sourceId" | "sha256" | "payloadJson" | "createdAtUtc"> }
  | { readonly state: "absent" | "unassigned" | "redacted" | "unavailable"; readonly message: string };
export interface ConfigurationDetailSnapshot {
  readonly applicationId: string;
  readonly application: string;
  readonly environment: string;
  readonly configurationId: string;
  readonly schemaVersion: string;
  readonly bindingId: string;
  readonly hostApplicationName: string;
  readonly description?: string;
  /** The default release's layers, independently of cohort overrides. */
  readonly origin: string;
  readonly hasCohortOverrides: boolean;
  readonly defaultReleaseId?: string;
  readonly generation?: string;
  readonly assignmentETag?: string;
  readonly applicationSourceId?: string;
  readonly commonSourceId?: string;
  readonly content: ConfigurationDetailContent;
  readonly actions: { readonly state: "available"; readonly binding: ConfigurationActions; readonly application?: ConfigurationActions }
    | { readonly state: "unavailable" };
}

/** This allowlist intentionally excludes schema defaults, actor identities and future native additions. */
export function projectConfigurationSummaryPage(value: Page<ConfigurationSummary>): Page<ConfigurationSummary> {
  return Object.freeze({ items: Object.freeze(value.items.map(row => Object.freeze({ applicationId: row.applicationId,
    environment: row.environment, configurationId: row.configurationId, schemaVersion: row.schemaVersion,
    sourceId: row.sourceId, bindingId: row.bindingId, hostApplicationName: row.hostApplicationName,
    origin: row.origin, hasCohortOverrides: row.hasCohortOverrides,
    ...(row.modifiedAtUtc === undefined ? {} : { modifiedAtUtc: row.modifiedAtUtc }),
    ...(row.description === undefined ? {} : { description: row.description }) }))),
    ...(value.nextCursor === undefined ? {} : { nextCursor: value.nextCursor }) });
}

export function projectConfigurationDetail(value: ConfigurationDetailSnapshot): ConfigurationDetailSnapshot {
  const content = value.content.state === "present" ? Object.freeze({ state: "present" as const, layer: value.content.layer,
    publication: Object.freeze({ publicationId: value.content.publication.publicationId, sourceId: value.content.publication.sourceId,
      sha256: value.content.publication.sha256, payloadJson: value.content.publication.payloadJson, createdAtUtc: value.content.publication.createdAtUtc }) }) : Object.freeze({ state: value.content.state, message: value.content.message });
  return Object.freeze({ applicationId: value.applicationId, application: value.application, environment: value.environment,
    configurationId: value.configurationId, schemaVersion: value.schemaVersion, bindingId: value.bindingId,
    hostApplicationName: value.hostApplicationName, origin: value.origin, hasCohortOverrides: value.hasCohortOverrides,
    ...(value.description === undefined ? {} : { description: value.description }),
    ...(value.defaultReleaseId === undefined ? {} : { defaultReleaseId: value.defaultReleaseId }),
    ...(value.generation === undefined ? {} : { generation: value.generation }),
    ...(value.assignmentETag === undefined ? {} : { assignmentETag: value.assignmentETag }),
    ...(value.applicationSourceId === undefined ? {} : { applicationSourceId: value.applicationSourceId }),
    ...(value.commonSourceId === undefined ? {} : { commonSourceId: value.commonSourceId }), content,
    actions: value.actions.state !== "available" ? Object.freeze({ state: "unavailable" as const })
      : Object.freeze({ state: "available" as const, binding: projectConfigurationActions(value.actions.binding),
        ...(value.actions.application === undefined ? {} : { application: projectConfigurationActions(value.actions.application) }) }) });
}

export function projectConfigurationRowActions(value: ConfigurationRowActionsSnapshot): ConfigurationRowActionsSnapshot {
  return Object.freeze({ applicationId: value.applicationId, configurationId: value.configurationId, schemaVersion: value.schemaVersion, bindingId: value.bindingId,
    actions: value.actions.state !== "available" ? Object.freeze({ state: "unavailable" as const }) : Object.freeze({ state: "available" as const,
      binding: projectConfigurationActions(value.actions.binding), ...(value.actions.application === undefined ? {} : { application: projectConfigurationActions(value.actions.application) }) }) });
}

export function configurationOrigin(origin: string | undefined): { readonly label: string; readonly status: string } {
  switch (origin) {
    case "shared": return { label: "Shared", status: "Assigned" };
    case "applicationOverride": return { label: "App override", status: "Assigned" };
    case "applicationOnly": return { label: "App only", status: "Assigned" };
    case "defaultsOnly": return { label: "Declared defaults", status: "Assigned" };
    case "mixed": return { label: "Varies by cohort", status: "Assigned" };
    case "unassigned": return { label: "No assigned source", status: "Unassigned" };
    case "protected": return { label: "Protected", status: "Not available" };
    case "unavailable": return { label: "Unavailable", status: "Not available" };
    default: return { label: "Not reported", status: "Not reported" };
  }
}

export function configurationDetailMatches(value: ConfigurationDetailSnapshot, app: string, configuration: string, schema: string): boolean {
  return value.applicationId === app && value.configurationId === configuration && value.schemaVersion === schema;
}

export function assignedConfigurationLayer(release: ConfigurationRelease, selected?: "common" | "application") {
  const common = release.commonSource, application = release.applicationSource;
  const origin = application.state === "present" ? common.state === "present" ? "applicationOverride" : "applicationOnly"
    : common.state === "present" ? "shared" : "defaultsOnly";
  const layer = selected ?? (application.state === "present" ? "application" : "common");
  return Object.freeze({ origin, layer, reference: layer === "application" ? application : common });
}

/** The native defaults availability describes ContentRead only for its exact selected source. */
export function configurationLayerAccess(context: ConfigurationEditorContext, layer: "common" | "application"):
  "available" | "redacted" | "unavailable" | undefined {
  const source = layer === "common" ? context.commonSource : context.applicationSource;
  if (source.state === "redacted") { return "redacted"; }
  if (source.state !== "available") { return "unavailable"; }
  return context.selectedSource === layer ? context.defaults.state : undefined;
}

export function configurationAction(value: Pick<ConfigurationDetailSnapshot, "actions"> | undefined, kind: "application" | "binding", operation: string): boolean {
  return value?.actions.state === "available" && value.actions[kind]?.actions.some(item => item.operationId === operation && item.supported && item.allowed) === true;
}
