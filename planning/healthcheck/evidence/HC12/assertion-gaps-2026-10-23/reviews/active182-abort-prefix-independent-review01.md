# Bounded actual182 abort-prefix review

READ-ONLY source/recorded-evidence review. No utility build, subject/controller/adapter entry, SSH, shared/canonical change, native retry, exclusion, classifier change or score credit. Root's full campaigns remain active and their eventual complete reports/audits are unavailable here. The native stdout prefixes are finite copies of open logs; their last lines are partial selected-case lists, not complete terminal records. The observation sidecars say2026-10-24T12:07:41Z for both start/end; this is not an atomic joint snapshot or a promise of later current state.

Actual local inputs under `C:/miscwork/portfolio/cislib/.artifacts/hcg/active182-progress-readback01/active182-progress01`:

| Input | Bytes | SHA-256 |
| --- | --- | --- |
| unit-complete-partition143-run01.stdout-prefix.log | 31271522 | 26b3164877f3eb5c0b4e7e787f660e01a1789a54e9ee86c0f002e079b14570c1 |
| unit-complete-partition170-run01.stdout-prefix.log | 28871391 | aa9f23bf716a05fe94af63436fb7227ea3df1fe7838b32f2c665a0a02293c6c1 |

Root-provided progress archive `active182-progress-readonly01.tar.gz` is7333898bytes/SHA `c6976f99d589a8bdc84d185d55a6d5ce955d5f1d7f9da312db11091181511a68`. Source for the logical mapping is frozen `assertion143-snapshot01/source/implementations/csharp/src/CIS.Core/Healthcheck/HealthCollectorRunner.cs`, SHA `386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8`.

## Distinct older143 sessions

Seven separate selected sessions have a per-runner request, their own diagnostic-path announcement, an unhandled exception, one explicit host-crash abort, per-runner completion/session-finished and a later selected session. The two added sessions are distinct from the historically preserved first five. This is stronger than an unqualified text-match count but does not replace typed host/parent/kernel lifetime receiving.

All times below are24October UTC. Host names are relative to the original143 `native/logs/` directory, not files newly generated here.

| Actual selector / runner | Request / fatal / abort | Actual diagnostic file | Prefix anchors |
| --- | --- | --- | --- |
| 353 /0 | 03:11:28 /03:12:37 /03:12:39 | Runner 0-log.host.26-10-24_03-11-28_93997_15.txt | 228510,228514,229825,229833,229836 |
| 371 /0 | 03:12:39 /03:13:49 /03:13:51 | Runner 0-log.host.26-10-24_03-12-40_19270_16.txt | 229872,229876,229877,229885,229888 |
| 383 /1 | 03:20:01 /03:21:10 /03:21:12 | Runner 1-log.host.26-10-24_03-20-01_41804_15.txt | 231807,231811,232767,232775,232778 |
| 396 /1 | 03:21:22 /03:22:31 /03:22:33 | Runner 1-log.host.26-10-24_03-21-22_89696_13.txt | 233110,233114,234324,234332,234335 |
| 1038 /0 | 04:03:53 /04:03:57 /04:03:58 | Runner 0-log.host.26-10-24_04-03-53_62861_25.txt | 368686,368690,368691,368699,368702 |
| 1279 /0 | 09:22:59 /09:24:46 /09:24:47 | Runner 0-log.host.26-10-24_09-23-00_25343_44.txt | 405187,405191,405192,405208,405211 |
| 1294 /0 | 10:26:19 /10:28:05 /10:28:06 | Runner 0-log.host.26-10-24_10-26-20_01429_25.txt | 409861,409865,410247,410263,410266 |

Selectors are only original-prefix lookup keys, not stable source identities, transferred outcomes or credit. The first four fatal stacks are HealthDiagnostic message validation, identifier validation, ProjectRuntimeText range and ProjectRuntimeText range respectively, all escaping background Pump557. Selector1038 escapes CloseLocked318→BeginStop374→Pump560. New1279 and1294 both escape SignalProgress423→Pump567/451→Wake447. Each of those two new sessions prints the same fatal stack twice in the same second (405192/405200 and410247/410255). They add two sessions, not four, and are not duplicate copies of the earlier five aborts.

## New143 source-specific hold mapping

The creation records at12242/12257 and final printed instrumented body at120478/120482 bind the two selected changes:

```csharp
// Original SignalProgress(RegistrationEntry? entry = null), frozen lines291–301:
entrySettled = entry is { Closed: true, StateSettled: true, Current: null };
if (entrySettled) { entry!.Disposal.TrySetResult(); }

// Actual generated1279 arm:
entry is not { Closed: true, StateSettled: true, Current: null }
// Actual generated1294 arm:
!(entrySettled)
```

Frozen Pump finally at399–403 calls `SignalProgress()` with the real default-null argument. A null does not match the original positive property pattern; either selected change makes its following disposal branch true. The null-forgiving operator adds no runtime null check. Dereferencing that null at `entry!.Disposal` therefore gives a direct source explanation matching the actual background fatal stack at instrumented423. The default-null calls also exist at StartAsync81, Register-finally136, BeginStop273 and timer-disposal-finally287; no claim is made that all those call sites executed in either closed host. The actual trace names Pump, and those source/stack facts suffice to distinguish this failure mechanism from an unspecified harness timeout.

This is a concrete subject-background failure, with two different source edits. It is not the separate1038/1039 collection-initializer nullable compiler question. No new compiler experiment, equivalent disposition, nontermination attestation or host-normality exception is inferred. All seven observed abort sessions remain receiving holds under current all-normal/zero-issue requirements, regardless of any completed assertions or raw textual `failed` labels. The original1038 post-abort label is `success`, which likewise does not certify host closure.

To complete the two newly identified143 lifetime records, root should preserve the exact two diagnostic files above, their actual Runner0 parent protocol/callback records, and the exact kernel exec/exit/reap files after binding actual PIDs. These are needed for active GUID/result order, original terminal state and complete lifetime identity. This prefix alone contains neither those PIDs nor their kernel closure. No new native run is needed to retain already closed originals.

## Current170 separate observation

The170 prefix contains one explicit aborted session: actual1038 selected369187 at11:52:31, host announcement369191, NRE369192 at11:52:34 and abort369200 at11:52:36, followed by completion369201/session-finished369202 and next1039 selection369205. Its stack is CloseLocked318→BeginStop374→Pump560. This is a separate campaign/host hold and must not be collapsed into older1431038 or cleared using later fixture/manual evidence.

The later copied actual host file is `C:/miscwork/portfolio/cislib/.artifacts/hcg/active182-host-readback01/active182-host-observation01/Runner 0-log.host.26-10-24_11-52-31_60645_30.txt`,466814bytes/SHA `04b63ad810cb2db45064a778cd5a75a09ed33c972330143ac919d2c7c8a4e34f`. Its header reports actual testhostPID1798969, parent1784319, VSTest18.10.1/runtime10.0.12 and the matching original diagnostic path. Its last observed StatsChange has no new result and active GUID150cb59c-8a3d-8aad-89d9-f034ec82f2d8, `FullMetricCapacityDistinguishesUpdatesFromEntirelyNewKeys (False,False)`. This supplies no completed assertion for that active case, no whole-host terminal fact and no independent kernel/PID audit. Unit author owns that row/source triage. Host archive26550bytes/SHA `6150ad596e775153180072df75fe7cefc152a045ceee457486ffcd7860aef151` remains separate.

Latest per-runner selections in the finite prefixes are143Runner0=1316 at11:23:26/Runner1=1321 at11:53:23 and170Runner0=1039 at11:52:36/Runner1=1055 at11:56:07. No later completion for those selections is visible here. The prefixes end during selected GUID-list output, so this is not proof those hosts were still active at copy end, that a deadline expired or that nontermination was established. No campaign final result, complete typed/capture audit or score update is asserted.
