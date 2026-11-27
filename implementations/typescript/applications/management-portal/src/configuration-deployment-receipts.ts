import type { CommandReceipt } from "./command-receipts.ts";
import type { ConfigurationCommandObservation } from "./configuration-command-results.ts";
import { configurationOperationCanAdvance } from "./configuration-operation-facts.ts";

/** Only an explicitly acknowledged exact clean completed deployment can release its local reference for the next Deploy. */
export function isCompletedConfigurationDeployment(receipt: CommandReceipt, value: ConfigurationCommandObservation | undefined,
  applicationId: string, configurationId: string, schemaVersion: string): boolean {
  if (receipt.family !== "management.configuration.assignment.replace" || value?.family !== receipt.family || value.key !== receipt.key
    || receipt.target.applicationId !== applicationId || receipt.target.configurationId !== configurationId || receipt.target.schemaVersion !== schemaVersion
    || !("applicationId" in value.target) || !("configurationId" in value.target) || !("schemaVersion" in value.target)
    || value.target.applicationId !== applicationId || value.target.configurationId !== configurationId || value.target.schemaVersion !== schemaVersion
    || value.operation?.name !== receipt.family || value.operation.key !== receipt.key || !["prepared", "replayed", "found"].includes(value.resultSource ?? "")
    || !configurationOperationCanAdvance(value)) { return false; }
  const decision = value.decision, map = decision?.assignment;
  return decision?.kind === "assignmentsReplaced" && decision.decision === "applied" && decision.applicationId === applicationId
    && decision.configurationId === configurationId && decision.schemaVersion === schemaVersion && map?.applicationId === applicationId
    && map.configurationId === configurationId && map.schemaVersion === schemaVersion;
}
