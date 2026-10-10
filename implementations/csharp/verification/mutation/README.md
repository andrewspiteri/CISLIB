# Mutation verification

Mutation is the sixth family. Stryker is a pinned development CLI restored through the language-local tool manifest and dedicated tool source mapping. Core, its lockfile and its packed dependencies contain no Stryker reference.

Run from the repository root:

```powershell
pwsh -File implementations/csharp/eng/verify-toolchain.ps1
```

The driver copies maintained source into a unique `.artifacts/csharp/toolchain/<run>/source` directory and uses fresh CLI and NuGet caches. Stryker **4.16.0** runs on the explicitly selected .NET **8.0.31** tool host with roll-forward disabled, mutates the genuine Core severity parser with the `Complete` mutation level, and runs its MSTest cases through **VSTest** against `net10.0`. Local JSON/HTML reporters are enabled; no dashboard reporter is selected.

[stryker-config.json](stryker-config.json) is the retained smoke scope, not a future full-release mutation matrix. The parser is currently Core's only handwritten executable behavior. Extend the release matrix when real Core/provider implementations and materially different targets exist, under WP07-S03.

The driver rejects zero valid mutants and checks `Killed / (Killed + Timeout + Survived + NoCoverage)` at 90% or above. Every testable mutant must be killed or explicitly diagnosed; native noncompiling/ignored mutants require a source/tool-bound review in `smoke-dispositions.json`. A changed source hash, tool pin or unexpected exclusion fails qualification. No excluded mutant receives kill credit. Native reports remain immutable. Source hashes must match before/after mutation, followed by a fresh unmutated build, pack and shipping dependency check in the isolated copy.

Read [toolchain qualification](../../../../docs/architecture/csharp-toolchain.md) for reports, prerequisites and retention limits. Retain the complete local run directory; generated caches and packages are ignored by Git.
