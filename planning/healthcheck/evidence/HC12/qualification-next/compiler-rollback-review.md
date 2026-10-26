# Bounded diagnosis of automatic03 compiler rollback

The measured **196/217 = 90.3225806451613%** is a passing score for the native tool-classified testable subset. It does **not** establish complete mutation qualification of the selected state source. Retained diagnostics show that the compiler rollback can remove an otherwise ordinary comparison mutant together with incompatible instrumentation in its expression. The 35 selected `CompileError` statuses therefore cannot all be accepted as intrinsically noncompiling source changes without individual reproduction. No native status, denominator, kill credit, equivalence or exclusion is changed by this review.

This was a read-only review. No source was edited, compiled or executed. The finite critical04 receipt is outside this compiler diagnosis; its completed 20-control result does not repair automatic-tool omissions.

## Immutable inputs and native evidence

- Original state source: `.artifacts/healthcheck/qualification-next/final-snapshot-06/source/implementations/csharp/src/CIS.Core/Healthcheck/Internal/State/HealthStateCoordinator.cs`, SHA-256 `7e1462309c085cf952cf0bff034f37eda57108165a1ac7f348fd84142e870c1a`.
- Native debug log: `.artifacts/healthcheck/qualification-next/automatic-state-windows-03/native/logs/log-20261021.txt`, SHA-256 `7169de6c1ed3ebbb4d9e41d08e8a385b8893f4402579df549b243127c0ed3f56`.
- Native report: `.artifacts/healthcheck/qualification-next/automatic-state-windows-03/native/reports/mutation-report.json`, SHA-256 `cf22c436cae175745414562918318b2e3cd23cb5e307e62c4c4cba9a448aae02`.
- Tool/config: Stryker 4.16.0, Complete mutation level, VSTest; config SHA-256 `a9b76e8cb4071df29920d2013b72a5e1e19dc8a55fb9da9a20c7571f9ab9c1b4`; SDK 10.0.401, target runtime 10.0.12, selected tool host 8.0.31. Existing toolchain/score gates are unchanged.

Log line numbers below are one-based in that exact retained log. The `SourceFile(...[start..end))` numbers printed by the compiler are character spans in the instrumented syntax tree, not original production line numbers. They must not be naively interpreted as original source offsets.

## Mutant 2089: concrete evidence of overlapping rollback

The original source at line 99 is:

```csharp
if (active.Count >= Options.MaxMonitors) { throw new HealthcheckCapacityException(Options.MaxMonitors); }
```

`active` is a `Dictionary<Guid, HealthMonitorEntry>` and `HealthcheckOptions.MaxMonitors` is an `int`. Replacing this comparison with `active.Count < Options.MaxMonitors` has ordinary integer operands and does not introduce/remove a local declaration or a pattern variable. It materially reverses capacity admission. This source/type inspection is not a recorded independent compiler run.

| Native log line | Recorded evidence |
| --- | --- |
| 6266 | Creates 2089: `active.Count >= Options.MaxMonitors` → `active.Count < Options.MaxMonitors`. |
| 6269 | Creates 2092 in the same expression: `active.Count` → `active.Sum`. |
| 27776 | Compiler: `Operator '>=' cannot be applied to operands of type 'target-typed conditional expression' and 'int'`, span `[15322..15423)`. |
| 27777 | Compiler: `Type of conditional expression cannot be determined because there is no implicit conversion between 'method group' and 'int'`, nested span `[15323..15398)`. |
| 28177 | Begins `RollBacking mutations from ...HealthStateCoordinator.cs`. |
| 28191–28192 | During that rollback, finds both 2089 (`Equality`) and 2092 (`Linq`) controlled by `ConditionalInstrumentationEngine`. |
| 34584, 34587 | Records each mutant as associated with zero tests. |
| Native JSON | Both 2089 and 2092 finish `CompileError`, with empty `coveredBy`/`killedBy` and reason `Mutant caused compile errors`. |

The compiler complaint is about a target-typed conditional/method-group expression, including the original `>=` operator, rather than the isolated integer `<` replacement. Together with removal of both controlled mutants, this is concrete evidence consistent with collateral rollback from the incompatible `Count`→`Sum` instrumentation. The retained log does not establish that the isolated 2089 source change fails to compile. Its zero-test association follows the rollback; it is not proof that the existing capacity tests would miss the defect.

This is a bounded inference from the native sequence and source types. It does not prove the exact internal rollback algorithm or independently execute 2089, and it does not award a kill or approve an exclusion.

## Similar selected comparisons and other compiler groups

The same pattern appears around three additional ordinary comparisons. Their individual source validity and native behavior still need reproduction rather than a blanket classification.

| Comparison mutant | Nearby LINQ mutant | Recorded log evidence |
| --- | --- | --- |
| 2202: `operations.Count == 0` → `!= 0` | 2203: `operations.Count` → `operations.Sum` | Creation at 6486; rollback finds both at 28203–28204. Compiler at 27840–27841 reports `==` on a target-typed conditional and method-group/int incompatibility. |
| 2204: `retiringCollections.Count == 0` → `!= 0` | 2205: count → sum | Creation at 6488; rollback finds both at 28205–28206; another conditional `==` error follows at 27842. |
| 2221: `entry.Measurements.Count == 32` → `!= 32` | 2222: count → sum | Creation at 6520; rollback finds both at 28207–28208; all finish `CompileError`, and 2221 has zero-test association at 34716. |

Other native diagnostics explain why some generated syntax is suspect without proving every rollback member independently invalid:

- Lines 27365–27367 reject `[default]` collection expressions because the three dictionary types have no single-argument `Add`; selected IDs 2027–2029 are collection-expression mutants.
- Lines 27732–27735 report missing required `HealthApplicationIdentity.Name`/`InstanceId` after initializer removal. Lines 27772–27775 similarly report required `HealthMonitorEntry` fields, and 27875–27876 report required `HealthSnapshotData` fields.
- Lines 27747–27748 report unreachable/impossible typed patterns. Lines 27778–27782 and 27784–27788 show duplicated or unassigned pattern/out variables (`initial`, `condition`, `current`) in instrumented expressions.
- Lines 27867–27874 reject Boolean-returning count predicates after sum substitution; lines 27877–27882 report unassigned snapshot locals following removed blocks.
- A second selected-file rollback appears at 28944 after further diagnostics at 28900–28901 and 28916. These are additional instrumentation attempts, not independent one-mutant compilation receipts.

The report groups the 35 selected compiler-error mutants as four block removals, one Boolean, three collection expressions, two conditionals, six equalities, nine LINQ count-to-sum changes, five logical changes, one logical-not change and four initializer removals. Diagnostic text supports source/tool-bound investigation, not automatic exclusion approval for those groups.

The broader log also contains explicit `Safe Mode!` whole-method rollback after unidentified `CS0165` errors in other files, for example `HealthReportProtocol.Integer`/`Number` at 29410–29416. No corresponding unidentified-error/Safe-Mode event for the selected main `HealthStateCoordinator.cs` was found in this bounded review. Its 2089 evidence is the controlled-expression rollback described above; it should not be mislabeled as a proven whole-method Safe-Mode rollback.

## Qualification meaning and bounded next action

Keep the existing native report immutable: 196 killed, nine survivors, nine timeouts, three uncovered, 35 compiler errors and 68 ignored mutants. The nine timeouts remain in the strict denominator and receive no kill credit. The 68 ignored block removals also receive no kill credit; their reason `Removed by block already covered filter` is a tool optimization, not a semantic-equivalence finding.

The immediate next action is a **single isolated reproduction of 2089** using the existing maintained-source-copy and native build/test entry points: pin the original source and baseline, apply only the exact `<` comparison replacement, freshly build Core/the unit project without Stryker's neighboring `Count`→`Sum` instrumentation, then run the existing actual capacity/Healthcheck native tests. Retain the compiler output, complete native TRX identities/outcomes, and exact source/binary restoration. An individual compiler failure, surviving test, unexpected infrastructure failure or timeout must keep its real status. Nothing in this diagnosis supplies the result in advance.

If that isolated variant compiles, the aggregate `CompileError` label has masked a testable mutant and the selected-source qualification must account for its actual outcome. Repeat this narrowly for 2202, 2204 and 2221 before considering the other ambiguous rollback members. Do not change production source, suppress analyzers, disable whole mutation kinds or call the existing 90.32% a complete selected-source pass to avoid the issue. No new workflow/tool feature is required for the first reproduction. Until the compiler/ignored dispositions and broader module scopes are qualified, report the current result as a passing **tool-classified subset score**, with complete Healthcheck mutation qualification still pending.


## Completed isolated follow-up

The coordinator subsequently compiled all four replacements independently: 2089, 2202, 2204 and 2221. Existing intended tests detected each variant (one capacity case, two disposal rows for each disposal guard, and one measurement-key case). The capacity/key cases raised the specific exceptions at valid public operations; the disposal rows failed their synchronous completion assertion. All four builds succeeded, each test copy contained the freshly mutated Core, and no timeout/infrastructure failure was credited. Exact original Core bytes, all 1,284 source inputs and the 650-case unmutated suite were restored. See [compiler-collateral.json](compiler-collateral.json). These are separate native observations; the original 35 CompileError statuses and automatic score remain unchanged. The other 31 compiler-error cases and 68 ignored block removals still require disposition. The proposed first four reproductions above are therefore completed; complete automatic qualification remains pending.

