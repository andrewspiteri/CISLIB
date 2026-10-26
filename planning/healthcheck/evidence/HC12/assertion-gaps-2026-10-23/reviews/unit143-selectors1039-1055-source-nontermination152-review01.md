# Open Unit143 selectors1039/1055: bounded source analysis

Source-only identification and terminal-invariant analysis. No Core/test build, selection, rerun, source overlay, process stop/kill, SSH, canonical/policy edit or credit change occurred. The actual143 campaign and both requests remain open. Actual active-host prefixes are still needed to establish current per-test progress; parent request silence is insufficient evidence of one stuck testcase.

Inputs are the already retained151 archive `unit143-fifth-abort151-prefix01.tar.gz` SHA5e41e4144c2a1a5ea5baa83e3eef8f062fc95162a7e7b288edb3845179e0f70b, its native parent prefix SHA38f5c31f35ee16fe60e344f20fb682a425a36a876dde35f9b9cf9c4eb7404d50, and frozen143 shipping HealthCollectorRunner.cs SHA386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8. The earlier fifth-abort note fdff3f9f5284754944cea9d155b640a893fc752e57eccb4bcf1de9c6a0b1725e binds that source and prefix. These are actual same-run creation/generated-source identities; no old report ID transfer is used, and no final report or loaded PE/PDB qualification is invented.

|Selector|Actual creation and generated dump|Frozen original source tuple|
|---|---|---|
|1039|Parent11601: CollectionExpressionMutator, []→[default];13188 selects1039 on operations initializer|HealthCollectorRunner.cs physical17, UTF16[779,781), HashSet<Operation> operations=[]→[default]|
|1055|Parent11671: NegateConditionMutator, started→!(started);13223 selects1055 on StartAsync condition|Same file physical47, UTF16[2424,2431), if(started)→if(!(started))|

##1039: sentinel operation defeats actual terminal accounting

Operation is a private sealed reference class at562. `[default]` seeds the HashSet with null. That is distinct from1038's null RegistrationEntry. No loop dereferences operations members: observed source uses Count at298/350/368, adds only a real `new Operation(...)` at358/363, and removes only that exact actual operation at537. No Clear, Remove(null), alternate collection assignment or other removal exists. Thus, under this exact single leaf, the original sentinel remains and Count cannot become0 even after every real evaluation finishes.

The real stop predicate at298 requires operations.Count==0. Consequently stopCompletion cannot be completed by SignalProgress, independently of timer disposal or callback speed. This is a precise subject terminal-accounting violation, not a proof that a fixture resource was never joined and not evidence of CPU spinning. The frozen first Capacity row configures MaxConcurrentCollections=1 (its Service helper); the sentinel also makes operations.Count<capacity false, so that row's real CollectAsync request cannot dispatch an owner. With higher capacity, real operations may proceed but the stop sentinel remains.

The actual parent selects1039 alone for Runner0 at368686,2026-10-24T04:03:58.9599776Z. It declares4,524,111ms at368688 and opens host path `Runner 0-log.host.26-10-24_04-03-59_46480_25.txt` at368690. The full original first row has a10s WaitAsync around collection and a separate10s watchdog around actual combined cleanup; those watchdogs can return errors while actual runner stop remains unfinished. They do not supply success or remove the sentinel. A request containing many cases can therefore continue collecting bounded failures for a long time; source alone does not establish the active row at04:24.

##1055: first Start returns a promise with no reachable producer

The runner's bool started initially equals false. The single condition inversion makes the first uncanceled valid Start return startCompletion.Task.WaitAsync(waitCancellation) inside the gate at47. It never reaches started=starting=true, timer creation, Wake or either startCompletion setter. A subsequent valid Start sees started still false and repeats the early return. Exhaustive source references show the only two completion producers are TrySetResult64 and TrySetException77, both in the skipped startup body. Stop does not complete startCompletion. Caller cancellation can settle only that caller's wait and does not perform startup.

This establishes a genuine noncompletion path in the subject under that exact condition, rather than a test harness needing arbitrary more time. It does not establish the current process is blocked on this task: the original first row wraps Start in10s WaitAsync, then its real runner stop can ordinarily settle because started/starting remain false and operations is actually empty for this mutant. Later cases may have other bounded or unbounded observations; their actual prefix is needed.

Parent373306 selects1055 alone for Runner1 at04:07:36.5700903Z,373308 declares4,522,984ms,373310 opens `Runner 1-log.host.26-10-24_04-07-37_03082_23.txt`. Both native request deadlines are roughly75.4minutes, retaining the maintained4,500,000ms additional timeout. At root's04:24 observation neither deadline had elapsed. Parent silence alone is not evidence that the existing watchdog failed or that a timeout disposition already exists.

##1038 nullable/compiler premise, with its limits

The original snapshot Directory.Build.props SHAfe0ec1ac2f0c4b213217a5758bad02699a8304bdcf864a4cce9e7db4ae0d8dae enables Nullable and TreatWarningsAsErrors. The actual baseline property dump repeats Nullable=enable, TreatWarningsAsErrors=true, WarningLevel=10, NoWarn=1701;1702. The1038 replacement inserts a null default element into List<RegistrationEntry>, whose element type is explicitly nonnullable, with no null-forgiving annotation. It is valid runtime C# syntax and a clear nullable-analysis violation candidate under those original strict settings.1039 has the analogous nullable Operation element.

This review did NOT compile it, inspect a new compiler diagnostic, prove the exact diagnostic ID/severity or derive Stryker's effective nullable-flow diagnostic/rollback options from MSBuild properties. The recorded native request and fatal stack must remain intact. Do not exclude1038/1039 as CompileError on the basis of this source premise. A separately authorized exact-source strict compiler experiment with actual warning settings/diagnostic receipt would be necessary to establish that narrower claim; it would still not rewrite this running campaign or its actual abort.

##Receiving and next evidence boundary

Request fresh finite prefixes of the exact two named active hosts plus current parent and original1389 cohort, with time and SHA/size pins. Read typed StartWithTests, the one actual selector parameter, requested/declared GUID equality, latest RecordStart/StatsChange/NewTestResults, and actual completed vs active rows. Distinguish progression through failed cases, a live unfinished test/callback, and any actual host exit/callback. A finite prefix cannot certify final report/raw outcome, normal host, full original/fresh restoration or complete kernel closure.

The Core guide's possible zero-numerator treatment of confirmed nontermination is a separate disposition/qualification rule. Current143 controller still requires every host normal and zero integrity issues. No policy change, timeout-as-kill, acceptance of unfinished actual work, relaxed capture or credit is authorized by this analysis. Source explanation can guide an exact fixture plan after actual host evidence; it does not waive the current holds.
