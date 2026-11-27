import { guid, issuer, providerId, tokenExpiryMarginSeconds } from "./config.ts";
import type { PortalConfig } from "./config.ts";

export interface VerifiedOperator {
  readonly accountId: string;
  readonly name: string;
  readonly email: string;
  readonly accessToken: string;
  readonly expiresAt: number;
}

/** Called only with the maintained OIDC client's already verified ID-token claims. */
export function mapVerifiedOperator(claims: Record<string, unknown>, tokens: { readonly access_token?: unknown; readonly expires_at?: unknown },
  config: PortalConfig, nowSeconds = Math.floor(Date.now() / 1000)): VerifiedOperator {
  if (claims.iss !== issuer(config) || typeof claims.tid !== "string" || claims.tid.toLowerCase() !== config.tenantId
    || typeof claims.oid !== "string") {
    throw new Error("OPERATOR_IDENTITY_DENIED");
  }
  const objectId = guid(claims.oid.toLowerCase());
  if (!config.allowedSubjects.has(objectId)) { throw new Error("OPERATOR_IDENTITY_DENIED"); }
  const token = tokens.access_token;
  const expiry = tokens.expires_at;
  if (typeof token !== "string" || token.length < 1 || token.length > 16384 || /[\u0000-\u0020\u007f]/u.test(token)
    || typeof expiry !== "number" || !Number.isSafeInteger(expiry)
    || expiry <= nowSeconds + tokenExpiryMarginSeconds || expiry > nowSeconds + 86400) {
    throw new Error("OPERATOR_CREDENTIAL_UNAVAILABLE");
  }
  const name = typeof claims.name === "string" && [...claims.name].length <= 256 && !/[\u0000-\u001f\u007f]/u.test(claims.name)
    && claims.name.length > 0 ? claims.name : "Operator";
  return Object.freeze({
    accountId: objectId,
    name,
    // A reserved adapter key, never a verified email address or authorization input.
    email: `${objectId}@${config.tenantId}.invalid`,
    accessToken: token,
    expiresAt: expiry
  });
}

export function safeReturnUrl(value: string, origin: string): string {
  if (value.length > 4096 || /[\u0000-\u0020\u007f\\]/u.test(value)) { return origin + "/applications"; }
  try {
    const url = new URL(value, origin);
    if (url.origin === origin && (url.pathname === "/applications" || url.pathname.startsWith("/applications/"))) {
      url.hash = "";
      return url.href;
    }
  } catch { /* Invalid browser input returns the fixed safe entry point. */ }
  return origin + "/applications";
}

export function allowedAuthAction(method: string, path: readonly string[]): boolean {
  if (method === "GET") {
    return path.length === 1 && ["signin", "signout", "csrf", "providers", "error"].includes(path[0]!)
      || path.length === 2 && path[0] === "callback" && path[1] === providerId;
  }
  return method === "POST" && (path.length === 1 && path[0] === "signout"
    || path.length === 2 && path[0] === "signin" && path[1] === providerId);
}

export function hasUniqueBoundedParameters(parameters: URLSearchParams, allowed: readonly string[], maximumValueLength: number): boolean {
  const names = new Set<string>();
  for (const [name, value] of parameters) {
    if (names.has(name) || !allowed.includes(name) || value.length > maximumValueLength || /[\u0000-\u001f\u007f]/u.test(value)) {
      return false;
    }
    names.add(name);
  }
  return true;
}

export function sameOriginMutation(headers: Headers, origin: string): boolean {
  return headers.get("origin") === origin && headers.get("sec-fetch-site") !== "cross-site";
}
