# Initial automatic state mutation review

This is a read-only source review of **`automatic-state-windows-01`**, not a review of a later test closure or rerun. No mutation status is changed by this document. No exclusion, suppression, threshold change or kill credit is proposed. The coordinator is adding tests separately; their existence or passing result cannot be inferred from this initial report.

The selected source is `implementations/csharp/src/CIS.Core/Healthcheck/Internal/State/HealthStateCoordinator.cs` in `.artifacts/healthcheck/qualification-next/linux-snapshot-02/source/`. Its SHA-256 is `7e1462309c085cf952cf0bff034f37eda57108165a1ac7f348fd84142e870c1a`. All line numbers below refer to that frozen source, as embedded in the initial native report. The snapshot's name does not establish Linux execution: this Stryker observation executed on Windows.

| Retained input/result | SHA-256 |
| --- | --- |
| `automatic-state-windows-01/native/reports/mutation-report.json` | `a15f85c1fec374e2a45d045eae6ff2c17248c09df2491dff035eabcba2460a23` |
| `automatic-state-windows-01/config.json` | `a9b76e8cb4071df29920d2013b72a5e1e19dc8a55fb9da9a20c7571f9ab9c1b4` |
| `automatic-state-windows-01/native.log` | `b0bd79e8118e5bd015b8a0d74e01750edbfc80f8ee3206396ba87436b7484d1e` |
| Original `process-results.json` | `5b6946e167b5eb890e95287346298ee60a99e82f9599b116266a754e221229b8` |
| Corrective `restored-02-results.json` | `e87e4660e2bba80a4c04e50609d11e608b24b83304193caf5b817061b71fd2fd` |

The invocation records Stryker `4.16.0` binary SHA-256 `1d5fbd3df14f3ab0425421c597a36a5cbdd474ad9804a0bd4e212e5b0320eba9`. The config selects this one state source at Complete level, the actual Healthcheck unit closure and its serialization control, VSTest, two sessions, and the unchanged 90% threshold. The baseline executed 585 passing cases. This is neither complete automatic Healthcheck mutation coverage nor all-Core/release qualification.

## Native result and restoration limits

| Native selected-source status | Count | Review treatment |
| --- | ---: | --- |
| Killed | 170 | Retain actual native status. |
| Survived | 32 | Unqualified; the analysis below grants no kill credit. |
| Timeout | 11 | No kill credit. |
| NoCoverage | 4 | Unqualified; unreachable/dominated interpretations remain separate from tool status. |
| CompileError | 35 | No kill credit and no automatic equivalence claim. |
| Ignored | 68 | No kill credit; native reason is the block filter. |
| Total selected-source mutants | 320 | Initial selection only. |

The tool reported **83.41%**, effectively `(170 + 11) / 217`. The repository's conservative score is **`170 / (170 + 32 + 11 + 4) = 78.341%`**. Both fail 90%; a timeout is not an assertion kill. This arithmetic does not silently remove survivors that this review considers dominated.

The 68 ignored entries all say `Removed by block already covered filter`; that is a tool filter reason, not proof that native tests covered or killed each omitted block. All 35 selected compile-error entries say `Mutant caused compile errors`. Some replacements are intrinsically ill-typed, such as dictionary collection `[default]`, missing required initializers, invalid type-pattern conjunctions or property-to-method-group `Count`/`Sum` replacements. Other entries, such as `2089` (`active.Count < Options.MaxMonitors`), are syntactically plausible on their own. The retained log shows broader safe-mode rollback while instrumenting Core. It does not provide an isolated compiler diagnosis for every selected ID. Therefore this review does not call all 35 intrinsically noncompiling, equivalent, killed or otherwise qualified. The log's larger all-Core compile-error/filter totals are not this selected-source inventory.

The original post-run build failed: `process-results.json` records build exit `1`, native exit `2`, and restored-test exit `0`. `restored-build.log` selected SDK `11.0.100-rc.2.26504.105` from the wrong working directory and failed `CS1705` when the net10 build helper referenced SDK11 MSBuild assemblies. The original passing no-build tests alone did not prove a successful fresh restored build.

The separately retained corrective `restored-02-*` attempt explicitly used the language working directory and observed SDK `10.0.401`, build exit `0` with zero warnings/errors, then 585 passing serialized focused cases with test exit `0`. This provides actual corrective restoration evidence for the original clone. It does not erase the first failure, change native mutant statuses, or qualify a future edited source/test candidate.

## Survivors that expose public or lifecycle detector gaps

These are gaps in the initial tests' ability to distinguish a deliberately broken implementation. They are not claims that the unmutated implementation is defective.

| ID | Line | Change and source-bound consequence |
| --- | ---: | --- |
| `2054` | 56 | `allowControls: false` becomes `true` in application identity validation. A caller-supplied otherwise valid identity containing a control in a bounded text field is accepted by the mutant. This differs at the public service constructor; the selected closure lacks that control-character boundary assertion. |
| `2075` | 71 | Numeric unit validation changes `allowControls: true` to `false`. The source deliberately accepts a nonblank bounded unit containing mixed text/control scalars. That accepted-input boundary is not observed by the initial plain-unit tests. This review does not change the selected validation policy. |
| `2085` | 90 | Removing the create-path `SweepDead` leaves a collected monitor occupying capacity until another sweep path, such as snapshot capture. With capacity one, abandon the first handle, establish collection, then create the replacement without an intervening snapshot: baseline admission succeeds and the mutant can reject capacity. |
| `2087` | 94 | Removing create-path `Dispatch` loses effects queued by `SweepDead`. An externally retained monitor-disposal task can remain pending even after the dead handle is swept. A membership-only snapshot assertion does not establish dispatch/settlement of that public barrier. |
| `2109` | 120 | The condition's initial `LastObservation` always becomes null. Initial explicit healthy/unhealthy condition state is an actual observation at registration; the detached public timestamp differs immediately, while outcome policies should still begin pending. |
| `2111` | 131 | Removing registration `revision++` allows monitor membership to change without advancing the public revision. A new nonnegative version must be observed independently of the monitor values. |
| `2127` | 150 | Removing admission checking from `ClaimCollectorRunner` allows a first runner to be constructed after service stopping/disposal when `runnerClaimed` is still false. The existing second-runner test is dominated by the separate claimed-runner guard and cannot prove this terminal admission boundary. |
| `2139`, `2140` | 161 | Startup revision increment is removed or decremented. Successful explicit startup must advance revision once; repeating startup must be idempotent. Testing readiness alone misses both changes. |
| `2150`, `2151` | 176 | Stopping revision increment is removed or decremented. First stopping must advance revision and repeated stopping must preserve it; readiness alone is insufficient. |
| `2189` | 219 | Removing `RetireBindingsLocked` leaves detached binding registry entries after monitor withdrawal. `IsBindingActive` still rejects reporting through the withdrawn entry, so a Retired-report assertion alone misses registry retirement. The baseline removes the registration, allowing a new supported monitor/binding lifetime to use the released registration identity; the mutant retains the duplicate-registration blocker. It also lacks the baseline's eager detached registry cleanup. |
| `2239` | 254 | The diagnostic always becomes the supplied diagnostic. `Clear()` supplies null and therefore cannot distinguish it. A healthy numeric observation or compatible clear collector result carrying a nonnull detached reason does: baseline clears current unhealthy diagnostic state, while the mutant retains it. |
| `2283` | 317 | Changing `< long.MaxValue` to `<=` permits another increment at the cap, wrapping in the ordinary unchecked build. The genuine counter contract requires saturation. A seeded technical state boundary followed by real reporting can observe the public counter/flag; no claim of executing `long.MaxValue` operations is needed. |
| `2346` | 427 | Removing disposal of queued cancellation sources is not proved equivalent. Completion/removal of the operation alone does not observe disposal of its owned resource. A collector can allocate and retain the supplied token's wait handle; after actual settlement, baseline source disposal closes that handle. The initial lifecycle closure does not establish this resource-disposal fact. This is a cleanup detector gap, not an assertion that health policy output alone must change. |

## Survivors dominated by reachable state or policy invariants

These interpretations apply only to the frozen production state machine and public call paths. They do not authorize suppressing a mutator, editing production to make a score, removing IDs from the denominator or replacing a rerun with a static opinion.

| ID | Line | Reachability reasoning |
| --- | ---: | --- |
| `2122` | 143 | Replacing `disposed || entry.Withdrawn` with `disposed && entry.Withdrawn` is dominated by the following dictionary/reference checks. Withdrawal sets `Withdrawn` and removes that exact entry atomically under the same gate. Disposal withdraws all active entries and terminal admission prevents reactivation. A withdrawn/disposed old entry consequently still fails `active.TryGetValue`; no public path reinserts that entry. An artificially corrupted registry is a different premise. |
| `2180`, `2181`, `2182`, `2184`, `2185` | 216 | These change/omit the generation increment during terminal **monitor withdrawal**, including its max-value guard. The entry is already marked withdrawn and removed before this increment. `IsCurrent` requires both an open attachment and a nonwithdrawn entry before its generation comparison. Public monitor instances are never reactivated or reused. Attached collection closure also closes its own identity, so stale work remains retired without this particular terminal increment. This does not establish that the separate generation logic for live collection detach/re-register is redundant; that logic is outside this selected line. |
| `2191` | 220 | The removed cancellation loop follows `CloseAttachmentLocked` for the current attachment. That earlier close already requests cancellation for its operation. Any remaining older retiring attachment was already closed and requested its cancellation; `RequestCancellation` itself is idempotent. Every operation originates from such an attachment. Thus the loop repeats already established cancellation under the frozen ownership model. A newly introduced operation source would require reassessment. |
| `2234` | 253 | `availability && flagged` becomes `availability || flagged` before clearing `HasUsableData`. The only public availability path into `SetCondition` is invalidation, which always supplies flagged=true; publication/refresh use `SetOutcome`. Nonavailability policies do not use `HasUsableData` to compute their policy state. Therefore the additional writes are either the same availability invalidation or to a field irrelevant to that policy. There is no public availability-clear path supplying false here. |

## Diagnostic-text survivors

IDs **`2042`, `2057`, `2063`, `2068`, `2076`, `2118`, `2131`, `2215`, `2225`** empty exception messages at lines **36, 57, 67, 70, 72, 138, 151, 234, 239**, respectively. The initial tests observe rejection/type and sometimes parameter identity, rather than these exact explanation strings. The message remains observably different through the public exception, so these are not literal runtime-equivalence claims. No frozen requirement for each exact prose string was established in this review, and none changes the actual validation decision or policy state. They remain nine native survivors and receive no score adjustment.

The three survivor groups above account for all **32** selected native survivors: 15 public/lifecycle detector gaps, eight source-bound dominated candidates, and nine diagnostic-text differences.

## NoCoverage and timeout observations

| ID | Line | Interpretation, while retaining native NoCoverage |
| --- | ---: | --- |
| `2079` | 76 | Unsupported base-policy fallback. Although the base options type is public/abstract, public factory methods accept the four supported sealed concrete option types; execution binding policies are likewise concretely typed. An internally manufactured unsupported policy can exercise defensive code, but there is no demonstrated public route in the frozen API. |
| `2097` | 112 | The `CreateLocked` fallback is dominated by `ValidatePolicy` on create and `ValidateBindingRequest` on binding-created monitors. Supported option types are sealed with immutable initialization, so no accepted policy can become a different runtime type between validation and dispatch. |
| `2258` | 275 | Unknown numeric comparison fallback. Policy validation requires a defined enum, all four defined comparisons are handled, and comparison is copied into an init-only entry field. Public observations cannot replace it with an unknown value. |
| `2290` | 317 | Setting `Saturated` to false is a genuine missing live counter-exhaustion observation. Formatter tests using an already constructed max-valued DTO do not execute `Increment`. It should be assessed with the same real-reporting/seeded-boundary approach as survivor `2283`. |

The 11 native timeouts are **`2157`, `2160`, `2162`, `2168`, `2206`, `2209`, `2292`, `2315`, `2340`, `2342`, `2344`**. Their affected areas include disposal admission/settlement, removed dispatch/cancellation/completion, and revision decrement. A deliberately broken completion path can plausibly strand a test or cleanup barrier, but a plausible cause is not a passing assertion kill. The report provides timeout status, not a successful native assertion diagnosis for those mutants. Keep each timeout in the conservative denominator and examine it in any later closure/rerun without granting kill credit in advance.

This review changes only this note. It leaves the initial native report immutable and the automatic 90% gate failed. Complete Healthcheck source selection, source/tool integrity, valid native restoration, other platform lanes, external integrations and release qualification remain separately evidenced requirements.
