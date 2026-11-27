import { isOperationKey, isOrderedInteger, isUuid } from "@cislib/management-client";
import type { CommandReceipt, CommandReceiptRecovery, CommandReceiptStore } from "./command-receipts.ts";
import type { ProtectedTarget } from "./protected-state.ts";

interface ConfigurationSelection { readonly applicationId: string; readonly configurationId: string; readonly schemaVersion: string; readonly view: "common" | "application" | "assignment"; }
interface TargetOwner { selectTarget(target: ProtectedTarget | undefined): void; }

/** Stable logical read selection. Returned native environment/binding still need their independent value checks. */
export function configurationReadTarget(selection: ConfigurationSelection): ProtectedTarget {
  const { applicationId, configurationId, schemaVersion, view } = selection;
  if (!isUuid(applicationId) || !isOperationKey(configurationId) || !isOrderedInteger(schemaVersion)
    || !["common", "application", "assignment"].includes(view)) { throw new TypeError("Select an exact Configuration view."); }
  const base = `/api/portal/applications/${applicationId}/configurations/${encodeURIComponent(configurationId)}`;
  return Object.freeze({ requestKey: view === "assignment" ? `${base}/assignments?schemaVersion=${schemaVersion}`
    : `${base}/editor?schemaVersion=${schemaVersion}&source=${view}`, applicationId, definitionId: configurationId, schemaVersion });
}

/** Background read initialization must select its stable view before the independent original lookup is admitted. */
export function recoverConfigurationOriginal(readOwner: TargetOwner, commandOwner: TargetOwner, receipts: CommandReceiptStore,
  receipt: CommandReceipt, selection: ConfigurationSelection, alreadyIssuedReadsSettled: boolean,
  lookup: (url: string, original: CommandReceipt, signal: AbortSignal) => Promise<void>): Promise<CommandReceiptRecovery<void>> {
  const readTarget = configurationReadTarget(selection);
  const binding = receipt.family === "management.configuration.release.create" || receipt.family === "management.configuration.assignment.replace";
  if (!isOperationKey(receipt.key) || binding && (receipt.target.applicationId !== selection.applicationId
    || receipt.target.configurationId !== selection.configurationId || receipt.target.schemaVersion !== selection.schemaVersion)
    || selection.view === "assignment" && receipt.family !== "management.configuration.assignment.replace"
    || selection.view !== "assignment" && receipt.family !== "management.configuration.release.create" && receipt.family !== "management.configuration.source.publish") {
    throw new TypeError("Select the exact retained Configuration operation.");
  }
  if (!alreadyIssuedReadsSettled || receipts.recoveryBusy) { return Promise.resolve({ kind: "blocked", reason: "busy" }); }
  const base = `/api/portal/applications/${selection.applicationId}/configurations/${encodeURIComponent(selection.configurationId)}`;
  let url: string;
  if (receipt.family === "management.configuration.source.publish") {
    if (!isUuid(receipt.target.sourceId)) { throw new TypeError("Select the exact retained source operation."); }
    url = `/api/portal/configuration/sources/${receipt.target.sourceId}/publication-operations/${encodeURIComponent(receipt.key)}`;
  } else {
    url = `${base}/${selection.view === "assignment" ? "assignment" : "release"}-operations/${encodeURIComponent(receipt.key)}?schemaVersion=${selection.schemaVersion}`;
  }
  readOwner.selectTarget(readTarget);
  commandOwner.selectTarget({ requestKey: url + "#lookup", ...(receipt.family === "management.configuration.source.publish" ? {}
    : { applicationId: selection.applicationId, definitionId: selection.configurationId, schemaVersion: selection.schemaVersion }) });
  return receipts.recover(receipt, async (original, signal) => { await lookup(url, original, signal); });
}
