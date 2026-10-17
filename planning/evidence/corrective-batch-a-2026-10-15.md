# Batch A and focused native loop: execution record

Base: `c1f37a2ee0bbc4772797aa54e44ca3b13c138132`, 15 October 2026. User authorized Batch A plus the smallest focused native C# test loop and coordination with logging chat `01a12085-7b0b-7e10-89ef-b152f9a4e4ba`. The broader native-script migration, defaults, pipeline changes and release qualification remain separate.

## Implementation

- Discovery logger and Type Manager build helper reference the selected SDK beside its imported `NETCoreSdkBundledVersionsProps` file instead of the executable MSBuild host directory. They retain approved identities, `Private=false`, existing project boundaries and runtime dependencies.
- Native architecture inspection records both SDK and host paths. C# and existing PowerShell dependency checks enforce the selected SDK origin. Five native controls distinguish host paths, reject a foreign origin, copy-local references, missing SDK facts and a mismatched SDK-version directory. The path is tied to NetCoreRoot/sdk/NETCoreSdkVersion and the two SDK pins must agree. The required negative-case minimum rises from 20 to 24.
- `eng/test.ps1` is invocation glue for a small C# command in the existing development checker. It accepts a registered project and class/category/native filter, incrementally builds dependencies, runs VSTest, owns fresh reports and fails empty, skipped, failed, stale, inconsistent or missing results and bounded process failures. It does not select dependency graphs, run a full checkpoint or claim omitted families.
- `ArchitectureAllowlistTests` can take an explicit inspection file for focused fixture execution without overwriting the Developer report. Documentation explains this prerequisite and its limits.
- Agent guidance and the test README now distinguish the edit loop, coherent checkpoints and release gates. The original empty-business/logging-scaffold guidance is corrected.

## Reproduced environment problems

The system installation no longer contains pinned SDK 10.0.401 or runtime 10.0.12. Normal SDK resolution from the language root failed before compilation (`initial-sdk.log`). Pins were preserved. A private Windows SDK ZIP from Microsoft's release metadata was SHA512-verified against `windows-sdk-source.json`, then extracted under `.artifacts/corrective/batch-a/dotnet`. It supplies SDK 10.0.401, MSBuild 18.9.11 and runtime 10.0.12 on win-x64; no global installation or user PATH was changed.

The only registered Visual Studio is Build Tools 2022 17.14.36. Its MSBuild directory does not contain `Microsoft.VisualStudio.TestPlatform.ObjectModel.dll`; its evaluation also fails SDK resolution (`vs2022-evaluation.log`). This demonstrates the host-directory assumption but is not an executable supported-VS reproduction. A supported IDE build, Test Explorer and actual design-time loading remain **unqualified**. See the [Microsoft SDK/VS compatibility table](https://learn.microsoft.com/dotnet/core/porting/versioning-sdk-msbuild-vs).

The main checkout initially failed SourceLink writes despite a passing clean checkout. Windows Restart Manager identified stale `VBCSCompiler` PID 138036, launched from the now-removed system 10.0.401 SDK, as the file owner (`lock-owners.json`). After notifying the inactive logging chat, only that process was stopped. The normal Release build then passed. No compiler/build concurrency policy was disabled to conceal the lock.

## Initial native Windows results (before review follow-up)

All detailed paths below are relative to `.artifacts/corrective/batch-a/` unless otherwise stated. The isolated `clean/` checkout contains only the pinned base and this batch's code changes, with no Developer checkpoint or packed-package prerequisites for ordinary unit tests.

| Check | Result and retained evidence |
| --- | --- |
| Unmodified clean solution, Debug and Release | Both pass, zero warnings/errors; 30.26 s and 23.01 s wall time, including restore. `clean-debug.log`, `clean-release.log`. |
| Final solution, Debug and Release | Both pass, zero warnings/errors. Existing clean-checkout incremental builds: 10.01 s and 9.37 s MSBuild time. `clean-final-debug.log`, `clean-final-release.log`. Main Release also passes (`final-release.log`). |
| Native discovery with the changed custom logger | 3,438 unit cases, zero discovery errors, not aborted. `discovery.json`, `discovery.log`. |
| Ordinary full unit execution | 3,438 passed, zero failed/skipped; native duration 48 s. `ordinary-unit/ordinary-unit.trx`, `ordinary-unit.log`. No Developer checkpoint or Core pack was run in this checkout. |
| Focused runner controls | 23 passed; class and category selectors both exercised. `focused-corrected.log`, `focused-class.log`, `focused-incremental.log`; native reports under `clean/.artifacts/csharp/focused/`. |
| Empty native filter | Exit 1 with explicit zero-case failure; fresh native TRX retained. `empty-filter.log`, focused run `3c35713e10d642128fe66baf180466ec`. |
| Real skipped and failed tests | Both rejected, exit 1. Deliberate source was added only to the isolated checkout, removed in `finally`, then the project rebuilt. `control-Skipped.log`, `control-Failed.log`, `control-restoration.log`. |
| One-second child deadline | Exit 1 with native child timeout. `timeout-control.log`; main focused rerun verifies normal recovery. |
| Focused integration fixture | Seven DiscoveryProcessTests passed with project-produced/copied DiscoveryHost. `integration-focused.log`; native report in main `.artifacts/csharp/focused/230c65ee2bf24b8b8129a74b1943a7e7/`. |
| Fresh architecture inspection | 27 projects, 33 restored target graphs, two packaged assemblies; amended allowlists and ownership checks pass. `inspection/inspection.json`, `inspection.log`. |
| Focused architecture controls | 28 passed against that fresh inspection, including the four new SDK/host controls. `architecture-tests.log`; main focused run `d34ddd03e1c64b639d77ff835ccef5b6`. |
| Existing dependency script | All 27 evaluated projects pass; exactly two approved Core runtime packages. `dependency-script.log`. |
| Packaged helper execution | Dedicated-cache versioned CIS.Core SDK consumer builds, generates inventory and runs successfully: four assemblies, 276 types. `package-sdk-consumer-build.log`, `package-consumer-run.log`. A plain PackageReference with inventory enabled still rejects CISTM007 (`package-consumer-build.log`). |
| Evaluated reference origins | Exact SDK properties and resolved reference paths saved in `logger-references.json`, `helper-references.json`; host identity in `pinned-host.txt`. |

The unchanged Windows focused category run took **7.31 s total command wall time**, including PowerShell/checker bootstrap. Its C# summary measured **2.19 s selected-project build**, **2.12 s combined native discovery/execution/reporting**, **4.36 s command-body total**. This is a warm incremental measurement, not a cold benchmark and not a comparison with Linux. Report: `clean/.artifacts/csharp/focused/7ad975a0c2ff408d816b852e3303c011/summary.json`. VSTest's discovery and execution are not separately timed by this small launcher.

## Native Linux scope

Ubuntu 24.04 under WSL uses a separate `linux/` checkout and the existing native Linux SDK 10.0.401/runtime 10.0.12. Before any solution build or packaging, the C# focused command built the unit-project closure and passed all 23 controls (`linux-focused.log`; focused run `882440b3a9794effabab7b98a89923f6`). Its cold command-body summary measured 30.31 s build and 8.29 s native discovery/execution/reporting. PowerShell is not installed in this distro, so this is native C# command qualification, not Linux PowerShell-wrapper qualification.

Both Linux solution configurations pass with zero warnings/errors: Debug 80.46 s (remaining solution projects cold after the focused-project build), Release 43.30 s (configuration cold). Native discovery reports 3,438 unit cases, zero errors and no abort. The subsequent focused category rerun passes all 23 cases; its MSBuild phase is 19.93 s on the mounted Windows filesystem. Logs: `linux-debug.log`, `linux-release.log`, `linux-discovery.json`, `linux-discovery.log`, `linux-incremental.log`. These results are not a full Linux unit/integration/architecture or package-consumer replay. Windows and Linux never shared bin/obj output.

## Review, coordination and remaining boundaries

The logging chat reported no running work and received the build/tooling ownership updates. Logging production behavior, original roadmap completion records and unrelated local graph/profile/Web-launch edits were preserved. No publication, push, release qualification or mutation queue restart occurred.

The first external review attempt was blocked by automatic approval. After explicit approval of payload SHA256 e5cf2e99a7db16146e1d4af90c6bd3b08d1a9e4e2a803c167ae1247d94df4f66, the read-only Claude review completed (`claude-review.json`). The user subsequently directed that Claude reviews stop rather than require further permission. AGENTS.md records that direction; all follow-up review and verification is local.

Batch A's CLI portion and the first focused B slice have executable evidence. Supported IDE acceptance remains open; complete B migration and C–F are not credited. This record is not full release evidence and does not relax any existing runtime, package, coverage or mutation gate.

For this machine's retained private SDK, use a process-local setup before the documented commands:

```powershell
$env:DOTNET_ROOT = 'C:/miscwork/portfolio/cislib/.artifacts/corrective/batch-a/dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Category FocusedLoop
```

## Local follow-up to the completed review

- R1: SDK references now derive from the actual imported bundled-versions file, not MSBuildSDKsPath. Both policies compare the SDK path to its root/version and require the global.json and toolchain.json pins to agree. The extra wrong-version-directory rejection raises the architecture selection to 29 native cases. This still does not qualify an unavailable Visual Studio host.
- R2: Developer and Release entry points reject CIS_ARCHITECTURE_INSPECTION before touching checkpoint reports. Both rejection controls passed (`override-Developer.log`, `override-Release.log`). The usage example clears its override in finally.
- R3: Confirmed Evidence.psm1 records unbound native methods as UnboundTechnical; it is not a closed-world method inventory. Linux architecture execution is not credited here.
- R4: ProcessRunner already kills the entire process tree and converts cancellation into TimeoutException. A real isolated testhost wrote its PID/start time and then hung. The 15-second test phase failed with exit 1 and passed=false; PID73616 no longer existed, and the next 23-case run passed. See `hung-test.log`, `hung-cleanup.json`, focused run `clean/.artifacts/csharp/focused/405b78a0b90d4710bf2d9aef60d4183c/summary.json`, and `hung-recovery.log`. The temporary hung source was removed in finally.
- Empty explicitly supplied PowerShell selectors are now forwarded to the C# parser and rejected (`empty-selector.log`), rather than silently running an entire project.
- Fresh Windows Release build/pack and the full 27-project/33-target inspection pass after the fixes (`review-build.log`, `review-pack.log`, `review-inspection/inspection.json`). Both packaged DLL hashes are unchanged from the successfully executed consumer (`review-packed-binaries.json`). Final Windows Debug/Release and Linux Debug/Release builds pass with zero warnings/errors. The refreshed architecture selection passes 29 cases, the dependency script passes all 27 projects, and the final Linux focused run passes 23 cases. See review-windows-debug.log, review-build.log, review-linux-debug.log, review-linux-release.log, review-architecture-tests.log, review-dependencies.log and review-linux-focused.log.

The loop deliberately retains normal incremental build-server behavior and requires coordination when sharing a checkout. A focused result remains distinct from a checkpoint. Optional streaming, nested-TRX support and richer failure timing were not expanded into this small launcher.
## 17 October receipt clarification

The five SDK observations comprise one positive host-independence case and four negative SDK-origin cases. The negative method has exactly 24 rows, including wrong-sdk-version-directory, matching its required minimum of 24. The positive host-independence method has a separate native identity. No negative minimum of 25 is required. Supplemental pins recover the original R1 focused run ID and retain previously unpinned logs; the original identical override logs do not prove distinct profile invocation identities. See the [receipt identity supplement](corrective-historical-receipt-pins-2026-10-17/README.md) and its fresh two-profile C# controls. This note does not rewrite the historical environment or qualification limits above.
