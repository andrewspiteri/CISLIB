# Current ASP.NET omissions versus historical replay controls

This is a read-only source/report comparison. It changes no native status,
compiler disposition, score, source file or old receipt. Historical 77-control
replay evidence cannot be presented as current omission qualification.

| Input | Historical `.artifacts/hcc/a1` | Current `.artifacts/hcg/a1` |
| --- | --- | --- |
| Report under `native/reports/mutation-report.json`, SHA-256 | `afcc1871cc1d4e12b74a352809b9e75c054c2fa3b9d13e61f1c166a900eb800e` | `5a109f16c2c355a571d0efb38fd59a297f2077e3f9a03d1eefb53fd074f7a750` |
| Selected ASP.NET source, SHA-256 | `d81256db1acc93e948d72bdc7c6df713cc6f9e436b02d3b9f3b22a8d7f66efa4` | `b4611cfccb7aafb377b7fbb663b81dbc6c58cb59af66a73b44fa85a1fd096d0e` |
| All selected definitions | 101 | 94 |
| Native Killed / Survived / Timeout / NoCoverage | 21 / 3 / 0 / 0 | 16 / 2 / 0 / 0 |
| Native CompileError / Ignored | 72 / 5 | 72 / 4 |
| Omission controls requiring independent review | 77 | **76** |

The selected file is
`Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs`, relative to
Core. Both reports' source text uses LF; raw UTF-8 and normalized-LF hashes
coincide. The current source incorporates segment-aware routing and direct
response-task forwarding. Those changes are absent from the historical source.

## Exact definition comparison

Current **CompileError IDs 205–276** correspond to historical **210–281** by a
five-ID shift. All 72 pairs match exactly in original start/end location,
original source slice, replacement, mutator name and status. They remain at
`HandleAsync` lines 41–94. There are zero mismatched definitions in those 72
pairs. This proves source-definition correspondence; it does not execute a
compiler or establish a test outcome under the changed surrounding source,
Core binary, fixture and detector harness.

The ignored definitions differ materially:

| Historical ID | Current ID | Comparison |
| --- | --- | --- |
| 191 | 191 | Whole registration-method block: same location/operator/replacement, changed original slice due routing simplification |
| 198 | 198 | Authorization-rejection block: exact location/slice/operator/replacement match |
| 201 | 201 | Middleware callback block: same location/operator/replacement, changed original slice |
| 282 | 277 | Whole response-writer block: changed slice and end location, and the surrounding method is now non-async task forwarding |
| 289 | None | The original conditional await-body block no longer exists |

Thus 73 current omission definitions have exact historical counterparts (72
compiler labels plus ignored 198), three current definitions need changed
patches, and one historical ignored definition is retired. Matching native IDs
alone is unsafe: 191 and 201 retain their IDs while their source slices change.
No retired mutation earns kill credit, and old compiler rejection is not
silently transferred to a new method body or source context.

## Required replay expansion and review

The current standalone verifier intentionally pins the historical report,
historical source hash, 101-status inventory and exact 77 IDs. It rejects this
current report. Its existing historical overlay must remain explicit, including
when executed with a newer 64-case detector harness. An older running 63-case
driver is another retained harness context and must not be relabelled.

A new explicitly selected, immutable report/source profile is required for
current replay; do not loosen those checks to accept arbitrary reports or reuse
old native IDs. If this current report is the chosen control source, pin its
exact two hashes above, all 94 native definitions and statuses, and required
omissions **205–276 plus 191, 198, 201 and 277**. A fresh automatic report after
the added HEAD detector may have different outcomes: inspect and pin its actual
bytes/statuses/definitions before adopting it, rather than predicting a score.

Reconstruct the full chosen report source as the report-source baseline in an
isolated frozen current input copy, retain the distinct canonical baseline,
and independently apply all 76 exact source replacements. Reconcile full
current HTTP/scheduling cases, including the required in-process HEAD case, and
retain actual compiler diagnostics, source/build/deployed Core pins, source-
replay child audits, native assertions and historical/current restoration
proofs. Existing manual child-control records can represent the new exact
report/source/IDs; shared child helpers need no source-profile relaxation.
The standalone verifier's report/source constants, expected status inventory,
exact ID set, full-selection counts and summary labels require coordinated
profile-specific changes after the native freeze.

All 72 current compiler labels still require actual independent target-source
compiler reproduction; a method-wide Safe Mode rollback is not that proof.
The four current ignored definitions also require actual independent builds and
native detector outcomes. A historical compilation/test result can guide
review, but provides no current Core/harness native credit. Unknown failure,
timeout, incomplete case inventory or incomplete real child lifecycle remains
unresolved. No historical raw Killed result, especially from an invalid child
audit, is imported into a passing current score. Fresh automatic execution,
source replay/dispositions, and broader Healthcheck/Release gates stay separate.
