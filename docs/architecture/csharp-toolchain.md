# C# test-toolchain qualification

The original WP01-S03 smoke qualified the tools against the severity parser before the logging runtime was implemented. Names, aliases and absence/error semantics follow the [user amendments](../../planning/logging/plan-amendments.md). The C# logging runtime is now implemented; the dated migration addendum below records current execution and limits.

## Exact selected inputs

| Component | Selected version / configuration |
| --- | --- |
| SDK / application target | `10.0.401`, scoped `global.json`, roll-forward disabled, `net10.0` |
| Application runtime qualification | `Microsoft.NETCore.App 10.0.12`; explicit host check disables roll-forward |
| Test host runtime | `Microsoft.NETCore.App 10.0.12`; test-only `RuntimeFrameworkVersion` and `RollForward=Disable` prevent silent patch substitution |
| MSTest framework and adapter | `4.5.1`; centrally pinned, private test references |
| Test SDK / VSTest and Microsoft collector packages | `18.10.1`; locked dependency closure |
| Coverage | Microsoft's `Code Coverage` collector URI, native Cobertura, Core module only |
| API compatibility CLI | `Microsoft.DotNet.ApiCompat.Tool 10.0.401`, Microsoft, MIT |
| Mutation CLI | `dotnet-stryker 4.16.0`, Stryker Mutator, Apache-2.0, development only |
| CLI host runtime | `Microsoft.NETCore.App 8.0.31`; both net8.0 tools invoked with `dotnet exec --fx-version 8.0.31 --roll-forward Disable` |

Stryker 4.16.0 is deliberately fixed to the stable version qualified here; a newer available version does not change the manifest or replay. Upgrades require deliberate pin changes and renewed qualification. Stryker's embedded test-platform/tool dependencies are listed separately in the recorded prerequisite inventory; they do not replace or enter Core's runtime graph.

[Toolchain policy](../../implementations/csharp/eng/toolchain.json), the language-local [manifest](../../implementations/csharp/.config/dotnet-tools.json), [central package versions](../../implementations/csharp/Directory.Packages.props), project lockfiles and the separate project/tool NuGet source mappings are reproducibility inputs. The approved graph contains two Core runtime packages, sixteen development/fixture packages and two CLI packages. Embedded CLI libraries are versioned in each tool's `.deps.json` and retained inventory.

## Replay and evidence

Install the exact SDK, both runtime versions, PowerShell 7 and Git. The Windows qualification uses PowerShell 7.6.5. Run from the repository root with HTTPS access to `api.nuget.org`:

```powershell
pwsh -NoProfile -File implementations/csharp/eng/verify-toolchain.ps1
```

Each invocation creates a unique `.artifacts/csharp/toolchain/<UTC-time>-<GUID>/` directory. It snapshots maintained source using Git's tracked/untracked nonignored inventory, establishes fresh `DOTNET_CLI_HOME`, `NUGET_PACKAGES` and HTTP cache directories, and restores exact local tools plus locked projects. A fresh CLI home is necessary because the machine's local-tool resolver can otherwise satisfy tool restore from another cache. Both tool entry points must exist in the new cache. Fresh-cache, telemetry and compiler-isolation settings are passed only to child processes.

The driver runs developer formatting/build/dependency/package checks and the populated unit/architecture suites. It collects parser coverage with the explicitly named Microsoft collector, identifies the native report using the TRX collector attachment, and rejects zero line/branch denominators or rates below 95%/90%. The collector may retain a GUID copy of the same report; it is not counted again.

APICompat compares the actual Core assembly to itself as an invocation check, accepts two matching disposable public fixtures and rejects a fixture with a removed `Parse` member using CP0002. These controls prove the tool is operative; they are not historical Core releases or a completed compatibility suite. Stryker executes the parser tests against genuine source mutations in the isolated copy. It leaves native JSON/HTML reports and logs, followed by source-hash verification and a fresh unmutated build/pack/dependency check.

Retain the entire run directory. It includes `commands.json` with executable arguments, working directories and expected/actual exits; tool version/banner output and runner diagnostics; native TRX/Cobertura/mutation reports; the source snapshot and SHA-256 inventory; `summary.json`; and `evidence-inventory.json` with report hashes. `prerequisites.json` lists exact restored identities, NuGet authors/licences, content hashes, embedded CLI dependency versions, actual OS/architecture and tool prerequisites. Approved project/tool and observed SDK framework-download archives are copied into `retained/sha256/<hash>/` beside tool licence and dependency-manifest files.

The [Windows evidence record](../../planning/logging/evidence/wp01-s03-windows.json) links the retained reports and records the measured denominators. Earlier failed harness-development attempts remain in their separate ignored run directories; they are not passing qualification evidence. The original supplied plans and prior S01/S02 evidence remain unchanged.

## Original qualification limits

This is Windows toolchain qualification for the current parser. Linux, the full logging runtime, three unpopulated functional families, historical replay, release promotion and full mutation scope remain pending. The release verification profile continues to fail.

This smoke retains NuGet/tool bytes locally. SDK/runtime installer distributions are not retained, and a full embedded third-party licence/notices review and immutable offline materialization kit remain WP07-S06 work. Do not describe these local reports as a published release-verification bundle. No Git commit, push or package publication is performed by the command.

Tool behavior and configuration references: [Microsoft coverage configuration](https://github.com/microsoft/codecoverage/blob/main/docs/configuration.md), [Microsoft APICompat CLI](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/global-tool), and [Stryker.NET configuration](https://stryker-mutator.io/docs/stryker-net/configuration/).

The current native exclusions are six `or`-to-`and` mutations that create impossible/unreachable constant patterns and one whole-method block candidate removed by Stryker's block-already-covered filter. Their IDs, replacements, reasons and source/tool identity are recorded in [smoke dispositions](../../implementations/csharp/verification/mutation/smoke-dispositions.json). Replay verifies that review and produces a separate report-hash-bound `mutation-dispositions.json`; the original native report is unchanged. The mutation tool bundles VSTest translation/object-model 18.7.0, distinct from the test projects' pinned 18.10.1 packages.

## Test-host patch selection correction (14 October 2026)

Installing .NET 10.0.13 exposed a gap: generated test configurations requested 10.0.0 and allowed patch updates, although qualification fixtures explicitly require the selected 10.0.12 runtime. The combined Developer run failed 62 such cases. An isolated comparison used identical compiled test/Core DLLs: default selection passed seven and failed nine coordination cases; changing only the test runtime configuration to exact 10.0.12 with roll-forward disabled passed all sixteen.

The existing tests/Directory.Build.targets now applies RuntimeFrameworkVersion=10.0.12 and RollForward=Disable only to IsTestProject projects. The real architecture test host and its generated runtime configuration are checked against eng/toolchain.json. This does not pin shipping Core or application examples, change the selected runtime version, weaken fixture expectations, or relabel the failed run. Requalification evidence remains separate from earlier checkpoints.

## Native migration (18 October 2026)

Both existing PowerShell commands now launch ToolchainQualification / ToolchainInputs in the C# engineering tool. OutputRoot is an optional verify-toolchain.ps1 argument; its default remains a unique artifact directory, and existing outputs are refused. Approved SDK downloads are recorded separately from runtime/PackageReference dependencies; NuGet nuspec filenames retain their archive casing. Original command streams are retained and hashed before diagnostic decoding. Archives and extracted metadata/licence/tool-manifest bytes must agree.

The complete fresh-snapshot Developer checkpoints pass 6,861 Windows / 6,863 Ubuntu cases, including all 59 native policy controls. Both real toolchain smoke commands then stop at the unchanged coverage gate: their 93 parser tests alone do not cover the now much larger Core assembly (Ubuntu 15/3,647 lines and 75/2,779 branches). Subsequent API controls, parser mutation and final rebuild/pack stages are NotRun. The preserved source-bound mutation review also differs from current parser bytes; no review was silently updated.

These are local tooling migration results, not current smoke qualification, complete shipping coverage, full mutation, historical compatibility, offline replay or release publication. [Control map, native receipts and remaining work](../../planning/evidence/corrective-toolchain-provenance-migration-2026-10-18/README.md). The original roadmap remains paused; Claude was not used.
