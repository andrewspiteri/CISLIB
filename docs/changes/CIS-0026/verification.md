# RM-07 review correction verification

Baseline: `fe029a85741705dc51c1093403248fc4aa2920ca` on the existing
`codex/rm07-central-management` branch. The pasted review identifies canceled
public startup retaining an open admission gate and native-fetch body/cleanup
failures escaping as exceptions instead of structured recovery facts.

Both findings are corrected and the focused checks below pass. Earlier CIS-0025
observations remain retained evidence for that candidate; they are not new passes
for the corrections.

The [bounded plan](implementation-plan.md) covers implementation, tests,
documentation, review and restoration. The current user request and earlier
no-permission-pause delegation authorize execution; Codex selected the finite
profile without representing its exact numbers as separately human-reviewed.

## Corrected behavior

Public `ManagementRegistration.StartAsync` now retains a startup cancellation
notification through initial registration, control and reporting. Cancellation
closes and invalidates admission immediately, including while an uncooperative
observation or inventory report still owns accepted work. Startup joins that work
and the notification before completing, and installs no recurring subscription
on the canceled path. Repeated startup joins the same outcome. Unexpected work
and admission-event faults remain visible together. The existing Stop owner and
REST-before-Heartbeat drain order are preserved.

Native fetch response bodies now classify expected read/cancel failures at the
transport boundary. The existing request engine retains status, timeout or caller
cancellation, the exact original operation key and mutation uncertainty. Native
cancellation is joined and a rejected cleanup remains a separate fact. Reader
state/release errors, decoding faults and unexpected injected `Error`/`TypeError`
sentinels still propagate. Default native browser TypeErrors are supported;
explicitly injected streams must retain a native marker or use the existing
`ClientTransportFailure` contract. No request-engine or public-port change was
required.

The new native fixture has the accurate `RequiresNode` category. Fast .NET-only
recipes exclude it; the existing service job already installs pinned Node and
builds TypeScript, and now includes this category alongside `RequiresDocker`.
Root and canonical testing prerequisites agree. No CI job or harness was added.

## Executed evidence

Native receipts are under `.artifacts/management/CIS-0026/`. The source-effort
estimates and logical attempts are in `.cis/local/management-review/resources.json`;
separate native and CIS journals retain measured commands, durations and failures.
Direct native checks are counted there rather than presented as CIS test batches.

| Check | Result | Receipt |
| --- | --- | --- |
| Core/CommonTesting and affected unit build | Zero warnings/errors | `WORK-001/startup-unit-build/native.log` |
| All Management unit cases, including six new cancellation/fault rows | 211 passed, 0 failed, 0 skipped | `WORK-001/startup-unit-tests/results/management-startup.trx` |
| TypeScript packages and Next compile-only example | Passed | `WORK-002/typescript-direct-build/native.log` and native journal |
| Portable Node tests, including four new native-body/fault/cleanup cases | 118 passed, 0 failed, 0 skipped | `WORK-002/typescript-corrected-tests/native.log` |
| Browser consumer typecheck | Passed | `WORK-002/browser-direct-typecheck` native journal entry |
| Product/integration and existing fixture build | Zero warnings/errors | `WORK-002/native-fetch-integration-build/native.log` |
| Actual default-fetch HTTPS GET/PUT body disconnect, deadline and caller cancellation | One MSTest passed, asserting all six native scenarios | `WORK-002/native-fetch-corrected-https-tests/results/native-fetch-corrected.trx` |
| Scoped pinned whitespace apply/verification, all four changed C# files | Passed | `WORK-001/startup-format-verify` and `WORK-002/native-fetch-format-verify` |
| Independent implementation, fixture and existing-CI selection review | No remaining blocker | Reviewed source and these reconciled records |

Checks used SDK 10.0.401, .NET/ASP.NET Core 10.0.12, Node 24.15.0, npm 11.12.1
and the existing TypeScript 5.9.3 compiler. The compiler/test/browser commands
were invoked directly through the pinned Node executable, matching the checked-in
npm script arguments without its failing child-shell command lookup.

Core source output and both unit/integration copies agree on SHA256
`8598372be86e3ead373e38856358523db6d4c731a499ab9c91b53e518817c086`.
The native child receives only newly generated private fixture PEM files and its
explicit public-root input, uses real HTTPS/native fetch, confirms headers and
the original PUT key reached the server, and joins response/listener/socket and
process cleanup. One attempt is asserted for every scenario despite configured
read retries. It does not establish whether a database mutation committed.

## Retained failures and limits

The initial npm build launcher failed to resolve its child `node` command before
compiler execution. Direct pinned invocations then built successfully. The first
118-case Node run passed 117 and failed one new test: its source eagerly pulled
before response ownership, so cancellation correctly retained a different primary
fault. Setting only that controlled source's high-water mark to zero made its
entry signal prove actual reader demand; all 118 then passed. Product code was
unchanged by this test correction.

The first native HTTPS run failed because the fixture threw an intentional TLS
`ECONNRESET` from an event callback. Its correction accepts only a bounded owned
reset allowance established after actual HTTPS headers and retains other TLS
errors until joined cleanup. Strict header, status, result-kind, original-key,
uncertainty and attempt assertions remain intact; the corrected native run passed.
These failures and actual costs remain in their original receipts. An initial
sandbox refusal to read retained Docker storage launched no product check; the
existing read-only storage observation then ran with the required access.

This is scoped Windows execution plus portable compilation, not a new Linux,
actual-browser, CI-run or release qualification claim. Existing PostgreSQL,
multi-replica, package/API and unchanged-module suites were not replayed for these
private lifecycle/transport corrections. The workflow selection is reviewed source;
an actual CI result remains separate. No dependencies, lockfiles, runtime pins,
service backend, trust stores or application configuration were changed.

The unrelated CountryData launch settings remain byte-identical at SHA256
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.
Product source hashes and cleanup/storage receipts are retained in
`WORK-001/final-snapshot/`. No commit, push, merge, release or deployment occurred.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2375; failed=325; possibleTokenSavings=591; ledgerDigest=sha256:fa401dc80da354857d72ce5b06cdb7997061e608d15d8301940b162c2ca962f9 |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2376; failed=325; possibleTokenSavings=591; ledgerDigest=sha256:a05ebdbffb9146a0f95ff0fa937c9baab3705b8b30c5ee773f40bf7e5f31a254 |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2377; failed=325; possibleTokenSavings=591; ledgerDigest=sha256:168719cf4a7e1c8d410c0096ab254836ef39a1c3a2ac90b61cc8e9214e832020 |


## Closure

All three CIS work items are Complete. Final plan validation is valid and accounting is known with no recovery hold. The post-closure snapshot records 16133700 milliseconds (about 4.48 shared active hours, including conservative source estimates), 5 logical attempts, 6054484820 counted bytes and 387966124 bytes of headroom. Every task remains within its ordinary allocation and protected reserves remain unused. The thirteen product source files match their frozen manifest, no fixture PEM files or running containers remain, and the user launch settings still match. See WORK-001/final-snapshot/post-closure-summary.json. No commit, push, merge, release or deployment occurred.
