# HC00 evidence and limits

The selected candidate and exact file/report hashes are in [the baseline receipt](../../baseline.json). The development base is `5f36bbcb6fb26c1818c868bfb06ad701895bef1b`; the candidate remains uncommitted and unpublished. Receipt metadata is finalized after native execution; the receipt pins actual verified code/build inputs separately from administrative metadata.

Developer-09 completed successfully on Windows x64: 3,969 unit, 2,121 integration, 1,048 architecture and 25 business cases passed (7,163 total), with zero failures or skips. The 59 actual Healthcheck foundation cases are identified individually in foundation-native.json. Locked restore, formatting, Release solution/reference compilation, compiler consumers, package/dependency checks, static architecture inspection, native discovery reconciliation and Developer evidence validation passed. The two nonshipping compiler assemblies are absent from the package. Eight exact build artifacts are retained with matching package/Core inspection hashes. All 870 captured C# code/build inputs match the final candidate.

The repository's derived graph is refreshed after final canonical documentation/receipt changes. Its separate graph-refresh record retains freshness, build identity, diagnostics and truncation; graph output does not replace native verification. Complete release coverage/mutation/compatibility and Linux qualification are not credited by the Developer profile.

The shipping foundation contains real option resolution, bounded detached diagnostic/measurement/result values, immutable copied snapshot DTOs, public interfaces/options/enums and concrete internal state seams. The separate reference facade and consumers are nonpackable class libraries. Their compiled signatures cover the service, all four monitor policies, collection ownership/lifecycle, execution binding, formatter, HTTP installation and pipes. Facade bodies are never executed. No HealthcheckService or collector/transport runtime pass is claimed.

The C# foundation tests exercise real option precedence/read-once selection, blank/invalid inputs, range boundaries, disabled transport validation, portable names, scalar/Unicode bounds, runtime exception projection, immutable collection ownership and actual observation-time preservation. Existing focused and Developer entry points run the native MSTest cases and reject failed/skipped/empty results. The test hosts pin both shared frameworks to 10.0.12 with roll-forward disabled.

Native commands used include:

```powershell
dotnet restore CisLib.slnx --locked-mode
dotnet build verification/contracts/healthcheck/Consumer/Consumer.csproj -c Debug --no-restore -m:1 -p:UseSharedCompilation=false
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Filter 'FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck'
pwsh -NoProfile -File eng/test.ps1 -Project IntegrationTests -Configuration Release -Filter 'FullyQualifiedName~ConfigurationPathProcessTests|FullyQualifiedName~DiscoveryProcessTests'
pwsh -NoProfile -File eng/verify.ps1 -Profile Developer -MaxBuildNodes 1 -OutputRoot <fresh-HC00-directory>
```

The first two commands run from implementations/csharp; the PowerShell entry points run from the repository root. Actual logs/arguments/results are referenced in baseline.json. Build subprocesses used process-local UseSharedCompilation=false, MSBUILDDISABLENODEREUSE=1 and DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 after sandboxed child builds exited without compiler diagnostics. Native verification outside the sandbox succeeded where those initial constrained runs did not. No analyzer, warning, native test or release threshold was relaxed.

Intermediate failures are preserved rather than presented as passing evidence:

* Initial Core restore produced NU1510 for its approved logging abstraction package after adding ASP.NET. A Core-only pruning setting preserves that existing two-package closure; subsequent locked restore passed.
* Initial sandbox solution/focused builds exited with no compiler errors reported. A standalone Core build and nonshipping consumer build passed; the same focused native launcher outside the sandbox passed.
* Developer-01 could not capture an actively written console log in the source tree. Later live logs use ignored artifact directories.
* Developer-02 found multiline-initializer whitespace in a new foundation test; it was corrected.
* Developer-03 was deliberately stopped after a fresh Console build proved that ASP.NET now supplies its logging abstraction DLLs. The old CopyLocal inventory expectation was corrected. Its unfinished raw summary remains unfinished.
* Developer-04 passed all 3969 unit cases, then exposed the fixture's ASP.NET 10.0.0 bootstrap requirement under roll-forward Disable. The fixture now pins both frameworks to 10.0.12; all 33 selected child-process regressions passed afterward. Those bootstrap failures are not accepted integration evidence.
* Developer-05 passed 3969 unit cases and 2074 integration cases, with 47 integration failures from remaining private-NuGet-versus-shared-framework origin assumptions and the candidate consumer's runtime pin. Corrected fixtures compare absolute origins and hashes against the trusted selected ASP.NET 10.0.12 installation, retain separate NuGet package evidence, and pin both candidate frameworks. All 64 selected provider/package-consumer regression cases then passed with no skips.
* Developer-06 was deliberately stopped after restore/dependency checks when independent review identified a missing internal handle-to-state access seam. IHealthStateAccess and its exact binder/runner validation rules were frozen before the replacement combined checkpoint. The interrupted summary is not presented as a pass.
* Developer-07 caught imports ordering in the corrected provider fixture before native-family execution. The two imports were reordered; no behavior or verification rule changed.
* Developer-08 passed all 3969 unit and 2121 integration cases, then passed 1046 of 1048 architecture cases. The two failures were fixture assumptions: a single-framework runtimeconfig and a NuGet compiler-reference origin now supplied by ASP.NET. The runtime check now requires exactly NETCore and ASP.NET at the pinned version; the wrong-target control substitutes an existing same-version but unselected asset and still requires ARCH-COMPILER-ORIGIN. All 39 selected architecture/runtime-policy cases passed afterward. Business did not run in Developer-08.

`contracts/healthcheck/v1` schemas and common fixtures are authored contract inputs. JSON syntax and scalar-regex sanity were checked; no formal JSON Schema-validator or actual HTTP/pipe reader pass is claimed. Registration ordering is a writer obligation because the wire exposes no ordinal for clients to verify it independently.

Only HC00 was dispatched. Future implementation task write sets and protected shared-file transactions are recorded in ownership.json/shared-changes.json. The six original attachments remain unchanged. There was no CIS task import, new orchestration engine, commit, push, publication or external review transmission.

All 59 source acceptance cases remain pending as complete feature cases. Local foundation sub-evidence does not replace policies/state/collection/transport execution. Actual Heartbeat and future startup-coordinator integration, Linux qualification, all six release families, mutation, cumulative historical replay and exact-package examples remain at their defined later boundaries. The publication ledger is actually empty; no historical release or replay pass is invented.
