# Current-source parallel campaigns, 25 October 2026

The verified binding-retirement and periodic-collector fixes are committed as
`150e99f58904a4c74d5e041e0fd735ad9a83eb3b`. Their bounded controls and earlier
verification remain in the [repair record](../binding-periodic-repair-2026-10-25/README.md).
This continuation reruns the complete Unit/runtime, Pipes and ASP.NET scopes.
The complete source-checked Ubuntu union is **2,111/3,104 = 68.009021%,
unqualified**. The historical diagnostic union was 1,591/3,065 = 51.908646%:
a 16.100375 percentage-point increase, with a different denominator. All three
new reports retain their own source definitions; no old exclusion or mutant
outcome transfers. No manual control or ordinary test pass supplies mutation credit.

## Storage

Ubuntu `192.168.88.161` had 4,754,173,952 bytes available on `/data` before cleanup.
The user explicitly approved clearing these eight idle transient locations:

- `/data/cache`
- `/data/cis-builds`
- `/data/work/PARR/.artifacts`
- `/data/user/.pnpm-store`
- `/data/github-runner/_work`
- `/data/github-runner-2/_work`
- `/data/github-runner-3/_work`
- `/data/github-runner-4/_work`

Resolved paths and active working directories/jobs were checked before removal.
The exact guarded cleanup exited 0, with empty stderr, and left 47,637,958,656
bytes available: 42,883,784,704 bytes reclaimed. Runner installations/services
and the Healthcheck task root were retained. Dependencies in cleared work/cache
folders may need restoring on later builds. Automatic approval review initially
rejected shared-folder deletion; the operation proceeded only after the user's
specific path approval.

The closed [cleanup records](retained/storage191-closed01.tar.gz) are 82,430 bytes,
SHA256 `10785181c4c5766892741240f95410b5cbd7cde1544080b2e5b97963e5b65132`.
The downloaded archive matched the Ubuntu hash and was extracted for readback.

The new owned-data limits are Unit 30,000,000,000 bytes, Pipes 6,000,000,000 and
ASP.NET 6,000,000,000. Each controller retains its 2,000,000,000-byte actual
free-space guard. The operational shared-capacity ledger additionally reserves
4,000,000,000 bytes: 46 GB total versus 47.638 GB observed free before staging.
Payloads count inside each owned cap. Shared NuGet/CLI caches and unrelated
filesystem growth remain outside those owned counters and must fit the shared
headroom. No active diagnostic pruning, compression, deduplication or automatic
guard extension is used. These are resource budgets, not completion guarantees.

## Source and ordinary verification

A narrowly defined ASP.NET short profile now permits the exact integer
additional timeout 1,000 ms, two workers and `disable-mix-mutants: true`.
The historical maintained 750,000 ms ASP.NET reference remains unchanged.
Full source/test scopes, coverage settings, no-bail policy and 100/90/90
thresholds remain checked. This is Stryker's stock adaptive timeout mechanism;
it does not impose a hard 15-second session ceiling.

The maintained configuration-focused run passed 238 cases. Its first sandboxed
attempt failed during restore before any cases ran; the successful second
attempt and the failure are retained separately. The fresh Developer checkpoint
passed **10,333** cases: Unit 5,299, Integration 2,323, Architecture 2,682 and
Business 29. Build warnings/errors, failed/skipped cases and verification issues
are zero. Run `6f53a97db13d497b8c8b30f93f2183cf` finished at
2026-10-24 23:54:34.6707897 UTC, exit 0, with source inventory
`e794653c3c435628d69c278aeb497e1f0769298d115ca0cf3c6313a40eafa46e`.

Fresh metadata readers bind all 1,171 implementation source/configuration
inputs, including 972 C# files, three physical Core/Unit/Integration DLL/PDB
pairs and 642 distinct PDB documents. They derive exact selected families of
501 Unit and 121 Integration cases for Pipes, and 82 Integration cases for
ASP.NET. These are post-run physical/source observations, not retrospective
loaded-image attestations. Every campaign still needs actual Ubuntu original
and fresh controls and its own execution/capture evidence.

Both selected platform snapshots contain the same 2,018 files and source digest
`c01ed8c045b07f0f38119ac9a7fab58fc1dca7428d41718d4620601a7e0b971f`.
The common source manifest is
`04d4cec455a252665927c5cddbb801c4c4519249369fbd7233445412b7073fdb`.
Their preparation metadata retains distinct output paths. All 146 shipping
sources remain unchanged. The archive omission policy records exact omitted
archive identities; it does not omit source files or fabricate native results.

## Campaign attempts

All paths below are under `/data/cis-healthcheck-20261021-01a15a72/`.
Complete payload archives were extracted and checked on Windows with the
existing C# retention tool, then hash-matched after transfer. The same C# tool
verified every extracted payload path, byte count and SHA on Ubuntu before entry.

| Retained payload archive | Bytes | SHA256 |
| --- | ---: | --- |
| [Unit/runtime](retained/unit191-payload01.tar.gz) | 10,235,821 | `44c3e37e9a6a7822093602fc9a85ebdd56573c859c19228a419399056b8c28c0` |
| [Pipes](retained/pipes191-payload01.tar.gz) | 12,931,453 | `7afbb2bc93589c28219666a41eafbd6a1641299a79196368f3652ee41dc101a6` |
| [ASP.NET attempt 1](retained/aspnet191-payload01.tar.gz) | 12,317,164 | `abbbb519f4fa58d911afac9e01204bc9928eb6fc6f3ef58bd13059abcf50c3d6` |
| [ASP.NET attempt 2](retained/aspnet191b-payload02.tar.gz) | 12,331,906 | `41afdedeb1440531b549cc253a7bd5cc982f566ef8be2929404bdd58fe642b98` |

The [preparation packet](retained/preparation191-closed01.tar.gz) retains ordinary
verification, metadata binding, controller/stager sources and compilation,
failed attempts, payload readback inventories and the source reviews available
at its capture. It is 28,918,336 bytes, SHA256
`000ae5785db59e5683241e0fb296d21a94e13bad5e946efa4e41c0c3bbb7255e`.
Its [manifest](retained/preparation191-retention-manifest01.json),
[selection](retained/preparation191-retention-selection01.json),
[tar list](retained/preparation191-tar-inputs01.txt) and
[readback receipt](retained/preparation191-readback01.json) record all 2,993
files / 195,619,957 uncompressed bytes. No source archives or native evidence
were deleted. The later final audit-tool peer is retained separately.

| Scope | Actual run | Input SHA256 | State |
| --- | --- | --- | --- |
| Unit/runtime | `unit-complete-current191-run01` | `7f77cd70b7813d268027eaa70dab0f216c1d8fe65a7faf90df8154daa770a27b` | Closed 03:22:36 UTC, controller exit 1; original/fresh 1,390-case controls passed; native exit 0; capture/receiving holds |
| Pipes | `pipes-complete-current191-run01` | `2e3a7c9dc20649380acd9283e5ce9f731649b6ac40fc957d97a7d1e0624dece3` | Closed 04:25:41 UTC, controller exit 1; original/fresh 501+121 controls passed; native exit 2; capture and child-evidence holds |
| ASP.NET, attempt 1 | `aspnet-complete-current191-run01` | `b3d070f9163a92560a143832c5ff7015bf080d27b6287e607de164e0c08bf7b5` | Failed source binding after 8.907 seconds, before preflight/build/tests/native entry; no mutation credit |
| ASP.NET, attempt 2 | `aspnet-complete-current191-run02` | `b3d070f9163a92560a143832c5ff7015bf080d27b6287e607de164e0c08bf7b5` | Closed, wrapper/native/child exits 0; independent union audit accepts 72 assertion kills / 80 eligible = 90.00% |

ASP.NET attempt 1 incorrectly required every file below the implementation
tree to appear in the Developer source/configuration inventory. The platform
snapshot additionally contains exactly 51 generated `__pycache__/*.pyc` files:
four under `eng` and 47 under the DiscoveryHost fixture. All 1,171 source/config
entries matched, with no missing source or SHA differences; both sets contain
the same 972 C# files. The failed attempt's raw result and empty process list
remain unchanged.

The [closed first-attempt and receiving packet](retained/bootstrap191-closed01.tar.gz)
retains that failed ASP.NET tree/wrapper and all seven payload/tool receiving
checks. It is 9,985,945 bytes, SHA256
`ee4d327598f1be89b3aada1cf8b28bffa9e5ecdf80741ca754844cb7a59588e9`.
The [manifest](retained/bootstrap191-retention-manifest01.json) pins all 2,054
files / 57,068,604 bytes. Archive creation exited 0 with empty stderr; its
downloaded hash matched Ubuntu. The C# [readback](retained/bootstrap191-readback02.json)
verified the exact file set and bodies. Root's first readback invocation had a
65-character manually transcribed expected hash and was rejected before
inventory verification; the [operational note](retained/bootstrap191-readback-attempt01-note.txt)
and successful second invocation are retained separately.
The [independent closed-record review](retained/capture191-bootstrap-closed-peer01.md)
confirms this is a bootstrap-only failure and that all seven receiving checks
passed; it supplies no retry outcome or mutation credit.

A new ASP.NET attempt launched with fresh `payload02`, `run02` and
`a191b` paths. Its shared runtime comparison requires the exact disjoint union
of the original 1,171 entries and an explicitly pinned 51-file path/length/SHA
manifest. It does not ignore arbitrary generated directories or extensions.
Controls execute that actual compiled comparison against the real snapshot,
then reject an omitted origin, extra C# source, wrong source SHA and wrong cache
entry. The stager invokes the same compiled comparison before and after copying.

## Audit and score

The unchanged maintained union auditor closed at 05:34 UTC, exit 1, with all
three complete reports and the original source, process, restoration and fresh
control inputs. Its actual result SHA256 is
`680279c203f24e05199cc90736132512ce6ae60aae1cdee0993d9ce7e0d1a730`.
Here `complete=true` means the report definitions were all classified; it does
not mean every test host, child or controller completed successfully.

| Ubuntu partition | Accepted assertion kills | Eligible definitions | Conservative score | Raw Killed without accepted evidence |
| --- | ---: | ---: | ---: | ---: |
| Unit/runtime | 1,920 | 2,774 | 69.214131% | 601 |
| Pipes | 119 | 250 | 47.600000% | 42 |
| ASP.NET | 72 | 80 | 90.000000% | 0 |
| Complete union | 2,111 | 3,104 | 68.009021% | 643 |

The 993 unresolved source-bound definitions are 643 unqualified raw Killed,
23 Timeout, 301 Survived and 26 NoCoverage. The 94,215 integrity-issue rows are
overlapping observations, not additional definitions or an extra subtraction
from the maintained score. There are no transferred equivalent exclusions,
semantic credits or earlier-assertion credits. CompileError 306 and Ignored
720 remain separately reported outside the eligible denominator.

Raw Killed alone would be 2,754/3,104 = 88.724227%; including native timeout
detections would be 89.465206%. Neither is the accepted conservative score.
Reaching 90% on this exact denominator requires 2,794 accepted kills, 683 more
than the present result. Even recovering all 643 unqualified raw Killed would
leave 40 additional direct kills to establish. These counts describe the
current report, not a prediction for a changed-source rerun.

The [independent actual union review](retained/union191-actual-peer01.md)
recounts all three reports and 993 unique unresolved definitions, checks the
maintained source hashes and confirms the separate integrity/child holds.
Its SHA256 is
`b271b2d0ddf62506fa1239b0da543c1f1cd981fe1a80f870e8cb70b56f7a4d59`.

The mandatory Index12 joined-gate entry independently recomputed both capture
proofs and finished at 05:47:31 UTC, exit 0. Its result SHA256 is
`8b8041647d390e77685023607581bac703f89fad24fa037129e030ce2b5fa0fe`.
It retains the complete raw result and identical aggregate, all five original
Unit holds and seven original Pipes holds, with no ASP.NET controller hold.
`joinedHC12Qualified=false`, `classificationOrScoreChanged=false` and
`appliedKillCredit=0`. Exit 0 means the joined computation completed; it does
not mean the mutation or release gate passed. In particular, original capture
flags, Unit's 73,312 original audit rows and Pipes' 1,301 original audit rows
and failed child audit remain unchanged alongside the newer union audit.
The [final independent joined review](retained/union191-joined-actual-peer01.md),
SHA256 `02ab7897c53c9426766ecaaeaed97855b090a57a12eb4ee52304cd99e930bc96`,
matches all 45 physical input pins, compares the entire embedded raw union,
and checks the three original state/audit/child bodies and all 12 holds.
It found no discrepancy; its review supplies no new kill credit.

The [closed final audit packet](retained/current191-final-audit-closed01.tar.gz)
is 76,485,804 bytes, SHA256
`d43d9338ade3bac23ae0ac2adf9efc09f0ab75621eb0141ee34728a4562d96d4`.
It retains all actual assessment/index/descriptor/assembly outputs, complete
raw union and joined results, failed entries, installed-tool receiving records
and exact commands, streams, exits and times. Its
[manifest](retained/current191-final-retention-manifest01.json), SHA256
`264230e8704f9b49cd0194791a1fe4dacd654e79c312d3eb8f125fd718255a94`,
pins 236 files / 475,918,109 bytes. Both the
[Ubuntu](retained/current191-final-linux-readback01.json) and
[Windows](retained/current191-final-windows-readback01.json) C# readbacks
passed with zero extra/missing/mismatched files. The separate
[external metadata packet](retained/current191-final-external-metadata01.tar.gz)
retains its preparation, tar and Ubuntu receiving receipts: 14,641 bytes,
SHA256 `99d8b8e08e7126fd1aeba2e1638a8644fd95895e56c0dac8c6005ade636dc345`.
All original raw campaign archives remain separately retained above/below.
After these closed operations `/data` had 19,105,034,240 bytes available;
no active-run evidence was pruned.

The next work should use bounded source-matched controls before another full
campaign. First finish Unit queued-caller completion/drain assertions outside
the two repaired fixtures, then resolve the exact normal-host exception
clusters and the child-monitor self-wait. The retained full 1,390-case negative
control still aborts and must close naturally before that repair is complete.
For Pipes, compare the timeout hosts against matching baseline/fixture
preparation first, repair interrupted child publication/terminal evidence, and
add a real owned-semaphore disposal assertion for the surviving resource case.
Keep Windows-only endpoint behavior in its own Windows lane. Finally address
the remaining source-joined survivors and uncovered Unit branches; ASP.NET's
eight survivors remain included. No broad exception catch, timeout credit or
denominator reduction is adopted by this plan.
The [final Pipes review](retained/pipes191-final-peer01.md), SHA256
`d7e5377bfe521bfe35ca82685c101a002906d5f887a3971369c36087a8c73a9b`,
checks its report counts, child-publication evidence and archive metadata.
New controls must observe genuine preparation and terminal events; the old
missing child receipt must stay missing in the retained report.

The latest ordinary checkpoint includes two further fixture repairs: diagnostic
objects are asserted non-null before their existing code assertions. This makes
those failures direct assertions instead of null dereferences. The focused run
passed four cases. Developer run `530f846d6df94ed298e6482d6e834dc9` then passed
all 10,333 cases, with zero failures, skips, warnings, errors or reported issues,
at 04:03:13 UTC. Its source inventory is
`2d2b8a2c7b17076b73926bba39b46f6af43c5468bd30e3a2a965d912aa3e971a`.
The [closed ordinary packet](retained/unit191-final-ordinary-closed01.tar.gz)
is 9,155,123 bytes, SHA256
`42679e934dc0f38f5a7a49c88a6bef607686ee57178d36739953f7d795de2cdb`;
its [C# readback](retained/unit191-final-ordinary-readback01.json) verifies
1,014 files / 93,577,041 bytes. The immutable current191 campaigns predate
these repairs, so this verification supplies no current191 mutation credit.
The [final code review](retained/code191-final-peer01.md), SHA256
`cc6f33efa430354e257218637dad08cb67e5ab509e1066681281b456625f83b2`,
matches all six changed C# files and the new ASP.NET profile to that executed
inventory and independently recounts the four passing native families. All
146 shipping C# files still match the frozen campaign snapshot.

The preceding fixture checkpoint finished at 03:00:03 UTC: Developer run
`9eaae8a7eff249ccba47ef20f79d4e62` passed all 10,333 cases (5,299 Unit,
2,323 Integration, 2,682 Architecture and 29 Business), with no failures,
skips, warnings, errors or reported issues. Its input inventory is
`0edc5d1ee915b8916a17830466df44f05310851ce9ced642e820bf0390fdf957`.
The preceding sandbox restore failure occurred before tests and is retained
alongside this successful attempt. The [ordinary verification archive](retained/manual191-developer-closed01.tar.gz)
is 10,995,972 bytes, SHA256
`5951d94f124828fec4f75025c4f60f206a26f076b78f6039c49d190692b96fc7`;
its [readback](retained/manual191-developer-readback01.json) verifies all
1,024 files / 105,026,594 bytes against the separately pinned manifest.

While the immutable campaigns ran, two Unit fixtures were repaired to assert
that the original queued manual request actually completes before awaiting
it. Thirteen Scheduling methods and one ManualIntervalBoundary method use
the real operation and owner-drain observations. The original callers,
concurrency barriers, stop/dispose joins and fault propagation remain tested.
These fixture changes were adopted before the successful checkpoint above;
they are absent from the already frozen current191 campaign inputs and add
no current191 mutation credit.

The [closed diagnostic packet](retained/manual191-controls-closed01.tar.gz)
retains every private attempt, source and executable pin, peer review and
actual process/TRX result. It is 96,378,133 bytes, SHA256
`2c575c5b3d9e7eb781b024428301d07f91603e7e3f08c21fdd5748a8da56e924`;
its [readback](retained/manual191-controls-readback01.json) verifies all
9,669 files / 387,517,513 bytes. Against one exact source mutation that
replaces the shared queued completion with a new completion, the expanded
25-case positive control passed all 25 cases. Its negative control completed
naturally with 14 direct assertion failures and 11 passes, no outer expiry,
forced termination or controller holds. This establishes a partial repair.

The separate full 1,390-case negative control remains **incomplete and held**:
the native test session aborted at 150 seconds after 145 unique results
(92 passed, one direct ManualInterval assertion and 52 runtime timeouts),
leaving 1,245 cases missing. All 24 Scheduling cases were missing from that
full invocation. Natural outer-process completion and drained output do not
clear this abort. The source-only wait map identifies approximately 50 more
queued-caller exposures outside Scheduling. Neither the small successful
control nor this incomplete full control closes the Unit runtime work or
supplies a campaign kill or gate qualification.

Pipes' native invocation closed at 04:06:52 UTC, exit 2, after 3 hours
52 minutes. Its report SHA256 is
`b3f3e42d2bd35ed9f0781a451bdcad68c1610da7f29b0758a7388ac1be51e4a5`:
161 Killed, 84 Survived and five Timeout across 250 eligible definitions.
The whole controller closed at 04:25:41 UTC, exit 1. The native Core DLL
was genuinely restored and both fresh families passed. Its capture helper
rejected two sequential discovery-parent processes sharing the original
`TestDiscoverer-log.txt` path. The unchanged main auditor supports those
separate PID/header generations; the additional capture helper does not.
The original capture=false and receiving failure remain preserved.

Pipes also has a separate child-evidence failure: child audit exit 1 and a
missing `children/launches/3dd54f4db6e443f483aaa6b5e23fb566/launch.json`.
Capture of native parent/host logs cannot replace that missing child record.
The original result SHA256 is
`e74476dbc12f46e520492dd87d500e9d327917e65665eb91d9c1e4df0e6d59da`.
These are audit holds; neither raw native detections nor fresh baseline passes
clear them.

The [closed Pipes regular-file archive](retained/pipes191-closed02.tar.gz)
is 373,831,447 bytes, SHA256
`04fc877c20656db2b00af2eaab31cb31dc3e90c3f5ef83b567699e44a0eaf8bd`.
Its [manifest](retained/pipes191-closed-retention-manifest02.json) pins
314,427 files / 2,776,100,629 bytes. The [Windows C# readback](retained/pipes191-closed-readback04.json)
passed with no extra or mismatched files; a separate Ubuntu extraction and
readback also passed. Exactly 205 nonregular endpoints (100 FIFOs and 105
sockets) have their path/type/mode/owner/inode/time metadata retained instead
of a regular-file body. Empty directories are recorded separately: the
advisory packet includes all 28,411 directory metadata rows and the two empty
publication directories for the missing child launch.

The [closed Pipes advisory packet](retained/pipes191-advisory-closed01.tar.gz)
is 1,860,258 bytes, SHA256
`dc11e587e87f73c2a719eb1b503fd941eb6250c6300385c4260398282405bb46`;
its C# readback verifies 11 files / 10,163,640 bytes. It source-joins all 364
definitions, including 28 CompileError and 86 Ignored definitions outside the
250 eligible denominator. All 84 survivors and five timeouts remain unchanged.
Four timeout definitions affect Windows-only endpoint code; the fifth changes
exception text. Their Ubuntu timing outcomes do not establish a causal library
hang or validate the Windows contract. The retained child advisory ties the
missing publication to a 97,765 ms aborted-session tail without reconstructing
the missing record or claiming a precise interrupted instruction.

Unit completed its native invocation at 03:13:29 UTC, exit 0, after 3 hours
3 minutes. Its exact report SHA256 is
`c9bef921ef5d34b6d1db42bcd5a9d12cfea597baab15c52c35547a35bc001b09`.
The 3,667 raw definitions comprise 2,521 Killed, 209 Survived, 26 NoCoverage,
18 Timeout, 278 CompileError and 615 Ignored. Native 91.53% includes timeout
detections and is not the evidence-validated conservative score.

The whole Unit controller finished at 03:22:36 UTC, **exit 1**. It restored the
genuine Core DLL and passed the fresh 1,390-case control, but its capture helper
rejected the first line of `Runner 0-log.txt`. Both execution-parent logs had
rolled into retained `.bak.txt` files; the active files start with bare
informational lines rather than the process header. The unchanged native
auditor also treats those physical segments independently. The original
`actualShutdownCaptureValidated=false`, receiving failure, completeExecution
false and every original audit issue remain held. No capture receipt was
successfully produced by that helper. The subsequent diagnostic adapter and
coverage assessment are separate observations and preserve this failure.

The complete [Unit regular-file archive](retained/unit191-closed02.tar.gz)
is 506,060,102 bytes, SHA256
`1a8360705287addca0493f6e9d5372d00a1f8a6891609d10a190258ec687d039`.
Its [C# readback](retained/unit191-closed-readback02.json) verifies all 61,814
regular files / 8,458,640,597 bytes against the externally pinned
[manifest](retained/unit191-closed-retention-manifest02.json), exit 0. The
first retention attempt failed while opening a Unix socket endpoint. The
second selection excludes only 23 explicitly listed nonregular socket
endpoints and retains their path/type/mode/owner/inode/time metadata, plus
the failed attempt. No socket or regular evidence file was deleted. Socket
endpoints have no portable regular-file body; this is a regular-file readback,
with their separate metadata retained inside the archive.

The [closed source and original-audit advisory](retained/unit191-advisory-closed01.tar.gz)
is 1,714,466 bytes, SHA256
`f05de87b641dd7c41452e2f50880350febd699502688b9ba66e7bc11a412cb2a`.
Its readback verifies seven files / 16,427,051 bytes. The maintained report
parser joins all 41 configured sources and all definition tuples to the
frozen source; all 146 shipping hashes also match. The original audit's
rotation-sensitive cascades remain separate from actual recorded aborts and
normal-host non-direct failures. Its candidate assertion IDs are not a final
qualified numerator: overlapping integrity issues still matter. The advisory
preserves and explicitly corrects an earlier NoCoverage 1077 phase description
to the timer-creation catch fallback. It applies no historical exclusions,
new assertion credit or final score.

The [closed physical-projection packet](retained/unit191-rotation-physical-metadata-closed01.tar.gz)
records a separate byte-identical logical view: 2,757 original log files become
2,755 files, with exactly 7,839,280,342 bytes in each view. The two execution
parents concatenate their retained backup and active extents without inserting
or repairing a byte; every other file keeps its original relative name and
body. The original kernel log paths remain unchanged. The physical copy alone
claims neither capture completeness nor qualification.

The subsequent actual coverage assessment exited 0 at 04:24:58 UTC, receipt
SHA256 `20925a0551dae2f7fc411dafb27f84b802431c398420818942020f347c2b779a`.
It joined three parent roles and 2,751 retained host logs to their original
kernel traces, source roles and exit callbacks. It separately records 301
abnormal lifecycle observations. This establishes coverage of the supported
retained-log roles; it does not replace the unchanged full native/kernel
auditor, turn abnormal executions into normal ones, or clear original holds.

The [complete Unit adapter/assessment tool packet](retained/unit191-capture-tools-closed01.tar.gz)
is 1,590,414 bytes, SHA256
`43d73b7ff57718d89aad69881983c57e73d81f2b8459b714276e5cf2633d3ffb`.
Its C# readback verifies 250 files / 3,778,322 bytes, including source,
compilation, failed/superseded attempts, physical PE/PDB bindings and the
29 passing synthetic parser controls. Those controls execute no Healthcheck
subject and supply no mutation credit.

ASP.NET attempt 2 completed its native invocation at 01:30:49 UTC. Its complete
report contains 99 definitions: 80 scheduled and 19 block-ignored. The native
outcomes are 72 killed, eight survived, and zero timeout/error outcomes. The
controller records complete execution, source-scope and shutdown-capture checks,
genuine Core DLL restoration before the fresh 82-case control, and child audit
exit 0. Its native audit records 81 normal hosts out of 81 and no issues. These
original observations still require the separate source/restoration derivation,
unchanged union audit and mandatory original-state join. **Raw ASP.NET 90.00%
does not establish the combined HC12 gate.** Unit and Pipes retain their
original capture and execution-evidence holds.

The closed ASP.NET run and its complete temporary tree were archived without
pruning: 109,024 files / 661,533,036 uncompressed bytes. The downloaded
[archive](retained/aspnet191-closed02.tar.gz) is 153,784,676 bytes, SHA256
`8113b218ae90fe49fa431d7a5ad534adcb32b0162f0fe05d8513ce36c844ccd7`.
The C# [readback](retained/aspnet191-closed-readback02.json) verified every
physical path, body and SHA against the separately pinned
[manifest](retained/aspnet191-closed-retention-manifest02.json), exit 0.

Actual Index10 and metadata assembly entries both exited 0. Their separate
[closed derivative archive](retained/aspnet191-derived-closed01.tar.gz) is
6,511,674 bytes, SHA256
`a58241bf57e72e9e9e2a9b10e9b1a7537be48ed2d1b78a93c8218e48f9907cf5`.
Its [readback](retained/aspnet191-derived-readback01.json) verified all 20 files /
28,907,735 bytes. It retains exact original result/report/process pins, backup
and fresh-control restoration addresses, all log/kernel inventory entries and
the original controller-state sidecar. These metadata passes supply no
standalone qualification or historical assertion credit.

The [independent actual-record review](retained/capture191-aspnet-closed-actual-peer01.md)
found no ASP.NET partition blocker: 72 direct qualified assertion definitions,
81 normal execution hosts plus one normal discovery host, zero native issues,
all 82 initial/fresh case identities matched, and all 3,594 child intent rows
closed normally. It also checked selected physical role/descriptor pins and
equality of the preserved original controller state, whose holds are empty.
The later complete union includes these 72 accepted kills without changing
the ASP.NET report or its eight survivors.

The [eight-survivor review](retained/ASP191_EIGHT_SURVIVORS_ADVISORY01.md)
keeps every survivor in the denominator. One changes exception message text;
seven change continuation-context selection. The inspected contracts do not
establish a required behavior violated by those exact changes, and the latter
are not universal semantic equivalences. No exclusion, waiver or extra credit
is applied to either category.

The new reports require full source/definition-tuple joins, actual emitted
timeout observations, normality/host ownership and EOF checks, process/kernel
evidence, child evidence for Pipes/ASP.NET, and genuine backup/restoration/fresh
roles. A complete report is not sufficient by itself. Unknown failures,
unfinished work, host aborts and cleanup/capture issues remain explicit holds.

The private postclosure adapter adds only exact current writer/path variants to
the previous checked machinery. Diagnostic score computation is separate from
release qualification: a mandatory C# outer join preserves the raw union result
and its score, rechecks original controller/audit/child holds, and forces the
joined HC12 qualification false whenever any remain. Missing complete reports,
native null/1 exits, missing fresh controls or missing capture proof do not
supply a computable partition. No previous numeric mutant outcome transfers to
the new reports.

Final private Index10 passed 677 synthetic parser/metadata controls (256 older
cases and 421 current/retry/join cases), with an
[independent source and recorded-evidence review](retained/index10-final-retry-independent-peer03.md).
These controls execute no Healthcheck subject. The
[complete tool archive](retained/postclosure-original-capture-index-current191-10-private01.tar.gz)
is 7,413,505 bytes, SHA256
`59152742033a08f909cc4bdcf7de096b70b0d01d2b9146786337a0f1001c3cbb`.
Its [whole-folder inventory](retained/capture191-index10-tool-files01.json) and
[readback receipt](retained/capture191-index10-tool-readback01.json) cover all
4,598 files / 39,681,574 bytes, including all intermediate and failed attempts.
The [handoff](retained/capture191-index10-root-handoff01.md) pins the final
four-file binary closure and mandatory projection/union/join sequence.
The independent [closed-report checklist](retained/CLOSED_PIPES_ASPNET191_AUDIT_CHECKLIST01.md)
and [union recipe review](retained/capture191-union-recipe-peer01.md) record the
remaining actual audit obligations. No current native outcome is supplied by
these preparation records.

The separate [metadata assembly tool](retained/current191-union-glue-private01.tar.gz)
has an [independent source review](retained/capture191-union-glue-source-peer01.md)
and [build/control review](retained/capture191-union-glue-build-peer02.md).
Its two private builds passed with zero warnings/errors, and nine focused argv
controls passed. The 204,314-byte whole-folder archive has SHA256
`40d6ea7fef57ac3c50dacde5bb8b522821794c4ea00716192724c00cde9c4e3a`;
the [archive receipt](retained/current191-union-glue-archive01.json) records the
72-file exact readback. Root independently inventoried those 72 files /
572,896 bytes and verified the installed Ubuntu copy with the C# retention
tool. This only assembles pinned inputs for the existing auditors and joined
gate; it awards no mutation credit.

A [conditional source assessment](retained/capture191-capture-completeness-assessment01.md)
anticipated that the controller's shutdown-capture flag also requires normal
host exits. The actual Unit and Pipes failures were investigated separately;
sealed Index10 remains unchanged and still requires its original positive
capture result.

The [separate Pipes coverage tool](retained/capture191-pipes-sequential-private01.tar.gz)
retains 330 files / 4,498,011 bytes, C# readback 0. The archive is 1,765,616
bytes, SHA256 `149a7a69542672acf0f608570bf3af3509d16abcfbd459cc5676e849671ec577`.
Its corrected source and 40 passing parser controls bind the actual two
discovery PID generations without splitting or renaming the physical log.
The actual entry exited 0 at 04:52:46 UTC. It accounts for four parent processes, 504
host logs and 508 original native execution identities across 174,471 kernel
files. Receipt SHA256 is
`93ba1de3bfd72849a802263e286b3bf27d2ed552aa28f1de9c5db249ef2b6b16`.
Its 108 abnormal lifecycle observations and the separate missing child
evidence remain held. This is log-role coverage, not child completeness or
normal execution.

The [corrected Index12 tool](retained/postclosure-original-capture-index-current191-12-private01.tar.gz)
is 1,321,313 bytes, SHA256
`04c30e3d7dc72c75668d423db07f33dbaad0f8750e90c2b302a9c8ebdf1d8a12`.
Its C# readback verifies 266 files / 3,709,727 bytes. Both private builds pass
with zero warnings/errors; 30 focused controls pass, including all 43 fixed
hash constants. The [independent final peer](retained/current191-index12-final-peer03.md)
checks the exact linked Unit/Pipes sources and all four current/preserved
PE/PDB pairs with 40 source documents. The earlier long-only in-memory integer
reader, a rejected 65-character transcribed hash and an incorrect installed
tool path remain preserved failures. None supplied successful evidence.

Index12 has only two fixed diagnostic alternatives for original capture=false:
the complete independently recomputed Unit or Pipes coverage receipt. Its
ordinary route stays strict. It preserves every original result, audit,
child observation and hold; Unit's descriptor changes only its log inventory.
The final join recomputes both proofs and forces HC12 qualification false for
these diagnostic routes. It does not alter the maintained classification or
score. Read-only metadata computations ran alongside archive receiving; their
evidence is accepted only after complete archive verification.

The [retention helper correction](retained/retention-fast191-private01.tar.gz)
is 159,003 bytes, SHA256
`1a6debed24e771b79fcf7046d15d347254080cb576dd60e647fdd55f1dbd14a5`.
Its C# readback verifies 299 regular files / 485,309 bytes; six intentional
empty control directories have separate metadata. The sole algorithm change
replaces a quadratic directory-prefix scan with an ordinal set of strict
file ancestors. Every file hash, path/order comparison, no-link rule,
extra-directory rejection and output guard remains unchanged. Fourteen
old/new scenarios (28 actual verify calls) pass, including rejection cases.
The superseded Windows read-only verifier was stopped only after its exact
PID, command line and creation time were checked; its interrupted attempt is
retained without acceptance. The replacement completed the full Windows
readback in 8 minutes 54.58 seconds. This is not a completed old/new large-tree
benchmark.
