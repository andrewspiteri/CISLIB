---
title: CIS-0028 verification
type: verification-record
status: Draft
change_id: CIS-0028
---

# Verification

This is a focused local tool-adoption record, not RM-08 acceptance or release
qualification. The original approved component sheet, design bundle, RM-08 source
specification and CIS-0027 dossier remain the product authority. The derived
[ownership](../../references/current-component-ownership.md) and
[criterion mapping](rm08-traceability.md) add current locations and one exact
applicable check; they do not amend that authority.

| Task | Check | Actual evidence | Result and limit |
| --- | --- | --- | --- |
| WORK-001 | Actual explicit installation | `.cis/local/testing/current-context-correction/install-preparation-budget-receipt.json`; unchanged `Invoke-Rm08Cis` wrapper version receipt | Installed all257 files from manifest SHA256 `6909cf98415d622a3396c8aea3d1125adf6974ffc8096b4a381b9e99e165eb42`; observed `0.3.0+worktree.d034618828e496d8c8e64ad55556ecce455871acc83ca54f9cf66d0b1415d5f1`. Host SHA256 `cb54f0c91151850537c2c3fb0ba7c9f300f6f637222bab1b0e8924c8fde03cb2`. |
| WORK-001 | Existing real AC-10 check | `.cis/local/testing/incremental/rm08-current-context-unit/reports/file-db950b7c67892f0443fd-unit.xml` | Exactly one MSTest case passed, zero failed/skipped. Retained report SHA256 `87b9a94760244bc5c410df963c647c132c87b1dee9a2cc207064b3823c2a6ce4` matches the original native TRX bytes. Case body73.5653ms; complete CLI581,339ms. |
| WORK-001 | Legitimate native settlement | `.cis/local/testing/current-context-correction/rm08-run-parent-budget.json` | `completed`, one executed unit batch, zero pending unit/mutation batches, known recovery and no reservation. Unit scope complete; broader `qualified` remains false. |
| WORK-001 | First current verification | `.cis/local/testing/current-context-correction/rm08-verify-first.json` | Installed read-only Verify returned `verified`, exit0, declared unit scope complete in96,821ms, without launching tests. Documentation warnings stay visible; release qualification remains separate. |
| WORK-001 | Unchanged repeat after result projection | `.cis/local/testing/current-context-correction/rm08-verify-repeat.json` | `verified`, exit0, declared unit scope complete in79,991ms. Same unit fingerprint and report SHA256; no native launch. |
| WORK-001 | Current ownership and exact trace | `.cis/local/testing/current-context-correction/graph-context-proof.json` |37 components including all4 original TI identities,39 declared ownership paths,24 source-declared criteria, one explicit AC-10 mapping and23 open mappings. Exact task/component/artifact/check/result edges are observed. |
| WORK-001 | Normal bounded task preparation | `.cis/local/testing/current-context-correction/agent-prepare.json`; `agent-prepare-operation.json` | `prepared`/`fresh`, actual complete task-context frame10,338 characters/UTF8 bytes within16,000/64,000 budgets;17 omissions. Canonical task bytes unchanged by prepare. |
| WORK-001 | Preservation | `.cis/local/testing/current-context-correction/preservation-proof.json` |388 original files including333 TRX,301 preexisting incremental files,27 old change-control subtrees/142 owners, adoption/corrections and2 npm aliases/55 target files unchanged. New native state/report files and execution IDs stayed unchanged through Verify/projection. |
| WORK-001 | Supported lifecycle closure | `.cis/local/configuration-publication-intake/current-context-WORK001-supported-complete-summary.json` | Applied `Complete`, exit0, zero blockers in112,175ms after the normal `InProgress` return. Earlier direct `ReadyForReview` to `Complete` refusal is retained. |
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2524; failed=339; possibleTokenSavings=591; ledgerDigest=sha256:334d8cc006bbd035c721ed5a2155ac6191c28abfeaa63e7c6e697140260bd577 |

The exact case is
`CIS.Tests.UnitTests.Configuration.ConfigurationCentralAcceptanceTests.PairedInitializationUnchangedAcceptanceAndExplicitRemovalKeepOneRootAndTheActualRevision`.
Its source assertions cover initial revision zero and unchanged effective values
without false revision/event, plus explicit source removal. One applicable check
does not establish every clause of AC-10. Its assertion meaning, whole-criterion
coverage and satisfaction remain unassessed; the other23 criterion mappings remain
open. The implementing task stays CIS-0027/WORK-004; CIS-0028/WORK-001 owns this
tool-installation and verification demonstration.

The authentic first Verify stdout is published byte-for-byte as
`.cis/local/testing/current-context-correction/rm08-evidence.json`. Graph facts
classify a recorded native pass separately from current native verification:
398 input pins contain383 repository matches and15 external inputs which the graph
leaves unassessed. Current installed Verify assesses its native scope separately.
The normal context frame includes the exact check's `recorded-passed` observation,
matching repository pins and `requires-current-native-verification` qualifier.
Its source excerpts are trimmed to zero; the3,300-character canonical task
instruction remains intact in the separate envelope, with detail records and
omissions explicit. This is bounded context, not complete source or assertion
coverage. Checked lifecycle and any post-bookkeeping revalidation are retained as
their actual CLI receipts; their real statuses govern those separate observations.

Earlier preparations remain retained: linked npm workspace inventory, a120-second
metadata window, and an operation-growth-domain guard prevented completed native
batches. The later f74e driver observation is unproven, not a passing or definitive
no-launch result. After its recorded driver/controller stopped, normal exact-stage
reconciliation cleared the hold and conservatively charged the full900-second
reservation. No reports, uncertain observations or used attempts were erased.
Both legitimate npm junctions remain intact. The profile and120-second native-test
timeout remain unchanged; the finite900-second controller envelope includes dense
preparation and settlement.

The current delegated distribution is source79,200seconds/23attempts plus
consumer7,200seconds/oneattempt: the original86,400seconds/24attempts shared grant.
Authorization history retains the earlier1800/2700/3600/5400 distributions; the
original1800 constraint is its historical initial snapshot. All1200 restoration
seconds stay with the source owner. Automatic consumer native accounting and
external bootstrap/metadata timings remain separate; dependency waiting is not
reported as free active execution or as a new grant.

Consumer physical storage remains capped at6GiB, including Docker2GiB, external
cache144MiB, TLS8MiB and64MiB protected reserve. The first schema7 refresh took
1,803,513ms with sampled conservative peak5,988,575,944B; it exited0 without crossing
the ceiling. Required result projection/normal prepare took190,711ms with sampled
conservative peak5,550,713,361B, including16MiB monitoring headroom. Its admission
also counted2,279,860B exact retained outside scratch. Native execution and
current-context projection run sequentially. Automatic native accounting after the
successful result is1,877,725ms, including the conservative900-second charge;
external graph/install/metadata work is separately timed and is not zero. Inclusive
phase timings can overlap and must not be summed as exclusive elapsed/model work.
The original2b916962 binary rollback and compatible schema6 archive are preserved
under source-owned WORK-007; exact relocation/installation receipts retain all
intermediate generations and hashes. Binary installer rollback does not claim an
automatic graph restoration. No product runtime change, old-owner reopening,
database access, push, merge or release is included.

The normal tool-usage snapshot includes historical estimates. Its possible-token-savings field does not establish net model-token savings; actual model-token counters are unavailable. The final read-only post-bookkeeping observation is retained separately as `.cis/local/testing/current-context-correction/rm08-verify-post-close.json` and its operation receipt. Native reports and profile inputs remain unchanged; that receipt governs its actual status.
