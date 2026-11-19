---
title: RM-01 bounded implementation plan
type: delivery-plan
status: Approved
change_id: CIS-0016
---

# Approved delivery and authority

Implement the three RM-01 profiles against the preserved
[source and reconciliation](../../../planning/security/README.md). The current
request authorized intake and preparation. Andrew Spiteri subsequently explicitly
approved this concrete profile, dependency decision and shared allocation on
19 November 2026. CIS recorded the authorization and derived seven canonical tasks;
the finite distribution is recorded in work-allocation.json.
Earlier completed owners and allocations do not fund this module. No push, merge,
release, cloud provisioning, cluster changes or use of existing credentials is
included. External acceptance remains subject to available infrastructure and
specific operational authority.

## Approved profile P01–P16

1. **P01 — Identity and package boundary.** Portable CIS.Security contracts stay
   in CIS.Core with the existing approved runtime closure. Project the existing
   static EnvironmentOptions identity exactly, including defaults and bounds.
   Provider/native types stay in platform registration APIs and the optional
   CIS.Security.Entra / CIS.Security.Certificates assemblies. Profile activation
   explicitly selects inbound, outbound or both roles; validate only enabled roles.
   Registration/constructors perform no credential I/O, provider construction or
   premature service resolution; loading/warmup uses existing sequential Startup.
2. **P02 — Trusted immutable policy.** Capture the complete configuration under
   the existing reader, deep-copy collections and validate a startup snapshot.
   Trust, grants, capabilities and mappings require instance replacement; credential
   leaf reload cannot mutate them. Retain source limits: profiles16, capabilities256,
   grants4096, aliases64, opaque IDs1024 and capability names128 ASCII characters.
   Existing app/environment names retain their255-scalar rule and ordinal matching.
3. **P03 — Caller provenance.** Immutable bounded CallerContext distinguishes
   NotProvided, Reported and Verified instance evidence. Use a scoped read-only
   accessor with integration-owned establishment/restoration and private native
   authentication state. No consumer setter or AsyncLocal request authority.
   Serialized/constructed contexts cannot authenticate; background work requires
   an explicit independent host-workload scope, not a retained request context.
4. **P04 — One evaluator.** Native IAuthorizationService and one complete-grant
   handler serve endpoints and imperative checks. One entire grant must match each
   capability and every actual target dimension. Additional native requirements
   must also pass; no principal union, fragment recombination or partially allowed
   multi-capability execution. Check cancellation before/after native evaluation,
   pass it through the operation resource to CIS handlers and await settlement.
5. **P05 — Explicit host stages.** Add narrow authentication/projection and
   post-readiness authorization stages to the facade, with no-op behavior for old
   integrations. Order routing/trusted proxy setup, authentication/projection,
   existing operational integrations and health, readiness, then business
   authorization/execution. Direct native hosts expose the same integration.
   Separate mTLS/bearer/operational bindings use actual listener identity; Host
   headers cannot open a business bypass. Preserve health/metrics access policies.
6. **P06 — Security-bound REST.** Bind a named profile to exact nonempty normalized
   HTTPS origins. Reuse finalized-request checks and enforce the immutable policy
   at the native transport boundary, including absolute overrides and borrowing.
   Reject DisableAuthentication, per-request Authenticate replacement and TRACE
   on bound clients. Preserve all unbound-client behavior, signing bytes, primary
   dispatch, native settings, root meter factory, deadlines and retry ownership.
   Keep the two-minute connection lifetime; reject infinite lifetime only when bound.
7. **P07 — Generation ownership.** Keep RestHttpTransport as stable owner with at
   most one current and one draining handler generation by default. Atomically
   select/lease on every actual exchange, including a later retry; retain no naked
   generation across its delay. Reserve capacity before constructing another
   handler/key, coalesce newest source checks, publish atomically, and drain actual
   response/body owners before disposal. Never dispose active work or extend expiry.
8. **P08 — Entra.** Use reused WorkloadIdentityCredential explicitly, with trusted
   scopes/origins and existing final REST authentication callback. No CLI credential
   fallback, extra cache/scheduler or implicit replay. The initial reference selects
   v2 app-only tokens: exact tenant/issuer/audience, oid and azp mapping; reject scp
   and user tokens; require configured idtyp=app and document that optional claim
   in the API registration. Require mapped application roles for capability grants.
   Native JWT skew defaults30seconds and cannot exceed120seconds. SDK/native-handler
   caching and key discovery retain their own ownership.
9. **P09 — Certificate authentication.** Native certificate handler plus explicit
   custom client roots/issuer relationship, non-CA leaf, digital signature key usage,
   explicit client EKU, exact authorized URI SAN and expiry at each protected
   operation. System public roots do not confer application identity. No principal
   cache. Server trust/hostname/EKU remain native and separate from client issuers;
   expose no accept-any callback. Online revocation fails closed by default;
   explicit ShortLived enforces actual lifetime<=24h; Development is isolated.
10. **P10 — PEM source and renewal.** Initially support PEM chain/key only; omit
    optional PFX/password handling. Defaults: at most256KiB certificate-chain file,
    64KiB key file and eight chain certificates, validated before activation.
    Capture a consistent generation and reject mismatch/identity/usage/expiry errors.
    Existing Heartbeat owns coalesced periodic checks at30seconds. Keep only bounded
    current/candidate state; unchanged rejected/deferred candidates are reported
    once per material transition. A callback does not block indefinitely on retired
    response drains. Signal cutoff/cancellation on ApplicationStopping, then join
    source work, retained drains and owned credentials in existing shutdown order.
    Profile health distinguishes essential credential availability from optional
    integrations. RequiredForReadiness is explicit and defaults false; an enabled
    requirement reports unavailable/expired credentials through existing Healthcheck
    without changing liveness or treating unused outbound credentials as required.
11. **P11 — Failures.** Keep the source's stable portable categories and safe detail.
    Add typed security detail backward compatibly to existing REST failure/context
    shapes while preserving Authentication/client.authentication_failed. Preserve
    actual HTTP/TLS observations; arbitrary403 is not proof of capability denial,
    and handshake failure may have no HTTP status. Do not convert faults into denials
    or refresh/reload into replay. Native cancellation/deadline/stopping remain distinct.
12. **P12 — Audit and diagnostics.** Observe the final composite authorization
    decision once; record operation outcome separately. Offer an explicit awaited
    audit hook whose delivery failure remains observable to its owner and cannot
    become a successful receipt. No durable store/queue or implicit durability
    policy. Metric/log failures do not change authorization/business results.
    Bound repeated availability/recovery/reload diagnostics and exclude credential
    material, raw claims, parser inputs and unrestricted identity details.
13. **P13 — Metrics.** Add exactly the three source catalog entries for composite
    decisions, certificate reload transitions and remaining active leaf validity.
    Reuse native HTTP instruments. Conditional security selection preserves old
    catalogs; registered aliases plus unresolved bound labels. Default reservations
    are34/51/17 combinations (102 total), three streams, no histogram. Higher
    deliberately configured profile limits must fit unchanged global capacities;
    reject insufficient reservations. Scrapes only read owned state and preserve
    central application/environment labels, including overflow/native samples.
14. **P14 — Local development.** Reuse the certificate path with all environment,
    origin and trust gates. One small nonpackable C# setup/reset tool creates a
    per-workspace CA30days, per-app/server leaves7days bounded by CA expiry, restricted
    untracked files and non-secret example configuration. Use native crypto and
    OS ACL/Unix modes. No machine trust installation, shared private keys, CA service
    or accept-all development scheme. Reset requires running instances to adopt trust.
15. **P15 — Optional dependency decision.** Include the three direct pins, the27
    finite source-mapping candidates and cert-manager recipe pin in
    [dependency-profile.md](dependency-profile.md). Coordinate central packages,
    mappings, projects/solution and generated locks under one owner. First approved
    restore verifies actual assets; expose unexpected IDs/direct-pin incompatibility
    before expanding scope. Core-only/certificate-only/Entra package consumers prove
    separation. Do not enable a certificate cache because its package depends on caching.
16. **P16 — Consumer and evidence.** Keep one CountryData tutorial with explicitly
    selectable service/caller profiles, harmless read and restricted country-selection
    change with a visible denied-effect assertion. Keep existing provider/cache and
    default unselected behavior. Add portable v1 caller/grant/result fixtures and
    current guides, checked-in security skill and root routing against real APIs.
    Use existing public/CommonTesting facilities; add only helpers that the actual
    consumer needs. Native signed-JWT, TLS, listener, PEM and packaged-consumer tests
    retain their exact scope. Preserve AC-02/AC-24 as unverified without infrastructure;
    local delivery cannot mark roadmap RM-01 complete.

## Seven coherent implementation outcomes

Each proposed executable leaf is medium: one bounded owning boundary, native
integration rather than a new engine, one file owner, and focused tests/docs in
that same outcome. The whole module is the containing large outcome. Split the
source's certificate and development/consumer units at real ownership handoffs.

| Task | Outcome, owning files and included evidence | Depends on | Hours / attempts / restoration sec |
| --- | --- | --- | --- |
| WORK-001 | Shared Core contract, immutable policy/caller scope, complete-grant native bridge, explicit facade stages, common audit/metric catalog, portable fixtures and shared guide. Tests grant cross-products, multi-requirement parity, spoofing, context/background isolation, snapshots, cancellation settlement, receiver-only roles and actual facade order. | none | 5 / 5 / 600 |
| WORK-002 | Existing REST security binding, typed failure detail and constrained generation seam. Own REST internals and ownership guide; test final destinations/overrides, native generation leases, held bodies, retirement/retry races, drain/fault precedence and unchanged ordinary behavior. | WORK-001 | 5.5 / 5 / 600 |
| WORK-003 | Optional Entra adapter and native JWT scheme, app-only mapping, cancellation/status, AKS guide and provider tests. Validate real native signature/issuer/audience/role failures locally and SDK adapter plumbing without calling that federation proof. | Start after WORK-001; final outbound integration waits for WORK-002 | 3 5/6 / 4 / 300 |
| WORK-004 | Optional certificate receiving integration and trust validation, scheme/listener policies, revocation semantics and certificate guide. Test real TLS, issuer/SAN/usage, production/dev isolation, operation expiry on established connections and operational listener bypass resistance. | WORK-001 | 3 / 3 / 600 |
| WORK-005 | PEM source, identity-validated reload, outbound certificate binding, once-only initial native trust, Heartbeat/cutoff ownership, finite rotation/status metrics and cert-manager recipe. Test actual file adoption, bad replacement, two-slot pressure, expiry/selection/retry races and held shutdown settlement. | WORK-002, WORK-004, WORK-008 | 5 / 6 / 900 |
| WORK-006 | Isolated local setup/reset tool using actual certificate integration, file permissions, renewal/recreation and copy-and-run guide. Test clean per-workspace generation, per-app identity separation, gates, reset adoption and no machine-wide trust changes. | Source after WORK-004; final runtime proof waits for WORK-005 | 3 / 3 / 300 |
| WORK-007 | CountryData selectable protected service/caller and bounded selected-country business effect, public testing reuse, three package-consumer shapes, actual metric exposition, current API/provider handoff and repository security skill/routing. Tests permission denial prevents effect and packages activate only selected roles. | Source after WORK-003; final certificate/local proof waits for WORK-005/006 | 4 / 5 / 300 |
| WORK-008 | Supporting W01 native cleanup correction: after actual TLS drain and successful certificate disposal, remove only its recorded newly owned container through the native provider and verify exact absence. Retain the actual expired RSA TLS negative and meaningful retained-key/custody regressions. | WORK-002 | 2/3 / 1 / 0 |

The second delegated redistribution adds WORK-008 within the same shared totals:
30 ordinary minutes and one attempt move from WORK-004, and ten ordinary minutes
move from WORK-003. Every existing task's protected restoration reserve remains
unchanged. Completed WORK-001/002 contracts and costs remain historical. The
supporting task owns the cleanup correction discovered by the certificate suite;
it does not reopen WORK-002. WORK-005's existing-scope reconciliation adds the
approved REST native path before that draft task starts, so initial trust loading
can remain sequential Startup work without a mutable trust setter.

After WORK-001 stabilizes the common contracts, WORK-002, WORK-003 and WORK-004
may run in parallel with disjoint ownership. WORK-005 starts only after its REST
and certificate contracts settle. One owner edits the tutorial. The coordinator
alone edits shared catalogs/project/solution/package/lock files and serializes
native commands sharing outputs. Runtime startup itself remains sequential.

WORK-001 publishes the shared REST binding and typed-security-detail signatures
before that parallel handoff. WORK-003 can implement acquisition/native JWT
independently, but its final outbound integration/build and completion wait for
WORK-002's actual binding enforcement. Keep this completion gate explicit rather
than treating authored adapter source as passing transport evidence.

## Acceptance and validation ownership

| Source acceptance | Primary owner / concrete boundary |
| --- | --- |
| AC-01,05–07,09,19,21 | WORK-001: identity, scoped grants, spoofing, context and policy replacement; provider identity validation is also tested by003/004. |
| AC-02 | WORK-003 implementation and reproducible AKS guide; real deployment evidence unverified until authorized prerequisites exist. |
| AC-03,08,10,11,15,16 | WORK-004 real TLS/native certificate behavior; WORK-003 covers native JWT rejection/scheme isolation; no simulated provider pass. |
| AC-04 | WORK-006 plus actual CountryData start/call in007; no Azure/Kubernetes or machine trust required. |
| AC-12–14 | WORK-005 real PEM change/adoption with bounded native owners, supported by002 transport races. |
| AC-17,18 | WORK-002 binding/deadline/retry and native ownership;003/005 cover acquisition/reload effects. |
| AC-20 | WORK-001 facade stages and004/007 actual listeners/probes/roles with one provider. |
| AC-22 | Shared catalogs/contract in001; source transitions in005; actual exported labels, audit and safe failure evidence in007. |
| AC-23 | WORK-007 actual Core-only, certificate-only and Entra package consumers using current package bytes. |
| AC-24 | WORK-005 guide and local adoption implementation; actual cert-manager issuance/mount/renewal remains unverified without authorized infrastructure. |
| AC-25 | WORK-007 compiling single tutorial, three provider guides, portable fixtures and validated checked-in skill. |

Use existing MSTest/IDE, C# fixtures, CommonTesting and ordinary native gates.
Each change gets scoped whitespace format plus verify, affected build/tests and
meaningful public/package integration. Validate docs and the actual security
skill with installed repository tooling. Broaden only for a concrete regression
or unresolved platform risk. No new test harness, manual test catalogue, broad
benchmark/mutation/coverage campaign or replay of closed historical evidence.
No UI or public database-backed endpoint is introduced, so no new UI/cache system
is justified by this feature. Existing operational/public behavior is preserved.

## One shared approved allocation

**30 active hours (108,000 seconds), 32 logical attempts, including 3,600 seconds
for restoration.** The task table is one distribution of the shared grant.
All agents, already-performed intake, research, source, tests, documentation,
formatting, review, native commands and restoration consume these same totals.
Task organization and redistribution are delegated within them; costs never reset.

Retain physical group CISLIB-CIS-0001-STORAGE-2026-11-10: **6,442,450,944 bytes**
counted across .artifacts, .cis/local and implementations/csharp, with
**67,108,864 bytes** protected. The last pre-intake observation was5,929,212,633
bytes (446,129,447 beyond the reserve). Measure before native execution and keep
new package/consumer outputs sequential; only this module's regenerable owned
scratch may be removed as needed. No historical cleanup, second pool or worktree.

Preserve untracked CountryData/Properties/launchSettings.json SHA-256
ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784.
The proposed local credential tool and tests create their own access-restricted
ignored credentials under .artifacts/security; they do not read existing user
credentials or install trust. Deployment values remain placeholders in checked-in
guides. No external deployment is authorized by approving this local plan.

## Checkpoints and completion limits

Provide concise checkpoints when the Core native authorization/host stages work,
when real mTLS and bounded renewal work, and when the three-profile consumer and
package separation work. Report actual passing evidence and remaining gaps.
AC-02 and AC-24 have named reproducible prerequisites; their absence does not
block independent implementation but does prevent marking RM-01 complete.
Implementation tasks may complete only their expressly scoped local outcomes;
the verification record must retain these two roadmap acceptance gaps.

On19November2026 Codex exercised the explicitly delegated distribution authority:
WORK-002 now has19,800seconds (5.5hours), and WORK-004 has12,600seconds
(3.5hours). This moves30ordinary minutes to the approved W01 native-backend
implementation/proof. The original shared108,000seconds/32attempts, every
restoration reserve, storage ceiling and completed WORK-001 allocation remain
unchanged; all earlier source/native costs are retained. The applied current
work-allocation.json is authoritative for this distribution. W01 is the later
resolved CIS DEC-001 operational exception, not a rewrite of baseline authority.
