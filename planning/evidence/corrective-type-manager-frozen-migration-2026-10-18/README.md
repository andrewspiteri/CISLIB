# Type Manager frozen-test migration — 18 October 2026

The roadmap remains paused. Claude was not used. This batch changes the two frozen-test entry points, not shipping Type Manager behavior.

The existing C# tool's TypeManagerFrozenTests command replaces substantive policy in verify-type-manager-runtime-tests.ps1 and verify-type-manager-business.ps1. Their parameters remain; PowerShell only bootstraps the tool and resolves paths against the caller's PowerShell directory.

| Control | Preserved native behavior |
| --- | --- |
| Frozen runtime suite | Copy the six named unit sources and runtime architecture source; compile against frozen Core/dependencies, pinned SDK/runtime and nonparallel test assembly; require exactly 90 passing native cases (81 unit plus nine architecture), zero failures/skips; retain inputs.json, native TRX and hashes. |
| Frozen business suite | Independent Core pin, complete producer and exact TypeManagement source set/hashes; producer-bound dependency copies; six exact methods with counts 2/4/1/1/1/1; ten native runtime/OS/X64/Core observations; executed dependency hashes and retained native report. |
| Copied evidence | Retain the native reader/tool and current managed runtime files, copied source/config/candidate hashes, logs, native TRX and results. Recheck frozen copies and the caller's Core after execution. |

Fresh native runtime producers completed their existing 17 checks on Windows and Ubuntu and supplied lane-local frozen candidates. Each migrated entry then passed **90 runtime cases and ten business cases**. The 22 new C# policy controls passed per lane: Windows 1599ed16aa3f42cab34ea7a44910c00f; Ubuntu 9735b2eba5ce49d6a9e031a0b91449da. They reject wrong counts/skips/failures, foreign business methods, missing/altered module membership, wrong candidate/producer pins and missing/duplicate/mismatched runtime observations.

The first Ubuntu launcher attempt exposed the distinction between PowerShell's current location and the process working directory when resolving relative paths. The wrapper now supplies the caller's location explicitly. Final runs also preserve the original standalone runtime input inventory. Preliminary attempts earn no final invocation credit.

The four retained JSON files are final native results. Complete producer, copied inputs, binaries, native TRX and logs remain under .artifacts/corrective/type-manager-frozen-migration-2026-10-18 on each lane (producer, runtime-final, business-final). No historical baseline or published contract changed. These candidates have different Git metadata and are not one unchanged cross-platform release package. The separate 100-case mutation baseline is neither substituted for this 90-case suite nor newly qualified.

This is focused batch evidence. The previous complete Developer checkpoint remains the last combined result until the remaining Type Manager migrations receive their coherent checkpoint. Compatibility, source inventory lifecycle and the eleven-control example verifier are the next batches.
