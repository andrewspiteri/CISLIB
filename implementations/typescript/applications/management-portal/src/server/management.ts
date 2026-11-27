import "server-only";
import type { ClientCallContext, ClientCallResult } from "@cislib/client-core";
import type { PortalReply } from "../portal-model.ts";
import { borrowRuntime, PortalStoppingError } from "./runtime.ts";
import { managementAuthorization, PortalAuthenticationRequired } from "./auth.ts";
import { portalContextHeader, portalContextKey, samePortalSession } from "../portal-context.ts";
import type { PortalSessionContext } from "../portal-context.ts";
import { guid } from "./config.ts";
import { projectApplication, projectCapabilities, projectDefinition, projectInstance, projectInventory, projectPage } from "./projection.ts";

/** A closed read boundary. There is no route to metadata/configuration mutations or arbitrary API forwarding. */
export async function handlePortalRead(request: Request, path: readonly string[]): Promise<Response> {
  if (request.method !== "GET") { return failure("invalid", "This view is read-only.", 405); }
  let release: (() => void) | undefined;
  let authenticatedContext: PortalSessionContext | undefined;
  let contextChanged = false;
  const respond = <T, U>(result: ClientCallResult<T, unknown>, project: (value: T) => U): Response => contextChanged
    ? failure("invalid", "The signed-in session changed. Refresh this view.", 409, authenticatedContext)
    : fromResult(result, project, authenticatedContext);
  try {
    const runtime = borrowRuntime();
    if (request.headers.get("sec-fetch-site") === "cross-site"
      || request.headers.has("origin") && request.headers.get("origin") !== runtime.config.origin) {
      return failure("denied", "This view is available only from the portal.", 403);
    }
    release = runtime.enterRequest();
    if (release === undefined) { return failure("unavailable", "The portal is busy. Try again shortly.", 503); }
    const url = new URL(request.url);
    const parameters = url.searchParams;
    const page = { pageSize: 25, ...cursor(parameters) };
    const signal = AbortSignal.any([request.signal, runtime.signal]);
    const expectedContext = request.headers.get(portalContextHeader);
    if (expectedContext !== null && !/^[a-f0-9]{64}\.[a-f0-9]{64}\.[a-f0-9]{64}$/u.test(expectedContext)) {
      return failure("invalid", "The protected view context is invalid. Refresh this view.", 400);
    }
    const context: ClientCallContext = { signal, credentials: async ({ signal: credentialSignal }) => {
      const authorization = await managementAuthorization(credentialSignal);
      const previous = authenticatedContext;
      authenticatedContext = authorization.context;
      if (expectedContext !== null && expectedContext !== portalContextKey(authorization.context)
        || previous !== undefined && !samePortalSession(previous, authorization.context)) {
        contextChanged = true;
        throw new PortalRequestContextChanged();
      }
      return authorization.credential;
    } };
    if (path.length === 1 && path[0] === "applications") {
      only(parameters, ["query", "environment", "cursor"]);
      const query = parameters.has("query") ? scalar(parameters.get("query")!, 128) : undefined;
      const environment = parameters.has("environment") ? scalar(parameters.get("environment")!, 255) : undefined;
      return respond(await runtime.management.queryApplications({ ...(query === undefined ? {} : { query }),
        ...(environment === undefined ? {} : { environment }) }, page, context), value => projectPage(value, projectApplication));
    }
    if (path.length === 1 && path[0] === "environments") {
      only(parameters, ["cursor"]);
      return respond(await runtime.management.listEnvironments({ ...page, pageSize: 32 }, context), value =>
        projectPage(value, item => ({ environment: item.environment })));
    }
    if (path[0] !== "applications" || path.length < 2 || path.length > 3) {
      return failure("not-found", "This view is unavailable.", 404);
    }
    let applicationId: string;
    try { applicationId = guid(path[1]!); }
    catch { throw new PortalQueryInvalid(); }
    if (path.length === 2) {
      only(parameters, []);
      return respond(await runtime.management.getApplication(applicationId, context), projectApplication);
    }
    switch (path[2]) {
      case "instances":
        only(parameters, ["cursor"]);
        return respond(await runtime.management.listInstances(applicationId, page, context), value => projectPage(value, projectInstance));
      case "configuration":
        only(parameters, ["cursor"]);
        return respond(await runtime.configuration.getApplicationDefinitions(applicationId, page, context), value =>
          projectPage(value, item => {
            if (item.kind !== "application" || item.applicationId !== applicationId) { throw new Error("Unrelated configuration definition."); }
            return projectDefinition(item);
          }));
      case "capabilities": {
        only(parameters, []);
        // Select the authorized application first. These existing HTTP descriptors remain secondary diagnostics;
        // no trusted logical definitions or participation are fabricated from this response or reported inventory.
        const application = await runtime.management.getApplication(applicationId, context);
        if (application.kind !== "http" || application.status !== 200 || application.value === undefined || !application.cleanupSucceeded) {
          return respond(application, projectApplication);
        }
        return respond(await runtime.management.getCapabilities(applicationId, context), projectCapabilities);
      }
      case "inventory":
        only(parameters, ["cursor"]);
        // Every item is one whole native-bounded registration report, never a truncated job subset.
        return respond(await runtime.management.listInventory(applicationId, { ...page, pageSize: 1 }, context), value => projectPage(value, projectInventory));
      default:
        return failure("not-found", "This view is unavailable.", 404);
    }
  } catch (error) {
    if (contextChanged || error instanceof PortalRequestContextChanged) {
      return failure("invalid", "The signed-in session changed. Refresh this view.", 409, authenticatedContext);
    }
    if (error instanceof PortalAuthenticationRequired) { return failure("unauthenticated", "Sign in to inspect applications.", 401); }
    if (error instanceof PortalStoppingError) { return failure("unavailable", "The portal is stopping.", 503, authenticatedContext); }
    if (error instanceof PortalQueryInvalid) { return failure("invalid", "The selected query is invalid. Return to the list and try again.", 400, authenticatedContext); }
    if (request.signal.aborted) { return new Response(null, { status: 499, headers: cacheHeaders }); }
    console.error("[Portal] Protected read failed.");
    return failure("unavailable", "This view is unavailable. Try again shortly.", 503, authenticatedContext);
  } finally { release?.(); }
}

const cacheHeaders = { "Cache-Control": "private, no-store, max-age=0", "Pragma": "no-cache", "Vary": "Cookie" };
function fromResult<T, U>(result: ClientCallResult<T, unknown>, project: (value: T) => U, context?: PortalSessionContext): Response {
  if (result.kind === "http" && result.status === 200 && result.value !== undefined && result.cleanupSucceeded) {
    return Response.json({ state: "ready", value: project(result.value), ...(context === undefined ? {} : { context }) } satisfies PortalReply<U>, { headers: cacheHeaders });
  }
  if (result.status === 401) { return failure("unauthenticated", "Sign in to inspect applications.", 401); }
  if (result.status === 403) { return failure("denied", "Access is not available for this target.", 403, context); }
  if (result.status === 404) { return failure("not-found", "The selected target is no longer available.", 404, context); }
  if (result.status === 400 || result.status === 422) { return failure("invalid", "The list changed or its query is invalid. Start from the first page.", 400, context); }
  if (result.kind === "canceled") { return new Response(null, { status: 499, headers: cacheHeaders }); }
  return failure("unavailable", result.status === 501 ? "This service does not support this view." : "This view is unavailable. Try again shortly.", result.kind === "timeout" ? 504 : 503, context);
}
function failure(state: Exclude<PortalReply<never>["state"], "ready">, message: string, status: number, context?: PortalSessionContext): Response {
  return Response.json({ state, message, ...(context === undefined ? {} : { context }) } satisfies PortalReply<never>, { status, headers: cacheHeaders });
}
function only(parameters: URLSearchParams, names: readonly string[]): void {
  const seen = new Set<string>();
  for (const key of parameters.keys()) {
    if (!names.includes(key) || seen.has(key)) { throw new PortalQueryInvalid(); }
    seen.add(key);
  }
}
function cursor(parameters: URLSearchParams): { cursor?: string } {
  if (!parameters.has("cursor")) { return {}; }
  const value = parameters.get("cursor")!;
  if (value.length < 1 || value.length > 2048 || !/^[\u0021-\u007e]+$/u.test(value)) { throw new PortalQueryInvalid(); }
  return { cursor: value };
}
function scalar(value: string, maximum: number): string {
  const points = [...value];
  if (points.length === 0 || points.length > maximum || /[\u0000-\u001f\u007f-\u009f\ud800-\udfff]/u.test(value)) {
    throw new PortalQueryInvalid();
  }
  return value;
}
class PortalQueryInvalid extends Error {
  constructor() { super("Invalid portal query."); this.name = "PortalQueryInvalid"; }
}
class PortalRequestContextChanged extends Error {
  constructor() { super("The protected portal context changed."); this.name = "PortalRequestContextChanged"; }
}
