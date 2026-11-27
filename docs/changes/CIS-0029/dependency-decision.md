---
title: "RM09 portal dependency decision"
type: dependency-decision
status: Proposed
change_id: CIS-0029
---

The controller selected the stable Auth.js composition below under the current
delegated portal scope. It supersedes the earlier Better Auth proposal retained
at `.cis/local/testing/rm09/dependency-proposal.md`. Tagged-source review found
that Better Auth's initial discovery/code exchange has no complete request
bound through its supported configuration; the proposed application requires
bounded owned provider I/O. No library has been installed yet and no build,
OIDC/browser result or live Entra proof is claimed.

Keep Next16.4.6, React/react-dom19.3.1, pg8.23.1, server-only0.0.1,
Radix Dialog1.3.1/Tabs1.2.1, TypeScript5.9.3, Node24.15.0/npm11.12.1
and the researched type/Playwright pins. Only the independent portal adopts
runtime dependencies; the portable packages remain framework-free. Pins,
resolved licenses, engine checks and lock integrity must be verified during
WORK-004. No offline_access or automatic token refresh is selected; access-token
expiry requires reauthentication. Keep provider state/nonce/PKCE, token/ID-token
validation and session cryptography in the maintained library. A small owned
HTTPS agent enforces transport deadline/body limits through its supported hook;
it adds no OAuth, authentication or transport framework.

# Concrete bounded stable auth composition option

Read-only follow-up, 2026-11-25; no code/install/native checks. This option supersedes the Better Auth proposal only if selected by the root during host task preparation.

## Supported exact choice

- `next-auth` **4.24.15**, ISC, current published stable release. Its tagged peer dependencies explicitly permit Next 16 and React 19; its Next route implementation awaits asynchronous params/cookies/headers. Retain Next 16.4.6, React/DOM 19.3.1 and the current Node/npm/TS pins.
- `@auth/pg-adapter` **1.11.3**, ISC, with `pg` 8.23.1. Official integration docs state the maintained @auth adapters are compatible with next-auth. Do not choose the stopped-maintenance @next-auth adapter namespace.
- Use database sessions in the separate owned auth schema. No refresh token required: request no offline_access, discard unexpected refresh tokens, and reauthenticate at the Management access-token expiry. Session validity/revocation stays in the maintained adapter; access-token lookup is limited to the authenticated user's fixed provider/account in that same auth store.

Sources: [stable package/tag](https://github.com/nextauthjs/next-auth/blob/next-auth%404.24.15/packages/next-auth/package.json), [publisher stable version](https://www.npmjs.com/package/next-auth?activeTab=versions), [Next route source](https://github.com/nextauthjs/next-auth/blob/next-auth%404.24.15/packages/next-auth/src/next/index.ts), [maintained adapter manifest](https://github.com/nextauthjs/next-auth/blob/@auth/pg-adapter@1.11.3/packages/adapter-pg/package.json), [compatibility statement](https://authjs.dev/getting-started/integrations). Auth.js v4 is in maintenance/security support, not feature development; this is a specific current capability gap in the new-project-preferred Better Auth, not a claim that v4 is the preferred general new-project choice.

## OIDC without Graph or unbounded discovery

Use the library's ordinary **generic OAuth/OIDC provider configuration**, not the Azure-AD provider's photo/profile callback. Pin exact tenant issuer, authorization URL, token URL and `jwks_endpoint`; omit `wellKnown` to avoid discovery entirely. Set `idToken: true`, `checks: ["pkce", "state", "nonce"]`, and fixed scope `openid profile` plus the configured Management delegated scope. Fixed token endpoint/JWKS derive only from the trusted tenant/authority configuration, never a browser or discovery override.

The stock callback calls openid-client's `client.callback` with all checks, then reads `tokens.claims()` for profile mapping. The application receives validated claims and maps exact issuer/tid/oid; it neither decodes a JWT nor calls Graph UserInfo. Use immutable tenant/oid account identity, no email authority/linking, and deterministic reserved placeholder email where the adapter requires one. Restrict the public route to the one sign-in/CSRF/callback/sign-out/session behavior actually needed; never return access/refresh/ID tokens from a session callback or response. Fixed server-only account lookup supplies the existing CIS client's per-request credential.

Sources: [generic options](https://github.com/nextauthjs/next-auth/blob/next-auth%404.24.15/packages/next-auth/src/providers/oauth.ts), [explicit metadata client](https://github.com/nextauthjs/next-auth/blob/next-auth%404.24.15/packages/next-auth/src/core/lib/oauth/client.ts), [validated callback/profile path](https://github.com/nextauthjs/next-auth/blob/next-auth%404.24.15/packages/next-auth/src/core/lib/oauth/callback.ts), [official idToken provider documentation](https://next-auth.js.org/configuration/providers/oauth).

## Finite request ownership through supported hooks

Set explicit `provider.httpOptions.timeout`, and supply **one owned native `https.Agent` through the documented `httpOptions.agent` option**. This normal HTTP policy adapter gives each actual ClientRequest a hard deadline from request admission through response-body completion; expiry destroys that request and its response/socket, and listeners/timers retire on terminal settlement. Keep keepAlive disabled for this small auth path and destroy the owner only after admitted requests drain. This is not a global fetch override, token parser or OAuth engine. The library continues to own all OAuth/JWT/cookie/session protocol behavior.

The hard-deadline agent is necessary: the pinned OpenID client v5 helper's default timeout is 3500ms and it explicitly destroys a timeout before headers, but its subsequent response-body loop has no independent deadline. `httpOptions.timeout` alone must not be advertised as a whole-body deadline. Its supported agent hook reaches the native HTTPS request that the helper owns, so a finite native request policy can destroy and settle that same request rather than abandon a Promise. Prove slow-header and slow/trickling-body termination in focused local provider tests before claiming success.

Sources: [supported HTTP options](https://github.com/panva/openid-client/blob/v5.7.1/docs/README.md), [native request/helper behavior](https://github.com/panva/openid-client/blob/v5.7.1/lib/helpers/request.js). Scope the agent to this one auth provider; no process-global fetch/HTTP monkeypatch or new general transport layer.

For normal Management reads, no external refresh runs during credential preparation. Use finite PostgreSQL connection/query/statement timeouts, check cancellation before/after auth store calls and join settlement; the existing CIS transport receives the actual request signal unchanged. Session callbacks never contain backend tokens. Key rotation, session-store schema/provisioning and replica secrets remain normal host configuration with local controlled proof explicitly separate from live Entra deployment.

## Better Auth alternative conclusion

Tagged GenericOAuthConfig accepts discoveryUrl/authorizationUrl/tokenUrl/userInfoUrl, but **no independent issuer/JWKS verifier options**. `requireIdTokenVerification: true` requires usable issuer/JWKS from discovery; explicit endpoint URLs alone do not establish it. No supported generic initial-discovery fetch timeout override was found. Therefore the stable Auth.js composition above is a concrete maintained option that preserves the required native boundaries without inventing discovery proxies, data URLs, abandoned work or a custom auth framework.
