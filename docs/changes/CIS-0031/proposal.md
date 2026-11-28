---
title: "RM11 Rule Engine"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0031
outcome: "Applications evaluate an immutable deployed ruleset locally and operators manage versions, deployments, counters and audited outcomes in the existing management portal."
baseline_kind: git
baseline: "cedfbfcf0265ebabd7054ec2434a443958123d73"
graph_build_id: "sha256:1328a3343309175f8c5ded3b41404886ba8b52b9387b43f1554ddf71cb5b52dd"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:f4d92c49f67f67ee48247c31b4a15ceeda51ed0cbaa03094e414cbe898d2bc05"
authority: derived-outcome-authorization
---

# CIS-0031: RM11 Rule Engine

## Outcome

Applications evaluate an immutable deployed ruleset locally and operators manage versions, deployments, counters and audited outcomes in the existing management portal.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- An admitted application invokes a mockable local evaluator against one immutable, eligible deployed version. It returns deterministic results for both execution modes after confirmed durable completion audit.
- Authorized operators can manage immutable versions and explicit deployment, observe per-instance adoption, reset counters and inspect the recorded execution in the existing backoffice portal.
- Acceptance requires attributable evidence for all 30 source scenarios, including the operator browser journeys and the opt-in CountryData example.

## Draft planning inputs

- [Retained specification](../../../planning/rule-engine/sources/CISLIB_RM11_Rule_Engine_Specification.md), registered as reference BRD-SRC-24748fd34504.
- [Feature input and acceptance criteria](feature-specification.md).
- [Contract reconciliation](contract-reconciliation.md) and [bounded implementation plan](implementation-plan.md).

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `USER-CIS-0031-RM11-OUTCOME-20261127`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Applications evaluate an immutable deployed ruleset locally and operators manage versions, deployments, counters and audited outcomes in the existing management portal.

- `RM11-AUDIT`: Use the existing durable RM06 paths for start and completion and qualify scoped native execution history together with the Management scope. Paths: `implementations/csharp/src/CIS.Core`, `implementations/csharp/tests/CIS.Core.IntegrationTests`.
- `RM11-BACKOFFICE`: Plan and review the desktop Management portal design, then deliver its typed ruleset grid, deployment and adoption, counter and protected execution history workflows under the applicable design approval. Paths: `docs/changes/CIS-0031`, `implementations/csharp/tests/CIS.Core.IntegrationTests`, `implementations/typescript/applications/management-portal`.
- `RM11-CONTRACTS`: Coordinate and record the shared RM11 portable ruleset, runtime, trace, Management, audit, deployment and counter contracts and conformance vectors. Paths: `conformance`, `contracts`, `docs/modules`.
- `RM11-COUNTERS`: Deliver the scoped idempotent hit projection and audited per-counter reset order, freshness and bulk outcomes using the existing Management provider and clients. Paths: `implementations/csharp/applications/CIS.Management.Service`, `implementations/csharp/src/CIS.Core/Management`, `implementations/csharp/tests/CIS.Core.IntegrationTests`, `implementations/typescript/packages/management-client`.
- `RM11-EXAMPLE`: Deliver one opt-in CountryData rule-engine example and the composed provider and consumer evidence within the maintained example and existing tests. Paths: `implementations/csharp/examples/CountryData`, `implementations/csharp/examples/CountryData.UnitTests`, `implementations/csharp/tests/CIS.Core.IntegrationTests`.
- `RM11-MANAGEMENT`: Deliver trusted ruleset registration, immutable save and read, same-scope deployment and rollback authority, retention, and typed Management client seams on the existing service. Paths: `implementations/csharp/applications/CIS.Management.Service`, `implementations/csharp/src/CIS.Core/Management`, `implementations/typescript/packages/management-client`.
- `RM11-RUNTIME`: Deliver the opt-in .NET evaluator, shared validator, host adoption and exact-version selection using the existing scope and startup contracts. Paths: `implementations/csharp/src/CIS.CommonTesting`, `implementations/csharp/src/CIS.Core`, `implementations/csharp/tests/CIS.Core.IntegrationTests`, `implementations/csharp/tests/CIS.Core.UnitTests`.

- A separate explicit user instruction is required before allocating resources, reserving or starting work, building or running product tests, or making implementation changes. Codex is delegated task organization only under this authorization.
- The 58-hour and 59-attempt shared ceilings include a 2-hour and 2-attempt restoration reserve. Record finite ceilings as planning metadata; no resources are allocated. The 6 GiB ceiling includes a 64 MiB restoration reserve and uses the existing physical CIS local storage bound, with no separate storage pool or scratch permission.
- The current instruction authorizes recording the outcome and importing the thirteen reviewed planning definitions. Keep WORK-001 through WORK-013 Draft, with assignees unset, until a separate user instruction authorizes progression.
- Use the supplied RM11 v1.0, SHA-256 ad4c1dac634279969e90f5a3ef24ccf2460f7ff93aac632375a9ec887604212c, and the CIS-0031 bounded plan covering AC01 through AC30. Preserve existing CISLIB runtime, provider and portal ownership and qualify dependent work through the existing review gates.

The total is 208,800 active seconds and 59 logical attempts, including 7,200 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.cis/local`.
Controllers: Codex. Task organization: delegated; resource distribution within the total: not delegated.
Visual UI: applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
