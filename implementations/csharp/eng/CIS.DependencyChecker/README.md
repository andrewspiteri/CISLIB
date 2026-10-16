# Type Manager development verification

Use the repository's pinned SDK and native runtime. From `implementations/csharp`, the existing C# checker runs the runtime producer:

```powershell
dotnet run --project eng/CIS.DependencyChecker -c Debug -- type-manager-runtime --repo-root ../.. --output-root ../../.artifacts/tm-native/runtime
```

This builds real loader fixtures, executes fifteen fresh-process scenarios and checks both reference-assembly metadata encodings through the actual build task. Each output root must be empty. Results are written to `results.json`, initially incomplete; child commands, PID, exit code and output hashes are retained under `logs`. A failure returns nonzero.

Use that completed producer's exact candidate and report for instrumentation and provenance rejection:

```powershell
dotnet run --project eng/CIS.DependencyChecker -c Debug -- type-manager-instrumentation --repo-root ../.. --candidate-core ../../.artifacts/tm-native/runtime/Host/bin/Release/net10.0/CIS.Core.dll --candidate-report ../../.artifacts/tm-native/runtime/results.json --output-root ../../.artifacts/tm-native/instrumentation
dotnet run --project eng/CIS.DependencyChecker -c Debug -- type-manager-instrumentation-controls --repo-root ../.. --candidate-core ../../.artifacts/tm-native/runtime/Host/bin/Release/net10.0/CIS.Core.dll --candidate-report ../../.artifacts/tm-native/runtime/results.json --output-root ../../.artifacts/tm-native/provenance
```

Instrumentation operates on an owned source copy. Its two positive modes and three deliberate code variants check enumeration counts, query reuse and final cancellation. The two provenance controls must reject changed Core bytes and source records before any generated/binary instrumentation directory appears. These observations concern the instrumented copy; the producer separately checks the unmodified candidate.

Both producer and instrumentation accept `--replay-root <completed-output>` to execute frozen managed binaries. Producer replay also accepts an independent `--expect-core-sha256 <hash>`. Replay requires the exact verifier assembly; copy its built directory alongside the fixture archive when changing OS. The recorded `verifierSha256` identifies that compiled C# tool, replacing the former Python source-script identity. Rebuilding the verifier or changing candidate/source inputs requires new matching evidence. `--dotnet <absolute-path>` selects the child executable.

These are development checks. Source provenance covers Core C# files and five named project/configuration inputs; it does not attest every transitive MSBuild input. Successful replay is not package, performance, mutation-score or release qualification. The [native migration record](../../../../planning/evidence/corrective-type-manager-runtime-migration-2026-10-16.md) retains the executed scope and hashes.
