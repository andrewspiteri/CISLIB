# C# target and platform support policy

Selected 10 October 2026 for `WP01-S01-T04`. The [contract-target record](../../implementations/csharp/support/contract-targets.json) states the intended package contract. The separate [execution matrix](../../implementations/csharp/support/execution-matrix.json) records what has actually run. A selected target or planned platform does not establish a passing verification result.

## Initial target contract

Ship one portable, managed `lib/net10.0/CIS.Core.dll` asset. Its minimum framework family is .NET 10; the package does not require an OS-specific TFM or contain per-RID native assets. The first consumer is [ConsoleHuman.csproj](../../implementations/csharp/examples/logging/ConsoleHuman/ConsoleHuman.csproj), inheriting `net10.0` from [Directory.Build.props](../../implementations/csharp/Directory.Build.props). No inspected consumer requires an older target.

The current development SDK is `10.0.401`; the observed .NET runtime is `10.0.12`. These exact pins identify verification inputs, not a requirement that library consumers install that SDK or use only that runtime patch.

From the first actually published package, including a prerelease, retain every published target asset and its minimum framework requirement in future compatible versions. Do not drop a target, raise its minimum or rewrite historical consumers to make an upgrade pass. Newer TFMs can be added after their dependencies and conformance are verified; they cannot replace old assets. Microsoft's runtime servicing lifecycle is independent of this package-retention rule.

## Platform qualification

| Initial lane | Execution mode | Current evidence |
| --- | --- | --- |
| Windows 11 x64 | Normal managed, framework-dependent .NET 10 | Scaffold checks on Windows 11 Home 25H2, build `10.0.26200.9550`; full logging/release suite pending. |
| Ubuntu 24.04 x64 | Normal managed, framework-dependent .NET 10, glibc | Selected release lane; unexecuted. Exact image, OS patch, runtime and architecture must be recorded when it runs. |

Both lanes must run all applicable release families and candidate-package examples before the initial release advertises their support. Ubuntu 24.04 and Windows 11 are listed by the [.NET 10 supported-OS record](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md), checked on the decision date. CisLib makes no automatic claim for every OS or architecture supported by .NET.

macOS, other Linux distributions/libc implementations, Windows Server, ARM64/x86, browser/mobile .NET, Native AOT, trimming, single-file and self-contained deployments are outside the initial qualified scope. Recognizing an OS in metadata discovery does not qualify execution on it. Add a declared lane and exercise the relevant suite before advertising additional support.

Record exact SDK/runtime, OS version/build or image digest, architecture, execution mode, source and candidate identities, commands, reports and case applicability for each run. Keep `planned`, scaffold verification and full release qualification distinct. Never infer a Linux pass from Windows results or call an empty suite successful.

## Runtime selection and historical replay

SDK selection and application runtime selection are separate. The scoped `global.json` pins SDK `10.0.401` with SDK roll-forward disabled. Run SDK commands from `implementations/csharp` so this file is found. It does not disable application runtime patch servicing.

Core is a library and does not select the host runtime. ConsoleHuman currently leaves runtime roll-forward unset, using the .NET default `Minor` policy, which selects the latest compatible patch in the requested minor family. Normal consumer documentation keeps that behavior. Every verification lane records the runtime that actually ran and any environment/command-line override, following Microsoft's [runtime selection rules](https://learn.microsoft.com/en-us/dotnet/core/versions/selection).

| Historical lane | Treatment |
| --- | --- |
| Original-runtime replay | Preserve the original consumer project, binary, runtime configuration, target and toolchain. Retain the exact runtime materialization inputs. For an exact-runtime test, use `dotnet exec --fx-version <recorded-version> --roll-forward Disable <consumer.dll>` and record that test-only override. Use isolated archived tooling when necessary. |
| Newer supported runtime | Run the same historical consumer binary unchanged on an explicitly selected, compatible execution environment. Record resolved runtime and roll-forward. Testing on a newer major runtime requires an explicit test override such as `--roll-forward Major` when needed; it must not alter the retained fixture or replace original-runtime evidence. |
| Runtime no longer vendor-supported | Retain target assets and historical evidence. Keep an isolated historical lane while that runtime remains part of the claimed execution contract; record vendor servicing status separately. Do not claim that CisLib extends vendor runtime support. |

If historical tooling/runtime material is unavailable, report that lane as unexecuted with the missing prerequisite. Do not rewrite an old consumer or silently retarget it. Replay/retention automation is later foundation/verification work, not completed by this policy document.

## Current evidence boundary

The Windows evidence for these tasks covers locked restore, dependency/format/build checks, the scaffold host, local packaging and assembly metadata inspection. The explicit-runtime host check verifies only that this scaffold can execute on `10.0.12`. There is no logging behavior, historical package or full six-family release evidence yet. Linux is unexecuted. The release profile continues to reject incomplete evidence.
