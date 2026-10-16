# Native queue and diagnostics driver migration

16 October 2026, based on `83b8b736`. Corrective Batch B changes verification only. Shipping Core, dependencies, public behavior and historical vectors are unchanged. Local review applies; no Claude transfer.

Seven Python drivers are retired. All 107 physical rows remain across eight catalogs: 11 bounded queues, 12 fanout, 12 overflow, 15 bursts, 11 accounting, 11 delivery, 12 output state and 23 diagnostics. Native checks require the exact original scenario IDs/counts. Diagnostics also bind the source profile/catalog/ID and hash the source catalog before/after the child; parent validation still checks the complete underlying delivery/state report.

| Legacy family | Former rejection cases | Native disposition |
| --- | ---: | --- |
| Bounded queues | 10 | Nine existing controls retained; framing plus six additional worker/record checks now produce 16 controls. |
| Fanout and overflow | 13 + 14 | The 13 common controls now execute for both catalogs. Two additional schema/time corruptions run for each. Existing eviction/lost-work/false-acceptance cases remain: 33 native controls cover all 27 former cases. |
| Bursts | 9 | All nine retained. |
| Accounting | 9 | All nine retained. |
| Delivery observations | 10 | All ten retained. |
| Output states | 7 | Seven retained plus an extra-field corruption. |
| Public diagnostics | 12 | Twelve retained plus an extra-field corruption. |

All 84 former checks remain within 98 native comparator cases, including 14 additional checks. Comparer controls validate original Core output before damage. Assertions now retain complete worker/output field sets, original schema/component/severity/event/time, raw-CR rejection, contiguous successful ticket prefixes, monotonic state counters and canonical UTC interval labels. Existing exact counter partitions, safe fault/time retention, cancellation observations, immutable bytes, partial writes, no retry/eviction, independence and owned snapshots remain.

The shared process helper adds actual child PID, before/after input/managed hashes, full parent environment/CWD preservation and original command/report retention. Original Python child limits are preserved: 30 seconds for bounded queues, 35 for the remaining readers. This exercises actual internal runtime/Channel workers with controlled streams. It does not replace public incoming-pipeline acceptance, physical destination ownership or release qualification.

## Verification

Windows unit `012c52ffde474d1d9e651a1c60c1d4cb` passed 259, integration `37f4d810df0d467e91220a6105b838e4` passed 107 and architecture `0c996f802c964d7cb8f694f17525673f` passed 12, zero failures/skips. Ubuntu unit `b3638eb289b941e3962a9022d80c9ae4` passed 259, integration `d8f59e8551d948a6aa75643a53fd05ec` passed 107 and architecture `f1ce848a508044a29e433fa7192316bf` passed 12, zero failures/skips. The audit confirms matching identities for all 378 cases. Ubuntu builds scoped current copies with isolated outputs, not a shared exact package or a full lane checkpoint.

The initial Windows unit attempt `7c1339e75f104687a6f25b13fbaff687` stopped at compilation because the added queue corruption helper needed a System.Text import. It has no executed-test credit. The import was added before the passing run. A formatting pass then completed cleanly; no shipping code changed.

The most recent full Windows Developer result remains the earlier 5,948-case value checkpoint. These later migrations use focused checks. Complete corrected coverage/mutation/compatibility, both-lane qualification and release gates remain open. The original roadmap remains paused.

## Retirement

The base revision preserves original bytes; guarded SHA-256 audit:

- `verify-output-queues.py`: `e020739f3c83c03e046431bd6ce120ff63a0552f6e41d578404c87c6299a2867`.
- `verify-queued-fanout.py`: `ddc164617db2b2111803697172c367fb97f894038b72dd86f7ae6ce701e6d89a`.
- `verify-queue-bursts.py`: `4a212a348abdf9c198918be0c6843b41f099d6b6c788b22e697406ede3f644ce`.
- `verify-output-accounting.py`: `81aaeac6e4c6477a0746080a364cebae6f35faaa6526bfa239312a7f96af4119`.
- `verify-delivery-observations.py`: `fffcdb45899943be7f596d698a9bb315314dd74322cab8ab14d7cd362f198615`.
- `verify-output-states.py`: `240734eec58bc3091be65d54f5f0d524a07ac671f976df1208692a5f466ccf69`.
- `verify-diagnostics.py`: `6b7d5f8485b6d3a181f1f88333abc1cc7f8a6063022c0b6dc0082847ced3d6ea`.

No maintained importer remains. Remaining: 35 nonhistorical implementation Python files (26 DiscoveryHost readers, six engineering utilities and three Type Manager fixture utilities), plus one maintained documentation selector. Five historical implementation files and 106 frozen planning/evidence files are preserved.

Final graph: schema 2, 10,657 nodes/51,085 edges, zero diagnostics, build `07de9e7254bfdd64bdd79d52e4e98b14ba4c4b475a01ecbf6d386b7dadc99c88`. DiagnosticsExpectations resolves uniquely; the depth-one related query is fresh/untruncated (three nodes/three traversals, limit ten, proposed edges excluded). Compiler/semantic extractors remain 6/3. This is bounded navigation evidence, not semantic completeness.
