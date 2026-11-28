---
title: RM11 Rule Engine bounded implementation plan
type: implementation-plan
status: Draft
change_id: CIS-0031
---

# RM11 Rule Engine bounded implementation plan

Deliver one opt-in .NET 10 evaluator that returns matching rows from an immutable deployed ruleset. Operators use the existing Management portal to save rules, deploy or roll back saved versions, inspect adoption and audit, and reset counters. Applications own their input facts, metadata interpretation and subsequent business actions.

The requirements baseline is [RM11 v1.0, 27 November 2026](../../../planning/rule-engine/sources/CISLIB_RM11_Rule_Engine_Specification.md). Its SHA-256 is `ad4c1dac634279969e90f5a3ef24ccf2460f7ff93aac632375a9ec887604212c`. [CIS proposal](proposal.md) and [contract reconciliation](contract-reconciliation.md) record the checkout and integration seams. The [CIS feature input](feature-specification.md) preserves all 30 source criteria.

## Delivery sequence

| Proposed leaf | Observable outcome and focused evidence | Needs | Hours / attempts |
| --- | --- | --- | ---: |
| R01 Shared contracts | Version the rule definition, schema revision, invocation, result/trace, audit-event, capability, deployment and Management DTOs. Use portable fixtures for decimal boundaries, missing/null, unknown inputs, opaque metadata, Unicode, IDs and limits. Agree the cross-boundary details in the reconciliation before consumers implement against them. | — | 4 / 4 |
| R02 Validator and evaluator | Add the mockable API surface and deterministic comparison/selection core in `CIS.Core`. Reuse the definition validator on save and host preparation. Unit cases cover all operators, exact bounds, Unicode/escape cases, lazy type checks, state/mode/error transitions and immutable inputs/results. | R01 | 4 / 4 |
| R03 Trusted registration and version save | Add the ruleset/schema capability, scope and immutable save/list/read in the existing Management service and clients. Guard the edit revision and recover writes by original key. Prove two-editor contention, copied/deleted row identity, registration/restart preservation, and coverage of the existing generic routes that could bypass validation. | R01, R02 validator | 5 / 5 |
| R04 Deployment authority | Select saved versions and roll back through RM08 assignment. Record same-scope deployment eligibility and protect referenced definitions using existing authority and retention. Provider cases exercise saved-only rejection, exact-version rollback, conflict/replay, real authority boundaries and cleanup around retained pins. | R03 | 5 / 5 |
| R05 Host adoption and selection | Integrate trusted schema/build validation with actual RM08 activation, local immutable capture, exact-version resolution and RM07 admission. Prove in-flight isolation, per-instance lag/rejection, a supported prior exact version executing while the newer version stays active, wrong-scope/incompatible/unavailable refusals, registration recovery and bootstrapped outage behavior. | R02, R04 contracts | 5 / 5 |
| R06 Durable invocation audit | Use typed RM06 start and completion with correlation, trusted application/environment/instance, actual build, engine contract and timestamps. Gate execution/result release on confirmed acceptance. Prove early refusal, partial progress, cancellation and terminal selection races, finalization failure/unknown, and confirmed outbox acceptance while downstream delivery is unavailable. | R02, R05 invocation seam | 4 / 5 |
| R07 Searchable execution history | Add a scoped, authenticated RM11 receiver/projection and bounded query/detail on the existing service/provider, with exact logical deduplication, out-of-order delivery, protected immutable rule evidence and truthful incomplete status. Prove real outbox/receiver recovery and permission-filtered reads with native fixtures. | R03, R04 retention seam, R06 event contract | 6 / 6 |
| R08 Counter extension | Project observed hits on existing storage with per-counter acceptance order, retry deduplication and audited individual/selected/all resets. Prove pre-reset replay, a first acceptance after reset for an older invocation, multi-instance aggregation and partial or unavailable status. | R03, R07 projection seam | 5 / 5 |
| R09 Backoffice wireframes | Define the grid, saved versions and rollout, counter operations, and history/detail with exact API and permission mappings. Include keyboard editing, lossless typed controls, conflicts, absent/null and stale/interrupted states. | R01 | 2 / 2 |
| R10 Backoffice design | Render the new surfaces with the existing portal shell and controls and synthetic data. Submit the exact wireframe and rendered review package through the existing CIS UI gate. | R09 | 3 / 3 |
| R11 Portal editing and deployment | Wire the reviewed schema-driven grid, immutable version browsing, Save, Deploy/rollback and per-instance rollout views to real APIs. Execute browser flows for typed fields and markers, add/copy/remove/reorder, mode and row-field edits, validation and conflict recovery with draft preservation, a new edit based on an older version, and pending/rejected adoption. | R03, R04, R05, approved R10 | 5 / 5 |
| R12 Portal counters and execution history | Wire reset time, freshness and per-counter outcomes, execution ID lookup, ruleset/version/instance/time/outcome filters and authorized detail to real APIs. Run browser journeys for each reset scope, raw typed inputs and presence, condition outcomes and stop reasons, Test hits, returned metadata/weights in result order, interrupted and completed-empty outcomes, and grant/session boundaries. | R07, R08, approved R10 | 5 / 5 |
| R13 CountryData consumer | Add one opt-in example using ResidenceCountry and Pensioner. Demonstrate both modes, explicit deployment and an execution ID that resolves to actual audit. Keep the default example database-free. Execute the composed consumer journey through the existing host/provider fixtures, including distinct application instances for capture and execution isolation. | R05–R08, R11–R12 | 3 / 3 |

R01 is the first reviewable implementation slice once the work is authorized. After it, runtime, Management, and backoffice design can proceed against the shared value and trace contracts. The indicated seams allow parallel design and development. Their producer and consumer behavior must both be proven before dependent leaves close. Serialize shared native outputs, migrations and CIS metadata writes. Plan the design handoff to respect the review pause when the exact backoffice package enters review.

The outcome authorization records a shared ceiling of **58 hours and 59 attempts**, including **2 hours and 2 attempts** for restoration. The ceiling covers tests, documentation and independent review within each leaf. The authorization records the existing **6 GiB** storage ceiling with a **64 MiB** restoration reserve. Following screen approval, CIS applied the [full work distribution](work-allocation.json) of 56 hours and 57 attempts across all thirteen items.

## Acceptance ownership

Each AC has one primary evidence owner. A leaf may close its own slice once its evidence is recorded. Shared acceptance passes after all listed cross-surface evidence is in place.

| Criteria | Primary owner | Additional evidence |
| --- | --- | --- |
| AC01 | R03 | R05 restart and R11 real editor |
| AC02–AC11 | R02 | R06 persisted trace and R12 detail where applicable |
| AC12 | R11 | R01 shared wire fixtures, R02 exact matching, R06/R07 persisted precision |
| AC13–AC15 | R02 | R06 failure/Test trace and R13 worked example |
| AC16 | R03 | R11 old-version editor and history |
| AC17 | R04 | R05 refusal at the normal evaluation entry point and R11 Save/Deploy distinction |
| AC18 | R05 | R04 assignment provenance and R11 instance-level visibility |
| AC19 | R04 | R05 exact selector and R11 rollback flow |
| AC20 | R05 | R02 detached snapshot, R06 same facts in audit and two real instances in R13 |
| AC21–AC23 | R08 | R06 observed-match source, R12 actual resets and freshness |
| AC24 | R07 | R06 source records and R12 detail |
| AC25–AC26 | R06 | R08 retained earlier hits when durable evidence survives |
| AC27–AC28 | R07 | R06 source-gate/outbox and real interrupted-process seam |
| AC29 | R12 | R11 editing/deployment browser results and R12 counter/history browser results. R13 links one actual consumer result to audit. |
| AC30 | R03 | R11 validates preserved operator draft/conflict recovery |

Use the pinned SDK and native MSTest, shared C#/TypeScript wire cases, existing optional CommonTesting substitutes, and the repository's PostgreSQL/RabbitMQ, host/process and browser fixtures. Use representative cases for coordinated failures, large accepted traces and one-over-limit refusals. Each leaf records the selected case IDs, commands and observed results in CIS. A real provider proves SQL arbitration and durable delivery. A real browser against the native service proves the operator workflows. Existing foundation results and synthetic design data are context for those checks.

## First-release boundary

Keep the evaluator embedded, dependency-free beyond existing approved runtime dependencies, and opt-in. Use explicit protocols, two execution modes, three states and the v1 inputs/operators. The management deliverable is the existing desktop backoffice area and its reviewed workflows. Scripts or expressions, implicit business transactions, scheduling, a second control service or deployment authority, a general audit archive, and additional evaluator languages are outside v1.


## CIS intake and validation status

CIS registered the exact attachment under BRD-SRC-24748fd34504 and reconciled its existing Reference assessment into the product BRD. The BRD, technical intent, solution design and high-level UI direction subsequently reported Active and current. The technical questionnaire reported all 16 answers current.

The draft feature import reached the impact review. A focused analysis of six RM06–RM09 roots removed traversal truncation and retained 11 proposed findings on the pinned stale graph. The [impact review](impact.md) remains proposed. The [contract reconciliation](contract-reconciliation.md) checks current repository sources.

The [recorded outcome](authorization.json) authorizes task organization. CIS retained the seed identity in WORK-001 and added WORK-002 through WORK-013 using the staged [seed update](task-inputs/work-001-update.json) and [twelve-item batch](task-inputs/work-002-013-add.json). The initial Draft work definitions matched the staged acceptance, validation, dependencies and scopes. WORK-001 and WORK-009 completed contract coordination and wireframes. Andrew Spiteri approved the exact [backoffice preview package](review-package.md) on 2026-11-27 at 21:13 UTC, opening the design gate. See the [canonical CIS plan](plan.md) for current task status.

Implementation proceeded under the subsequent execution instruction and [bounded allocation](work-allocation.json). The [verification record](verification.md) links the runtime, provider, approved browser workflows and CountryData evidence to each CIS work item.
