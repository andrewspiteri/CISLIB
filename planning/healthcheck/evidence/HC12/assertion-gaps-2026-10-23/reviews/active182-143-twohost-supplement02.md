# Actual closed143 two-host supplement

Read-only source/recorded-evidence supplement to `active182-abort-prefix-independent-review01.md` (corrected note SHA `eb2a232b4d72055971b47240909c2a1b771ac0bd3e531e569f6092f024d7ff9c`). That earlier note and its inputs were not changed. No utility build/entry/replay, subject/shared/Core build, SSH, canonical/source edit, policy change, exclusion or credit. These two host logs add process and recorded-result identity; they do not add kernel closure or a new full NativeAudit execution.

Root's physically retained actual packet is `C:/miscwork/portfolio/cislib/.artifacts/hcg/active182-143-closed-hosts01.tar.gz`,134642bytes,SHA `e708f6a41fa55b10c4f7c5f07eec2995fb97bb8d2a8cf603044b1e4fa8259a83`. The exact two members read are under `C:/miscwork/portfolio/cislib/.artifacts/hcg/active182-143-host-readback01/active182-143-closed-hosts01`:

| Original diagnostic filename | Bytes | SHA-256 | Actual hostPID / parentPID |
| --- | --- | --- | --- |
| Runner 0-log.host.26-10-24_09-23-00_25343_44.txt | 1314105 | 494a789ed406f80f024645f7d0ed3f13afa85c96cf47e27896115ca03e1c1276 | 1779540 /1751794 |
| Runner 0-log.host.26-10-24_10-26-20_01429_25.txt | 1313921 | b17fb3052ebe199740612148d89bdeaa1f51ef34c2295130395c246ab86fefd3 | 1781851 /1751794 |

Both header/DefaultEngineInvoker argument records report VSTest18.10.1, runtime `/data/cis-healthcheck-20261021-01a15a72/dotnet/shared/Microsoft.NETCore.App/10.0.12`, roleclient, parentprocessid1751794 and the matching original143 `native/logs/` diagnostic path. Actual first trace times are09:23:00.772 and10:26:20.460 UTC. These are observed host-header claims tied to matching original paths, not independent kernel-verified executable/environment/parent lifetime facts.

## Session and result order

The already pinned143 parent stdout prefix links selector1279 at09:22:59 to the first filename, fatal09:24:46, explicit crash-abort09:24:47 and later next selection1280. It separately links selector1294 at10:26:19 to the second filename, fatal10:28:05, crash-abort10:28:06 and next selection1297. The matching two actual diagnostic files, different host PIDs and nonoverlapping times confirm two different recorded sessions. The shared parent1751794 does not collapse them into one host. Exact source creation/body mapping remains the original note's SignalProgress pattern/condition changes, with shippingRunner SHA386c3d5c.

For EACH host, ordinary read-only log inspection finds69 `TestExecutionRecorder.RecordStart`,69 `RecordEnd` and69 `RecordResult` events. Those are counts of those specific local adapter events, not a claim that69 complete unique GUIDs were rejoined by a new parser, that69 results reached the parent, or that69 failures meet direct-assertion policy.

The last transmitted `TestExecution.StatsChange` in each file reports `ExecutedTests:63`/`Stats:{Failed:63}`. The messages are at first-file1684 (09:24:46.095), second-file1683 (10:28:05.283). Their `NewTestResults` contains the preceding `ApplicationStoppingRejectsNewStateAttachmentsWhileFinalDirectReportsRemainAvailable` result, GUID14bac457-8ed5-8595-9ecd-21df68ae0df4. Their `ActiveTests` contains:

- GUID `176213a5-ca3a-87c6-b757-3cd6e20975c7`;
- FQN `CIS.Tests.UnitTests.Healthcheck.HealthCollectorReceivingAndDueSelectionQualificationTests.ARejectedNullCollectionTargetIdentifiesTheReceivingArgumentWithoutChangingTheAttachment`;
- display `ARejectedNullCollectionTargetIdentifiesTheReceivingArgumentWithoutChangingTheAttachment ("reserve")`.

This is the LAST FORWARDED active-case observation, not the final active test at the instant of process death. Both files continue with local RecordEnd/RecordResult for that row, its `"withdraw"` counterpart, two PeriodicCompletion rows and two UnexpectedTimerChange rows. There is no later StatsChange transmitting those six local result events. The local tail ends with `UnexpectedTimerChangeFailureStopsAdmissionAndReportsActualResourceFailure (True)`, GUID1dcdba41-0cc7-8f01-97d5-4fa4593e79b3: first file has RecordStart1792/RecordEnd1796/RecordResult1809 (09:24:46.223–.233); second has1791/1795/1808 (10:28:05.395–.403). Thus it would be incorrect to describe forwarded176213a5 as an unfinished case merely because it is the last protocol ActiveTests value.

Neither host log contains `TestExecution.Completed`, a normal SessionEnd or a normal testhost exit observation. Each ends with local test-cache diagnostics, including `InProgressTests is null` and no matching in-progress test for the final True row. The same cache warning also occurs earlier, so its cause is not assigned to the final abort here. The stdout has the actual crash-abort; the missing final host protocol does not erase that evidence. Kernel exit/signal and parent callback/reap remain pending separate evidence.

Preceding failed result records are genuinely present before the fatal-second tail. For example, first-file125/138 and second-file125/138 show the earliest FullMetricCapacity row's local completion/result; the following StatsChange307 carries its failed result object. Its actual ErrorMessage is a NullReferenceException, and ErrorStackTrace begins `SignalProgress`423→`BeginStop`388→`DisposeAsync`234→the fixture's real DrainRunner/DrainAsync. This is a runtime cleanup failure, not an inferred direct assertion. No new classifier was run for this supplement; no blanket claim is made about all63 transmitted or69 local failures. Source-proven earlier-assertion eligibility, if later needed, still requires the actual exact assertion/error/source/position join and does not clear either abnormal host. Seven observed143 abort holds remain unchanged.

## Exact next kernel and parent inputs

The immutable143 controller explicitly configures native strace prefix `<original143run>/kernel/actual-process` (Program86). The minimal known initial files to preserve are therefore:

```text
/data/cis-healthcheck-20261021-01a15a72/unit-complete-partition143-run01/kernel/actual-process.1779540
/data/cis-healthcheck-20261021-01a15a72/unit-complete-partition143-run01/kernel/actual-process.1781851
/data/cis-healthcheck-20261021-01a15a72/unit-complete-partition143-run01/kernel/actual-process.1751794
/data/cis-healthcheck-20261021-01a15a72/unit-complete-partition143-run01/native/logs/Runner 0-log.txt
```

The first two should bind actual exec arguments, host lifetime and terminal signal/exit if those are in their original process files. The long-lived parent1751794 and Runner0 records are still open under the active campaign, so any captured copies must be explicitly bounded prefixes with observation time and actual byte/hash pins. Their exact clone/wait/callback records are needed to join launch, PID, abnormal completion and reaping. These three process files alone are not presumed to cover every host thread/descendant or process creator; preserve further original kernel files identified by actual clone/thread/parent joins before asserting a complete lifetime. Do not substitute managed trace thread numbers for Linux TIDs.

This request retains existing original evidence and requires no new campaign, host retry, timer/timeout change or source overlay. No actual kernel signal/exit/normality, complete role/capture audit or mutation qualification credit is claimed from this host-only supplement.
