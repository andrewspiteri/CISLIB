import { isOperationKey, isUuid } from "@cislib/management-client";
import type { CommandReceipt, CommandReceiptRecovery, CommandReceiptStore } from "./command-receipts.ts";
import type { MetadataCommandObservation } from "./command-results.ts";
import type { ProtectedStateOwner } from "./protected-state.ts";

/** Select the exact view before admission: selecting a different view intentionally cancels existing recovery. */
export function recoverMetadataOriginal(owner: Pick<ProtectedStateOwner<MetadataCommandObservation>, "selectTarget">,
  receipts: CommandReceiptStore, receipt: CommandReceipt, applicationId: string,
  lookup: (url: string, original: CommandReceipt, signal: AbortSignal) => Promise<void>): Promise<CommandReceiptRecovery<void>> {
  if (receipt.family !== "management.metadata.replace" || receipt.target.applicationId !== applicationId
    || !isUuid(applicationId) || !isOperationKey(receipt.key)) { throw new TypeError("Select the exact retained metadata operation."); }
  if (receipts.recoveryBusy) { return Promise.resolve({ kind: "blocked", reason: "busy" }); }
  const url = `/api/portal/applications/${applicationId}/metadata-operations/${encodeURIComponent(receipt.key)}`;
  owner.selectTarget({ requestKey: url + "#lookup", applicationId });
  return receipts.recover(receipt, async (original, signal) => { await lookup(url, original, signal); });
}
