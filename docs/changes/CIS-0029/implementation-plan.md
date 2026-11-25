---
title: "RM09 approved desktop inspection implementation"
type: implementation-plan
status: Active
change_id: CIS-0029
---

# Current implementation scope

Deliver the approved R06 desktop inspection journey through real Management
APIs: filterable Applications, application Instances/Configuration/Capabilities/
Scheduled jobs, quick details panels and Job full details. Report application
versions per registration. Keep application/environment and selected report
identity across navigation. Run history and typed Task/Event details are
unavailable until the named P6/P7/P8 RM-10 contracts exist; never display
synthetic data in production.

This scope follows Andrew Spiteri's explicit R02–R06 decisions and standing
delegation to proceed with task work, pausing only for UI screenshot approval.
R06 was approved at 17:04:52 UTC on 25 November 2026; exact digests and decision
are in [design.md](design.md). The earlier broad plan remains in Git history.
The unchanged original specification and twenty-scenario adapter remain future
requirements. This slice does not complete the full original RM09 specification.

Metadata mutations/recovery, Configuration publication/assignment/adoption,
mobile, scheduler execution, durable run history and Task/Event services are
deferred. No new DI/auth/test framework, Configuration authority, tooling
campaign, existing database access, deployment, push, merge or release is included.

# Shared bounds and tasks

Native outcome authorization is
`cislib:CIS-0029:outcome:dc66ea8b85f04962a869fde8ce1e9939`.
The derived plan and five concrete contracts validate without errors/warnings.
Finite native allocation reuses the exact existing physical ceiling reference
`CISLIB-CIS-0001-STORAGE-2026-11-10`; it creates no second physical pool.

One controller-selected allocation under the standing delegation retains
48 active hours and 48 logical attempts, including one hour/attempt for
restoration. The design distribution is now 20 hours to include all six reviews and normal legacy-direction reconciliation; this redistributes within the same total. All agents, historical intake, renders, failed operations, source
work, tests, docs and review share it. These numbers are not represented as a
separate numerical approval by Andrew. The physical 6 GiB ceiling and 64 MiB
reserve remain shared with historical consumer data; the existing 2 GiB Docker
allowance stays inside that ceiling. No cleanup is added. Preserve every cost
receipt and acknowledge historical unmeasured effort; conservative estimates
continue counting against admissions rather than being reset by native adoption.

| Owner | Independently owned outcome and dependencies | Shared distribution |
| --- | --- | --- |
| WORK-001 | Exact desktop backoffice design, requirement reconciliation and preserved approvals; includes all prior intake/design work | 20 h / 10 attempts |
| WORK-002 | Native independently validated human authority through current Security extensions; after WORK-001 | 6 h / 8 attempts |
| WORK-003 | Bounded authorized application/environment and app-specific definition discovery, contracts and clients; after WORK-001 | 6 h / 8 attempts |
| WORK-004 | Independent Next host and maintained OIDC/session boundary; after WORK-001 | 7 h / 10 attempts |
| WORK-005 | Approved backoffice inspection UI and connected native/browser proof; after WORK-002, WORK-003, WORK-004 | 8 h / 11 attempts |
| Reserve | Actually required restoration only | 1 h / 1 attempt |

Each implementation leaf is medium or smaller and includes meaningful tests,
documentation, formatting and independent review. Parallelize WORK-002/003/004
after their common gate with disjoint file scopes; serialize shared native
builds. Host startup remains sequential. The portal uses portable client server
transport, per-request delegated credentials and cancellation. Portable entry
points gain no Next/React/Node dependencies.

# Observable acceptance and targeted verification

**WORK-001:** Preserve the original source, R01–R06 history and costs. Reconcile
the historical no-UI baseline only for the explicitly approved desktop
backoffice surface through supported CIS operations. Pin the exact approval and
current narrow scope; retain P6/P7/P8 and deferred original scenarios. Verify
native design/plan validation and protected user launch settings. It owns
requirements/design, not a new implementation coordination framework.

**WORK-002:** A real human/delegated native JWT validation path checks the fixed
issuer, tenant, Management audience, allowed portal client and required delegated
scope before trusted resource mapping. Distinguish human and calling application
attribution. Preserve strict existing workload Entra behavior and native
resource authorization. Extend existing signed-token/HTTPS tests for wrong
issuer/audience/client/scope, forged identity and denied resources. Controlled
issuer proof is not live Entra deployment evidence. Document configuration at
the reference application boundary; no browser headers create authority.

**WORK-003:** Server-side bounded display-name/identity search and exact
environment filters precede paging. Enumerate environments only from authorized
applications. Exact application/environment Configuration queries select
application definitions before source authorization; common sources are not
inferred members. Bind cursors to canonical filters and current actor/profile/
policy without changing original-key recovery actor scope. Preserve omitted
filters and public backend compatibility through optional query interfaces.
Update canonical OpenAPI/schema and both clients together. Reuse current unit,
client, native and PostgreSQL fixtures for beyond-page matches, hidden records,
malformed filters, cursor/policy mismatch and old callers. No fleet scan in Next.

**WORK-004:** Build an independent Next application using exact supported pins,
licensed components and maintained OIDC authorization-code/PKCE integration.
Tokens stay server-side; secure HttpOnly cookies, state/nonce/PKCE validation,
restricted callback/return paths, same-origin protections, expiry and sign-out
are real boundaries. Credentials are request-scoped with no cross-user cache.
Verify protected endpoints, session isolation/expiry, cancellation and endpoint
configuration. Public health has no repository/database I/O. Public responses
traverse an explicit cache policy; authentication responses use no-store.
Authentication routes delegate only to the maintained library's owned session
adapter, with no direct Management repository or fleet database access.
Keep native/bootstrap ownership unchanged and
document secrets/replica configuration without recording credentials. A maintained
library's ordinary session adapter may use a separate owned PostgreSQL schema;
this adds no generic auth framework or access to an existing database.

**WORK-005:** Implement the exact approved desktop list/tab/panel/full-details
interactions with typed projections and bounded paging. Show reported versions,
presence, timestamps, selected registration/report and scoped IDs accurately.
Configuration lists expose definition metadata only; capabilities are actual
APIs/advisory hints, not authority. Jobs show whole reported inventories, resource
mode/state and optional local revision/enabled. Missing facts say Not reported;
missing services say unavailable, not empty or successful. Do not infer cron,
current-run chronology or richer configuration bindings. Preserve unknown enum
text. Abort superseded reads, reject stale target responses, clear sensitive
state on denied/expired sessions, and retain keyboard/focus/Escape behavior.
Verify meaningful UI/session cases and affected clients; reuse existing native
service/isolated PostgreSQL for connected proof, with no replacement harness.
Review actual desktop screenshots against R06; material new journeys alone need
another screenshot decision. Record passing checks and external proof limits in
verification.md. This integration belongs to the UI outcome, not a separate
generic test/docs campaign.

Native builds/tests/installations begin only after normal outcome/task
eligibility and finite owner allocations are established. Recheck physical
storage before dependency installation/builds; count npm cache, node_modules,
.next, test outputs and browser artifacts. Prefer installed browser binaries
and one workspace install. Progress records preserve actual costs and bounded
next actions before broader checks.
