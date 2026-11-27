import { decodeManagement, isOperationKey, isStrongETag, isUuid } from "@cislib/management-client";
import type { MetadataCommand } from "@cislib/management-client";
import type { ClientCallResult } from "@cislib/client-core";
import { projectMetadataEditor } from "../metadata-draft.ts";
import { projectMetadataCommand } from "../command-results.ts";
import { PortalRequestInvalid, readPortalJson, withPortalCall } from "./portal-call.ts";

export function isMetadataPath(path: readonly string[]): boolean {
  return path[0] === "applications" && (path[2] === "metadata" || path[2] === "metadata-operations");
}

/** Three finite paths. Original-result lookup deliberately has no application/read prefetch. */
export function handleMetadata(request: Request, path: readonly string[]): Promise<Response> {
  const writing = request.method === "PUT";
  return withPortalCall(request, { mutating: writing, requireContext: writing }, async scope => {
    const applicationId = path[1];
    if (applicationId === undefined || !isUuid(applicationId) || new URL(request.url).search !== "") { throw new PortalRequestInvalid(); }
    if (path.length === 3 && path[2] === "metadata") {
      if (request.method === "GET") {
        if (request.body !== null) { throw new PortalRequestInvalid(); }
        const result = await scope.runtime.management.getApplication(applicationId, scope.context);
        if (result.kind !== "http" || result.status !== 200 || result.value === undefined || !result.cleanupSucceeded) {
          return readFailure(result, scope.failure);
        }
        const value = result.value;
        if (value.applicationId !== applicationId) { return scope.failure("unavailable", "Current metadata could not be verified.", 503); }
        const capabilities = await scope.runtime.management.getCapabilities(applicationId, scope.context);
        if (capabilities.kind !== "http" || capabilities.status !== 200 || capabilities.value === undefined || !capabilities.cleanupSucceeded) {
          return readFailure(capabilities, scope.failure);
        }
        if (capabilities.value.applicationId !== applicationId) { return scope.failure("unavailable", "Current permissions could not be verified.", 503); }
        const hints = capabilities.value.callerHints;
        const canReplace = hints?.some(item => item.operationId === "replaceApplicationMetadata" && item.allowed) === true
          && hints.some(item => item.operationId === "getMetadataOperation" && item.allowed);
        return scope.ready(projectMetadataEditor(value, canReplace));
      }
      if (request.method === "PUT") {
        const key = request.headers.get("Idempotency-Key");
        const ifMatch = request.headers.get("If-Match");
        if (!isOperationKey(key) || !isStrongETag(ifMatch)) { throw new PortalRequestInvalid(); }
        const raw = await readPortalJson(request, scope.signal);
        let command: MetadataCommand;
        try { command = decodeManagement<MetadataCommand>("MetadataCommand", raw, "request"); }
        catch { throw new PortalRequestInvalid("The display metadata is invalid."); }
        const result = await scope.runtime.management.replaceMetadata(applicationId, key!, ifMatch!, command, scope.context);
        if (result.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
        return scope.ready(projectMetadataCommand(result, applicationId, key!));
      }
      return scope.failure("invalid", "This method is unavailable for display metadata.", 405);
    }
    if (path.length === 4 && path[2] === "metadata-operations" && request.method === "GET" && isOperationKey(path[3])) {
      if (request.body !== null) { throw new PortalRequestInvalid(); }
      const result = await scope.runtime.management.getMetadataOperation(applicationId, path[3]!, scope.context);
      if (result.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
      return scope.ready(projectMetadataCommand(result, applicationId, path[3]!));
    }
    return scope.failure("not-found", "This metadata operation is unavailable.", 404);
  });
}

function readFailure(result: ClientCallResult<unknown, unknown>, failure: (state: "unauthenticated" | "denied" | "not-found" | "invalid" | "unavailable", message: string, status: number) => Response): Response {
  if (result.status === 401) { return failure("unauthenticated", "Sign in to edit application metadata.", 401); }
  if (result.status === 403) { return failure("denied", "Metadata is unavailable for this application.", 403); }
  if (result.status === 404) { return failure("not-found", "This application is unavailable.", 404); }
  return failure("unavailable", "Current metadata could not be read. Try Refresh.", result.kind === "timeout" ? 504 : 503);
}
