# Unit startup investigation, 24 October 2026

The timeout/parallelism changes were committed as `bc08eec1`. Investigation then
found a substantial regression in usable results with eight traced workers and
short adaptive session budgets. This is execution evidence, not a new mutation
score or a finding that the shipping implementation is defective.

## What the native records establish

The closed two-worker185 console logs contain 553 received completion frames:
552 non-aborted and one aborted. The finite eight-worker187 observation contains
798 frames: 187 non-aborted and 611 aborted. These are actual request-completion
frames, including baseline/infrastructure requests, not distinct mutants or
accepted kills. Forwarded protocol duplicates and stale timeout callbacks for
already-disposed requests are excluded. Even a non-aborted frame needs the
remaining identity, result, ownership and capture checks before qualification.

One exact187 request, selector1370, asked for 151 GUID/FQN cases with a 20,390 ms
session budget. The host's in-process collector initialization callback took
7,928 ms. Its abort arrived before the first recorded test start; the completed
frame reported zero completed results and `IsAborted=true`. A late test start
raced after the abort, so zero completed results does not prove that no body
ever began or that fixture work drained. The process subsequently exited zero;
that exit does not make the aborted test session normal.

The source-local1370 mutation negates
`operation is null && operations.Count < capacity` in `HealthCollectorRunner.cs`,
UTF-16 span [15891,15939), source SHA256
`386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8`.
The exact semantic request/host receipt validates all 151 identities and the
zero-result aborted completion. It grants no mutation credit.

Stryker4.16.0 calculates each budget using initial baseline elapsed time,
aggregated initial case times, selected initial case times and the configured
additional allowance. The baseline precedes instrumentation. Its initialization
allowance therefore does not establish the cost of fresh instrumented hosts
with eight concurrent workers. Several observed budgets were 15–21 seconds.
The server had approximately 122 GB available RAM, no swap use and no session
CPU/memory cap. Increasing a memory allowance does not address this finding.

## Tracing and the bounded original control

The existing `strace -ff ... -e trace=%process` invocation filters output but,
without seccomp filtering, still stops tracees on unselected system calls.
Actual tracing processes were consuming roughly one CPU core each. Installed
strace6.8 documents `--seccomp-bpf`; a separate process-status control confirmed
filter activation. The option retains the process filter and full argv capture,
can fall back to ordinary tracing, and implicitly enables kill-on-exit.

A first bounded control used the actual instrumented Unit binaries, the pinned
Stryker collector, inactive mutant `-1`, eight workers and the same 20,390 ms
budget. Plain tracing produced eight zero-result aborts; optimized tracing
produced three passes and five zero-result aborts. All processes were reaped
and streams drained without an outer deadline expiry. BPF activation was
observed in all eight optimized test hosts.

That first control used SDK VSTest's source request rather than Stryker's cached
selected-test request. It consequently added full-assembly discovery, finding
3,668 cases and reporting missing unrelated discovery fixture inputs from the
relocated binary directory. This is a material confound. Its results do not
establish that optimized tracing fixes the actual mutation execution path.
A replacement diagnostic therefore used the actual cached TestCase metadata
and verified `StartWithTests`, without source-discovery fallback.

The first selected-test attempt failed before launching a test host: its
diagnostic launcher resolved system dotnet10.0.9 while the console required
10.0.12. That failed attempt remains intact. The corrected launcher prepends
the pinned dotnet directory to PATH, explicitly supplies the console runtime
environment and limits diagnostic connection setup to15 seconds. It uses a new
output root; no failed result was rewritten.

The corrected selected02 controls both passed8/8. A separate C# receiving check
and independent review verified exactly one passing result and one identical
console/host completion per worker, no source discovery, actual pinned dotnet
executables/runtime10.0.12, clean host exit/drain and unchanged fixture files.
All eight optimized hosts were observed with seccomp filtering active. Plain
tracing took17.336 seconds for the batch; optimized tracing took11.308 seconds.
The latest completed request was11.767 seconds after its budget began with
plain tracing and8.716 seconds with optimized tracing.

This is a fixed20,390 ms, single-case control in a quiet server state. It does
not establish steady-state throughput, explain every mutant hang or validate
eight workers with an adaptive1,000 ms additional allowance. Optimizing the
baseline can also shrink later adaptive budgets. The failed source-request
control ran under different background load and is not a one-variable timing
comparison with this corrected control.

## Campaign state and gate

The187 native trees were stopped by the fixed, identity-checked Stop188 utility
at 22:18:53 Europe/Malta (20:18:53 UTC). Both parent controllers remained alive
to retain their actual outcomes, then closed with exit1. Native exits were137;
both report incomplete execution and failed native DLL restoration checks after
the forced stop. These are preserved holds, with no cleanup or completion claim.
The full closed roots were archived, compared successfully and copied locally
with matching SHA256:292,641,659 bytes,
`055d02cec5bc1653ab752ff582a10d95625de1c427842f4121606ce3a532e963`.
They remain on the server and in local
`.artifacts/hcg/closed-parallel187-retained188-01`. No raw root was deleted.

The prepared188 Unit/Pipes derivatives add only the tracing option and new
physical roots; they remain unstaged and unlaunched. Their eight-worker configurations, complete scopes,
source families, coverage modes, no-bail rule, timeout policy and 90% threshold
are unchanged. No implementation or fixture assertion has been weakened.

Unit instead uses [profile189](../../../qualification/gate-closure/stable/README.md):
two workers, the maintained10,000 ms additional allowance, separate mutants and
optimized tracing. The existing canonical validator accepts this configuration
by removing only the added no-mixing flag. No policy or threshold was relaxed.
All1,171 current C# source/project/configuration inputs matched the passing187
family origin, including972 C# files; no new Developer189 result is claimed.
The new controller/stager passed independent source review, private builds,
PE/PDB/source checks and actual staging. All2,015 uploaded files matched before
the payload became read-only.

`unit-complete-stable189-run01` started on192.168.88.161 under the existing
`/data/cis-healthcheck-20261021-01a15a72` root at22:44:05 Europe/Malta
(20:44:05 UTC). Wrapper/controller PIDs:2004496/2004499. Native tracer/Stryker
PIDs:2005197/2005202. Input SHA256:
`9afc176e6adc40efd4ee4dc4881b8ceb4f66bd1ec26f60da9f51ee133ed55a0b`.
Controller SHA256:
`adeb3bfd1a211e7a19159043e3b13b98cbd783ccc9ea92d7460d3419b95c7738`.
Its fresh original baseline passed all1,390 Healthcheck Unit cases with zero
failures/skips; actual configuration acceptance and tracing preflight passed.
The live native Stryker process was observed with seccomp2/filter1. The full
mutation campaign and its post-run audits remain outstanding.

The first immutable parent-log prefix contains106 positive-mutant requests,
104 completed and two still open. All104 completions are non-aborted,
non-cancelled, Error-null and return every requested GUID/FQN/display/source
identity exactly once. These are completed result sets, not104 passing tests,
qualified mutants or assertion kills.

A later copied prefix contains285 positive-mutant requests,283 completed and
two open. Of the completions,281 have the same full-result properties and two
are aborted. Both aborted hosts executed substantial test bodies: selector433
returned264 results before stopping in
`ConcurrentReportsAndWithdrawalPublishOnlyCompleteSnapshotVersions`;
selector494 returned730 results before stopping during the second
`ClosingTheTransportPrecedesSettlementOfItsActuallyRunningSource` row. Their
session budgets were32,099 ms and33,046 ms. This is a different remaining
symptom from the earlier zero-result startup aborts. Exact assertion and owner
settlement analysis remains required; no timeout kill credit is inferred.

The exact closed-host receiving check establishes the next two fixture issues:

* Selector433 removes the `HealthMonitorCounts` constructor assignments. The
  request asks for733 cases and completes264 (249 passed/15 failed), leaving469
  missing. Its active case starts at20:54:24.828 UTC. A snapshot-count assertion
  can fail before the reader reaches the next barrier; `Task.WhenAll` then
  waits for the other participant's30-second barrier deadline. The global
  abort arrives at20:54:48.638, before that wait can end. Fifteen earlier
  count assertions already failed; this receipt grants them no automatic credit.
* Selector494 removes the `HealthcheckOptions.ValidatedCopy` initializer,
  restoring `PipeEnabled=false` and other defaults. Server startup legitimately
  opens zero slots under those mutated settings. The first active-cancellation
  row consequently waits15.026 seconds trying to connect, then returns an
  `OperationCanceledException` from `ConnectInternal`, rather than reaching its
  intended source gate. The second row starts at20:56:25.072 UTC and is cut
  short by the global abort at20:56:30.100. Of806 requested cases,730 complete
  (709 passed/21 failed) and76 are missing.

The collector startup callbacks take723 ms and776 ms in these hosts. The
request identities, unique completed results and active missing cases match
the parent and host records. These are body/fixture-progress failures with
distinct source causes, rather than the earlier wholesale startup loss.
The two fixture fixes below are now adopted locally. The live189 payload and
its frozen source remain unchanged. Unknown failures, unfinished real work
and capture obligations remain holds.

## Adopted fixture corrections

The concurrent snapshot test now cancels only its own barrier/signal waits
after recording a real participant failure. Both actual participants must
return, and actual service disposal must finish, before that failure is
re-thrown. All32 rounds and normal-path assertions remain. A single original
exception is preserved; multiple real failures remain aggregated. Production
operations receive no fixture cancellation token.

The pipe test now verifies its actual detached transport settings before
creating a server or client. An enabled/name/client-count/request-budget
copy assertion fails promptly if the exact options mutation removes its
prerequisite. The original options still reach the real server constructor;
the connected source, IO, timer and ownership checks remain unchanged.

Independent source and actual-run peers reviewed both fixes. Isolated Windows
controls use unchanged original Core and the exact source-local one-leaf
variants, keeping each fixture closure identical apart from its Core pair:

| Scenario | Original | Exact variant | Settlement |
| --- | --- | --- | --- |
| Concurrent snapshots | 1 passed,50.9130 ms | 1 intended count assertion failed,45.9790 ms TRX /65.7708 ms start-to-end | Both real participants and service joined; natural native1, reaped/drained, no expiry/kill/holds |
| Pipe stop/dispose | 2 passed,54.9968 /5.5418 ms | 2 intended enabled-option assertions failed,26.1432 /0.6537 ms | Actual service cleanup joined before assertions returned; natural native1, reaped/drained, no expiry/kill/holds |

A separate controlled concurrent-participant `IOException` remains that exact
runtime error after the actual peer returns, rather than becoming an assertion
or disappearing behind fixture cancellation. It finishes in34.5338 ms with
natural native1 and no supervisor holds. These diagnostic controls grant no
mutation credit and are not Linux campaign qualification.

The concurrent controls additionally pass full physical/source receiving for
11 PE/PDB pairs. One old TestSupport generated document is reconstructed from
the actual original assembly's informational version, matches its full PDB
checksum and is explicitly labelled reconstructed. Earlier failed metadata
attempts remain intact. The pipe controls have a narrower successful receipt:
both Core PE/PDB pairs and all146 shipping source documents match; their
earlier full historical generated-document check remains held on a changed
shared `AssemblyInfo.cs`. The three unclaimed generated documents are listed
explicitly. No source check was waived or failed attempt rewritten.

The maintained normal Healthcheck entry point passes all1,390 cases after
adoption, zero failed/skipped and zero build warnings/errors. The fresh full
Developer189 checkpoint passed at23:39:14 Europe/Malta (21:39:14 UTC):
10,315 cases, zero failed/skipped/issues. Its four native families contain
5,299 Unit,2,323 Integration,2,664 Architecture and29 Business cases.
Exactly two canonical test files changed; all146 shipping source files remain
unchanged. The source origin is
`a3fd7752b99ac1c7a406eabd8568b97688403307790e037b17f11db3f39b379a`.
Root's post-run freeze and family binding both pass: all1,171 current C#
inputs/972 C# files, three complete PE/PDB pairs and the actual501 Unit/121
Integration selected families. This is observed source/family evidence,
with no retrospective loaded-image or mutation qualification claim.

The later four-host [retirement diagnosis](retirement-source-review03.md)
identifies another unresolved family. Guard negation or statement removal
skips `Release()` in `ObserveExternalRetirement` or `Retire`, after
`ClaimRelease()` has already set its flag. Later binding disposal consequently
returns the same pending settlement task. Actual3-second/10-second body waits
then accumulate across rows before19.96-second session aborts. Startup is
604–897 ms. Those are genuine mutation-induced disposal-progress failures;
they require a separate terminal-contract/ownership correction. Their timeout,
unfinished-work and missing-result holds remain intact. Later live requests
can reveal additional failures; the281/283 prefix is a dated observation,
not a current or final campaign success ratio.

The [next bounded retirement experiment](retirement-next-diagnostic.md) is
source-only: one external-monitor False/False row and exact Retire-finally
release omission. It requires real state/monitor/service joins and death of
the complete private ownership graph. A non-null actual settlement task,
unknown fault, live owner or unfinished operation stays held. No new fixture,
subject run or settlement exemption is implemented by that plan.

The live campaign still uses the frozen pre-fix fixtures. At21:41:28 UTC its
main log has784 timeout-budget dispatch lines and102 actual abort lines;
neither is a final distinct-mutant count. Later retained hosts also show
collector progress failures: source-local1334 completes50 cases before aborting
with `PeriodicCompletionAcceptsTheLastRepresentableDueTimeAndRetiresOnOverflow`
active;1336 completes one failed case before aborting with
`CompletionWhileTimerChangesArePendingSchedulesTheEarliestRealPeriodicEligibility`
active. Those closed-host snapshots and the retirement group demonstrate that
Unit execution problems remain. Their complete source/ownership/eligibility
analysis is pending. At that observation the controller/tracer/Stryker PIDs
remain2004499/2005197/2005202; owned bytes total approximately2.010 GB and
actual `/data` free space is6.849 GB. The immutable4 GB cap/2 GB reserve remain.

The existing4 GB owned-output bound also remains unchanged. Two measured
incremental storage samples indicate a conditional risk of reaching it before
completion; request sizes vary substantially by source family. Keep observing
actual growth, preserve full raw capture and retain any guard stop as a hold.
That estimate does not validate capacity for a replacement profile. Preserve
the immutable current bound and raw capture when making the next measured
capacity decision.

Pipes remains stopped. Its187 parent logs contain86 completion frames:41 Unit
frames with501 executed/non-aborted, and45 Integration frames,44 of which
aborted after58–90 executed cases. That is a different symptom from zero-result
Unit startup aborts. Mutant-body, child-process and cleanup causes remain
unresolved; no causal startup-regression or normal-host claim follows from
those frame counts.

The accepted historical union remains **1,591/3,065 = 51.908646%, unqualified**.
Neither faster dispatch, timeout, partial completion nor a diagnostic control
changes that score. Earlier completed assertions remain eligible only under
the user-approved, source-proven assertion rule; unresolved abort and unfinished
work holds remain.

## Retained investigation inputs

All packets below are immutable, hash-checked local copies. The preparation
packet has a separate859-file readback receipt. It includes the private C#
sources/binaries, failed diagnostic attempts, source and actual receiving
reviews, the uploaded Unit189 payload, inventories and reviewed inputs. The
original whole stopped187 archive remains under `.artifacts` and on Ubuntu;
the smaller stopped-campaign metadata packet below links its receipt.

| Packet in `retained/` | Bytes | SHA256 |
| --- | ---: | --- |
| `proof-and-unit189-preparation01.tar.gz` | 16,737,720 | `e72fb04b702dad9546497a460983b7c3d328f610001edea487789d2003b6ad85` |
| `startup-observation01.tar.gz` | 36,590,877 | `a07312b0f4c52196a90ea7f7eb2ca548702b0cc354ee4d6eb867ad7837d5e562` |
| `sdk-control01.tar.gz` | 5,953,911 | `41bfeb33a3b6a3cfaf09b6d63881a09470540538cfc14cec03eea1faa542e9d0` |
| `selected-control-attempts01.tar.gz` | 2,117,779 | `72a4a0e2b3fcc6f2106ca502bdb99fce45011e19cfda7199564c61edbfccd3ff` |
| `stopped-campaigns-metadata01.tar.gz` | 7,406 | `2bfc8b3d098776a398a10802ea83149be21f341fef906dd095eb3f1ec2d7c29b` |
| `unit189-progress01.tar.gz` | 8,811,240 | `05e802aea87d0729291c40e7a9453d600e0be7aec21b48eedd85085fd2641e24` |
| `unit189-progress-review01.tar.gz` | 1,538,645 | `b4859636f4ab093988c36274ba814f8c695f990d795ae690db8e37d025182d9a` |
| `unit189-progress02.tar.gz` | 23,366,895 | `28ebf88217ccb6280a4e57c2cb1d03a95c1063660dc702a9613cea69a3b40fd2` |
| `unit189-aborts02-review.tar.gz` | 5,028,597 | `f0939431cfd432c979ce838fb15d8e00e19dbd3595f966d795c74c702398b6f8` |
| `source-peer-retirement-storage03.tar.gz` | 36,891 | `9372c64e248bf6eda601fd09ef5e3b0ddcf01fbfe751be0c51e8a65c6c86e42b` |
| `fixture-controls189-01.tar.gz` | 39,855,977 | `060ec5a80fc91f0ffab9952e63a52ff371603c5d9d192378e2631e62b46211f2` |
| `unit189-late-runtime04.tar.gz` | 55,448 | `d5e678d85c1c1f4d1a7e7d1247a582db130f03b09006757b79c656a0f72c0115` |
| `developer189-and-family-origin01.tar.gz` | 17,890,399 | `f3fc319afed31c582194a798994a04df09fcafcf35db5207d4cd74c85287f704` |

The progress-review01 packet compresses the20,775,387-byte C# progress receipt
(`160c88801fecc5572f1e0ac64970e12418510a4e4506da60aa990e2fe23b9a9f`)
and its independent peer note. Both extracted files matched their originals
before the uncompressed tracked-location duplicate was removed. The original
ignored diagnostic output remains intact.

The second review packet retains the later full progress projection, bounded
433/494 C# receiving source/build/entry and all requested/missing/failed case
rows. All43 extracted regular files matched their originals, with no extras.
Its exact host/request/result observations are separate from the source repair
and future original/one-leaf validation evidence.

The fixture-controls packet retains both complete private proposals, including
all sources, builds, failed utility attempts, original/negative/unknown-error
raw runs, physical/source receipts and final independent peers. Its separate
manifest is SHA256
`8848a06beca13fb619372fae3f99f3ebd56f32d98e2e3c12018cbd3ae8636496`.
The C# readback verifies all5,434 regular files/157,764,131 uncompressed bytes
with no extra or missing files. Only the verified temporary extraction was
removed; the original controls and retained archive remain intact.

The Developer189 packet contains the complete closed Developer output,
focused1,390-case output, current source/binary/document freeze, family reader
and actual independent metadata peer. Its manifest is SHA256
`4a1475f9514e122059fab8233f60d763f8cb75ca8926a873ff2b027a1c63accd`;
the C# readback verifies all2,252 files/137,279,054 uncompressed bytes with no
extras. Fresh family hashes are Unit501
`c95436f6` and Integration121 `4ff41bbd` (full values are in the packet).
The complete642 PDB documents and exactly two test-source origin differences
are independently checked. These fresh current families are not substituted
into the already-running old189 snapshot.
