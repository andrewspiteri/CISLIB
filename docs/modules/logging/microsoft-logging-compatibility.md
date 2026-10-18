# Microsoft provider compatibility fixtures

The PowerShell entry point now launches MicrosoftLoggingCompatibilityChecks in the existing nonshipping C# engineering tool. C# owns dependency resolution, result/API validation, frozen input checks and report completion. The launcher resolves relative paths against the caller's PowerShell directory and preserves the existing parameters.

WP06-S03-T04 registers the implemented provider in the [C# compatibility inventory](../../../compatibility/csharp/microsoft-logging-fixtures.json). The source consumer, compile settings and literal API expectations live under implementations/csharp/verification/compatibility/microsoft-logging. The original reference-only samples remain separate.

After locked repository restore and a Release build, run from the repository root:

```powershell
pwsh -NoProfile -File implementations/csharp/eng/check-microsoft-logging-compatibility.ps1 -CoreAssembly implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll
```

This nonshipping command compiles against the selected real Core assembly and its two approved Microsoft runtime dependencies. It uses the pinned SDK and exact target runtime, C# 14, nullable checking and warnings as errors. Generated projects have no package dependencies and clear package sources; local props/targets isolate them from ancestor module build hooks. The application owns a real file runtime and asserts three deterministic provider/direct records, borrowed and typed scope values, extension/template/EventId/generic calls, successful flush/stop and nonowning disposal. Generated resource facts are not compared or broadly normalized.

The same process reports actual loaded paths, identities and SHA256 for Core, both Microsoft dependencies and the consumer. A literal public API fixture checks supported type/interface/member/parameter paths while allowing additional methods/interfaces. The driver validates the selected Core bytes and the actual loaded copies. It also pins the source/API fixtures, toolchain, restored dependency metadata, approved runtime policy and publication ledger, and rechecks them before reporting completion. Consumer stdout/stderr and command logs remain available if a check fails.

Source execution precedes binary capture. The driver copies the consumer DLL, PDB, deps and runtime configuration into an owned frozen directory, then copies those unchanged inputs to CoreReplacement and CompleteUpgrade directories. No build occurs during either binary scenario. Both scenarios use the embedded provider in CIS.Core and the same approved closure today. They execute separately and check all frozen hashes before/after. This is newly frozen development qualification, not an upgrade from a historical release or candidate-package restore proof.

The publication ledger still has zero actual rows; the command reads that ledger and reports its count separately from executed fixture controls. Future replay must enumerate every applicable archived provider consumer, preserve original settings/dependencies, and record each permitted replacement and exact loaded candidate package. Complete first-release negative controls, historical/original/current runtime and both platform lanes remain WP07; actual immutable publication capture remains WP09. The four original compatibility case groups remain Planned, and Release continues to refuse qualification.
