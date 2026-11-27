import "server-only";
import type { ClientCallContext } from "@cislib/client-core";
import { PortalRequestInvalid } from "../portal-body.ts";
export { PortalRequestInvalid, readPortalJson } from "../portal-body.ts";
import type { PortalReply } from "../portal-model.ts";
import type { PortalSessionContext } from "../portal-context.ts";
import { portalContextHeader, portalContextKey, samePortalSession } from "../portal-context.ts";
import { managementAuthorization, PortalAuthenticationRequired } from "./auth.ts";
import { borrowRuntime, PortalRuntime, PortalStoppingError } from "./runtime.ts";

type FailureState = Exclude<PortalReply<never>["state"], "ready">;
export interface PortalCallScope {
  readonly runtime: PortalRuntime;
  readonly signal: AbortSignal;
  readonly context: ClientCallContext;
  ready<T>(value: T, status?: number): Response;
  failure(state: FailureState, message: string, status: number): Response;
}
class PortalCallContextChanged extends Error {}
const cacheHeaders = Object.freeze({ "Cache-Control": "private, no-store, max-age=0", Pragma: "no-cache", Vary: "Cookie" });

/** One ordinary BFF request owner. Callers still select only their finite typed native methods. */
export async function withPortalCall(request: Request, options: { readonly mutating?: boolean; readonly requireContext?: boolean },
  execute: (scope: PortalCallScope) => Promise<Response>): Promise<Response> {
  let release: (() => void) | undefined;
  let authenticated: PortalSessionContext | undefined;
  let changed = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const fail = (state: FailureState, message: string, status: number) => Response.json({ state, message,
    ...(state === "unauthenticated" || authenticated === undefined ? {} : { context: authenticated }) }, { status, headers: cacheHeaders });
  try {
    const runtime = borrowRuntime();
    const origin = request.headers.get("origin");
    const site = request.headers.get("sec-fetch-site");
    if (site === "cross-site" || origin !== null && origin !== runtime.config.origin
      || options.mutating && (origin !== runtime.config.origin || site !== null && site !== "same-origin")) {
      return fail("denied", "This operation is available only from the portal.", 403);
    }
    const expected = request.headers.get(portalContextHeader);
    if (expected !== null && !/^[a-f0-9]{64}\.[a-f0-9]{64}\.[a-f0-9]{64}$/u.test(expected)
      || options.requireContext && expected === null) {
      return fail("invalid", "Refresh the protected view before continuing.", 400);
    }
    release = runtime.enterRequest();
    if (release === undefined) { return fail("unavailable", "The portal is busy. Try again shortly.", 503); }
    const deadline = new AbortController();
    timer = setTimeout(() => deadline.abort(), 30000);
    const signal = AbortSignal.any([request.signal, runtime.signal, deadline.signal]);
    const context: ClientCallContext = { signal, credentials: async ({ signal: credentialSignal }) => {
      const authorization = await managementAuthorization(credentialSignal);
      const previous = authenticated;
      authenticated = authorization.context;
      if (expected !== null && expected !== portalContextKey(authorization.context)
        || previous !== undefined && !samePortalSession(previous, authorization.context)) {
        changed = true;
        throw new PortalCallContextChanged();
      }
      return authorization.credential;
    } };
    return await execute({ runtime, signal, context,
      ready: (value, status = 200) => changed
        ? fail("invalid", "The signed-in session changed. Refresh this view.", 409)
        : Response.json({ state: "ready", value, ...(authenticated === undefined ? {} : { context: authenticated }) },
          { status, headers: cacheHeaders }),
      failure: (state, message, status) => changed
        ? fail("invalid", "The signed-in session changed. Refresh this view.", 409) : fail(state, message, status)
    });
  } catch (error) {
    if (changed || error instanceof PortalCallContextChanged) { return fail("invalid", "The signed-in session changed. Refresh this view.", 409); }
    if (error instanceof PortalAuthenticationRequired) { authenticated = undefined; return fail("unauthenticated", "Sign in to continue.", 401); }
    if (error instanceof PortalRequestInvalid) { return fail("invalid", error.message, error.status); }
    if (error instanceof PortalStoppingError) { return fail("unavailable", "The portal is stopping.", 503); }
    if (request.signal.aborted) { return new Response(null, { status: 499, headers: cacheHeaders }); }
    console.error("[Portal] Protected operation failed.");
    return fail("unavailable", "This operation is unavailable. Check its original result before starting another change.", 503);
  } finally {
    if (timer !== undefined) { clearTimeout(timer); }
    release?.();
  }
}


