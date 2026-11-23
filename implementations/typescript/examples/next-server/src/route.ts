import "server-only";
import type { CredentialAttachment } from "@cislib/client-core";
import type { ManagementClient } from "@cislib/management-client";
import { applicationView } from "./projection.js";

/** Implement with the application's native session/delegation owner, not copied authority headers. */
export type RequestCredential = (request: Request, signal: AbortSignal) => Promise<CredentialAttachment>;

export function createApplicationsGet(client: ManagementClient, resolveCredential: RequestCredential): (request: Request) => Promise<Response> {
  return async request => {
    const result = await client.listApplications({ pageSize: 20 }, {
      signal: request.signal,
      credentials: ({ signal }) => resolveCredential(request, signal)
    });
    if (result.kind === "http" && result.status === 200 && result.value !== undefined) {
      return Response.json({ applications: result.value.items.map(applicationView) }, {
        headers: { "Cache-Control": "no-store" }
      });
    }
    const status = result.status === 401 || result.status === 403 ? result.status : result.kind === "timeout" ? 504 : 502;
    return Response.json({ error: "Management data is unavailable." }, { status, headers: { "Cache-Control": "no-store" } });
  };
}
