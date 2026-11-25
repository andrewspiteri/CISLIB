---
title: "RM-08 criterion and current evidence projection"
type: verification-record
status: Draft
change_id: CIS-0028
cis:
  stable_id: cislib:change:cis-0028:rm08-traceability
---

# RM-08 criterion and current evidence projection

The following IDs and exact criterion wording are projected from section17 of the
[accepted RM-08 specification](../../../planning/configuration/publication/sources/CISLIB_RM08_Central_Configuration_Publication_and_Distribution_Specification_v1.0.md),
SHA256 `592f6af2e49d982fa7bfbe4581fac8f66eeb9939afd7c4c6fa9008e3e1e2db2f`.
The [original implementation authority](../CIS-0027/authorization.json) and
[observed acceptance](../CIS-0027/acceptance.md) remain unchanged. This derived
projection namespaces existing AC identifiers by this document; it introduces no
new requirement or approval.

## Acceptance criteria

| ID | Criterion |
| --- | --- |
| AC-01 | Existing non-central/defaults-only and custom `IConfigurationStore` consumers retain their public contracts, object identity, initialization, merge and event behavior. |
| AC-02 | A centrally managed value is available in a real consuming constructor before Build/startup completion, using one application provider and authenticated bootstrap resources. Failed preparation/Build joins cleanup. |
| AC-03 | A fresh or restarted managed process with no live authoritative bootstrap remains not ready even when a valid cached or formerly pending release exists. An explicit live empty release can authorize captured defaults. |
| AC-04 | Publish without assignment leaves running applications unchanged. Successful publication, assignment commit, fetch and application are represented separately. |
| AC-05 | Defaults/common/application precedence, nested merge, collection replacement, explicit null, masked invalid common values and override removal match the existing Configuration contract. |
| AC-06 | Concurrent common/application publications cannot produce a mixed release. Central reads require the paired-snapshot capability; independent-read fallback is refused on that path. Missing referenced content fails without becoming source absence. |
| AC-07 | Application/environment/schema/registration mismatches and unauthorized source or status reads are refused without leaking another application's override. Self-reported cohort labels do not redirect targeting. |
| AC-08 | Default/cohort resolution is deterministic. New and restarted processes receive the assignment for their trusted cohort. Cohort membership changes require new bootstrap; old-session responses cannot cross into the new context. |
| AC-09 | Canary promotion updates the default and explicitly participating overrides atomically. A later default change reaches a cohort whose promotion override was removed; deliberate unrelated pins remain intact. |
| AC-10 | Duplicate delivery, unchanged effective values and higher map generation with the same resolved release update provenance without a false local revision or event. Initial baseline remains revision zero. |
| AC-11 | Late lower generations and equal-generation conflicting content cannot overwrite a newer observation or accepted state. A canceled/superseded candidate cannot commit under the wrong session. |
| AC-12 | Invalid schema, JSON, digest, typed value, cross-property condition or size limit rejects the candidate while preserving the valid previously accepted object and provenance. Repeated failures remain bounded. |
| AC-13 | Hot reload retains the root; concurrent supported readers see complete accepted root states. Subscribers run under existing order/isolation rules; a throwing subscriber does not undo application or prevent later handlers. |
| AC-14 | Restart-required updates keep active values unchanged and report both active and desired versions. A newer assignment supersedes the pending candidate. Desired values returning to active values clear pending restart without a false event. |
| AC-15 | A restart fetches and applies the current live desired release, not a stale staged release. A restart-required rollback to different values remains pending restart until that actual restart. |
| AC-16 | A throwing live setter produces disabled/Faulted state, no false candidate acceptance and no claim that the last successful release still describes intact live values. |
| AC-17 | Management and authoritative-backend outages preserve existing accepted configuration. RM-07 independently closes new managed-work admission; admitted work follows its own rules. No replica cache, configuration poll or disk cache renews authority. |
| AC-18 | Reconnect converges after missed/duplicate notifications and several skipped releases using authoritative current state. Required polling recovers an update whose notification was lost. |
| AC-19 | Rollback allocates a higher generation and restores the explicitly selected default/cohort entries without changing unrelated pins. Conflicting concurrent assignment edits produce one winner and a defined conflict. |
| AC-20 | A lost source-publication, release-create or assignment response recovers the original committed result with the original key; effect and required audit are not repeated. Required audit staging failure prevents commit. Unknown completion is not rewritten as rollback. |
| AC-21 | Two real management replicas sharing PostgreSQL observe one committed assignment and its complete references. Writer races, process restart and backend outage preserve ordering, idempotency and atomic visibility. |
| AC-22 | Adoption reports preserve actual registration/workload ownership, independent sequencing and receipt freshness without inventing process proof. Old/equal-conflicting reports cannot fabricate convergence. An unidentified failed poll preserves prior adoption facts. Offline, stale, unknown, no-instance, rejected and pending-restart targets remain visible. |
| AC-23 | C# and TypeScript round-trip exact revisions above 2^53, local revision zero, timestamps, optional response additions, explicit unknown statuses, non-ASCII payload text and exact payload digests. |
| AC-24 | Resource limits, combined encoded release distributability, pinned-history cleanup, source-reference retention, diagnostic budgets and actual shutdown drainage hold under the existing focused tools. Trusted manifest provisioning is idempotent and does not reset existing catalogs/assignments. Feature discovery does not advertise durable publication on an incapable store. |
## Implementation and verification

This one row maps the actual accepted AC-10 behavior to its original implementing
task, explicit component/artifact and one exact applicable check. The native
receipt is created only by installed `test incremental verify`; absence is
unresolved evidence, never a pass. Other23 criteria retain their accepted meaning
but have no new exact check/result mapping in this focused demonstration.

| Criterion | Task | Component | Artifact | Check | Result reference |
| --- | --- | --- | --- | --- | --- |
| cislib:change:cis-0028:rm08-traceability#AC-10 | cislib:change:cis-0027:task:work-004 | CIS.Configuration | implementations/csharp/src/CIS.Core/Configuration/Refresh/ConfigurationRefreshCoordinator.cs | implementations/csharp/tests/CIS.Core.UnitTests/Configuration/ConfigurationCentralAcceptanceTests.cs#PairedInitializationUnchangedAcceptanceAndExplicitRemovalKeepOneRootAndTheActualRevision | .cis/local/testing/current-context-correction/rm08-evidence.json |

Recorded passed cases, current input/report pins and full native applicability are
separate facts. The graph retains recorded result/provenance; installed verification
reassesses current selection, tools, environment and original settlement. A lexical
association or a task's changed-file list supplies neither assertion coverage nor
passed execution. Broader release and external deployment qualification remain open.
