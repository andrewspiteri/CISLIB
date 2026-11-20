---
title: "Application identity and security (RM-01)"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0016
outcome: "Import RM-01, reconcile existing identity, native security, REST and lifecycle contracts, and prepare bounded implementation work for three usable profiles, scoped authorization, credential renewal and consumer guidance. Implementation and new dependency decisions await explicit profile and finite allocation approval; live AKS and cert-manager proofs require separately available authorized infrastructure."
baseline_kind: git
baseline: "b53b4eaef6b3374e7387e9bbb065b57533248445"
graph_build_id: "sha256:bf7b91eedf53d5acb325e7a28a86adbe0cbd4db60e8b092665eeb97c0a5a3cd5"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:4b0f451d5e3a265eb954d04e5b1febb964a50dabd61c1c3880ebe6d2fb53ac1a"
authority: derived-outcome-authorization
---

# CIS-0016: Application identity and security (RM-01)

## Outcome

Import RM-01, reconcile existing identity, native security, REST and lifecycle contracts, and prepare bounded implementation work for three usable profiles, scoped authorization, credential renewal and consumer guidance. Implementation and new dependency decisions await explicit profile and finite allocation approval; live AKS and cert-manager proofs require separately available authorized infrastructure.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.
- The attachment is preserved as requirements at planning/security/sources. Its agent handoff grants no execution or dependency authority. Existing completed owners remain closed.
- Proposed P01–P16 and seven coherent outcomes include affected tests, documentation and review. The concrete optional dependency profile and one shared finite allocation await user approval.
- Preserve canonical environment identity, native authentication/authorization, complete scoped grants, one provider, sequential startup, existing REST/lifecycle ownership, central Metrics limits and the user's untracked launch settings.
- No new identity server, policy language, scheduler, HTTP stack, CA service, audit store, test harness, broad qualification campaign, external deployment, push, merge or release is included.

## Acceptance criteria

- Three real profile implementations use native facilities: explicit Entra workload/JWT, direct certificate mTLS and isolated LocalDevelopment certificates. Package installation is inert; roles and bindings are explicit.
- Trusted immutable snapshots and scoped caller provenance feed one complete-grant native evaluator. Per-operation boundaries reject spoofing, incomplete grants, wrong targets and unusable credentials before protected work.
- Existing REST safely binds profile/origins and at most two credential generations by default, preserving actual response drain, retry/deadline/cancellation ownership and native failure observations.
- Current CountryData service/caller, portable fixtures, profile/deployment guides, three optional-package consumer shapes, bounded instrumentation and checked-in security skill use actual implemented APIs and focused native evidence once authorized.
- AC-02 and AC-24 remain expressly unverified until suitable authorized AKS/cert-manager environments exist; local tests do not mark RM-01 complete.

## Prepared review artifacts

- [Preserved source and current-contract reconciliation](../../../planning/security/README.md)
- [P01–P16, seven outcomes, acceptance ownership and shared allocation](implementation-plan.md)
- [Optional packages and finite source-mapping candidates](dependency-profile.md)
- [Intake evidence and authority boundary](verification.md)

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-RM01-2026-11-19:P01-P16-APPROVED-ALLOCATION`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Implement CIS-0016 RM-01 P01-P16 locally executable three-profile security: trusted identity/caller and complete-grant native authorization, constrained REST bindings and bounded certificate renewal, real Entra/certificate/development adapters, CountryData consumer and current guides/portable fixtures/skill. Include focused native/public/package tests and review in seven coherent outcomes; preserve AC-02/AC-24 as unverified without authorized infrastructure and do not mark roadmap RM-01 complete.

- `certificate-inbound`: P05/P09/P11/P15: real native certificate integration validates explicit roots/issuer relationship, exact URI SAN, non-CA/usage/EKU, selected revocation and each-operation expiry. Separate actual listener bindings preserve probes without business bypass and development trust never authenticates production. Paths: `.artifacts/security`, `.cis/local/security-intake`, `.cis/local/testing`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0016`, `docs/modules/security/certificates.md`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/NuGet.config`, `implementations/csharp/src/CIS.Security.Certificates`, `implementations/csharp/tests`, `planning/security`.
- `certificate-renewal`: P07/P09-P13: PEM-only bounded source validates consistent material/local identity, uses existing Heartbeat30second coalesced checks, atomically adopts within two native generations, retains valid old owners and cuts off on stopping. Publish safe status/transition metrics and pinned cert-manager recipe without claiming AC-24 deployment. Explicit RequiredForReadiness defaults false; essential unavailability uses existing Healthcheck without changing liveness or receiver-only role validation. Paths: `.artifacts/security`, `.cis/local/security-intake`, `.cis/local/testing`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0016`, `docs/modules/security/cert-manager.md`, `docs/modules/security/credentials.md`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/NuGet.config`, `implementations/csharp/src/CIS.Security.Certificates`, `implementations/csharp/tests`, `planning/security`.
- `consumer`: P16: one compiling selectable CountryData protected service/caller shows harmless read, resource-scoped denial and restricted selected-country effect. Existing public testing helpers support genuine consumer needs. Current portable/profile guides, three optional package-consumer shapes, actual metrics exposition and checked-in security skill/routing agree with implemented APIs. Paths: `.artifacts/security`, `.cis/local/security-intake`, `.cis/local/testing`, `.github/skills/cislib-security`, `AGENTS.md`, `README.md`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0016`, `docs/modules/common-testing.md`, `docs/modules/security`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/NuGet.config`, `implementations/csharp/examples/CountryData`, `implementations/csharp/examples/CountryData.UnitTests`, `implementations/csharp/src/CIS.CommonTesting`, `implementations/csharp/tests`, `planning/security`.
- `core`: P01-P05/P11-P13: immutable policy and scoped trusted caller, complete-grant native endpoint/imperative parity, explicit facade order and role validation; portable fixtures, audit contract and three bounded instrument definitions preserve identity, native requirements, cancellation settlement and old hosts. Publish shared REST binding and typed security failure signatures before parallel provider handoff. Registration and constructors do no credential I/O or premature resolution; existing sequential Startup owns activation. Paths: `.artifacts/security`, `.cis/local/security-intake`, `.cis/local/testing`, `.github/skills/cislib-metrics/SKILL.md`, `contracts/metrics`, `contracts/security`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0016`, `docs/modules/application/core.md`, `docs/modules/security`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/NuGet.config`, `implementations/csharp/src/CIS.Core/Application`, `implementations/csharp/src/CIS.Core/Metrics`, `implementations/csharp/src/CIS.Core/RestClient/Contracts`, `implementations/csharp/src/CIS.Core/Security`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry`, `implementations/csharp/tests`, `planning/security`.
- `entra`: P08/P11/P15: real optional WorkloadIdentityCredential and native JWT bearer integration use exact v2 app-only mapping, roles, trusted scopes/origins and SDK caching; no fallback/cache/replay owner. Guide documents tenant/federation/API inputs and AC-02 deployment prerequisites. Paths: `.artifacts/security`, `.cis/local/security-intake`, `.cis/local/testing`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0016`, `docs/modules/security/entra.md`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/NuGet.config`, `implementations/csharp/src/CIS.Security.Entra`, `implementations/csharp/tests`, `planning/security`.
- `local-development`: P14: one small nonpackable C# tool creates per-workspace isolated CA/server/per-app PEM material with restricted ignored files, explicit non-secret config and deliberate renewal/reset. Reuse certificate adapter; enforce environment/origin/trust gates and no machine trust installation. Paths: `.artifacts/security`, `.cis/local/security-intake`, `.cis/local/testing`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0016`, `docs/modules/security/local-development.md`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/NuGet.config`, `implementations/csharp/tests`, `implementations/csharp/tools/CisSecurity.LocalDevelopment`, `planning/security`.
- `rest-binding`: P06-P07/P11: stable native transport owns immutable profile/origins and bounded current/draining generations, with atomic per-exchange leases, safe borrowing and backward-compatible typed security detail. Reject bound suppression/replacement/TRACE while preserving ordinary clients, deadlines, retry and cleanup. Paths: `.artifacts/security`, `.cis/local/security-intake`, `.cis/local/testing`, `contracts/rest-client`, `docs/architecture/csharp-dependency-policy.md`, `docs/catalog.yml`, `docs/changes/CIS-0016`, `docs/modules/rest-client`, `docs/modules/security/transport.md`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/Directory.Packages.props`, `implementations/csharp/NuGet.config`, `implementations/csharp/src/CIS.Core/RestClient`, `implementations/csharp/tests`, `planning/security`.

- All tasks, agents, already-performed intake/research, implementation, native commands, tests, formatting, docs, review and restoration share108000active seconds32logical attempts including3600restoration seconds. No counter reset or reopened historical owner. Codex may organize and redistribute within these totals.
- Andrew Spiteri explicitly approved CIS-0016 P01-P16, seven-task plan, optional dependencies and shared30active hours32logical attempts including3600restoration seconds on19November2026. External deployments, push, merge and release remain excluded.
- Keep focused ordinary native tests and actual optional package consumers; no broad benchmark/mutation/coverage campaign, unnecessary full-suite repeats, unrelated tooling/skill campaign or manual test catalogue.
- Local native test/setup credentials may be generated only as isolated restricted ignored material under.artifacts/security, with no machine trust installation, shared private keys or access to existing user credentials.
- No cloud provisioning, cluster writes, live credential use, external deployment, push, merge or release. AC-02 real AKS federation and AC-24 cert-manager deployment remain unverified until suitable infrastructure and operational authority are supplied; local native evidence cannot replace them.
- One central-file/native-output coordinator; after shared contracts stabilize parallelize disjoint Core/REST/provider work only. Preserve existing one CountryData tutorial and separate example unit project.
- Package activation is explicit. No new identity server, token issuer, CA service, policy engine, scheduler, HTTP stack, audit store/queue, configuration/DI framework or test harness. Initial credential source is PEM-only.
- Preserve physical storage group CISLIB-CIS-0001-STORAGE-2026-11-10:6442450944bytes including67108864protected across.artifacts,.cis/local,implementations/csharp. No additional pool/worktree or historical cleanup. Only newly owned regenerable module scratch may be cleaned.
- Preserve untracked CountryData/Properties/launchSettings.json SHA256ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784; baselineb53b4eaef6b3374e7387e9bbb065b57533248445.
- Retain existing static environment identity, immutable startup trust/grant snapshots, native authentication/authorization, per-operation credential expiry, exact origins, no automatic auth replay, sequential Startup, one provider and actual response/handler/credential cleanup.
- Use implementation-plan.md P01-P16 and dependency-profile.md: direct Azure.Identity1.21.0/JwtBearer10.0.12/Certificate10.0.12 and27exact source-mapping candidates. First approved restore must confirm actual closure/assets; new IDs, changed direct pins or material incompatibility require a specific decision. Preserve Core two-package closure, SDK10.0.401 and runtime10.0.12.

The total is 108,000 active seconds and 32 logical attempts, including 3,600 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
