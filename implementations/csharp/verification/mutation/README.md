# Mutation verification

Mutation is the sixth family. Stryker is a pinned development CLI restored through the language-local tool manifest and dedicated tool source mapping. Core, its lockfile and its packed dependencies contain no Stryker reference.

Run from the repository root:

```powershell
pwsh -File implementations/csharp/eng/verify-toolchain.ps1
```

The driver copies maintained source into a unique `.artifacts/csharp/toolchain/<run>/source` directory and uses fresh CLI and NuGet caches. Stryker **4.16.0** runs on the explicitly selected .NET **8.0.31** tool host with roll-forward disabled, mutates the genuine Core severity parser with the `Complete` mutation level, and runs its MSTest cases through **VSTest** against `net10.0`. Local JSON/HTML reporters are enabled; no dashboard reporter is selected.

[stryker-config.json](stryker-config.json) is the retained smoke scope, not a future full-release mutation matrix. The parser run remains a smoke scope. Core now contains logging/provider and other module implementations; their full shipping scope and materially different targets must be covered by the WP07 release matrix.

The driver rejects zero valid mutants and checks `Killed / (Killed + Timeout + Survived + NoCoverage)` at 90% or above. Every testable mutant must be killed or explicitly diagnosed; native noncompiling/ignored mutants require a source/tool-bound review in `smoke-dispositions.json`. A changed source hash, tool pin or unexpected exclusion fails qualification. No excluded mutant receives kill credit. Native reports remain immutable. Source hashes must match before/after mutation, followed by a fresh unmutated build, pack and shipping dependency check in the isolated copy.

Read [toolchain qualification](../../../../docs/architecture/csharp-toolchain.md) for reports, prerequisites and retention limits. Retain the complete local run directory; generated caches and packages are ignored by Git.

## Embedded Microsoft adapter

`microsoft-logging-config.json` selects all three `Logging/MicrosoftLogger/*.cs` source files within CIS.Core at Complete mutation level and runs the actual provider unit tests. `eng/verify-microsoft-logging.ps1 -Mode Mutation` executes it in a maintained-source copy beneath the ignored .artifacts folder, with pinned tool/runtime and local JSON/HTML output. Integration child processes are measured separately by coverage; this invocation does not claim to inject mutants into those children. Native non-killed/ignored/noncompiling observations keep their statuses, without kill credit or automatic exclusions. A below90% score stays a failed mutation gate. Full source-bound dispositions, gap fixes and both recorded platform lanes remain WP07 work.
