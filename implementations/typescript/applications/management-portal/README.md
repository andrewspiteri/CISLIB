# Management portal host

This independent Next application supplies desktop inspection and explicitly
authorized operator workflows. It reuses `@cislib/management-client/server` and the Management service's
native resource authorization. The portable SDKs acquire no Next, React, Node
session or database dependency.

Applications, instances, definition metadata
and complete reported Jobs inventories use delivered APIs. Names and reported
versions lead; exact identifiers remain under Technical details with Copy.
Capabilities separates application feature groups from secondary Service API
details. The trusted logical catalog is unavailable in this portal; reported
Jobs and HTTP descriptors do not become executable feature definitions.
See the [inspection guide](../../../../docs/modules/management/portal-inspection.md).
Run history and typed Task/Event services remain RM-10
prerequisites; production never substitutes demonstration records for them.

Display [metadata editing](../../../../docs/modules/management/portal-metadata.md),
exact [Configuration authoring/publication and separate releases](../../../../docs/modules/management/portal-configuration.md),
reviewed [whole-map assignment](../../../../docs/modules/management/portal-configuration-assignment.md)
and bounded [adoption observations](../../../../docs/modules/management/portal-configuration-adoption.md)
use the existing native service. Each guide separates implemented behavior,
actual focused proof and pending or unreliable platform observations. Publishing,
creating a release, assigning it and a consumer reporting acceptance are separate actions.

## Build and run

Use Node **24.16.0**, npm **11.12.1** and the checked-in workspace lock. From
`implementations/typescript`:

```text
npm ci
npm run build
npm run build:portal
npm run migrate --workspace @cislib/management-portal
npm run start --workspace @cislib/management-portal
```

The portal build prepares one digest-checked response-close correction for the
pinned Next16.4.6 dependency. Startup verifies it before loading Next and does
not write deployment files. This lets an aborted response settle its actual
transport wait while producer work remains owned. See the
[correction and real HTTP regression](../../../../docs/changes/CIS-0030/next-response-close-correction.md).
Review the correction when changing the framework version.

The current Configuration workflow is documented in
[Configuration versions](../../../../docs/modules/management/portal-configuration-versions.md).
Its [automated testing guide and combination matrix](../../../../docs/changes/CIS-0030/configuration-testing.md)
give the focused ordinary Node command and named Playwright/native MSTest cases,
including independent SQL, Operations, Audit and cleanup assertions.

Migration is an explicit deployment action against a newly selected, separately
owned session database/schema. The host never creates or repairs tables during
ordinary startup. Applying the same version-one schema again verifies its shape;
an occupied unrelated schema is refused. Use a schema owner for installation
and a separate runtime role with only schema usage and required table DML.
Management tables and application databases are outside this host's ownership.

`server.mts` is the ordinary documented [Next custom-server entry](https://nextjs.org/docs/app/guides/custom-server).
The extra entry is necessary to join the actual PostgreSQL pool and native OIDC
HTTP owner before process completion. It prepares the session store and Next
sequentially, then starts admission. SIGTERM/SIGINT stop admission, abort owned
Management requests/OIDC transport, close and drain the HTTP server and handlers,
close Next, and await the pool/native HTTP owner. It sets an exit code on error;
it does not call `process.exit` before asynchronous cleanup. There is no second
DI container, scheduler, standalone Next output or lifecycle framework.

## Explicit deployment inputs

Secrets are provisioned outside source control. No default account, bearer,
client secret, session secret or database is selected.

| Variable | Meaning |
| --- | --- |
| `NEXTAUTH_URL` | Fixed external HTTPS origin, such as `https://management-portal.example/`; no base path. |
| `NEXTAUTH_SECRET` | Newly provisioned random base64url secret, at least 32 random bytes; shared by replicas. |
| `CIS_PORTAL_CLIENT_ID` / `CIS_PORTAL_CLIENT_SECRET` | The confidential portal OIDC client and its deployment-owned secret. |
| `CIS_PORTAL_TENANT_ID` | One exact tenant UUID. Multitenant/common issuers are not selected. |
| `CIS_PORTAL_MANAGEMENT_AUDIENCE` | Management API's Entra application UUID, matching its native token audience. |
| `CIS_PORTAL_OPERATOR_SCOPE` | Delegated API scope suffix, default `management.operator`. |
| `CIS_PORTAL_ALLOWED_SUBJECTS` | Comma-separated exact tenant object UUIDs, at most 256; not email addresses or display names. |
| `CIS_PORTAL_MANAGEMENT_URL` | Independently trusted Management HTTPS base prefix, including any service prefix. |
| `CIS_PORTAL_AUTHORITY_ORIGIN` | Trusted HTTPS authority origin; default `https://login.microsoftonline.com`. |
| `CIS_PORTAL_SESSION_DATABASE` | Explicit PostgreSQL URI with user, password, host and database for the owned session store. An omitted port becomes 5432. Only one `sslmode` query parameter is supported: remote hosts require `verify-full`, while loopback can omit it or use `disable`. Ambient `PG*` values cannot select the connection identity, endpoint, TLS mode, schema or budgets. |
| `CIS_PORTAL_SESSION_SCHEMA` | Dedicated `cis_portal` or `cis_portal_<suffix>` schema. |
| `CIS_PORTAL_MANAGEMENT_CA_FILE` | Optional absolute public CA PEM path for the Management transport. Native certificate validation stays enabled. |
| `CIS_PORTAL_BIND_ADDRESS` / `CIS_PORTAL_PORT` | Literal listener IP and port; defaults `127.0.0.1:3000`. |
| `CIS_PORTAL_SERVER_CERTIFICATE_FILE` / `CIS_PORTAL_SERVER_KEY_FILE` | Optional pair of owned absolute HTTPS PEM files. Otherwise a trusted proxy terminates external HTTPS before the restricted listener. |

This reference requests `openid profile` and
`api://<Management API application UUID>/management.operator`. Register that
identifier URI/scope and the exact callback
`<NEXTAUTH_URL>/api/auth/callback/cis-management`. The native Management
`entra-management` profile separately validates its API audience, tenant,
issuer, portal client `azp`, required short `scp` and exact issuer/object-ID
mapping. Grant the actual Management application/environment and resolved
application/source resources. Select only the reads each operator needs. Authoring
additionally requires the applicable native write and original-result lookup
grant bundle at the actual source or binding; action hints grant no authority.
The delegated scope is authentication context, not a substitute for native
resource permission; it does not inherently prohibit writes in other clients.

The portal pins explicit issuer, authorization/token URLs and JWKS endpoint to
the trusted authority and tenant. The maintained [Auth.js OIDC provider](https://next-auth.js.org/configuration/providers/oauth)
and OpenID client verify signatures, issuer, client audience, state, nonce and
PKCE before profile mapping. No application JWT decoding, Graph UserInfo call,
unbounded discovery, browser-selected provider, copied identity header or
workload fallback establishes an operator. This choice uses stable Auth.js
4.24.15, which is in maintenance/security support. The ordinary
[PostgreSQL adapter](https://authjs.dev/getting-started/adapters/pg) is retained.

## Session and HTTP boundaries

The host uses `Referrer-Policy: same-origin`, so ordinary same-origin sign-in and
sign-out forms retain their Origin header while cross-origin navigation omits
the referrer. Exact Origin validation and the maintained CSRF check remain
required. One runtime Content Security Policy permits form destinations only at
the portal and its independently configured concrete HTTPS OIDC authority (a DNS
hostname or `127.0.0.1`, as supported by CSP); a
build-time policy cannot accidentally block that configured redirect.

The direct host disables Next response compression. The local native browser
proof exposed retained request promises with compression enabled; the same
journey and every actual shutdown join completed with it disabled. An existing
deployment proxy can own compression independently. Request handlers still
settle before Next and session infrastructure are retired.

Only the opaque secure HttpOnly, host-scoped session ticket reaches the browser.
Database sessions are checked for every protected read and can be revoked by
deleting the selected session. A callback-local adapter wrapper caps the stored
session and cookie to the validated access-token expiry (less a 30-second safety
margin) and at most one hour. Session reads do not extend that expiry. No
`offline_access` is requested; unexpected refresh/ID tokens are discarded and
the schema rejects their persistence. Expiry requires ordinary reauthentication.

The isolated local review fixture issues its synthetic operator tokens only
within the explicitly bounded review lease, up to one hour. A later sign-in
receives the remaining lease rather than a fresh hour; the same 30-second
credential safety margin applies. Ordinary automated fixture tokens retain their
ten-minute lifetime. The host closes at the stated review deadline regardless of
browser activity. These fixture lifetimes do not change production expiry,
revocation, provider validation or refresh-token policy.

An already valid local session must sign out before signing in again, preventing
the library's normal account-linking flow. Expired sessions are removed on read,
so this rule does not block reauthentication at token expiry. Subsequent
authenticated callbacks update only the same fixed provider/object-ID account's
credential. A reserved `.invalid` email serves only the adapter's internal key;
it is neither a verified mailbox nor an authorization input.

The account bearer is a **server-side session-store secret**. The standard
adapter does not encrypt individual token columns. Deploy the separately owned
database, backups and volumes with encryption and restricted DML credentials;
do not expose that database to browser clients. Production encryption, identity
deployment and replica operations require deployment-owner evidence and are not
established by local tests.

Auth routes admit only fixed-provider sign-in/callback, CSRF, sign-out and the
library's generic sign-in/error pages. Browser token, account, linking,
registration and administration routes are absent. Mutating auth requests need
the fixed origin plus the library's CSRF protection. Duplicate/unknown request
fields, alternate providers/scopes and arbitrary redirects are rejected.
Authentication and protected read responses are private/no-store. Public
`/healthz` is a five-second cacheable constant with no database or repository
I/O; it is liveness, not current Management readiness.

The host admits at most 64 handlers, with at most eight protected Management
calls, including admitted commands and original-result lookups. The session pool has four connections, a three-second connection limit
and five-second query/server-statement limits. OIDC has eight sockets, an
eight-second complete request deadline and one-MiB response ceiling. The ninth
OIDC request is rejected before native admission; closed request/response/socket
ownership remains counted until actual settlement, so there is no internal
request queue to outlive shutdown. This scoped native agent uses the library's
supported HTTP policy hook and the pinned Node 24 `Agent.addRequest` runtime
method (described by a local declaration augmentation). It changes no
process-global fetch/HTTP implementation. Rerun the native header/body/limit/
saturation/stop proof when upgrading Node or the OIDC library. Slow bodies cannot keep work alive by
trickling bytes. Management reads retain the SDK's one 30-second entry deadline,
actual request/stop signal and awaited native cleanup. Database credential
preparation checks cancellation before/after the awaited library operation;
there is no abandoned `Promise.race` task. A removed database client can retire
after a library query error, and remains retained by its host owner until its
actual native end rather than being mistaken for a completed transport.

Session-store ownership uses the maintained pool's supported `Client` constructor
option to track every native client's public `end` event, including failed
connections. Both host shutdown and explicit migration cleanup await those ends
after `pool.end()`, whose empty client list alone can precede socket settlement.
It also asks remaining tracked clients to end through their public API, so a
removed failed connection remains owned and receives an explicit stop request.
The connected fixture holds a real client's ordinary shutdown call to prove
that disposal remains pending until its actual native connection closes, and
also exercises rejected PostgreSQL authentication.

Expired session retention belongs to the owned database's operating procedure.
Use finite batches, for example the statement below, under the dedicated schema
search path. Repeat only within the deployment's selected maintenance allowance:

```sql
DELETE FROM sessions
WHERE id IN (SELECT id FROM sessions WHERE expires <= now() ORDER BY expires LIMIT 128);
```

This host adds no background scheduler or generic quota/retention framework.
The deployment owner selects database capacity and maintenance admission before
production use.

## Protected read API

`src/portal-model.ts` and the dedicated Configuration projections are the
browser-safe boundaries. Replies are
`{state:"ready", value:...}` or an explicit unauthenticated, denied, not-found,
invalid or unavailable state. They contain no credentials, raw SDK result,
transport headers or arbitrary inventory module details. Inspection omits
default/payload JSON. The dedicated Configuration routes can return exact selected
source content and declared defaults only under their independent native grants.
Current application authorization precedes service-wide capability descriptors.
Hints remain advisory.

| GET route under `/api/portal` | Bound |
| --- | --- |
| `/applications?query=&environment=&cursor=` | 25 native-filtered applications; omitted empty filters. |
| `/environments?cursor=` | 32 authorized environment names. |
| `/applications/{id}` | One actual authorized application. |
| `/applications/{id}/instances?cursor=` | 25 registrations, with each reported version preserved. |
| `/applications/{id}/configuration?cursor=` | 25 exact application definitions, metadata only. |
| `/applications/{id}/capabilities` | Explicit unavailable logical feature groups, with separate Service API diagnostics and advisory hints. |
| `/applications/{id}/inventory?cursor=` | One complete bounded registration inventory, never truncated jobs. |

Inventory provides a registration UUID and application version, not an instance
label. The UI may use a label only from an already returned matching instance;
otherwise it displays Not reported with the exact UUID in Technical details. It never scans all instance pages
or infers report-time history, recurrence, worker authority or configuration
bindings.

## Desktop inspection and data meaning

On **Applications**, filter by a literal, case-sensitive substring of application
identity or display name and an exact authorized environment. Environment choices
and applications are bounded pages; neither is a fleet total. Applying filters
restarts application paging. Opening an application retains that filter context.

| Application tab | Meaning |
| --- | --- |
| Instances | Each registration's reported app version, presence and timestamps. Different registrations may report different versions; there is no application-wide current version. |
| Configuration | Exact application/environment definition metadata, with no defaults, values or publication content. |
| Capabilities | Actually implemented Management APIs and separate advisory permission hints. A hint grants no authority. |
| Scheduled jobs | One complete registration inventory per page, with scoped job/resource identities, supported modes, actual reported mode/state and report provenance. Supporting Scheduled mode does not establish a trigger or recurrence. |

Click an item for its read-only side panel. Close or Escape returns focus to the
originating item when it still exists, otherwise to the application heading.
The Job panel's **View full details** pins the registration, report sequence,
scoped job and resource/mode. Its Overview retains that report's version and
timestamps; Configuration shows only reported local revision/enablement.

Run history and typed Scheduled Task/Event records remain unavailable pending
RM-10. Task configuration retains **Trigger** and **Job** tabs with a separate
**View run history** action. These views explain missing contracts; their links
do not invent a task/event association or captured historical configuration.
Missing facts say **Not reported**.

**Refresh** rereads the current pages; an existing same-page result can remain
visible with **Refreshing…** while that read runs. Superseded reads are aborted
and cannot replace a newer target/tab. If a pinned report disappears, return to
Scheduled jobs and select a current item. Unavailable/denied reads show their
actual state. When a read reports session expiry, the page clears protected
content and paging and presents **Sign in**. Presence and receipt timestamps remain observations, not readiness,
current ownership or run-history evidence.

## Verification

`npm run test:portal` executes focused native `node:test` profile/input and
projection checks. They do not prove OIDC signatures, PostgreSQL or browser
behavior. The [existing C# portal fixtures](../../../csharp/tests/CIS.Core.IntegrationTests/Management/ManagementPortalHostTests.cs)
own the selected native processes and isolated resources:

- `ManagementPortalTransportTests.AuthHttpDeadlinesBodyBoundsSaturationAndStopJoinActualNativeSockets`
  launches `tests/native-oidc-http-consumer.mjs` with newly owned TLS files for
  slow headers/bodies, response limits, saturation and actual owner/socket drain.
- `ManagementPortalHostTests.ActualOidcCallbacksIsolateRevocableDatabaseSessionsAndNativeTargetGrants`
  launches `tests/native-host-consumer.mjs` against a controlled signed OIDC
  issuer, real isolated PostgreSQL sessions and the native Management fixture.
- `ManagementPortalHostTests.ActualDesktopInspectionPreservesReportContextAndKeyboardSessionBoundaries`
  uses that same consumer with browser checks enabled. It imports
  `tests/native-browser-checks.mjs` to use installed Edge against the production
  host. Real session/native API journeys and controlled browser race/paging
  responses are recorded separately.

These source relationships are not passing results. Actual executions, counts
and evidence limits belong to [CIS-0029 verification](../../../../docs/changes/CIS-0029/verification.md).
No live Entra, production session-store deployment, external encryption or release
qualification is established by authored source or controlled local fixtures.

### Interactive local fixture

For manual desktop testing, the existing host-only C# fixture also accepts an
explicit opt-in session. It starts fresh isolated PostgreSQL/RabbitMQ services,
the real Management service and ordinary production portal, plus a test-only
signed OIDC issuer. There is no production demo or authentication bypass.
Build the integration project and portal first, and run under an active bounded
native-work owner. Keep the fixture process alive while testing.

Set `CIS_PORTAL_MANUAL_CONTROL_DIRECTORY` to a new ordinary directory below
`.artifacts/management-portal/CIS-0029/WORK-006`,
`CIS_PORTAL_MANUAL_LEASE_SECONDS` to 30–3600, and
`CIS_PORTAL_MANUAL_OPEN_BROWSER` to `true`. Select only the host-only test above;
use a runsettings test-session timeout longer than the lease plus startup and
cleanup. Ordinary invocations without these variables still run their original
automated assertions and shut down normally.

The fixture opens a new isolated Edge window. Its context accepts the owned
local test certificates; native backend TLS validation stays enabled and no
trust-store entry is installed. Read `status.json` in the control directory for
the generated portal URL and expiry. A separate ordinary browser may show a
local certificate warning.

Choose **Operator A — Country Services and Billing Worker editor** on **Local
test sign-in** to inspect/edit both samples, or **Operator B — Billing Worker
reader** for read-only Billing access. No password is required for these isolated
synthetic identities. Their actual native identities remain `managed-a` and
`managed-b`. See the [manual review data guide](../../../../docs/modules/management/portal-manual-review-data.md)
for the regional instance versions, closed/unreported registrations, Shared and
Application JSON examples, and reported Refresh country cache job. These are
native snapshots; they naturally become stale without further reports. The
fixture supplies no Scheduler execution/history or typed Task/Event records.

The manual host lease is 30–3600 seconds. Synthetic access tokens are minted at
each sign-in for the remaining captured lease, with the ordinary 30-second
session safety margin; signing in again does not restart the lease. Ordinary
automated tokens still last ten minutes. Production sessions remain capped at
the earlier one-hour limit or actual access-token expiry minus that margin;
activity does not extend them and no refresh token is retained. Sign out to
switch operators, or reauthenticate after expiry. Publishing and assignment
still require explicit reviewed commands; editing never enables autosave.

A sufficiently long manual lease also schedules one actual protected read after
130 seconds using a separate, hidden read-only identity. `status.json` records
pending/passed/failed/cancelled continuity independently of live browser and
cleanup state. A short lease or cancelled probe does not establish continuity.
Current live continuity and cleanup remain pending until their real results are
recorded; source assertions and pure/build checks alone do not establish them.

The manual fixture also seeds retained baseline/candidate/Finance releases, a
complete assignment map with trusted cohort pins, and two historical consumer
adoption reports. Operator A can author Configuration, publish/release, review
assignment changes and inspect adoption; Operator B remains read-only. Historical
Applied/Faulted reports are intentionally separate from the current desired map
and do not claim fresh consumer convergence. These remain isolated test records
served through the real native APIs, not production fallback data or Scheduler
execution/history.

Create `stop.request` in the control directory to request normal cleanup, or let
the one-hour maximum lease expire. `status.json` reports Node host/browser/issuer
drainage; `fixture-cleanup.json` reports C# fixture cleanup separately. Wait for
the parent test runner to exit successfully before treating assembly-owned
containers as disposed. Generated credentials and keys stay in owned private
fixture directories and are removed after actual native work settles.

`node tests/manual-browser-smoke.mjs <control-directory>` verifies the actual
chooser, sign-in/sign-out/reauthentication forms, native target isolation and
seeded data, then requests stop and writes a safe report. It consumes the same
existing fixture and finite work owner; it does not provision another service.

Retain [third-party notices](THIRD_PARTY_NOTICES.md) with deployment artifacts.
