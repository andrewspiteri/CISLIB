import type { Application, Instance, Capabilities, InventoryItem, ConfigurationDefinition, Page } from "@cislib/management-client";
import type { PortalApplication, PortalInstance, PortalDefinition, PortalCapabilities, PortalInventory, PortalPage } from "../portal-model.ts";

export function projectPage<T, U>(value: Page<T>, project: (item: T) => U): PortalPage<U> {
  return { items: value.items.map(project), ...(value.nextCursor === undefined ? {} : { nextCursor: value.nextCursor }) };
}
export function projectApplication(value: Application): PortalApplication {
  return {
    applicationId: value.applicationId, application: value.application, environment: value.environment,
    metadata: { displayName: value.metadata.displayName, ...optional(value.metadata, "description") },
    metadataRevision: value.metadataRevision
  };
}
export function projectInstance(value: Instance): PortalInstance {
  return {
    registrationId: value.registrationId, applicationId: value.applicationId,
    reportedInstanceId: value.reportedInstanceId,
    instanceEvidence: { kind: value.instanceEvidence.kind, ...optional(value.instanceEvidence, "reportedIdentifier"),
      ...optional(value.instanceEvidence, "verifiedIdentifier") },
    processStartedAtUtc: value.processStartedAtUtc, applicationVersion: value.applicationVersion,
    protocolMajor: value.protocolMajor, registeredAtUtc: value.registeredAtUtc, presence: value.presence,
    ...optional(value, "lastSeenAtUtc"), ...optional(value, "closedAtUtc"), ...optional(value, "observationReceivedAtUtc"),
    ...optional(value, "inventorySequence"), ...optional(value, "inventoryReceivedAtUtc"),
    ...(value.observation === undefined ? {} : { observation: {
      sequence: value.observation.sequence, observedAtUtc: value.observation.observedAtUtc,
      lifecycle: value.observation.lifecycle, health: value.observation.health
    } })
  };
}
export function projectDefinition(value: ConfigurationDefinition): PortalDefinition {
  if (value.kind !== "application") { throw new Error("Application definition query returned an unrelated source."); }
  return {
    kind: "application", configurationId: value.configurationId, schemaVersion: value.schemaVersion,
    sourceId: value.sourceId, environment: value.environment, applicationId: value.applicationId,
    bindingId: value.bindingId, hostApplicationName: value.hostApplicationName,
    ...optional(value, "description"), shapeKind: value.shape.kind, nullable: value.shape.nullable,
    memberCount: value.shape.members?.length ?? 0
  };
}
export function projectCapabilities(value: Capabilities): PortalCapabilities {
  const hints = new Map(value.callerHints.map(item => [item.operationId, item.allowed]));
  return {
    applicationId: value.applicationId,
    catalog: { state: "unsupported", message: "Trusted application feature catalog inspection is unavailable in this portal." },
    serviceApis: value.implementedApis.map(item => ({
      operationId: item.operationId, version: item.version, method: item.method, path: item.path,
      requiredCapabilities: [...item.requiredCapabilities],
      callerHint: !hints.has(item.operationId) ? "notEvaluated" : hints.get(item.operationId) ? "allowed" : "denied"
    }))
  };
}
export function projectInventory(value: InventoryItem): PortalInventory {
  return {
    registrationId: value.registrationId, applicationVersion: value.applicationVersion, receivedAtUtc: value.receivedAtUtc,
    inventory: {
      sequence: value.inventory.sequence, observedAtUtc: value.inventory.observedAtUtc,
      categories: value.inventory.categories.map(item => ({ executionScope: item.executionScope, categoryId: item.categoryId,
        displayName: item.displayName, ...optional(item, "description") })),
      jobs: value.inventory.jobs.map(item => ({
        executionScope: item.executionScope, jobId: item.jobId, categoryId: item.categoryId,
        displayName: item.displayName, supportedModes: [...item.supportedModes],
        ...optional(item, "description"), ...optional(item, "resourceScopeDescription"),
        ...(item.effectiveConfiguration === undefined ? {} : { effectiveConfiguration: {
          revision: item.effectiveConfiguration.revision, enabled: item.effectiveConfiguration.enabled } }),
        ...(item.observations === undefined ? {} : { observations: item.observations.map(observation => ({
          resourceId: observation.resourceId, mode: observation.mode, state: observation.state,
          ...optional(observation, "lastOutcome"), ...optional(observation, "lastOccurrenceUtc"), ...optional(observation, "nextOccurrenceUtc"),
          ...(observation.observedOwner === undefined ? {} : { observedOwner: {
            ownerApplication: observation.observedOwner.ownerApplication, instanceId: observation.observedOwner.instanceId,
            generation: observation.observedOwner.generation, expiresAtUtc: observation.observedOwner.expiresAtUtc,
            observedAtUtc: observation.observedOwner.observedAtUtc } })
        })) })
      }))
    }
  };
}
function optional<T extends object, K extends keyof T>(value: T, key: K): { [P in K]?: Exclude<T[P], undefined> } {
  return value[key] === undefined ? {} : { [key]: value[key] } as { [P in K]?: Exclude<T[P], undefined> };
}
