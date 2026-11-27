import type { ConfigurationCreationContext } from "@cislib/management-client";
import { isOperationKey } from "@cislib/management-client";
import { captureConfigurationDraft, captureConfigurationReason } from "./configuration-draft.ts";
import type { ConfigurationEditorSnapshot } from "./configuration-editor-model.ts";

export interface ConfigurationCreationReview {
  readonly applicationId: string;
  readonly environment: string;
  readonly configurationId: string;
  readonly schemaVersion: string;
  readonly hostApplicationName: string;
  readonly ifMatch: string;
  readonly intent: "createGeneric" | "reuseGeneric";
  readonly reason: string;
  /** Held in memory only; not part of the creation command or its receipt. */
  readonly raw: string;
}
export interface ConfigurationInitialDraft {
  readonly applicationId: string;
  readonly environment: string;
  readonly configurationId: string;
  readonly schemaVersion: string;
  readonly bindingId: string;
  readonly sourceId: string;
  readonly hostApplicationName: string;
  readonly sessionKey: string;
  readonly raw: string;
}

export function projectConfigurationCreationContext(value: ConfigurationCreationContext): ConfigurationCreationContext {
  return Object.freeze({ applicationId: value.applicationId, application: value.application, environment: value.environment,
    eTag: value.eTag, schemaVersion: value.schemaVersion, hostApplicationName: value.hostApplicationName });
}

export function reviewConfigurationCreation(context: ConfigurationCreationContext, configurationId: string, raw: string, reason: string,
  intent: "createGeneric" | "reuseGeneric"): ConfigurationCreationReview {
  if (!isOperationKey(configurationId) || intent !== "createGeneric" && intent !== "reuseGeneric" || context.schemaVersion !== "1") {
    throw new TypeError("Provide one Configuration name for the fixed object schema.");
  }
  const captured = captureConfigurationDraft(raw);
  // The native generic schema is fixed. Checking the root does not infer a schema from values.
  if (captured.trimStart()[0] !== "{") { throw new TypeError("The new Configuration must contain a JSON object."); }
  return Object.freeze({ applicationId: context.applicationId, environment: context.environment, configurationId,
    schemaVersion: context.schemaVersion, hostApplicationName: context.hostApplicationName, ifMatch: context.eTag,
    intent, reason: captureConfigurationReason(reason), raw: captured });
}

export function initialConfigurationDraftMatches(seed: ConfigurationInitialDraft, value: ConfigurationEditorSnapshot, sessionKey: string): boolean {
  return seed.sessionKey === sessionKey && value.context.selectedSource === "application"
    && seed.applicationId === value.context.applicationId && seed.environment === value.context.environment
    && seed.configurationId === value.context.configurationId && seed.schemaVersion === value.context.schemaVersion
    && seed.bindingId === value.context.bindingId && seed.hostApplicationName === value.context.hostApplicationName
    && value.context.applicationSource.state === "available" && seed.sourceId === value.context.applicationSource.sourceId;
}
