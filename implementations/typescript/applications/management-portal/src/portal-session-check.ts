import { capturePortalContext, portalContextHeader, portalContextKey, samePortalSession } from "./portal-context.ts";
import type { PortalSessionContext } from "./portal-context.ts";

export const portalSessionInvalidationChannel = "cis.management.portal.session.v1";
export const portalSessionInvalidationMessage = "invalidate";

/** A notification only invalidates: it carries no identity, value, receipt or reusable credential. */
export function publishPortalSessionInvalidation(channel: Pick<BroadcastChannel, "postMessage"> | undefined,
  storage: () => Pick<Storage, "setItem" | "removeItem">): void {
  try { channel?.postMessage(portalSessionInvalidationMessage); } catch { /* The storage notification remains available. */ }
  try {
    const target = storage();
    target.setItem(portalSessionInvalidationChannel, portalSessionInvalidationMessage);
    target.removeItem(portalSessionInvalidationChannel);
  } catch { /* The active document still revalidates its actual server session. */ }
}

export type PortalSessionCheckResult = { readonly state: "current"; readonly context: PortalSessionContext }
  | { readonly state: "changed" | "unauthenticated" | "unavailable" };

/** The existing authenticated BFF read establishes context; its application values are never retained here. */
export async function readPortalSessionCheck(context: PortalSessionContext, signal: AbortSignal,
  read: typeof fetch = fetch): Promise<PortalSessionCheckResult> {
  const response = await read("/api/portal/applications", { credentials: "same-origin", cache: "no-store", redirect: "error",
    headers: { [portalContextHeader]: portalContextKey(context) }, signal });
  let body: unknown;
  try { body = await response.json(); }
  finally { if (!response.bodyUsed) { await response.body?.cancel(); } }
  if (typeof body !== "object" || body === null || Array.isArray(body) || !("state" in body)) { return { state: "unavailable" }; }
  if (response.status === 401 && body.state === "unauthenticated") { return { state: "unauthenticated" }; }
  const actual = capturePortalContext("context" in body ? body.context : undefined);
  if (actual === undefined) { return { state: "unavailable" }; }
  if ((response.status === 409 && body.state === "invalid" || response.status === 200 && body.state === "ready")
    && !samePortalSession(context, actual)) { return { state: "changed" }; }
  return response.status === 200 && body.state === "ready" ? { state: "current", context: actual } : { state: "unavailable" };
}
