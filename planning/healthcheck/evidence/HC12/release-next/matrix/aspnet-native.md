# Complete ASP.NET mutation: retained Windows and Linux executions

The completed short-path Windows run `.artifacts/hcc/a1` and Linux
`aspnet-linux-01` both report **21 Killed / 24 testable = 87.5%** and native exit
2, below the strict 90% gate. Their actual child audits pass independently:
Windows uses v4 owner-aware evidence and Linux uses v3 evidence. Successful child
propagation does not qualify these failed mutation scores, the other Healthcheck
partitions, a packaged release candidate or the full Release profile.

## Exact scope and native outcomes

The only selected source is
`implementations/csharp/src/CIS.Core/Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs`.
The configurations use Complete, IntegrationTests with the exact
`HealthcheckHttpTransportTests` class and scheduling control, Release/net10.0,
VSTest, two sessions, `disable-bail: true`, explicit `coverage-analysis: off` and
thresholds 100/90/90. Each later native log reports **53 tests found** for its
initial run. These are mutation-run discovery counts, separate from restored
build/TRX evidence.

| Execution | Killed | Survived | Timeout / NoCoverage | CompileError | Ignored | Native / audit exits |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Original Windows `aspnet-windows-01`, v1 | 14 | 10 | 0 / 0 | 72 | 5 | 2 / 1 |
| Windows `aspnet-windows-02`, v3 | 21 | 3 | 0 / 0 | 72 | 5 | 2 / 1 |
| Linux `aspnet-linux-01`, v3 | 21 | 3 | 0 / 0 | 72 | 5 | 2 / 0 |
| Deep-path Windows `aspnet-windows-03`, v4 | No mutation measurement: initial test run failed | — | — | — | — | 1 / 1 |
| Short-path Windows `.artifacts/hcc/a1`, v4 | 21 | 3 | 0 / 0 | 72 | 5 | 2 / 0 |

All completed mutation reports retain 101 selected definitions. Comparison by
ID, full location, operator and LF-normalized replacement finds no definition
differences; the three later measured runs have identical statuses. Their
original source text also matches the original run and the frozen scope
inventory, SHA-256
`d81256db1acc93e948d72bdc7c6df713cc6f9e436b02d3b9f3b22a8d7f66efa4`.
There is no selected-source omission. The later reports contain 146 source
objects; the other 145 retain no mutant records.

The strict denominator is Killed + Survived + Timeout + NoCoverage = 24.
Timeouts receive no kill credit; here there are none, so strict and native scores
coincide. The original score is 14/24 = 58.3333333333%; the later score is 21/24 =
87.5%. CompileError and Ignored remain outside that denominator and require
separate review/replay.

The whole-Core generator logs record 10,241 created, 1,490 CompileError, 5
block-covered Ignored and 8,722 mutate-filter Ignored, leaving 24 tested. Only
72 CompileError records belong to this selected report, with 1,418 outside this
scope. Native Safe Mode rolls back `HandleAsync` after compiler diagnostics;
the 72 retained labels do not establish 72 independently uncompilable mutations.
All five selected Ignored records retain the block-already-covered reason.
Neither status group is waived or credited as executed whole-Core mutation.

## Seven added detector outcomes and remaining survivors

Exactly seven definitions change from Survived in the original run to Killed in
each later measured run:

| Native ID | Observable detector |
| --- | --- |
| 192, 193, 194 | Required app/source/options null guards, including exception type and `ParamName` |
| 199 | Enabled details rejects missing authorization with the required exception and parameter |
| 283 | Cancellation after real snapshot capture aborts before response header/body commitment |
| 286 | Actual HTTP response requires `Pragma: no-cache` |
| 287 | Actual HTTP response requires `Expires: 0` |

The later sessions pin the changed HTTP test source to
`d3abeb7a96cac7efd80174a9b2e13f5a84300b8c35a5a797ac963c20c1f03c62`.
These outcomes apply to those new frozen harnesses; they do not retroactively
change the original 14/24 report.

Three survivors remain: ID 200 empties the authorization-registration diagnostic
message; ID 203 changes the null request-path fallback from empty to another
nonreserved string; ID 291 changes `ConfigureAwait(false)` to `true`. Their
source-bound equivalence assessment remains pending. No unpublished message
text, artificial scheduler requirement or private state is asserted to force a
kill, and this note grants no equivalence exemption. The unchanged 72 compiler
rollback and five block-covered outcomes also remain pending disposition.

## Actual positive child audits

Both successful audits contain **1,125 actual HTTP-host rows**: 45 initial
uninstrumented baseline rows and 1,080 selected instrumented rows. Each has 996
normal completions (45 baseline plus 951 instrumented), with actual parent and
child exit records agreeing on exit 0. The remaining 129 are attributed managed
Core registration failures: IDs 195:42, 196:45 and 197:42. Their actual parent
exit is 134 on Linux and -532462766 on Windows.

Those 129 rows use origin `receipt.json`, `managed-failure.json` and actual
`parent-exit.json`, rather than a manufactured normal exit. The successful
audits validate terminating `ArgumentException`, frame zero in
`UseCisHealthcheck`, exact metadata token/Core identity, active mutant,
origin/launch hashes and actual parent lifecycle without timeout or forced stop.
An origin receipt alone is not full positive lifecycle evidence. These are full
successful audits of their own frozen executions, distinct from invalid audits
and any separate origin-only or intentionally corrupted control receipts.

Linux's actual schema is `cis.healthcheck.stryker-children/v3-development`,
status `QualifiedChildPropagation`, exit 0, zero failures, session 863 source
inputs. Windows short-path uses `cis.healthcheck.stryker-children/v4-development`
with that same positive status, exit 0 and zero failures, session 864 inputs.
Its rows retain **25 distinct StageOwnerId values**, each with 45 launches, one
parent PID and one Core hash; each of the 25 parent PIDs maps to one owner.
The owner-aware audit checks the exact parent/owner/Core/executable stage path.
The actual owner IDs and every per-child hash remain in the pinned audit rows.

Linux v3 and Windows v3 use `additional-timeout: 300000`; short-path Windows v4
and its failed deep-path attempt use **750000**. The completed short-path native
runner explicitly logs an effective test-run timeout of 805836 ms. These are
aggregate native test-run budgets. The actual `TransportTestSupport.Limit` is
**15 seconds in all versions**, with no per-fixture deadline relaxation. A
per-child timeout, forced stop or missing lifecycle record remains invalid.
Owner identity, output root and aggregate budget all differ across attempts;
this audit does not isolate or claim one change caused the successful result.

## Failed attempts remain failed

Original Windows v1 retains its 14/24 score and invalid audit: 987 accepted rows
and 34 incomplete lifecycle directories. Later v3/v4 results provide no credit
to that original audit.

Windows v3 `aspnet-windows-02` uses the retained `private-launcher-08` auditor.
It accepts 1,118 rows (45 baseline, 951 instrumented normal and 122 instrumented
managed-registration rows) and rejects five of 1,123 directories. One active-195
directory lacks a parent exit; one active-195 directory records host-read timeout
and an actual parent exit with TimedOut true, ForcedStop false; two active-195
and one active-196 directories record host-read timeout, cleanup forced-stop and
actual parent exit -1 with both flags true. All five retain origin and managed
failure records, which do not override those lifecycle defects.

The four host-read timeout markers occur about 15 seconds after launch; the
three cleanup stops occur about 30 seconds after launch. Runner 0 records
336687 ms aggregate timeout, starts at 15:57:21.358 and aborts at 16:02:58.086;
its test host exits at 16:02:58.118. The missing-parent record launches at
16:02:55.215 and records managed failure at 16:02:56.054. These observations
correlate with the aggregate deadline; they do not establish the cause of every
missing record or turn a timeout/forced stop into an expected kill.

Deep-path Windows v4 `aspnet-windows-03` fails the initial native test run with
more than 50% failing tests. It has **no mutation report or score**. Its invalid
audit has zero accepted/instrumented rows, 45 rejected baseline directories and
the additional `No actual child loaded an instrumented Core` failure. Each of
those 45 directories records observer failure `The child started a different
executable.` The successful short-path run is a separate session; it does not
repair or supply a measurement for this failed attempt.

## Exact retained pins

The run abbreviations below resolve to repository-relative artifact roots:
O = `.artifacts/healthcheck/release-next/aspnet-windows-01`;
W2 = `.artifacts/healthcheck/release-next/aspnet-windows-02`;
L = `.artifacts/healthcheck/release-next/aspnet-linux-01`;
D = `.artifacts/healthcheck/release-next/aspnet-windows-03`;
W = `.artifacts/hcc/a1`. All SHA-256 values hash actual file bytes.

The separate fresh restored integration receipt `W/restored-native/restored.trx`
executes **145/145 passed**, zero failed or not executed: 52 HTTP, 37 pipe, 55
probe cases and one scheduling control. Its wider 145-case restoration scope
does not change the mutation configuration's 53-case discovery count or its
failed 87.5% score.

| Run / file under its root | SHA-256 |
| --- | --- |
| W / `config.json` | `3495ad51e0713121f58d647f72d1a971480d83c178ae540ee48122827db7bba3` |
| W / `native/reports/mutation-report.json` | `afcc1871cc1d4e12b74a352809b9e75c054c2fa3b9d13e61f1c166a900eb800e` |
| W / `audit/results.json` | `278b0bbb240ceb42dffc77fc9284e6ea7a33ca2df9de8f03cb097689794a9592` |
| W / `session/session.json` | `297d3f9f3438b3a8ee674edda568f3fbb33be07ab398c10805b9a92bda528067` |
| W / `native.log` | `9bdf02f4f86fbc5d8b413dc265e58ff435b44db53ed59fe1fb032693a582d57a` |
| W / `exits.json` | `950c80ceebfd4d2bf33050011fc1cbef3ac6f22889b264a4f0ecef0139ddd057` |
| W / `restored-native/restored.trx` | `d50b0bfab0c08d745e5c690651013f20da36b3ce31bbf8257f4f2da5d36cb5d3` |
| L / `config.json` | `a4c80e3cae45133401ad9f440a48b45d37526b1f1d161222b1f375eaf322d293` |
| L / `native/reports/mutation-report.json` | `0e640900d0fcdb963d456a411c228a9e987ac31076029e1b10882755012407d2` |
| L / `audit/results.json` | `3f071a4455ba8fc2737b3442d2d6c862b09720e3a0bdfc5181fd0ebf159bfaf3` |
| L / `session/session.json` | `f98523cda98ac75d9e62539dc03ea108eccfb4a5d875aaee1b53849964c05055` |
| L / `native.log` | `423c291b432662756bfb450a8bbc8c91b9822405e83884c66de38d5944ec3cf8` |
| L / `exits.json` | `cc0c041038c03f312f689fd703036ae58a5698d3540a04e21b3e7d17738d2d39` |
| W2 / `config.json` | `a4c80e3cae45133401ad9f440a48b45d37526b1f1d161222b1f375eaf322d293` |
| W2 / `native/reports/mutation-report.json` | `5f463e6c45b7c414e3b9f7a3e87ed577b82c66e4d5f5e1b9b72f141607c40f8b` |
| W2 / `audit/results.json` | `4b1c50d3cc81beb59179a6fc20a413800231154b34e4463edb2bdc17ebce9802` |
| W2 / `session/session.json` | `f5d185ba7a894a06ed22a9922618851559aed748892e02d6872837c2767f7db7` |
| W2 / `native.log` | `525756cd850eded062e006afa120330ee545be332f2b5ccf489af5d9355be641` |
| W2 / `exits.json` | `1cb28caaa8a50e8d317bf91262c1dfc334a7003dffce1ba851359123390b05b6` |
| D / `config.json` | `3495ad51e0713121f58d647f72d1a971480d83c178ae540ee48122827db7bba3` |
| D / `audit/results.json` | `20a238673f76a28a7dbd85c27533bf1e11837084548e7e4c1382f92c99ebb868` |
| D / `session/session.json` | `ac83c7c3dd62904329868023dc5c9346047edabd2996672103428717cb8908e7` |
| D / `native.log` | `c6b54bcbcfd8303a4221ded0a201d4dc83b096afa8c20b744c39a9880b94a114` |
| D / `exits.json` | `c5616c6b0e74e857c01734754d0a9c8132f634b359c6ad28555eeb7a16a28511` |
| O / `config.json` | `a85df9629c2ae0a09780e4f498cf45b4bee83e7df75b856a177b6e6c8bc04926` |
| O / `native/reports/mutation-report.json` | `fd2927cc7b9497ce1bddd34cbe9721f42cc857bbd38339375c64bdaac0b73fa6` |
| O / `audit/results.json` | `29205a89ec242c4b502924091c39aa61337eb548e19bc833cf2029af01a2f6dc` |
| O / `session/session.json` | `9d6df2cc4e6c69571be7929f0a66f5dfd87d0b918aec86fcc3f12971ccc7b018` |
| O / `native.log` | `fbc839009cdb4296deb7f5813b953ece5866cbc7164a31a2ed531285aae651ba` |
| O / `exits.json` | `1cb28caaa8a50e8d317bf91262c1dfc334a7003dffce1ba851359123390b05b6` |

Each successful audit's recorded `sessionSha256` equals its file hash above.
The sessions pin exact source/structural inputs, native host/runtime and
Stryker.Core 4.16.0, SHA-256
`bf02f78d3725e1243edb7a32d88dc1a02c88f1f9f74b60b18730636073f234d7`.
The actual Windows v3 `private-launcher-08/CIS.DependencyChecker.dll` hashes to
`5dc482dbebe0a266664b5094201191215581ee51a68d27e1bddeb0778676e100`.
Different helper schemas, stage roots, source sets and aggregate budgets remain
explicitly different frozen contexts. Restored build/source receipts are
separate from this mutation score and child audit. Compiler rollback review,
survivor disposition and remaining mutation/Release, external-integration and
publication-history obligations remain open.
