# Unit189 four retirement aborts — bounded source/host review

The four closed hosts show binding-disposal promise waits after actual tests have begun; they do not show another whole-host startup regression. Their completed results are TimeoutException failures, and each second boundary row remains active without a completed result when the request budget aborts. All four requests remain holds. No fixture/Core change, subject build/run, remote operation, eligibility decision or kill credit was performed here.

Root-retained host-only packet33340bytes/SHA256 `c388f2793f027b671b76f291f240b720b5bd6d2b1a2b01eb94a87b3de56c2605`. Read the actual host request selector/budget, RecordStart/Result/errors and abort/exit traces; source mapping also uses the preserved prefix02 main creation/instrumentation records. There is no new parent-console or kernel inventory in this packet, so full requested/missing-set or lifetime qualification is not claimed.

| Actual selector / PID | Budget | Earlier completed failures | Active row without result | Host AbortUTC |
| --- | --- | --- | --- | --- |
|983 /2017254 |19967ms |2Lifecycle+1ConcurrentWithdrawal rows |ConcurrentWithdrawal(True), starts21:14:19.638 |21:14:21.296 |
|985 /2017273 |19967ms |2Lifecycle+1ConcurrentWithdrawal rows |ConcurrentWithdrawal(True), starts21:14:22.536 |21:14:23.640 |
|994 /2017376 |19963ms |1RetirementWithoutOutstandingWork row |RetirementWithoutOutstandingWork(False,True), starts21:14:41.520 |21:14:48.448 |
|996 /2017393 |19963ms |1RetirementWithoutOutstandingWork row |RetirementWithoutOutstandingWork(False,True), starts21:14:43.136 |21:14:50.592 |

The full FQNs are in `CIS.Tests.UnitTests.Healthcheck`: HealthcheckLifecycleQualificationTests.WithdrawingMonitorReleasesBindingRegistrationBeforeAnyOldBindingCall; HealthBindingRetirementBoundaryTests.ConcurrentWithdrawalAndFactDeliveryReleaseTheExternalBinding; and HealthBindingRetirementBoundaryTests.RetirementWithoutOutstandingWorkCompletesTheSharedBindingBarrier. The active ConcurrentWithdrawal(True) identity is GUID `1453a5c9-fc8d-8bf3-85f5-ce34873a3e04`; RetirementWithoutOutstandingWork(False,True) is `190ce1a5-8450-8f89-9c4a-d1050c53c819`. Native Source is the actual Unit189 run's UnitTests.dll; no cross-run numeric-ID outcome transfer is made.

Lifecycle failures occur at the real `await old.DisposeAsync().AsTask().WaitAsync(Deadline)` line141, after roughly3seconds each. ConcurrentWithdrawal's first row waits roughly10seconds at binding.DisposeAsync line170, with cleanup frames170/184/144 retained. RetirementWithoutOutstandingWork waits roughly10seconds at Task.WhenAll(first,second).WaitAsync line40; its cleanup frames remain68. Actual SessionStart callbacks are612/838/897/604ms, and MSTest is running from testcases before these waits. Testhost logs exiting shortly after each abort do not prove complete fixture/resource drain or normal protocol completion.

## Source-proven common cause

Shipping `HealthExecutionBinder.cs` SHA256 `1854defb571eb52d4066f53a994d5fcd5125f359660fd33ef5ccd486926aef6a` and preserved native instrumentation bind983/985 to ObserveExternalRetirement's `if(release){Release();}` and994/996 to the corresponding Retire finally. Creation records are main lines11044/11046/11078/11080: guard `release`→`!(release)` under NegateConditionMutator, or statement `Release();`→`;` under StatementMutator; native dump lines11431–11455 independently place each in its actual method.

ClaimRelease sets releaseStarted=true before the guarded call. Skipping that first Release prevents actual state.ReleaseBinding(target) and settlement completion. A later DisposeAsync calls ClaimRelease again, obtainsfalse, and returns the same still-pending settlement.Task. Thus repeated cleanup cannot repair that skipped first release. This is a concrete source explanation for the observed disposal waits; it does not authorize suppressing them, declaring resource settlement, a compiler/equivalence/nontermination disposition, or converting timeout to a direct kill.

Current fixture source hashes read: HealthBindingRetirementBoundaryTests `a415a809dbf6cadcf5f388445a20983ed7a392c9288994113632992ae4e0d198`; HealthcheckLifecycleQualificationTests `1831e9cadf03d9acd9d6d7a52f2a78888074632d14806cb82cf3a520c67060ed`. These source reads are not a new loaded-image/PDB attestation. Root may request a separately bounded ownership/terminal-contract plan later; none is implemented here.

Host hashes:983 `8cc4075771d36313f3e910d0302660a73b0884cc2fed7dea97e46066d649a437`;985 `97530eb35ba03193dd2b9a6e8888f5b871f5bea59d76aff524193223ebe73495`;994 `8d5d7415571140c3cc82c3c66b915fa104a3c3c813a86f829c83f906969a8ca0`;996 `00df702adb3ffaa5df4223023de835a5050b03de4345def140d39f03c015a69a`.
