import type { PortalSessionContext } from "./portal-context.ts";

/** Browser-safe, read-only projections. Native credentials and arbitrary module data never cross this boundary. */
export type PortalReply<T> = ({ readonly state: "ready"; readonly value: T }
  | { readonly state: "unauthenticated" | "denied" | "not-found" | "invalid" | "unavailable"; readonly message: string })
  & { readonly context?: PortalSessionContext };

export interface PortalPage<T> { readonly items: readonly T[]; readonly nextCursor?: string; }
export interface PortalApplication {
  readonly applicationId: string;
  readonly application: string;
  readonly environment: string;
  readonly metadata: { readonly displayName: string; readonly description?: string };
  readonly metadataRevision: string;
}
export interface PortalInstance {
  readonly registrationId: string;
  readonly applicationId: string;
  readonly reportedInstanceId: string;
  readonly instanceEvidence: { readonly kind: string; readonly reportedIdentifier?: string; readonly verifiedIdentifier?: string };
  readonly processStartedAtUtc: string;
  readonly applicationVersion: string;
  readonly protocolMajor: number;
  readonly registeredAtUtc: string;
  readonly presence: string;
  readonly lastSeenAtUtc?: string;
  readonly closedAtUtc?: string;
  readonly observation?: { readonly sequence: string; readonly observedAtUtc: string; readonly lifecycle: string; readonly health: string };
  readonly observationReceivedAtUtc?: string;
  readonly inventorySequence?: string;
  readonly inventoryReceivedAtUtc?: string;
}
export interface PortalDefinition {
  readonly kind: "application";
  readonly configurationId: string;
  readonly schemaVersion: string;
  readonly sourceId: string;
  readonly environment: string;
  readonly applicationId: string;
  readonly bindingId: string;
  readonly hostApplicationName: string;
  readonly description?: string;
  readonly shapeKind: string;
  readonly nullable: boolean;
  readonly memberCount: number;
}
export interface PortalCapability {
  readonly operationId: string;
  readonly version: number;
  readonly method: string;
  readonly path: string;
  readonly requiredCapabilities: readonly string[];
  readonly callerHint: "allowed" | "denied" | "notEvaluated";
}
/** Current feature availability is explicit; service HTTP diagnostics are never application definitions. */
export interface PortalCapabilities {
  readonly applicationId: string;
  readonly catalog: { readonly state: "unsupported"; readonly message: string };
  readonly serviceApis: readonly PortalCapability[];
}
export interface PortalCategory {
  readonly executionScope: string;
  readonly categoryId: string;
  readonly displayName: string;
  readonly description?: string;
}
export interface PortalJobObservation {
  readonly resourceId: string;
  readonly mode: string;
  readonly state: string;
  readonly lastOutcome?: string;
  readonly observedOwner?: {
    readonly ownerApplication: string;
    readonly instanceId: string;
    readonly generation: string;
    readonly expiresAtUtc: string;
    readonly observedAtUtc: string;
  };
  readonly lastOccurrenceUtc?: string;
  readonly nextOccurrenceUtc?: string;
}
export interface PortalJob {
  readonly executionScope: string;
  readonly jobId: string;
  readonly categoryId: string;
  readonly displayName: string;
  readonly supportedModes: readonly string[];
  readonly description?: string;
  readonly resourceScopeDescription?: string;
  readonly effectiveConfiguration?: { readonly revision: string; readonly enabled: boolean };
  readonly observations?: readonly PortalJobObservation[];
}
export interface PortalInventory {
  readonly registrationId: string;
  readonly applicationVersion: string;
  readonly receivedAtUtc: string;
  readonly inventory: {
    readonly sequence: string;
    readonly observedAtUtc: string;
    readonly categories: readonly PortalCategory[];
    readonly jobs: readonly PortalJob[];
  };
}

/** A reported UUID remains exact technical data; it is not manufactured into a human-readable name. */
export function reportedLabel(value: string, unnamed: string): string {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu.test(value) ? unnamed : value;
}
