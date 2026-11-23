---
title: "RM-07 startup cancellation and native-fetch recovery corrections"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0026
outcome: "Close managed admission on canceled public startup while joining accepted work; preserve structured expected native-fetch body/cleanup failures with original mutation recovery identity and actual native HTTPS regressions."
baseline_kind: git
baseline: "fe029a85741705dc51c1093403248fc4aa2920ca"
graph_build_id: "sha256:b2c41ab765c2b1d03e349095198d917dc459d29eb045fe267c7f6a76612325f2"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:f589ed5c19717fc9b8d6f2f1c701bec89d829b57f5a09db98eae0c13265a4f70"
authority: derived-outcome-authorization
---

# CIS-0026: RM-07 startup cancellation and native-fetch recovery corrections

## Outcome

Close managed admission on canceled public startup while joining accepted work; preserve structured expected native-fetch body/cleanup failures with original mutation recovery identity and actual native HTTPS regressions.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Deliver the authorized outcome and its task-specific acceptance criteria within the shared constraints and limits.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-RM07-REVIEW-2026-11-23-DELEGATED-NATIVE-PREREQUISITES`, recorded for Codex under Andrew Spiteri delegation. Tasks reference this shared context; their organization is not a separate human review.

Correct the two reproduced RM07 review defects without expanding module scope.

- `fetch`: Native fetch body and cleanup expected-failure translation, structured mutation recovery and existing native HTTPS fixture/Node tests/docs Paths: `docs/modules/management/csharp-client.md`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Management`, `implementations/csharp/tests/CIS.Core.IntegrationTests/RestClient`, `implementations/typescript`.
- `native-prerequisites`: Route the new Node-dependent native regression truthfully through existing fast/CI selections and prerequisite documentation Paths: `.github/workflows/service-tests.yml`, `README.md`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Management/ManagementFetchTransportTests.cs`, `implementations/csharp/tests/README.md`.
- `records`: Fresh coherent correction plan, review requirements and honest native evidence/accounting Paths: `.artifacts/management/CIS-0026`, `.cis/local/management-review`, `docs/catalog.yml`, `docs/changes/CIS-0026`.
- `startup`: Public managed-startup cancellation cutoff, existing lifecycle regression tests and reporting documentation Paths: `docs/modules/management/registration.md`, `implementations/csharp/src/CIS.Core/Management/Registration`, `implementations/csharp/tests/CIS.Core.UnitTests/Management`.

- No Docker container/image build or new external service. Existing retained Docker allowance2GiB remains counted; external cache144MiB and bounded W01 owned native TLS backing8MiB/max16 imports stay within unchanged physical ceiling. No trust-store installation, machine keys or existing-credential access.
- No commit, push, merge, release or deployment included in this correction. Preserve unrelated CountryData launchSettings SHAea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784.
- Preserve joined accepted work, original cancellation facts and unexpected programming faults. No new dependency, toolchain pin, framework, test harness, backend, database access or broad qualification campaign.
- Six shared active hours and eight logical attempts including 600 restoration seconds, all agents/intake/source/native checks/docs/review included. Existing physical6GiB ceiling and64MiB reserve unchanged.
- Two coherent independent source tasks; root serializes native builds and CIS writes. Startup remains sequential. No completed CIS0024 or0025 owner is reopened.
- User pasted review requests the two P2 corrections; prior RM07 no-permission-pause delegation authorizes finite profile selection. Codex selected exact bounds; not separately human-reviewed.

The total is 21,600 active seconds and 8 logical attempts, including 600 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only), `implementations/typescript` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
