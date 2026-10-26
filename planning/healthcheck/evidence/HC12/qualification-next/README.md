# Healthcheck qualification follow-up

The transport/application candidate was committed as
`0bf0c4c8d8c0c6646f591c634e66cfa480348aa6`. This later qualification wave remains
uncommitted. [receipt.json](receipt.json) records the executed gates, input hashes,
toolchains, retained attempts and outstanding obligations. Earlier receipts retain
their original bytes and historical meaning.

| Gate | Actual result |
| --- | --- |
| Windows Developer04 | 16 stages passed; 7,978 native cases: unit 4,559, integration 2,258, architecture 1,132, business 29; no failures/skips. |
| Current Windows compatibility | 127 native cases passed for initial public API/configuration/frozen wire fixtures and independent mismatch controls. |
| Linux focused07 | 991 native cases passed: unit 649, integration 137, business 4, compatibility 127, selected architecture 74; no failures/skips. All 1,284 source inputs remained unchanged. |
| Bootstrap regression | Seven source-inventory controls passed separately on each OS, including building a verifier copy without prior outputs. Linux's 1,284 inputs remained unchanged. |
| Exact package consumers | Windows and Linux each passed 25 observation rows, 25 owned processes and 13 HTTP observations for CountryList, HTTP host and matching probe. These are observations, not additional MSTest cases. |
| Linux ordinary identities | Existing owner/nobody/owner probes returned 0/2/0 at socket modes 0755 and 0777. Denial caused no health capture; owner recovery succeeded. |
| Windows Healthcheck coverage | 2,866/2,990 lines (95.8528428%); conservative branch lower bound 1,981/2,184 (90.7051282%). Required thresholds remain 95%/90%. |
| Critical04 | All 20 explicit production controls matched their intended native failures. Fresh 650-unit/138-integration baselines and restoration passed; all 1,134 captured canonical inputs stayed unchanged during the run. |
| Automatic state03 | Tool-classified runnable subset scored 196/217 = 90.3225806%, with no timeout credit. Baseline/restoration passed 650 cases and all 1,284 inputs stayed unchanged. Complete automatic qualification remains pending. |
| Compiler collateral | Four separately isolated comparisons compiled and were detected by six intended native cases. The original Core DLL hash, all 1,284 inputs and the 650-case unmutated suite were restored. The automatic report/score was not changed. |

The exact unpublished `CIS.Core.0.1.0-dev.1.nupkg` has SHA-256
`1d48cfd58d26a2cdf2f694c7996d596ce058427e65837b3c2c565e230c280cc2`.
Its Core DLL has SHA-256
`fde622b8a3db47f331ea1735896fc92e7b80af11853c94b8e0efcf337eb146ac`.
The final Windows build produced those same Core DLL bytes. Source-copy Linux
builds retain their own origins; they are not represented as that packaged DLL.

## Corrections and attribution

The first real Linux nonowner probe raised an uncaught `SocketException` and
exited 134. The client now maps it to bounded `TransportOrProtocolFailure` and the
documented exit 2. The actual packaged consumer and identity runs verify the fix.
Unix socket mode follows umask; the old 0600 test assumption was corrected.
The 0777 run tests the peer-UID boundary with a world-writable socket.

Developer03 found one additional fixture defect: its verifier bootstrap copy
omitted nested source folders. That existing test now copies nested C# inputs and
excludes bin/obj. [bootstrap-input-comparison.json](bootstrap-input-comparison.json)
shows this is the sole source change after the focused/mutation snapshots.
Shipping Core and selected Healthcheck detector logic are unchanged. The final
Developer04 and the separate Windows/Linux bootstrap runs include the correction.
Evidence documents were generated after native execution and are not retroactively
included in its captured source snapshots.

Coverage used 640 unit, 137 integration and four business cases against the same
unchanged shipping Core. The final nine public error-reason tests receive no
retroactive credit in that collection. Complete native Core totals are validated
before selecting Healthcheck; matching line hits combine by OR and branch covered
counts by MAX. Eight declaration-only sources are explicitly reviewed and pinned.
See [coverage.json](coverage.json).

Automatic03 retains 196 killed, nine survived, nine timed out, three uncovered,
35 compiler errors and 68 ignored mutants in the selected state source. The
native tool score of 94.47% credits timeouts; the strict 90.32% does not.
Overlapping invalid Count-to-Sum instrumentation caused collateral compiler
rollback of some valid comparisons. Isolated checks of 2089, 2202, 2204 and 2221
compiled and were detected. Their separate outcomes do not relabel the immutable
automatic report or silently adjust its denominator. Remaining compiler/ignored
dispositions and broader module mutation still require qualification. See
[automatic-state.json](automatic-state.json),
[compiler-rollback-review.md](compiler-rollback-review.md) and
[compiler-collateral.json](compiler-collateral.json).

## Retained attempts and remaining work

Raw logs, TRX, Cobertura, actual binaries, process identities and source inventories
remain under `.artifacts/healthcheck/qualification-next/`; copied reports and hashes
here identify the completed observations. Earlier failures remain failures:
initial tool escaping, Forbidden-body verifier expectation, changing-source
preparation, false Unix 0600 assertion, uncaught Linux SocketException, critical01/02
survivors/input drift, automatic01/02 strict-score failures, the SDK11 restoration
attempt, Developer01 DLL locks, Developer02 formatting and Developer03 bootstrap.
Linux post-inventory shell line-ending/quoting attempts also remain recorded; the
successful replacement checked all 1,284 inputs. Long jobs now use private verifier
launchers and separate source/output copies.

Complete automatic Healthcheck/required Core mutation, remaining compiler rollback
and ignored-mutant review, and the required release profiles/coverage/platform
matrix remain open. Current compatibility qualifies the initial candidate; the
real publication ledger is empty and historical release replay is not applicable.
Real Heartbeat/watchdog and the future generic IStartable coordinator remain
external integrations. Ordinary Linux identities do not qualify privileged-root,
Windows alternate/elevated-user, remote-host or arbitrary container claims.
Kubernetes fragments remain deployment guidance.

Three subagents authored disjoint work; the coordinator owned structural changes
and native execution. There is no matched serial baseline or complete cost
telemetry, so no causal speedup is claimed. No push, publication, deployment or
external review was performed. Derived graph refresh follows canonical evidence
integration; its local outputs remain under the same ignored artifact root.
