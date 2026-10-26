# Binding retirement and periodic collection, 25 October 2026

The preceding verified fixes were committed as `6e60acc6`. This continuation
repairs tests that waited on absent signals before checking observable contract
failures in deliberately changed implementations. Shipping code, timeouts,
mutation thresholds and the receiving classifier are unchanged.

## Binding completion

The four `RetirementWithoutOutstandingWorkCompletesTheSharedBindingBarrier`
rows now preserve their actual public disposal tasks, independently join the
actual state release, entry/monitor and service lifetimes, and check the shared
public completion promise after those real owners settle. The special pending
promise path requires the exact reviewed no-collection phase, retired/release
started, strictly null `actualSettlement`, successful original body/tail calls,
and death of the entire private owner graph after a synchronous non-inlined
capsule returns. A live graph, actual observer, unavailable field, unknown fault,
failed cleanup or unfinished operation remains a runtime failure/hold.

The concurrent withdrawal case retains all sixteen rounds on one actual
service and joins both actual report/withdrawal tasks, including partial setup.
The replacement-registration case preserves replacement binding before any
old-binding call, and joins the current binding's real task before reporting
success through its external monitor. Their actual state, entry, monitor and
service lifetimes remain independently required. Only bare original tasks,
weak references and detached observations escape the capsule.

| Exact isolated control | Original | Changed implementation |
| --- | --- | --- |
| Retire-finally `Release();` removal, observed189 selector996 | Four original rows passed | Four intended public completion assertions, approximately1–40ms, after real cleanup and graph observation |
| Observe-external-retirement `Release();` removal, observed189 selector985 | Complete two-class 16-case cohort passed | Both deterministic replacement rows and Concurrent(False) fail intended completion assertions; Concurrent(True) passed because the altered race was not established |
| Observe-external-retirement guard negation, observed189 selector983 | Same frozen 16-case positive prerequisite | Both deterministic replacement rows and Concurrent(False) fail intended completion assertions; Concurrent(True) passed |
| Retire-finally guard negation, observed189 selector994 | Same frozen 16-case positive prerequisite | All four retirement rows fail intended completion assertions |

The passing concurrent race is retained without retry or kill credit. Initial
guards retain the actual binder strongly or throw from the actual supplied
clock. They remain, respectively, a live-owner runtime hold and the actual
unknown IOException; neither becomes the completion assertion. Those actual
guards are bounded to their recorded initial phase, not relabelled as executions
of every later scenario. All four exact source variants have direct witnesses;
their sixteen targeted results comprise fourteen intended assertions and two
honest race passes. No targeted result times out or has an unresolved cleanup
hold. These counts describe test results across four mutations, not fourteen
different killed mutants.

## Periodic admission

Tests now observe a real returned timer `Change` before waiting for a callback
that may never enter. They first require zero acquisitions before explicit
activation, then require the source-proven first acquisition after activation.
Observers are bounded one-shot signals. They do not publish from timer creation,
replay earlier changes, invent callbacks or replace real disposal operations.
System/manual timer forwarding and original caller, lease and cleanup checks
remain intact.

| Complete original class | Original result | Exact1334 /1336 controls |
| --- | --- | --- |
| ResourceBoundary | 7 passed | Both Boolean rows fail actual admission assertions for each leaf |
| PeriodicTimerProgress | 2 passed | Both rows fail startup assertions / existing actual timer due assertions before the absent callback wait |
| CapacityAndFairness | 5 passed | Original fairness case fails actual startup / activation acquisition assertions |
| RunnerScheduling, RetiredCurrentFairness and RunnerLifecycle | Included in the complete 96-case six-class positive control | Twelve additional cases fail startup / activation acquisition assertions |

The changed rows complete in approximately1–43ms and the original real cleanup
returns. Every recorded control process exited naturally, was reaped and
log-drained, with no expiry, kill or supervisor hold. Original GUID/FQN/display
identities were checked. Core variants preserve all146 shipping inputs except
the exact recorded leaf; complete PE/PDB checksum and source-document checks,
pair-only complete host staging, raw results and independent reviews are
retained. These are physical/source checks, not retrospective loaded-image
attestations.

Source audit of the actual 1336 request found another twelve selected periodic
cases with the same first-callback wait, outside the initial three classes.
Those cases are repaired and all twelve fail the intended assertions under
each exact leaf. The complete original six-class cohort passes 96/96. A single
final host then executes the exact seventeen-case 1336 selection: all seventeen
reach intended assertions, with zero missing/skipped results or runtime/cleanup
holds. Test duration is 122ms; actual host elapsed 203.8029ms and supervised process
elapsed 946.9502ms. That control preserves actual identities and typed normal
completion, without extending a deadline or retrying the subject.

The broader 1334 request population and other runtime mutations remain outside
these bounded controls. This result closes the known first-admission waits for
the exact seventeen-case 1336 selection; it is not a complete Unit campaign.

## Ubuntu confirmation

The authorized Ubuntu server `192.168.88.161` executed the same portable fixture
and exact 1336 Core pair under `/data/cis-healthcheck-20261021-01a15a72/`.
The original six-class control passed all 96 cases. A separate single host ran
the complete seventeen-case negative selection: all seventeen reached the
reviewed admission/timer assertions, with no missing or skipped results.
Both processes exited naturally, were reaped and log-drained, and had no
expiry, kill or runtime/cleanup hold. Supervised elapsed times were 4.299s and
3.1204s; the negative test duration was 519ms.

The C# receiver checked all 113 exact GUID/FQN/display identities and outcomes,
one typed non-aborted/non-cancelled completion per host, the unchanged portable
input manifest, and the entire 143-file negative closure with only the exact
Core DLL/PDB pair replaced. Explicit runtime paths and inherited environment
pins are recorded; this is not a loaded-image attestation. Receiving passed on
Ubuntu and again after downloading and extracting the closed archive on Windows.
The remote receipt is
`3f237f3e7074e34f302e0560b31b82621cd8fd34d8cd954f648f0e07dcb377d9`;
the relocated readback receipt is
`71343d69096e5d5b2632d509da5f779e00b59dca77187ac388e367b3e4801f1b`.
These bounded controls do not add automatic mutation-gate credit.
An independent raw/metadata review joins every typed host result to its TRX and
frozen identity, with zero mismatches, duplicates or missing rows. It also
checks all 169 static input pins and the complete negative closure. The two
receipts agree after CRLF/LF normalization; their original bytes remain retained.

## Retained evidence

| Packet | Bytes | SHA256 |
| --- | ---: | --- |
| [Retirement and Resource controls](retained/retirement-and-resource-controls190-01.tar.gz) | 58,297,044 | `c6eb3305e9857ccc0c82a6ba1e6c11b792e80a63eaa742a323658b7e02a9130b` |
| [Periodic progress and fairness](retained/periodic-progress-and-fairness190-01.tar.gz) | 23,122,502 | `60eb7661c2df706f6a08433548a265f78e6fff8997a10b4e09941e35aaff52e8` |
| [Withdrawal and rebinding](retained/withdrawal-and-rebinding-controls190-01.tar.gz) | 18,435,523 | `9df4a84e8ee63b79b1c04030c5e94943bbb84c227534c00a94c40abfe1191c0e` |
| [Retirement guard variants](retained/retirement-guard-controls190-01.tar.gz) | 31,278,458 | `dce3e4492a98e070b043bd20c0170b53b8aaae3877b077ac074c9f89e472d1df` |
| [Complete periodic controls](retained/periodic-complete-controls190-01.tar.gz) | 12,381,649 | `9fed5fd8f155ab597824990cfe868e5a6c37270c757aacb41f640d8976758e4f` |
| [Old189 storage-guard stop metadata](retained/unit189-guard-stop190-01.tar.gz) | 6,387 | `04408821e0fe80f8583ee65079440ff53bd862bd3ccce5baf9bbfb53c4738e4e` |
| [Developer checkpoint and current family](retained/developer190-and-current-family01.tar.gz) | 20,366,589 | `6afca89f8dce1f08cb07c3e90147b3dbc2003e0e5755396bfe6764977436e8e3` |
| [Complete Ubuntu controls](retained/periodic190-linux-completed01.tar.gz) | 8,312,531 | `b2600cc2cf29a81783e856c0c050a238ce7e51844745ac0fd5fe9692d14e1178` |
| [Ubuntu preparation, receiving and peers](retained/periodic190-linux-preparation-and-peer01.tar.gz) | 17,295,501 | `6d31824bf196646424032af5db5bd72fb06a80b2cd29b18008e27de65c1b9d63` |

The first C# readback verifies all 6,844 files/188,794,710 uncompressed bytes,
manifest `52d4bac7e6adb6a9bd8fb0a9787d49343d9d96adffd50ca1d3e7a29613db1799`.
The second verifies all 1,220 files/59,420,623 bytes, manifest
`d34a36734067cc9ba8aa449016b9eb30fb004eeddca236bb4dc5f90574b5f74f`.
Separate manifests/readback receipts accompany each archive. Only verified
temporary extraction copies were removed; original control trees remain.
The unchanged C# retention tool is retained in the previous
[preparation packet](../unit-startup-investigation-2026-10-24/retained/proof-and-unit189-preparation01.tar.gz).
The rejected unbounded-observer draft and superseding bounded source/peer are
preserved in the first packet; that draft was never executed or adopted.

The later three C# readbacks respectively verify 2,539 files/75,706,342 bytes,
4,719 files/134,557,575 bytes and 639 files/33,428,429 bytes. Their separate
manifests/readback receipts accompany the archives. The complete periodic
packet retains the author/peer prose errata: Resource staged 144 files; Progress,
Fairness and the final combined fixture staged 143. The original dynamically
checked manifests were correct throughout; archived earlier prose is preserved.

The Developer/current-family readback verifies 2,350 files/152,237,316 bytes,
manifest `fc7a861d8276649c2925595a9e54b9db33c9985490b2e6e7df9f657db92cfdee`.
It retains both Developer attempts, the formatting preimages and exact inverse
receipt, both focused runs, and the current source/family reader and peer.
The Ubuntu preparation/receiving readback verifies 644 files/37,854,926 bytes,
manifest `e167176b4a5a3e2499faff13b9e9fb2e3a3c2b417aeca53c00ede8a02ef58d2a`.
Its final independent actual-control peer is
`1a63f735b6265f518b2cc7c10b6a917cc18c4f6cd8e3e6496e6f92666efd488d`.

## Verification and remaining qualification

The maintained focused filter passed 1,389 Healthcheck cases, zero failures or
skips. The exact maintained campaign selector additionally includes the
separate assembly scheduling control and passed 1,390/1,390, build 0 warnings and
errors. Their actual focused outputs are `50affdd89b094b89bb926d6cc1e596fb` and
`3caef8c651be401dbeabcd4fd2167f53`.

Developer190 attempt01 failed formatting before executing any verification
family: four CRLF line endings and one import-order diagnostic in the two
retirement test files. The scoped formatter changes only those line endings
and adjacent using order. A C# complete-byte inverse receipt binds the original
control sources to these formatted canonical files. The failed attempt remains
retained. Corrected checkpoint02 passed all **10,315** cases: 5,299 Unit,
2,323 Integration, 2,664 Architecture and 29 Business, with zero failures,
skips or issues and a full solution build with zero warnings/errors. Run
`59c40e0faad3474980b9d7b0531c29a0` finished at 2026-10-24 23:08:29.8909388 UTC
(25 October 01:08:29 Malta), with source inventory
`2b55053dc09da7060981837688a71a8ed0adc2c3bb3c1ff25929cfe2f0f7856e`.

The post-run C# source/family observation and independent peer passed. It checks
all 1,171 inputs/972 C# files, all 146 unchanged shipping sources, the three
current Core/Unit/Integration DLL/PDB pairs and all 642 distinct PDB source
documents. All 5,299 Unit and 2,323 Integration passing results join to native
discovery; the selected family contains 501 Unit and 121 Integration cases.
Display escaping differences in eight Unit and one Integration unselected rows
remain explicit. Receipt
`416e42a1314546cf85d84a97e403c6ce3bba13fc5a6de1df1c14d9529d040b95`
binds the unchanged current source inventory to these observed family inputs.
This is post-run physical/source evidence, not a loaded-image attestation or
mutation qualification.

The old Ubuntu189 campaign used its immutable earlier fixture snapshot. Its
controller stopped when the fixed 4,000,000,000-byte owned-data bound was crossed,
after 7,550 seconds. The sampled peak was 4,002,126,164 bytes and minimum free
space 4,802,048,000 bytes; this was not the 2GB free-space reserve or a native
completion. Controller exit 1 and owned native process exit 137 are preserved;
`completeExecution`, restoration and qualification claims remain false. Its
whole raw tree stays on the authorized Ubuntu server. Isolated controls do not
rewrite any old abort, unfinished operation or storage-stop result.

The accepted historical union remains **1,591/3,065 = 51.908646%, unqualified**.
There is no automatic mutation credit from these manual controls. Complete
current Unit/Pipes/ASP.NET campaigns, capture/host/ownership audit, remaining
assertion gaps and the conservative 90% gate remain outstanding.

## Derived graph limitation

The final graph build computed 24,161 nodes/117,321 edges, build
`c7b3eeb77e68c8bd9aba8584383f8dc2db9526d1309626d519b654a9bc275e67`,
but returned exit 5 with `CIS-GRAPH-STORE-002`: SQLite persistence reported
access denied. The immediate status returned exit 0, the same new build ID,
freshness `fresh`, zero errors and 265 existing conflicting-ownership warnings.
Both [build](retained/graph-build190-01.log) and
[status](retained/graph-status190-01.log) outputs are retained; the failed build
is not relabelled as a successful invocation. Derived local state is uncommitted.

Earlier bounded graph-find calls stalled and their exact owned processes were
stopped. This investigation therefore used exact text/source inspection and
the native evidence described above. No complete graph semantic, compiler or
caller-coverage claim follows; graph diagnostics include omitted evidence paths.
