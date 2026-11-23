---
title: "RM-07 v1.1 authoritative bootstrap and managed admission"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0025
outcome: "Close updated RM07 v1.1 gaps with authoritative control and provider capabilities, mandatory managed bootstrap and freshness/reconciliation, bounded new-work admission at existing Jobs/Messaging/HTTP boundaries, independent readiness/liveness, PostgreSQL standalone/shared profiles and focused native/public/portable evidence while preserving original metadata-result-Audit atomicity."
baseline_kind: git
baseline: "38031b41b4d8ce4f7e991b71038abdd60689532c"
graph_build_id: "sha256:b2c41ab765c2b1d03e349095198d917dc459d29eb045fe267c7f6a76612325f2"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:6c417899b348b63faf5f02b992af3edabec02a7533d13585bec36edad6179b61"
authority: derived-outcome-authorization
---

# CIS-0025: RM-07 v1.1 authoritative bootstrap and managed admission

## Outcome

Close updated RM07 v1.1 gaps with authoritative control and provider capabilities, mandatory managed bootstrap and freshness/reconciliation, bounded new-work admission at existing Jobs/Messaging/HTTP boundaries, independent readiness/liveness, PostgreSQL standalone/shared profiles and focused native/public/portable evidence while preserving original metadata-result-Audit atomicity.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Deliver the authorized outcome and its task-specific acceptance criteria within the shared constraints and limits.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-RM07-V11-2026-11-23-DELEGATED-PRESERVED-CONTRACT-HEALTH-SUPPORT`, recorded for Codex under Andrew Spiteri delegation. Tasks reference this shared context; their organization is not a separate human review.

Implement updated RM07v1.1 control/backend/managed admission requirements preserving existing durability and public behavior outside selected managed execution.

- `backend`: Actual typed PostgreSQL backend, stable authority/recovery and standalone/shared profiles, migrations and native provider tests Paths: `docs/modules/management`, `implementations/csharp/applications/CIS.Management.Service`, `implementations/csharp/tests`.
- `contracts`: Portable authoritative control and substitutable admission contracts with both clients, CommonTesting, fixtures and documentation Paths: `contracts/management`, `docs/modules/management`, `implementations/csharp/src/CIS.CommonTesting/Management`, `implementations/csharp/src/CIS.Core/Management`, `implementations/csharp/tests`, `implementations/typescript`, `planning/management`.
- `delivery`: Integrated focused evidence, source-exact public clients, provider deployment profiles and current skill/guidance Paths: `.github/skills/cislib-management`, `AGENTS.md`, `README.md`, `contracts/management`, `docs/modules/management`, `implementations/csharp`, `implementations/typescript`, `planning/management`.
- `health-reader`: Necessary existing Health report reader readiness safety invariant with focused actual-snapshot reader tests and current documentation; preserves default output and HTTP mapping. Paths: `contracts/healthcheck`, `docs/modules/healthcheck`, `implementations/csharp/src/CIS.Core/Healthcheck/Reporting/HealthReportProtocol.cs`, `implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck`.
- `ingress`: Messaging application dispatch, explicit effect HTTP admission and readiness-only composition, tests and docs Paths: `docs/modules/management`, `docs/modules/messaging`, `implementations/csharp/src/CIS.Core/Healthcheck`, `implementations/csharp/src/CIS.Core/Management`, `implementations/csharp/src/CIS.Core/Messaging`, `implementations/csharp/tests`.
- `jobs`: Existing finite Jobs admission and wakeup boundaries, tests and docs; preserve native leases and admitted work Paths: `contracts/coordination`, `docs/modules/coordination`, `implementations/csharp/src/CIS.Core/Jobs`, `implementations/csharp/tests`.
- `managed`: Mandatory managed bootstrap, one-owner control/reporting, freshness and reconciliation with focused tests and guides Paths: `docs/modules/management`, `implementations/csharp/src/CIS.CommonTesting/Management`, `implementations/csharp/src/CIS.Core/Application`, `implementations/csharp/src/CIS.Core/Management`, `implementations/csharp/tests`.
- `records`: CIS correction requirements, bounded plan, honest resource accounting and owned native receipts Paths: `.artifacts/management/CIS-0025`, `.cis/local/management-v11`, `docs/catalog.yml`, `docs/changes/CIS-0025`.

- 48 shared active hours,56 logical attempts,7200 restoration seconds; all agents/intake/source/tests/docs/review/restoration share these totals. Physical6GiB with64MiB reserve unchanged;2GiB Docker allowance inside ceiling, max4 task containers including Ryuk;150994944 external cache+8388608 owned TLS backing counted.
- Close new Jobs acquisition/occurrence/finite iteration, application-message claim/handler and explicit effect HTTP boundaries. Preserve admitted finite work, valid renewals/fencing, native RPC replies, original-result recovery and existing outbox/transaction/Audit obligations.
- Complete PostgreSQL provider only, actual application-level domain backend, Standalone/Shared over same qualified primary. No extra embedded/Redis/Cosmos adapter, new packages/pins/source mappings, DB framework or second coordinator.
- Managed execution opt-in; once selected initial registration+authoritative control+local prerequisites mandatory before workers. Closed from construction; passive client remains passive; Startup remains sequential.
- No existing DB access, external deployment, credentials/trust-store provisioning, image build campaign, push/merge/release. W01 only existing bounded newly owned current-user TLS backing and joined cleanup.
- One nonoverlapping Heartbeat owner, slower reporting coalesced, bounded reconnect/diagnostics. Reopen only fresh control after module-owned reconciliation, no RM08 implementation.
- Preserve original RM02/RM03/RM06 transaction atomicity, existing operation scopes and original results across upgrade; authority-bound cursor version. Effective inventory max in service-info.
- Preserve originalv1.0 source/history and user CountryData launchSettings SHAea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784. Root serializes native builds/CIS; source parallel only distinct files/ports. No completed work owner reopened.
- Stable persisted authority/revision/recovery per namespace, explicit migration/hold/expected recovery epoch; no silently minted restart authority. Primary/read-write/durability checks; isolated backend fails control/bootstrap/mutation/readiness while liveness survives.
- States AwaitingBootstrap/Ready/ContinuingOnly/Reconciling/Stopping. Request-start monotonic freshness,5s control/15s max, per-attempt nonce/no retries, first failure closes new admission. Check every admission, fence late/old authority/registration, conservative suspension revalidation.
- User current updated-spec necessary-changes request and prior RM07 no-permission-pause delegation authorize implementation. Codex selects this finite profile; exact numbers are not separately human-reviewed.

The total is 172,800 active seconds and 56 logical attempts, including 7,200 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only), `implementations/typescript` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
