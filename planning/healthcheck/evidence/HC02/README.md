# HC02 collector runtime evidence

The user requested a local commit of the completed changes before HC02. Checkpoint `801451fb7b1cef088fa8d427dfc1c9117f1512da` contains HC00/HC01 and the byte-preservation attributes required for their hash receipts. It was created from a clean verified candidate; nothing was pushed or published. HC00/HC01 receipts retain their historical precommit context unchanged.

HC02 implements `HealthCollectorRunner`: inert registration, explicit start/manual collection/periodic activation, one coalesced dispatcher and timer, rotating bounded admission, shared operations and caller-only wait cancellation. Registration handles own their collectors and monitors; the idle registry has detached entries and weak handle references. Active wrappers retain their real owner leases through evaluation, handoff and cleanup; capacity is released only after actual state settlement. Stopping/withdrawal immediately close admission while actual work remains tracked.

The [coordinated internal addendum](contract-amendment.md) records the settlement/stopping/retirement signals and self-wait guards needed at the real HC01 integration boundary. Public signatures and historical contracts remain unchanged. The Core runner replaces the nonshipping `Reference/HealthCollectorRunner.cs` facade, and compiler consumers will use the actual implementation. No other runtime task was dispatched.

The parallel pilot reuses the current committed checkout with disjoint files and one coordinator build stream: separate lifecycle and scheduling test owners, a state-integration reviewer/test owner, and the coordinator's runner implementation/integration. No subagent runs builds. Owner fixtures implement real protection/cleanup barriers around the dependencies they own; no fake state or snapshots represent the runner.

The final Release focused run passed 232 actual Healthcheck cases with zero failures or skips: 147 retained HC00/HC01 cases and 85 HC02 cases across 57 new test methods. The first focused run passed 223 cases before the final retirement/clock regressions and coordinated callback-settlement tests; both native attempts are retained.

Developer-01 passed on Windows x64: 4,142 unit, 2,121 integration, 1,048 architecture and 25 business cases (7,336 total), with zero failures or skips. Locked restore, formatting, Release solution/reference compilation, compiler consumers, package/dependency checks, static architecture inspection, native discovery reconciliation, console startup and Developer evidence validation passed. All 927 captured files under the C# implementation tree still match the candidate. There were no project/package changes or verification-gate relaxations.

The [HC02 receipt](receipt.json) pins the candidate, internal addendum, native reports and source comparison. [collection-native.json](collection-native.json) identifies each of the 85 new executed cases; [runtime-inputs.json](runtime-inputs.json) pins all 11 new/changed runtime and test files. The historical HC00/HC01 receipts are unchanged; their raw files and retired facade source remain recoverable from the committed checkpoint. HC02 remains uncommitted and unpublished.

Native commands used from the repository root:

```powershell
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Configuration Release -Filter 'FullyQualifiedName~Healthcheck' -TimeoutSeconds 600
pwsh -NoProfile -File eng/verify.ps1 -Profile Developer -MaxBuildNodes 1 -OutputRoot .artifacts/healthcheck/HC02/developer-01
```

Build subprocesses used process-local `UseSharedCompilation=false`, `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`. No subagent ran a competing build. Source-formatting corrections preceded the frozen Developer candidate. Evidence metadata is finalized after execution; runtime/build input equality is checked independently.

The [graph refresh record](graph-refresh.json) reports a fresh schema-2 graph with 14,327 nodes, 75,136 edges and zero errors/warnings, using C# compiler extractor 6 and semantic-facts extractor 3. Its exact runner-symbol lookup is untruncated. Planning, compiler-verification sources and historical DiscoveryHost fixtures are excluded by graph configuration; graph output does not replace native verification.

Complete module acceptance, transport/codec/binder implementation, external Heartbeat/startup integration, Linux and release coverage/mutation/compatibility qualification remain later work. The Developer pass does not establish M2 or release qualification.
