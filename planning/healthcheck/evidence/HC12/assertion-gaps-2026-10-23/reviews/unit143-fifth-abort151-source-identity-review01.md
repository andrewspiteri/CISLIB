# Fifth Unit143 abort: actual selector, active row and source binding

Read-only source/actual-prefix identification complete. No native selection, source mutation, Core/test build, production fix, controller/policy edit, SSH, replay or credit change occurred. The immutable whole143 campaign remains open; no final report/status/killedBy, normal host or qualification is inferred.

Root-retained original archive `.artifacts/hcg/unit143-fifth-abort151-prefix01.tar.gz`,4133015bytes, physically rechecked SHA256 `5e41e4144c2a1a5ea5baa83e3eef8f062fc95162a7e7b288edb3845179e0f70b`. Reviewer reused root's existing read-only extraction `unit143-fifth-abort151-receiving01/unit143-fifth-abort151-prefix01/`; no redundant extraction was created. All four contained input hashes match SHA256SUMS and were physically rechecked:

|Actual finite input|SHA256|
|---|---|
|native-parent-prefix.log,30561095bytes|38f5c31f35ee16fe60e344f20fb682a425a36a876dde35f9b9cf9c4eb7404d50|
|fifth-aborted-host.log,467050bytes|332018a39fb4ff165e83dcaa68bbc0ac67f2c3b3ef273f6fe0dea2176004acbc|
|actual-guid-fqn-cohort.json,445146bytes|73f73fcd9d250b888fe68311b81551cadd44707a3ca11a05ca3009d20880d603|
|observation.txt,331bytes|33953145be5b9d436968f77edeaac0a1c55071d315454dfe0f61ca35d6aa4415|

## Actual source identity is available in the prefix

This is not an ID transfer from an older catalog. Original native-parent-prefix line11599 logs mutation of the exact current-run `.../Healthcheck/HealthCollectorRunner.cs`; line11600 logs `Mutant 1038 created "[]" -> "[default]" using "Stryker.Core.Mutators.CollectionExpressionMutator"`. The actual generated-source dump begins at13171 and line13187 binds the selector directly:

```csharp
private readonly List<RegistrationEntry> entries = (StrykerTRT6vyUWtuvtB7Z.MutantControl.IsActive(1038)?[default]:[]);
```

The same field/selector binding remains present in later captured generated-source dumps at61000,62020,98931,99758,119235,120060. The adjacent1039 dump binds the DIFFERENT `HashSet<Operation> operations` initializer. Those two collection expressions must not be conflated.

Frozen shipping file `.artifacts/hcg/unit-complete-partition143-payload01/snapshot/source/implementations/csharp/src/CIS.Core/Healthcheck/HealthCollectorRunner.cs` physically hashes `386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8`. Its original entries initializer is physical line16, decoded UTF16 source span `[722,724)`, original `[]`, replacement `[default]`, mutator CollectionExpressionMutator. The generated-source dump plus creation record therefore establish the actual logged source/selector binding. This does not claim a final report definition record or a new loaded-image/PDB/kernel qualification.

## The host request independently selects the same1038

Actual host PID1765213, actual parent1751794, runtime10.0.12 and diag path `Runner 0-log.host.26-10-24_04-03-53_62861_25.txt` are in the host header. Parent prefix line368672 names that same Runner0 host path after Runner0's exact solo1038 selection at line368668, timestamp `2026-10-24T04:03:53.1917843+00:00`.

Host line95 is the actual typed TestExecution.StartWithTests request. Its original RunSettings contains exactly one Stryker InProcDataCollector Parameter Mutant, id1038, with190 unique declared GUIDs. The actual request has190 tests and the declared/request GUID sets match with no differences. This is stronger evidence than a global last-selection guess or a GUID substring match elsewhere in a large log.

Host line109 starts `CIS.Tests.UnitTests.Healthcheck.HealthCollectionCapacityAndFairnessBoundaryTests.FullMetricCapacityDistinguishesUpdatesFromEntirelyNewKeys`. Its actual StatsChange at111 identifies:

* GUID `150cb59c-8a3d-8aad-89d9-f034ec82f2d8`.
* Exact FQN `CIS.Tests.UnitTests.Healthcheck.HealthCollectionCapacityAndFairnessBoundaryTests.FullMetricCapacityDistinguishesUpdatesFromEntirelyNewKeys`.
* Display `FullMetricCapacityDistinguishesUpdatesFromEntirelyNewKeys (False,False)`.

Those values match the original1389 cohort map. There are zero completed NewTestResults, zero RecordEnd/RecordResult and no TestExecution.Completed in this captured host. This fifth host supplies no earlier completed assertion witness. It does not change the separately retained four-host25 assertion-shaped candidate map or eight LeaseCleanup holds.

## Actual fatal sequence, retaining the misleading raw summary

All timestamps below are UTC2026-10-24, from the original parent prefix:

|Line|Time|Actual observation|
|---:|---|---|
|368668|04:03:53.1917843|Runner0 Testing [1038:Collection expression mutation]|
|368673|04:03:57.3706620|Unhandled System.NullReferenceException|
|368674|04:03:57.3711641|HealthCollectorRunner.CloseLocked, instrumented line318|
|368675|04:03:57.3715113|BeginStop, instrumented line374|
|368676|04:03:57.3717471|Pump, instrumented line560|
|368677|04:03:57.3722717|Wake queued callback, instrumented line447|
|368681|04:03:58.9060737|Active test run aborted: Test host process crashed|
|368684|04:03:58.9582550|Stryker raw text: test run for1038 is "success"|
|368686|04:03:58.9599776|Runner0 moves to1039|

The raw "success" line is retained verbatim as a Stryker diagnostic string. It does not make the crashed host normal, prove any requested tests passed, resolve unfinished work or establish a final Survived/Killed disposition. The actual crash/abort and zero completed rows remain holds.

## Bounded source explanation

`RegistrationEntry` is a private sealed CLASS (physical source549); default therefore means null. The actual logged field replacement seeds the entries List with one null before construction/registration. The valid public Register path appends a real new RegistrationEntry and never removes that initial null. The original first-capacity row creates a real runner/registration and then starts the dispatcher; it is not a source-only or inert diagnostic test.

Frozen Pump iterates entries.ToArray and dereferences entry.Handle at342. A seeded null can throw there on the queued dispatcher. Pump's catch at392–397 projects the exception, stores resourceError and calls BeginStop. BeginStop sets stopping=true, takes entries.ToArray and calls CloseLocked on each entry. CloseLocked immediately writes entry.Closed at219, so that SAME seeded null throws again inside the catch's cleanup path and escapes the background callback. This source path explains the observed CloseLocked→BeginStop→Pump→Wake fatal stack without a production-source defect or an assumption that a lease/collector callback ran.

The frozen active test source hashes `eb836152bd088491bd5570cbf7323e58a8138c60862a432e03c585e34a0ce315`. It starts the runner at37 and requests CollectAsync at39, then has actual disposal in finally. The prefix proves this row was active, but does not prove which await had resumed when the background thread failed, that collection owner work ran, or that any test cleanup settled. Do not turn source path explanation into unobserved runtime milestones.

No production defensive change or testcase precondition is proposed/applied by this identity review. Root can now assess a source-specific remedy with the exact binding established. Any later manual/new-native test must retain this actual abort, source/request identity, raw summary and every unresolved hold, and must not transfer1038 to another report by number. Applied credit:0.
