# HC01 core runtime evidence

The current user authorized HC01 after the completed HC00 foundation. The six attachments remain design inputs; they do not independently authorize the later task list. ASP.NET in the single Core package and the parallel pilot remain approved. This candidate is uncommitted and unpublished.

HC01 implements the real `HealthcheckService`, condition, consecutive-failure, numeric and availability handles, a weak monitor registry, coherent copied snapshots, aggregate health/readiness, and immediate withdrawal followed by actual reservation/cancellation settlement. One private state gate protects policy and lifecycle changes; injected clocks, cancellation handlers and task-source completion run outside it. Direct reports preserve their weak target through acceptance even if a supplied clock forces GC and reenters snapshot capture.

The same state owner implements the frozen collection and execution seams. Collection reservations join one wrapper, capture health/per-key observation baselines, reject stale fields independently, and remain tracked after withdrawal until actual completion and cancellation handlers settle. Optional collection failures change diagnostics only. Required evaluation failures apply the declared monitor policy once. A batch exceeding 32 retained metric keys is rejected atomically while an otherwise valid required health observation still commits. Acquisition and cleanup faults never invent policy outcomes.

Execution associations validate and publish atomically, retain only detached state in the registry, and share the actual created-monitor drain. Distinct registration identities may share an external monitor. Lifecycle updates retain bounded current/previous execution context and cannot replace the exposed current execution with an older settlement. Full source-fact validation, ordering and duplicate detection remain the receiving binder's HC06 responsibility.

The parallel pilot used disjoint files in the current checkout because HC00 is not committed: separate public-policy tests, state-seam tests and execution-state implementation/review, with the coordinator owning integration and the sole build stream. No other runtime task was dispatched. There is no collector runner, binder, codec, HTTP branch, pipe runtime or real Heartbeat/startup-coordinator integration evidence in HC01.

The original [HC00 receipt](../../baseline.json) and its canonical input records remain historical and unchanged. Before edits, 135 unique pinned inputs were checked and copied to `.artifacts/healthcheck/HC01/input-snapshot`. HC01 removes only the nonshipping `Reference/HealthcheckService.cs` facade now supplied by Core. Remaining facade declarations still throw and are never executed. The compiler consumer will bind the actual service from Core. The HC01 receipt records the promotion and exact candidate/report hashes separately rather than rewriting HC00 evidence.

Focused Release verification passed 147 actual Healthcheck cases with zero failures or skips: 59 retained foundation cases and 88 new core/state cases across 68 test methods. The initial attempt compiled Core but failed the test analyzer on an expected timestamp named `actualObservation`; the test input was renamed and the unchanged assertion then compiled. All three focused attempts are retained. The final case uses barriers to check coherent policy/counter/aggregate/readiness snapshots while reports and withdrawal race.

Developer-02 passed on Windows x64: 4,057 unit, 2,121 integration, 1,048 architecture and 25 business cases (7,251 total), with zero failures or skips. Locked restore, formatting, Release solution/reference compilation, compiler consumers, package/dependency checks, static architecture inspection, native discovery reconciliation, console-host startup and Developer evidence validation passed. Developer-01 stopped at initializer whitespace in the new coordinator; the repository formatter corrected that file before the fresh successful checkpoint. No gate or threshold was relaxed. This profile does not credit release coverage, mutation, cumulative publication compatibility or Linux qualification.

The [HC01 receipt](receipt.json) pins the final runtime/test inputs, compiler-facade promotion and exact native reports. [core-native.json](core-native.json) identifies each of the 88 new executed cases; [runtime-inputs.json](runtime-inputs.json) pins all eight new source/test files. The historical HC00 receipt is unchanged. Administrative evidence is finalized after native execution, with the captured C# inputs compared independently against the live candidate.

Native commands used from the repository root:

```powershell
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Configuration Release -Filter 'FullyQualifiedName~Healthcheck' -TimeoutSeconds 600
pwsh -NoProfile -File eng/verify.ps1 -Profile Developer -MaxBuildNodes 1 -OutputRoot .artifacts/healthcheck/HC01/developer-02
```

Build subprocesses used process-local `UseSharedCompilation=false`, `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`. Only the coordinator ran builds; no subagent ran a competing build stream.

The [graph refresh record](graph-refresh.json) reports a fresh schema-2 graph with 13,962 nodes, 71,921 edges and zero errors/warnings, using C# compiler extractor 6 and semantic-facts extractor 3. Its exact real-service symbol lookup is untruncated; the broad five-result text search is truncated. Planning, compiler-verification sources and historical DiscoveryHost fixtures are excluded by graph configuration. These graph limits do not replace the actual native evidence above.

HC00-era task/ownership/acceptance files describe that historical milestone. This directory records the later HC01 dispatch and results. Full source-case acceptance, release coverage/mutation/compatibility, Linux qualification, exact-package examples and external integrations remain at their specified later boundaries.
