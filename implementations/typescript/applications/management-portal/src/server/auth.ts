import "server-only";
import { randomUUID } from "node:crypto";
import NextAuth, { getServerSession } from "next-auth";
import type { NextAuthOptions, Session } from "next-auth";
import type { Adapter, AdapterUser, AdapterAccount } from "next-auth/adapters";
import PostgresAdapter from "@auth/pg-adapter";
import { NextRequest } from "next/server";
import type { CredentialAttachment } from "@cislib/client-core";
import { borrowRuntime } from "./runtime.ts";
import type { PortalRuntime } from "./runtime.ts";
import { issuer, providerId, sessionCookieName, sessionMaximumSeconds, tokenExpiryMarginSeconds } from "./config.ts";
import { allowedAuthAction, hasUniqueBoundedParameters, mapVerifiedOperator, safeReturnUrl, sameOriginMutation } from "./auth-policy.ts";
import type { VerifiedOperator } from "./auth-policy.ts";

interface OperatorSession extends Session { readonly operatorId?: string; }
export class PortalAuthenticationRequired extends Error {
  constructor() { super("Sign in to inspect applications."); this.name = "PortalAuthenticationRequired"; }
}

/** An option set is request-local. Only infrastructure belongs to the long-lived host. */
export function createAuthOptions(runtime: PortalRuntime): NextAuthOptions {
  const config = runtime.config;
  const base = PostgresAdapter(runtime.pool);
  let login: VerifiedOperator | undefined;

  const adapter: Adapter = {
    // The stable v4 caller omits id; the maintained adapter returns the database's generated UUID.
    createUser: (user: Omit<AdapterUser, "id">) => base.createUser!({ ...user, id: randomUUID() }),
    getUser: id => base.getUser!(id),
    getUserByEmail: email => base.getUserByEmail!(email),
    getUserByAccount: account => base.getUserByAccount!(account),
    updateUser: user => base.updateUser!(user),
    async deleteUser(id) { await base.deleteUser!(id); },
    async linkAccount(account: AdapterAccount) {
      if (runtime.stopping || login === undefined || account.provider !== providerId || account.type !== "oauth"
        || account.providerAccountId !== login.accountId) { throw new Error("OPERATOR_ACCOUNT_DENIED"); }
      // Copy the closed, validated OAuth boundary; never retain incidental token fields.
      await base.linkAccount!({ userId: account.userId, provider: providerId, providerAccountId: login.accountId,
        type: "oauth", token_type: "bearer", access_token: login.accessToken, expires_at: login.expiresAt,
        scope: config.managementScope });
    },
    async createSession(input) {
      if (runtime.stopping || login === undefined) { throw new Error("OPERATOR_SESSION_DENIED"); }
      const expires = new Date(Math.min(input.expires.getTime(), (login.expiresAt - tokenExpiryMarginSeconds) * 1000));
      if (expires.getTime() <= Date.now()) { throw new Error("OPERATOR_CREDENTIAL_UNAVAILABLE"); }
      return await base.createSession!({ ...input, expires });
    },
    async getSessionAndUser(token) {
      const result = await base.getSessionAndUser!(token);
      if (result !== null && (!(result.session.expires instanceof Date) || result.session.expires.getTime() <= Date.now())) {
        await base.deleteSession!(token);
        return null;
      }
      return result;
    },
    async updateSession(input) {
      const existing = await base.getSessionAndUser!(input.sessionToken);
      if (existing === null || existing.session.expires.getTime() <= Date.now()) { return null; }
      const expires = input.expires === undefined ? existing.session.expires
        : new Date(Math.min(existing.session.expires.getTime(), input.expires.getTime()));
      await base.updateSession!({ ...input, expires });
      return { ...existing.session, ...input, expires };
    },
    async deleteSession(token) { await base.deleteSession!(token); }
  };
  return {
    adapter,
    secret: config.authSecret,
    useSecureCookies: true,
    session: { strategy: "database", maxAge: sessionMaximumSeconds, updateAge: sessionMaximumSeconds },
    cookies: {
      sessionToken: { name: sessionCookieName, options: { httpOnly: true, sameSite: "lax", path: "/", secure: true } }
    },
    providers: [{
      id: providerId, name: "Management operator", type: "oauth", issuer: issuer(config),
      authorization: { url: `${config.authorityOrigin}/${config.tenantId}/oauth2/v2.0/authorize`,
        params: { scope: `openid profile ${config.managementScope}`, response_type: "code", response_mode: "query" } },
      token: { url: `${config.authorityOrigin}/${config.tenantId}/oauth2/v2.0/token` },
      jwks_endpoint: `${config.authorityOrigin}/${config.tenantId}/discovery/v2.0/keys`,
      clientId: config.clientId, clientSecret: config.clientSecret, idToken: true,
      checks: ["pkce", "state", "nonce"], allowDangerousEmailAccountLinking: false,
      client: { id_token_signed_response_alg: "RS256", token_endpoint_auth_method: "client_secret_post" },
      httpOptions: { timeout: 8000, agent: runtime.oidc },
      profile(claims, tokens) {
        if (runtime.stopping) { throw new Error("OPERATOR_SESSION_DENIED"); }
        login = mapVerifiedOperator(claims as Record<string, unknown>, tokens, config);
        return { id: login.accountId, name: login.name, email: login.email, image: null };
      }
    }],
    callbacks: {
      async signIn({ account }) {
        if (runtime.stopping || login === undefined || account?.provider !== providerId || account.providerAccountId !== login.accountId) {
          return false;
        }
        // Auth.js links a new account, but does not refresh an existing account's
        // credential on reauthentication. The already verified account is the key.
        await runtime.pool.query(
          'UPDATE accounts SET access_token=$1, expires_at=$2, refresh_token=NULL, id_token=NULL, scope=$3 WHERE provider=$4 AND "providerAccountId"=$5',
          [login.accessToken, login.expiresAt, config.managementScope, providerId, login.accountId]);
        return true;
      },
      async session({ session, user }) {
        return { expires: session.expires, user: { name: user.name ?? "Operator" }, operatorId: user.id };
      },
      async redirect({ url }) { return safeReturnUrl(url, config.origin); }
    },
    logger: {
      error(code) { console.error("[PortalAuth] " + safeLogCode(code)); },
      warn(code) { console.warn("[PortalAuth] " + safeLogCode(code)); },
      debug() { /* Never log profile, token or callback metadata. */ }
    },
    debug: false
  };
}

/** Tokens are looked up only after the current opaque database session is validated. */
export async function managementCredential(signal: AbortSignal): Promise<CredentialAttachment> {
  signal.throwIfAborted();
  const runtime = borrowRuntime();
  const session = await getServerSession(createAuthOptions(runtime)) as OperatorSession | null;
  signal.throwIfAborted();
  if (session?.operatorId === undefined) { throw new PortalAuthenticationRequired(); }
  const account = await runtime.pool.query<{ access_token: string | null; expires_at: string | number | null; providerAccountId: string }>(
    'SELECT access_token, expires_at, "providerAccountId" FROM accounts WHERE "userId"=$1 AND provider=$2 LIMIT 2',
    [session.operatorId, providerId]);
  signal.throwIfAborted();
  const value = account.rows[0];
  const expiry = value === undefined ? NaN : Number(value.expires_at);
  if (account.rowCount !== 1 || value === undefined || !runtime.config.allowedSubjects.has(value.providerAccountId)
    || typeof value.access_token !== "string" || value.access_token.length < 1 || value.access_token.length > 16384
    || /[\u0000-\u0020\u007f]/u.test(value.access_token) || !Number.isSafeInteger(expiry)
    || expiry <= Math.floor(Date.now() / 1000) + tokenExpiryMarginSeconds) {
    throw new PortalAuthenticationRequired();
  }
  return { kind: "bearer", token: value.access_token };
}

/** Positive path/method and input allowlists surround the maintained handler. */
export async function handleAuth(request: NextRequest, context: { params: Promise<{ nextauth: string[] }> }): Promise<Response> {
  const runtime = borrowRuntime();
  const path = (await context.params).nextauth;
  if (!allowedAuthAction(request.method, path)) { return authResponse("Authentication route unavailable.", 404); }
  const url = new URL(request.url);
  const callback = path[0] === "callback";
  const allowedQuery = callback ? ["code", "state", "session_state", "error", "error_description", "error_uri"]
    : path[0] === "error" ? ["error"] : ["callbackUrl", "error"];
  if (url.search.length > 16384 || !hasUniqueBoundedParameters(url.searchParams, allowedQuery, 8192)) {
    return authResponse("Authentication request is invalid.", 400);
  }
  let forwarded = request;
  if (request.method === "POST") {
    if (!sameOriginMutation(request.headers, runtime.config.origin)) { return authResponse("Authentication origin denied.", 403); }
    if (request.headers.get("content-type")?.split(";")[0] !== "application/x-www-form-urlencoded") {
      return authResponse("Authentication request is invalid.", 415);
    }
    const body = await readForm(request);
    if (body === undefined || !hasUniqueBoundedParameters(body, ["csrfToken", "callbackUrl", "json"], 4096)) {
      return authResponse("Authentication request is invalid.", 400);
    }
    if (body.has("callbackUrl")) { body.set("callbackUrl", safeReturnUrl(body.get("callbackUrl")!, runtime.config.origin)); }
    const headers = new Headers(request.headers);
    headers.delete("content-length");
    headers.delete("transfer-encoding");
    forwarded = new NextRequest(url, { method: "POST", headers, body: body.toString(), signal: request.signal });
  }
  const options = createAuthOptions(runtime);
  if (path[0] === "signin" || callback) {
    const current = await getServerSession(options);
    if (current !== null) { return authResponse("Sign out before changing operator.", 409); }
  }
  const response = await NextAuth(options)(forwarded, context);
  response.headers.set("Cache-Control", "private, no-store, max-age=0");
  response.headers.set("Pragma", "no-cache");
  response.headers.set("Vary", "Cookie");
  return response;
}

function authResponse(message: string, status: number): Response {
  return new Response(message, { status, headers: { "Cache-Control": "private, no-store, max-age=0", "Content-Type": "text/plain; charset=utf-8" } });
}
async function readForm(request: Request): Promise<URLSearchParams | undefined> {
  if (request.body === null) { return undefined; }
  const reader = request.body.getReader();
  const parts: Uint8Array[] = [];
  let bytes = 0;
  try {
    for (;;) {
      const item = await reader.read();
      if (item.done) { break; }
      bytes += item.value.byteLength;
      if (bytes > 8192) { await reader.cancel(); return undefined; }
      parts.push(item.value);
    }
    return new URLSearchParams(new TextDecoder("utf-8", { fatal: true }).decode(Buffer.concat(parts)));
  } catch (error) {
    if (request.signal.aborted) { throw error; }
    return undefined;
  } finally { reader.releaseLock(); }
}
function safeLogCode(code: string): string { return /^[A-Z0-9_]{1,96}$/u.test(code) ? code : "AUTH_ERROR"; }
