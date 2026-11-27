import { isOperationKey, isUuid } from "@cislib/management-client";
import type { ConfigurationCommandObservation } from "./configuration-command-results.ts";
import type { ConfigurationCreationReview, ConfigurationInitialDraft } from "./configuration-creation-model.ts";
import { captureConfigurationDraft } from "./configuration-draft.ts";
import { configurationOperationCanAdvance } from "./configuration-operation-facts.ts";

/** Admit only this Save's known setup result. The child still reads and verifies the actual source before any publication. */
export function configurationNewVersionDraft(review: ConfigurationCreationReview, result: ConfigurationCommandObservation, originalKey: string,
  capturedSessionKey: string, currentSessionKey: string | undefined): ConfigurationInitialDraft {
  const decision = result.decision;
  if (!isOperationKey(originalKey) || result.key !== originalKey || result.family !== "management.configuration.create"
    || !("applicationId" in result.target) || result.target.applicationId !== review.applicationId
    || capturedSessionKey.length === 0 || capturedSessionKey !== currentSessionKey
    || !configurationOperationCanAdvance(result)
    || decision?.kind !== "configurationCreated" || decision.applicationId !== review.applicationId
    || decision.configurationId !== review.configurationId || decision.schemaVersion !== review.schemaVersion || decision.schemaVersion !== "1"
    || decision.hostApplicationName !== review.hostApplicationName || !isUuid(decision.sourceId) || !isUuid(decision.bindingId)) {
    throw new TypeError("This Save's exact setup, session and cleanup must be confirmed before its JSON can be saved.");
  }
  return Object.freeze({ applicationId: review.applicationId, environment: review.environment, configurationId: review.configurationId,
    schemaVersion: review.schemaVersion, bindingId: decision.bindingId, sourceId: decision.sourceId,
    hostApplicationName: review.hostApplicationName, sessionKey: capturedSessionKey, raw: captureConfigurationDraft(review.raw) });
}
