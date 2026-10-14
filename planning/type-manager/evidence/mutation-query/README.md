# Mutation discovery and public-query regressions

14 October 2026. **TM-027 is not qualified.** Full-scope Stryker discovery exposed
coverage gaps and instrumentation limitations. This checkpoint retains both failing
runs, strengthens three public-argument regressions and records the unresolved work.
Production Type Manager source is unchanged.

| Native status | Initial run | After query regressions |
| --- | ---: | ---: |
| Killed | 100 | 103 |
| Survived | 48 | 45 |
| NoCoverage | 40 | 40 |
| CompileError | 238 | 238 |
| Ignored | 57 | 57 |
| Total | 483 | 483 |

The existing repository formula is Killed / (Killed + Timeout + Survived +
NoCoverage). The denominator is 188, with no timeouts. Its score changes from
53.19% to 54.79%; both runs exit 2 because the unchanged break threshold is 90%.
CompileError and Ignored receive no kill credit. Every non-killed mutant remains
unresolved in this checkpoint; no equivalence or unreachable-code disposition is
approved here. The complete native JSON/HTML reports remain unchanged.

All 57 Ignored entries have Stryker's reason `Removed by block already covered
filter`. They are tool-generated exclusions, not configured user exclusions.
All 238 CompileError entries say `Mutant caused compile errors`. The tool logs
show definite-assignment/return errors (CS0165/CS0161) and Safe Mode rollback in
`Capture`, `Load`, `ValidateExecutable` and `ReadAsync`. These exclusions do not
prove the corresponding source behavior is tested. They require individual review
or separate executable mutants; loader/error code is not blanket-exempted.

## Three detected regressions

The same production source gives stable mutant IDs across the two native reports:

- 186 removes the generic extension's null-manager guard. The test now requires
  `ArgumentNullException` with public parameter name `manager`.
- 397 removes `GetDefinedTypes`' null guard. The test now distinguishes its public
  `assembly` parameter from a dictionary's internal key parameter.
- 403 removes validation from `GetImplementations(Type)`. Tests now query a
  successfully published manager with nine unsupported type shapes, foreign-context
  types/closed generic arguments and a dynamic type, rather than testing preparation
  validation alone. Null query input must identify `contractType`.

All three move from Survived to Killed. Separate source-copy builds remove each
guard without Stryker instrumentation and run the unchanged 90-case test assembly.
Each fails its expected canonical test; original TRX reports and mutant DLL hashes
are retained. This guards against treating an instrumentation-only failure as proof
for these three regressions. It does not independently qualify every other kill.

The strengthened tests pass 80/80 against the frozen actual Core on Windows and
native Ubuntu, with no failures/skips. The scoped runtime verifier now explicitly
pins runtime 10.0.12 and disables roll-forward; its generated C# uses LF. Final
formatting passes. The source-module baseline combines 71 unit, nine architecture
and ten existing business cases; a fresh unmutated rebuild after Stryker passes
90/90 under SDK 10.0.401. Strict native TRX validation confirms those counts.
Shared test source hashes match between the frozen-Core and mutation runs. The
unchanged business source also matches its separate frozen-Core checkpoint.

## Reproduce and interpret

`implementations/csharp/eng/prepare-type-manager-mutation.py` creates an empty,
isolated source project from the exact nine TypeManagement files and explicit six
test files. Supply `--repo-root`, an empty `--output-root`, `--candidate-core`,
`--candidate-report`, `--expected-core-sha256` and
`--expected-candidate-report-sha256`. The frozen Core pin is
`d70fcfdc41b6d15829227f1cee60130ddad3f7f82cc11c7a2fcbf8d5f700fb09`;
the trusted producer report pin is
`ce0f08017666e3942d8493b5bb3391abc2403bd7303209afebe5df083f5edaa2`.
Three guards reject wrong Core, report or module provenance before creating output.

From the emitted `source` directory, run its baseline command and verify exactly
90 passing cases, then invoke pinned Stryker 4.16.0 on the explicit .NET 8.0.31
tool host using the emitted configuration. The helper prepares inputs only:
`prepared: true` and `qualified: false` are distinct. Its generated source/project
bytes and configuration values reproduce the executed run. The complete nine-file
mutation scope, Complete mutation level, VSTest runner, single-worker concurrency,
local JSON/HTML reporters and 90% threshold are retained in the canonical config.

The compiled mutation assembly is an isolated TypeManagement source module named
Core for test compatibility; it is not the shipping Core binary. Candidate producer
and source hashes link the experiment to the unchanged module. Runtime package
dependencies are not changed. Existing trusted test-package caches were reused
with sources cleared; generated lock files and output/tool hashes are retained.
No clean-cache or final package qualification is claimed.

The two initial manual baseline commands were invoked from the repository root,
which selected SDK 10.0.402. They remain superseded diagnostic attempts. Stryker
runs from the isolated directory containing the 10.0.401 global.json, and the final
fresh rebuild/test explicitly verifies that SDK. An unsuccessful post-run command
using `dotnet test -t:Rebuild` is retained; it was corrected to a separate build and
test. A generated-line-ending formatting failure is likewise retained. No passing
result is claimed for those failed attempts.

The remaining work includes 45 survivors, 40 uncovered mutants, all tool exclusions,
focused build-helper mutations, loader/discovery process coverage and final artifact
qualification. The original 90% gate and per-mutant review requirements stay intact.
