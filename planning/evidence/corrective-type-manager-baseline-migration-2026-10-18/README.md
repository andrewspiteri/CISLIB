# Original Type Manager compatibility entry point — 18 October 2026

The roadmap stays paused and Claude was not used. This batch replaces policy in check-type-manager-compatibility.ps1 with TypeManagerBaselineChecks in the existing C# engineering tool. It changes no shipping API, dependency, historical baseline or publication record.

The three original controls remain: the pinned ApiCompat comparison against the original reference; execution of the unchanged compiled consumer against the selected frozen Core; and rebuilding/executing the original source consumer against that Core. Both runtime executions retain their two selected assemblies. Baseline members are contained and hash-checked before/after; the manifest, original consumer, candidate and tool are pinned. ApiCompat 10.0.401 runs on pinned .NET 8.0.31; consumers run on .NET 10.0.12 without roll-forward.

The original consumer predates the approved Microsoft runtime closure. Its binary and deps file stay unchanged; a separate additional-deps manifest supplies the exact two candidate dependency copies, following the existing native compatibility fixture. The initial Windows run correctly failed to load these dependencies before that deployment correction.

All **three legacy controls**, **six new baseline-policy controls** and the separate **111-case native compatibility suite** passed on Windows and Ubuntu, with no skipped or failed cases. The focused baseline policy run IDs are Windows f3e2dd4586de4a33a0e83936d7274398 and Ubuntu af91e3225ff6451a8f0418ea19a4ce12. The broader compatibility run IDs are Windows 3c287f7cf14c415a8f51e747546ba374 and Ubuntu 54c7d3d13abe420f96e4950caae7a6c4. Final legacy runs also check the tool's version and unchanged bytes.

Ubuntu needed the official 8.0.31 Linux x64 runtime, verified against Microsoft's release-metadata SHA-512, and the exact ApiCompat tool. The ordinary tool restore selected unrelated tools and package mapping rejected the tool; a local tool installation used a dedicated nuget.org configuration. CIS_COMPAT_API_TOOL selects that exact local entry DLL. Repository package mapping and pins remain unchanged.

The retained JSON reports are from baseline-pinned on each lane. Full copied consumers, source builds, process arguments/hashes, tool-version output, API comparison and runtime logs remain under .artifacts/corrective/type-manager-baseline-migration-2026-10-18. Frozen candidate provenance is retained in the preceding frozen-test migration receipt. Native candidates differ in Git metadata; these focused checks do not establish published cumulative compatibility, a full Release profile or one unchanged cross-platform package. The broader 111-case fixture remains independently maintained.

Source inventory SDK lifecycle and eleven example-boundary checks remain the final migration batch. A coherent Developer checkpoint will follow that batch.
