# Type Manager implementation status

14 October 2026. In progress, in the same CIS.Core assembly and solution as logging.
The supplied completion pack remains unchanged design input under `sources/`;
it does not approve publication or establish implementation evidence.

## Implemented in the repository

The `CIS.TypeManagement` module now provides asynchronous preparation, strict
schema-1 inventory validation, default-context loading with identity checks,
one complete type catalog, exact assembly-qualified lookup, CLR assignability
queries, immutable results, captured inputs and a synchronized query cache.
Preparation diagnostics retain selected/reused/loaded identities, counts and
elapsed time. Required loading/discovery failures prevent publication and preserve
inner/loader errors. No runtime dependencies were added to Core.

Argument errors are synchronous; cancellation and operational failures belong to
the returned task. Relative alternate manifest paths resolve from
`AppContext.BaseDirectory`. UTF-8 BOMs and additive fields are accepted, while
ambiguous duplicate keys and invalid UTF-8 are rejected. Invalid path diagnostics
can preserve the submitted relative path; validated paths are absolute.

The source build integration uses pinned SDK selections to write automatic build
and folder-publish inventories. It supports native/explicit win-x64 and linux-x64
framework-dependent, untrimmed output, excludes native/reference/resource assets,
and fails unsupported deployment modes. Its metadata-only helper has SDK
references and no NuGet dependencies. Package delivery includes it as build
tooling; it is excluded from application runtime dependencies and deployment.

Real process fixtures cover broken base dependencies, wrong file/loaded/runtime
binding versions, both reference-attribute metadata forms, corrupt executables,
forwarding and same-name types from different assemblies. The ordinary generated
host now actually prepares Core before its container boundary and discovers an
initially unused private-constructor inherited implementation. A separate process
cancels during the real library load and proves no manager/container is published.

The initial reference DLL, original compiled consumer, matching source and hashes
are retained under `implementations/csharp/verification/contracts/type-manager`.
They are an unpublished development baseline, not a historical released API.

## Roadmap state

No full milestone or release gate is declared complete. The following separates
implemented behaviour from outstanding qualification; no original checkbox is
silently marked complete by a prototype or an uncommitted test result.

| Tasks | Current state and outstanding work |
| --- | --- |
| TM-001 | Repository boundaries inspected: one CIS.Core net10.0 assembly, SDK 10.0.401, CIS.TypeManagement namespace, existing approved Microsoft runtime closure preserved. |
| TM-002 | Public runtime contracts and frozen initial reference/source/compiled-consumer baseline present; API/source/binary checks pass against actual Core. Targeted runtime review closure is retained; this is an unpublished development baseline. |
| TM-003 | Real inventory and loader fixtures present; fresh-process ordinary unused discovery and failures exercised. |
| TM-004–TM-005 | Source SDK selection and manifest generation implemented; native/RID matrices pass on Windows/Linux. Actual Core package delivery is implemented; its separate checkpoint is recorded below. |
| TM-006 | Strict reader and captured inputs promoted into Core; focused unit and process checks pass. |
| TM-007 | Reviewed Windows source lifecycle has 32 passing checks. The exact-package verifier now passes 57 checks per OS, including reference removal/readdition with stale assets, rename, clean/rebuild and independent design-time controls for console/Web SDK hosts. CLI folder publication is covered; Visual Studio/PublishUrl copying is not claimed. Final release consolidation remains open. |
| TM-008 | M1 gate remains open pending complete supported-profile inventory qualification and setup reconciliation; reviewed source-mode checkpoint evidence is retained. |
| TM-009–TM-010 | Selected maps/default-context loading implemented; identity/reuse/failure cases pass on both platforms. |
| TM-011 | Complete definition/relationship enumeration implemented; forwarding/nonpublic/open-definition and side-effect tests pass. Test-only source instrumentation records per-selection counts and rejects double/query enumeration mutants. |
| TM-012 | All five failure kinds exercised with contextual diagnostics, including a locationless host. Final artifact replay and milestone reconciliation remain. |
| TM-013 | Pre-work, real mid-load and post-success cancellation cases pass. A separate source-instrumented probe verifies deterministic final-publication cancellation and its negative control. |
| TM-014 | Expanded loader/publication acceptance is executed, including both conflict orders and a deterministic final-publication source probe. Final milestone reconciliation remains open; instrumentation is separate from the unmodified candidate. |
| TM-015–TM-019 | Protected identity maps, assignability matcher, exact lookup, cache/concurrency and precomputed queries implemented with focused tests. |
| TM-020 | Generic equivalence, concurrent awaiters, immutable membership and protected collection replacement pass. Three culture/reversed-valid-inventory cases now exercise prepared/lazy results, explicit global/per-assembly ordering, identity and concurrent cached reads; the expanded 80-case native suite passes on both OSs. Enumeration counters and three targeted mutants have separate source-instrumented evidence. Final milestone review remains open. |
| TM-021 | Preparation report implemented and reconciled in tests. Runnable hosts display counts, elapsed time and identities; broken-deployment examples preserve the original error and selected file path before startup. |
| TM-022 | Minimal plain-.NET console example builds/runs from documented commands, discovers the initially unused inherited/nonpublic candidate, and reaches its application boundary only after preparation. |
| TM-023–TM-024 | Separate Web example constructs its builder/container after preparation, registers the exact manager instance and proves reuse across root, scopes and actual HTTP requests. Configuration boundary documented without adding a public Configuration contract or object lifetime implementation. |
| TM-025 | Source and exact-package examples pass 66 fresh-process/layout checks on each supported OS, including Debug/Release, folder publish, publish without building, three broken-deployment cases per layout, later-load fixed membership and HTTP scoped lifetime. The 26-project solution and 11 targeted framework-policy checks pass on Windows. Formal milestone and final release-candidate consolidation remain open. |
| TM-026 | A01–A30 mapping is recorded in `acceptance-evidence.md`, with executed checks and explicit gaps. The previously empty business project now has ten startup/consumer stories, all passing against the same frozen Core on Windows and native Ubuntu. Full acceptance/profile reconciliation and final artifact replay remain. |
| TM-027 | Full nine-file Stryker discovery now runs: 103 killed, 45 survived, 40 uncovered, 238 compile errors and 57 native-filter exclusions. The unchanged 90% gate fails at 54.79%. Three public-query guard regressions are newly killed and independently confirmed with standalone mutant DLLs. All remaining mutants/exclusions, build-helper scope and final artifact qualification remain open. |
| TM-028 | Complete architecture qualification remains; initial build/dependency and targeted mutation controls exist. |
| TM-029 | Development behavior/schema compatibility checkpoint passes 86 checks per OS: original API/binary/source replay, 22 frozen behavior fixtures, exact original-API consumer reproduction and targeted API/binary/source/behavior negatives. Three provenance guards reject mismatched inputs. Final exact-package replay remains; no released historical baseline is claimed. |
| TM-030 | Development performance observations are recorded for 334/4,366-type catalogs: 56 fresh processes per OS, 48 measured per OS, exact deployment replay, same-work deferred control, allocations and whole-process retained-memory deltas. Five artifact/timeout controls pass; unchanged-source enumeration evidence is linked separately. Final clean-package replay remains. |
| TM-031 | Final maintainability review and the six-family gate remain. |
| TM-032 | Implemented optional SDK assets in the same CIS.Core package: first restore sees RID selection, the runtime reference matches the SDK version, and no build target performs a second restore. Windows and Ubuntu each pass 35 checks against identical package/verifier bytes; 34 architecture/dependency cases pass and closure review returned PASS. The final Linux JSON report is retained; its temporary detailed logs were unavailable afterward, as recorded in the checkpoint. Final release qualification remains open. The earlier second-restore experiment is not adopted. |
| TM-033–TM-036 | Final documentation, exact-artifact verification bundle, clean package consumer smoke tests and handoff remain. |

## Executed evidence and limits

The [mutation/query checkpoint](evidence/mutation-query/README.md) retains two
failing full-module Stryker reports and the complete unresolved-mutant inventory.
Published-query argument tests now detect three previously surviving guard removals.
The strengthened 80-case frozen-Core suite passes on both OSs; the separate source
module passes 90 cases after a fresh unmutated rebuild. Neither native exclusions
nor targeted kills are used to claim that the full mutation family passes.

The [business stories checkpoint](evidence/business-stories/README.md) adds ten
native cases per OS for preparation before application startup, application-owned
activation, failures, pre-cancellation, prepared-catalog reuse and independent
repair. The scoped verifier checks observed runtime/Core identity and two provenance
controls. These reuse an already-loaded host; the earlier fresh-process and actual
DI/HTTP evidence remains separate. No combined working-tree build is claimed here.

The [performance checkpoint](evidence/performance/README.md) retains native
Windows/Ubuntu observations, 20 raw query batches per metric/process, runtime/JIT
metadata, exact runnable deployments and [numeric results](evidence/performance/observations.md).
Both modes execute the same discovery work, placed at bootstrap or first use.
No speedup, steady JIT tier, cold disk cache, isolated manager size or timing
threshold is claimed. It uses the frozen `d70fcf…` Core, separate from the later
combined Developer build and future final package. No production code changed.

The [behavior/schema compatibility checkpoint](evidence/compatibility-behavior/README.md)
adds an unpublished, authored behavior baseline while preserving all six original
API-baseline files. Windows and Ubuntu each pass 86 checks against the same frozen
`d70fcf…` Core candidate. Both reproduce the frozen behavior consumer byte for byte
from source against the original API reference. Targeted source mutants have
positive twins and must produce their specified regression; they do not complete
the full mutation family or replace final release qualification.

Combined Developer run `ce9cc2bf66cd49fdac044aed2e5c70dc` subsequently passed all
14 stages and 4,998 tests (3,259 unit, 1,276 integration, 463 architecture), with
no failures/skips and zero build warnings/errors. Its retained evidence is under
the compatibility checkpoint's `developer/` directory. That working-tree build
includes coordinated logging changes and remains distinct from frozen Core replay.

The [package lifecycle checkpoint](evidence/package-lifecycle/README.md) adds 22
checks per OS to the existing 35 package cases using the unchanged development
package. [Query ordering](evidence/query-ordering/README.md) adds three runtime
cases against the separate frozen `d70fcf…` Core candidate; its 80 native cases
pass on both OSs. Neither result silently replaces the final release bundle.

The logging thread subsequently corrected test-host patch selection and retained
a passing combined Developer run `61dd60f6ea324dd1aecfa57297057b22`: 3,256 unit,
1,276 integration and 463 architecture results, no failures/skips. Its local
evidence is `.artifacts/csharp/tasks/wp05-s10-t01/runtime-drift/corrected-developer/`.
That run predates the three new ordering cases. The earlier failed combined run
remains retained unchanged under host integration; no production Type Manager
fix was needed for that failure.

The [host integration checkpoint](evidence/host-integration/README.md) records the
console/Web examples against the unchanged package SHA-256
`d3249a77c8ee4c504e87b9c3f52b3be691430bcca47a7bc70468757dc9452ea9`
and separate native source builds. Package consumers use Core
`abb66e6159e3fd111f341e32c4a61cec12c2bcde738510be9229e5fe76155025`;
they must not be conflated with the runtime-acceptance candidate below. Only the
registered Web example is allowed the implicit ASP.NET shared framework. Core's
runtime package inventory and production source are unchanged by this checkpoint.

The expanded [runtime acceptance checkpoint](evidence/runtime-acceptance/README.md)
uses Core SHA-256 `d70fcfdc41b6d15829227f1cee60130ddad3f7f82cc11c7a2fcbf8d5f700fb09`:
77 native cases (68 unit plus 9 runtime architecture) and 17 process checks pass
on both Windows and Ubuntu. A separate source-instrumented build passes five
checks per platform, including three targeted mutant rejections. Two provenance
controls reject mismatched Core bytes and source records before instrumentation.
The producer forces recompilation. Its source hashes cover C# files and five
named project/config files, not the complete transitive MSBuild input closure.
No production Type Manager code changed for this acceptance checkpoint.

See `verification.md` and `evidence/runtime-closure/` for exact reports and commands.
The earlier frozen runtime candidate Core SHA-256 is `53619d263b200fa3d53bb5d9455dfa599110ce83ad30b83448b480bd542e1cb3`. Its result: 63 Core unit cases plus 9 runtime architecture
cases (72 total) pass on both Windows and Ubuntu. Twelve runtime process cases
and two generator rejection cases (14 total) pass on each platform with verified
identical fixture/Core bytes. ApiCompat, original binary replay and historical-
source rebuild/run pass against that DLL. These are source-development results, not
package release or all-six-family qualification.

The initial Claude inventory/runtime reviews found actionable defects. Runtime
fixes and new regressions are present; final targeted Claude review returned PASS.
An incorrect review assumption about a collectible resolver fixture was caught
by a negative control; the fixture was strengthened and requalified on both OSs. The user
has given standing authorization to proceed with required read-only reviews
without further permission prompts. Reviews exclude unrelated logging source.

## Coordination and shared state

Logging chat `01a12085-7b0b-7e10-89ef-b152f9a4e4ba` authorized a shared source window
through runtime promotion, then resumed logging-only implementation. Type Manager
owns its module, dedicated tests/fixtures/build assets, compatibility baseline,
verification scripts and planning files. Solution/dependency registrations and the
UnitTests unsafe setting (only to test function-pointer rejection) are coordinated.
Logging's planning, conformance and Core changes are excluded from Type Manager
staging. Root Developer reports and graph refreshes are coordinated separately.

This implementation checkpoint does not authorize publication. Coordinated Windows Developer
run `06673e19d022465091e1f241285f454f` exited 0: all 3,251 unit, 1,276 integration
and 452 architecture tests passed with no skips. Restore, dependency boundaries,
formatting, build, contract/consumer checks, pack and package boundaries also passed;
the build reported zero warnings and errors. See `evidence/combined-developer/`.
This combined result includes logging's new shutdown tests and is separate from
the frozen Type Manager candidate evidence above. It is not a release gate result.
Graph extraction remains partial; its recorded diagnostics and source freshness
must be considered separately from executed verification.

The latest fixes validate every selected file before loading any new assembly,
reject dynamic and custom-context bind results, handle query-cache reentrancy,
and align report classification and host/path diagnostics. Regression controls
reject prior DLLs for early loading, dynamic substitution/reporting and foreign
resolver acceptance. These controls supplement the still-incomplete mutation gate.

The subsequent inventory review found and corrected disabled package-copy handling,
relative publish-directory resolution and verification false positives. The
32-case Windows lifecycle and a seeded stale-manifest negative control pass in an
isolated snapshot. Ubuntu passes the nine-case Web SDK CLI matrix plus separate
default-relative-publish and disabled-copy checks. The targeted follow-up review
returned PASS; see `evidence/inventory-review-fixes/`. These build-only changes
postdate the combined Developer result and have separate recorded qualification.
An exported Git index without uncommitted logging changes also builds the complete
solution with zero warnings/errors and passes all 63 Type Manager unit plus 14
Type Manager architecture cases. Frozen baseline hashes survive that checkout.
