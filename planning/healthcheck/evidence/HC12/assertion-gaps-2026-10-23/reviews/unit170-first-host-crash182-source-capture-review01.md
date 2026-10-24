# Current170 host crash: source and capture triage

**This is the previously investigated null-entry invariant violation, not a newly established valid-input fixture preemption. Keep it as a current runtime/crashed-host hold. No fixture or shipping-source change is proposed.** The findings below come from this current170 observation, not from transferring a historical numeric ID or result.

## Exact current evidence

- Parent append-only observed prefix: `.artifacts/hcg/active182-progress-readback01/active182-progress01/unit-complete-partition170-run01.stdout-prefix.log`, 28,871,391 bytes, SHA256 `aa9f23bf716a05fe94af63436fb7227ea3df1fe7838b32f2c665a0a02293c6c1`. This is the observed prefix, not a closed complete campaign log.
- Closed host: `.artifacts/hcg/active182-host-readback01/active182-host-observation01/Runner 0-log.host.26-10-24_11-52-31_60645_30.txt`, 466,814 bytes, SHA256 `04b63ad810cb2db45064a778cd5a75a09ed33c972330143ac919d2c7c8a4e34f`.
- Host PID1798969; parent PID1784319. Runtime10.0.12, actual testhost version18.10.1. No loaded binary/PDB identity is inferred from those version strings.
- Prefix lines369186–369191 independently assign Runner0 to its own selector1038 at11:52:31 and this exact host path. Runner1's contemporaneous selectors are not used to assign this host.
- The actual typed `TestExecution.StartWithTests` request contains190 original tests. Parsing its actual RunSettings XML finds exactly one Mutant element, id1038, with190 explicit test GUIDs. No RunAll, subset retry or guessed request association was used.
- The sole RecordStart is at11:52:34.121 for `CIS.Tests.UnitTests.Healthcheck.HealthCollectionCapacityAndFairnessBoundaryTests.FullMetricCapacityDistinguishesUpdatesFromEntirelyNewKeys`.
- The actual typed StatsChange at11:52:34.399 reports ExecutedTests=0, NewTestResults=[], and one ActiveTests row: GUID `150cb59c-8a3d-8aad-89d9-f034ec82f2d8`, display `FullMetricCapacityDistinguishesUpdatesFromEntirelyNewKeys (False,False)`. There is no RecordResult and no TestExecution.Completed message in this closed host log.
- Prefix lines369192–369200 report fatal NullReferenceException at11:52:34, then host abort at11:52:36. Stack: generated Runner.CloseLocked318 → BeginStop374 → Pump560 → Wake callback447 → ThreadPool. The fatal exception is recorded by the parent; the host log ends while the case remains active.

The parent subsequently prints `Test run for 1038 ... is success` at line369203. That native-runner completion text does not supply a completed test, normal-host lifetime, direct assertion or qualification result. There is no earlier completed assertion in this particular host to consider for source-proven earlier-assertion credit.

## Source-bound mutation

The prefix itself publishes this current instrumented branch (first occurrence at line13203 and repeated source dumps thereafter):

```csharp
private readonly List<RegistrationEntry> entries =
    (StrykerFYcd8XZW6gWB0jG.MutantControl.IsActive(1038) ? [default] : []);
```

Current frozen170 source is `snapshot/source/implementations/csharp/src/CIS.Core/Healthcheck/HealthCollectorRunner.cs`, full SHA256 `386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8` under the staged170 payload. Independently locating the unique entries declaration gives original UTF16 span `[722,724)`, original `[]`, replacement `[default]`, mutator name `Collection expression mutation`. Thus the actual run-local selector is tied to the exact current physical source slice. The prefix does not publish a full native definition record's static flag; no unstated field is invented to form a new acceptance catalog row.

This inserts a null element into an otherwise non-null `List<RegistrationEntry>` at object construction. It is not an actual public null registration or malformed owner response supplied by this test. The current active fixture file SHA is `92d3d1937932acbaecd7c5a226a0b195d6ca9043c0383e4c65fc8a16a7f4da94`; its original bool values are false,false.

## Causal path and cleanup limit

The real case builds its32 measurements and valid optional diagnostics result, constructs a runner, registers its FixedOwner, starts the runner and then requests collection. With unchanged source, those are ordinary valid public operations. The first runner Pump can already encounter the injected null at the entries scan (`entry.Handle` in original source line342). Pump's catch projects that real exception and calls BeginStop. BeginStop snapshots the entries and passes the null element to CloseLocked; CloseLocked's first `entry.Closed = true` dereference then throws again. The second exception escapes the ThreadPool callback, matching the actual fatal stack. The log does not provide an exact internal body milestone or owner counter, so this source reasoning is not a claim that a particular original await or lease callback had returned.

This differs from the repaired diagnostic-conversion cases: here the internal list invariant is already violated before the background dispatcher scans it. Moving an unrelated public diagnostic assertion before runner construction would not test the defect. Constructing an extra probe runner or stopping this runner before Start would change the original scenario. Adding production null-entry tolerance solely for this mutation is not justified by a real public-input counterexample.

Real resources cannot be declared settled. Start normally creates a real initially inert System timer. The fatal BeginStop path throws while closing entries, before reaching timer detachment/disposal and the retirement dispatch loop. The fixture's unchanged DrainAsync starts actual runner/service/registration/monitor cleanup calls and awaits their genuine Tasks with its watchdog; neither this source audit nor the crashed process proves those calls ran or returned in this host. There is no owner-death, native-handle or callback-return waiver. Any disposal or pending-work fault remains a hold.

## Existing strict compiler evidence: corroboration only

I read retained `.artifacts/hcg/unit-compiler1038-152-proposal/compiler-experiment-receipt01.json`, SHA256 `396a35d11971e574c59dbbaa02937d40ea753f09e2aecad11600b9d35206c220`. It records a successful strict original baseline (zero warnings/errors), a sole `[722,724)` `[]`→`[default]` source replacement and actual CS8625 error at line16,column57: null literal cannot convert to the non-nullable reference type. The full shipping population, warning/nullable/audit settings and failed setup history are retained there; appliedCredit=0, no native invocation and no disposition.

The adjacent operations initializer has its own retained strict experiment `.artifacts/hcg/unit-compiler1039-153-proposal/compiler-experiment-receipt01.json`, SHA256 `578de292532888399d48453d23333cab9581245d9699597796ec7eec02deb4e5`, recording actual CS8625 at line17,column55 for `[779,781)` `[]`→`[default]`. It supplies no status for the next current170 selector. The present prefix only shows Runner0 starting that next selector after the crash; it does not close that execution.

Those prior receipts corroborate that this source family is already known and violates the strict source contract. They do not transform current170's actual host crash into CompileError, kill credit, an exclusion or an equivalent mutation. No new compiler disposition or audit-policy waiver is authorized or applied.

Recommendation: retain the exact current crash/capture issue and join its eventual completed native report through the existing strict auditor. Reuse the already preserved source/strict-compiler evidence when root reviews disposition under the existing policy. Do not spend a new fixture patch, probe campaign or shipping change on this observation. All three actual campaigns remain untouched and active at this prefix observation; no complete score is inferred.
