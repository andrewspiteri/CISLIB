---
title: "Local transaction integration (RM-02)"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0017
outcome: "Implement the specified portable local transaction owner/context and guarded ADO.NET participation in Core, with conservative persistence outcomes and actual cleanup; qualify the PostgreSQL three-table pattern outside Core dependencies, add bounded existing telemetry/public testing/package consumption/docs/skill, preserving laterRM03/RM04 and actual platform/evidence boundaries."
baseline_kind: git
baseline: "539a05d184432b5b8c131caf8454a44d0134c692"
graph_build_id: "sha256:bf7b91eedf53d5acb325e7a28a86adbe0cbd4db60e8b092665eeb97c0a5a3cd5"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:e2121662ba37300252e1400ba733da030906866c39e3560fc25776ac1c57303e"
authority: proposed
---

# CIS-0017: Local transaction integration (RM-02)

## Outcome

Implement the specified portable local transaction owner/context and guarded ADO.NET participation in Core, with conservative persistence outcomes and actual cleanup; qualify the PostgreSQL three-table pattern outside Core dependencies, add bounded existing telemetry/public testing/package consumption/docs/skill, preserving laterRM03/RM04 and actual platform/evidence boundaries.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Core exposes the specified portable contracts and public ADO.NET participation without a new shipping dependency, ambient transaction selection or framework.
- Named composition is passive/immutable/bounded; begin, admission, rollback-only, cancellation, completion and cleanup preserve exact ownership, original application faults and the three persistence outcomes.
- A real isolated PostgreSQL consumer proves the three-table unit, caught-error marking, decline-only result, isolation and active-query cancellation from independent connections; actual provider/platform limits remain explicit.
- Existing Logging/Metrics and bounded CommonTesting/public packages work through current contracts. Documentation, compiling usage, agent skill, focused native tests and independent review are included with their owned implementation outcomes.
- All source AC-01–28 groups are mapped by the five-task plan. Missing native database/platform proof remains unverified; scripted lost-acknowledgement behavior is not a real network-failure claim.

## Requirements and proposed execution

[Preserved source](../../../planning/transactions/sources/CISLIB_RM02_Local_Transaction_Integration_Specification_v1.0.md), SHA-256 `8a9e056a7ad30a2d2d6af475a1d1f0e40a138f10e1f61905a374f6f1d1fcbbb7`.

Read the proposed [P01–P14 plan and allocation](implementation-plan.md),
[dependency/Docker profile](dependency-profile.md), `task-contracts.json`,
`authorization-request.json` and `proposed-allocation.json`.
No authority or completion is asserted by these drafts. One actual human decision
is required for fresh RM-02 scope, Npgsql and the amended physical storage ceiling.
After it, CIS registers the five canonical tasks under the delegated outcome;
there is no separate per-task approval stage. No product-document approval,
existing database access, external deployment, push, merge or release is included.
