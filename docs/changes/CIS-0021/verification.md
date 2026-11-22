---
title: CIS-0021 verification
type: verification-record
status: Draft
change_id: CIS-0021
---

# Verification

The implemented source head `ce9b1e8d842851563258df672c3d339417babf2b`
passed [actual Linux CI 45034280172](https://github.com/andrewspiteri/CISLIB/actions/runs/45034280172):
fresh locked restore, a zero-warning/error solution build and all 145 selected
services passed, with zero failures/skips. Native TRX elapsed is 518.8271011
seconds. Its BCL result gate passed; the downloaded report SHA256 is
`2ca0b7bfbaa28d90021068770969ae5eff77de345b83d1b024cffe82c5af07ef`.
The final CIS closeout commit must pass the same check on its own exact head
before merge. Earlier pending checkpoints and failed runs below remain historical
evidence; they do not override this observed result.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Pinned locked restore and full solution build | `solution-locked-restore`, `solution-build-null-fixed` | Passed | SDK 10.0.401, NET/ASP 10.0.12; zero build warnings/errors. Later narrow RabbitMQ corrections also build cleanly. |
| WORK-001 | PostgreSQL lifecycle and retained-result recovery | `postgres-owned-lifecycle-restart` | Passed | Windows: 5 passed, 0 failed/skipped, 36 seconds. Includes independent containers, client lease protection, failed-start cleanup, failed schema reconstruction followed by continuation, and clean same-storage restart. |
| WORK-002 | Initial unavailability and two restart boundaries | `rabbit-reconciled-restarts` | Passed | Windows: 3 passed, 0 failed/skipped, 52 seconds. Live application restart and full container restart have distinct evidence boundaries below. |
| WORK-001 / WORK-002 | Actual image and resource profile | Ignored intake manifests and selective Docker observations | Observed | PostgreSQL 18.7 / RabbitMQ 4.4.1 immutable Linux amd64 manifests; Ryuk 0.14.0 pinned index supports the same platform. Broker TLS-only loopback, 512 MiB/2 CPU/256 PIDs; enabled Ryuk is nonprivileged. No Ryuk hard-memory cap is claimed. |
| WORK-001 / WORK-002 | Full selected service lane, Windows | `windows-selected-services` | Passed | 145 passed, 0 failed/skipped; native TRX elapsed 461.141 seconds. Includes the actual outside-friend packaged RPC and genuine process-death/recovery cases. |
| WORK-003 | Ordinary whole-solution fast selection, Windows | `windows-fast-unreachable-docker` | Passed | 9,956 passed, 0 failed/skipped across seven families; command elapsed 383.917 seconds. Docker endpoint deliberately unreachable; zero containers before/after. |
| WORK-003 | Actual BCL result checker | `guard-current-service-report-fixed`, `guard-negative-controls-fixed` | Passed | Accepts the real 145-case TRX; rejects zero, skipped, failed-summary, cleanup-error and nonzero native-exit controls with controlled exit 1. |
| WORK-001 / WORK-003 | Shipping closure and dependency direction | Fast Architecture cohort; ignored `shipping-direction-assets.json` | Passed | 361 Architecture cases include the current Core package closure. Seven shipping asset graphs and actual package/project edges have no Testcontainers/Docker dependency. |
| WORK-001 / WORK-002 | Exact local restoration | Ignored `local-restoration.json` | Passed | Zero retained owned children, containers or volumes; three newly pulled unshared image references removed. Removed 39 owned scratch directories / 63,253,292 bytes after drain, retaining hashes/reports/capped logs. |
| WORK-003 | Linux service CI | [Run 45034280172](https://github.com/andrewspiteri/CISLIB/actions/runs/45034280172) | Passed | Exact implemented source head ce9b1e8d: 145/145, zero failures/skips; native gate passed on Ubuntu 24.04 / Linux amd64. Final metadata head must also qualify before merge. |
| All | Independent final local receipt review | [Review](review.md), frozen receipt | Passed | All 51 source, six context and ten report hashes match; ten binary/configuration snapshots match both their original and snapshot paths. Actual result counts/counters agree; closure, restoration and protected user file independently checked. |
| WORK-004 | Portable consumer locked restore/build and affected Windows consumers | `portable-solution-locked-restore`, `portable-solution-build`, `portable-countrydata-unit-consumer`, `portable-source-configuration-consumer` | Passed | Zero build warnings/errors; CountryData 29/29 and the current source-fixture 1/1, zero failures/skips. Both RIDs locked; existing groups/package IDs/versions unchanged. |

Native phase receipts and reports are retained under ignored
`.artifacts/testcontainers/CIS-0021/`; accounting and exact resource observations
are under `.cis/local/testcontainers-intake/`. Actual Linux CI remains pending at
this local-ready checkpoint.

The frozen local receipt is
`.artifacts/testcontainers/CIS-0021/frozen-local/receipt.json`, SHA256
`f1469af9e3bbc866c64807a92899352545030d01871da60337ec6b71c9782306`.
It binds 51 source inputs, ten immutable binary/configuration snapshots and ten
passing TRX reports. Core SHA256 is
`c250069427bca3cb23cae887e8a8635430d7e2666df01be2f2f0217a27f0dd27`,
identical in the source, Unit, Integration and actual RPC child outputs. Mutable
CIS closeout documents/catalog metadata are excluded. The working candidate is
based on merged main `df1e5d90584fd88d7b48bfb0144ff574166bb1a5`; subsequent source
commit/CI identities will be recorded separately.

Fast family counts are Unit 6,679; Integration 2,557; Architecture 361;
Compatibility 179; Security 126; CountryData 29; Business Cases 25. Selected
services cover 11 transaction, 19 operation, 14 retention, two operation restart,
three PostgreSQL lifecycle, 42 message store/reply, 23 RabbitMQ transport and
31 combined host/publication/dispatch/request/footprint/package cases. Every
selected service result passed; the fixed real-service footprint case remains
selected rather than being hidden as optional performance work.

At local readiness, conservative source/preparation estimates total 446 minutes
(including the brief unmeasured first direct guard probe); separately measured
native elapsed including failures/restoration is 27.4915 minutes. Four external
logical attempts are retained under the shared 12 hours / 16 attempts. Automatic
CIS counters remain separate and are not fabricated. Counted local storage is
5,651,310,024 / 6,442,450,944 bytes before the small frozen snapshots, including
the conservative 2 GiB Docker allowance, cache charges and 64 MiB reserve. All
restoration reserves remain protected. The user-owned untracked launch settings
remain byte-identical at SHA256
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.

## Retained failures and restart reconciliation

The first sandbox formatting invocation could not connect to Roslyn's native
pipe. Native formatting and verification then passed. Compilation identified
unsupported `WithUser` and nullable API usage in the new test-only RabbitMQ owner;
the supported native create modifier and guarded nullable access corrected them.
Those failures and their costs remain in the native ledger.

The first broker startup exposed a module binding/readiness mismatch: two builder
bindings were configured while the native modifier removed one. The fixture now
moves the module's single random binding to TLS 5671, removes plaintext exposure,
retains the module startup wait and adds actual internal TLS-port readiness before
authenticated AMQP readiness. Its failed-start path removed its owned resources.

Separate full stop/start and atomic engine restart both changed the assigned
host port on Docker Desktop 29.6.1. The decisive atomic probe retained its container
but changed port 53195 to 50992 after authenticated fresh readiness. That combined
same-live-endpoint proof failed and is not presented as passing. An earlier atomic
probe also exposed old startup-log matching before fresh readiness, with a cleanup
connection failure masking the original fault; retained diagnostics and reaper
postflight distinguish that path from normal explicit cleanup.

The reconciled tests preserve the meaningful behaviors separately. A real RabbitMQ
application stop/start keeps the Erlang VM and Docker network, asserts the same
original endpoint, and exercises the unchanged live transport's native recovery,
persistent unacknowledged redelivery and a subsequent confirmed publication. A full
same-container/data-volume stop/start retains persistent unacknowledged work for a
fresh production transport at the current inspected endpoint. No live transport is
retargeted, no fixed host port or proxy is introduced, and production code is
unchanged. Application restart does not establish Erlang VM/container restart;
single-node container restart does not establish power loss or multi-node failover.

The first published head `36d58a540dbcbb838df1f740293d302efb70a1c3`
created [run 45026413257](https://github.com/andrewspiteri/CISLIB/actions/runs/45026413257),
which GitHub rejected before creating any jobs. The workflow used `runner.temp`
at job-level `env`, where that context is unavailable. A narrow workflow-only
correction selects owned tool paths from `RUNNER_TEMP` through `GITHUB_ENV` in a
normal step before SDK setup. The original frozen local receipt retains its old
workflow hash; the ignored CI-definition delta records the corrected workflow
hash and subsequent qualified head. Executable test/product sources and native
binary/report snapshots remain unchanged. No Linux service pass is inferred from
this definition correction.

The corrected workflow's [first actual Linux job](https://github.com/andrewspiteri/CISLIB/actions/runs/45026847247)
verified Ubuntu 24.04.5, SDK 10.0.401, both 10.0.12 frameworks and Linux amd64
Docker, then failed locked restore before build or tests. CountryData's selected
RID changed to Linux while its lock contained only Windows. The cold cache also
exposed missing exact source mappings for retained `Microsoft.Extensions.Configuration`,
`Microsoft.Extensions.Configuration.Binder` and
`Microsoft.Extensions.Options.ConfigurationExtensions` packages. WORK-004 adds only
those mappings and declares `win-x64;linux-x64` as supported lock RIDs, leaving
the actual selected RID, inventory/output behavior, existing lock groups and
package identities unchanged. Its one new seven-line Linux group repeats the
existing ProtectedData 4.5.0 identity. Independent three-file review passed.

The resulting Windows locked restore and full build pass. The maintained
CountryData unit example passes 29/29; the current
`ActualBuiltSourceFixtureDiscoversPassiveConfigurationAndStartsWithTheSameDefaultRoots`
case passes 1/1 using the existing Type Manager host, exact Core bytes and joined
native stop. Historical Console/Web rows were not resurrected. Rebuilding after
publication changes Git metadata in the binaries: the new Core SHA256 is
`00e02daeee14d66ae606798292689bcf5d2a96babac64c6b6f58919b5e808b74`.
The original frozen local snapshots/reports remain intact as earlier local-ready
proof; a separate portable-consumer receipt records the new source/binary delta.
No executable product/test C# changed for this portability correction. Actual
Linux selected-service qualification remains required on the corrected head.

The next [Linux run 45028902924](https://github.com/andrewspiteri/CISLIB/actions/runs/45028902924)
passed fresh locked restore and a zero-warning/error solution build, then executed
145 service cases: 95 passed, 50 failed, zero skipped. All 50 failures preserved
the production guard requiring a known captured local application/environment;
the cold test host had no environment identity. The earlier local helper had
supplied those facts, exposing a test-launch profile prerequisite rather than a
broker or production authorization defect. The failed native TRX and exact
164-second job cost are retained.

The integration project now conditionally selects an anchored identity-only
native runsettings file for ordinary CLI/IDE launches. The explicitly selected
strict service profile supplies the same known test identity and retains its
empty-selection, timeout and cleanup gates. Ordinary focus filters retain their
previous zero-match success and long-test semantics. No global initializer or
parent-process environment mutation is added, and production identity/security
guards remain unchanged. Independent profile review passed. With inherited CORE
variables removed from the actual SDK process, the initial RabbitMQ cohort passed
23/23; the final ordinary identity-only profile then passed authentic publication
1/1 and a deliberately empty ordinary filter exited successfully. Its Docker-free
fast integration sweep executed 2,557 cases: 2,556 passed and one failed. The
offline CountryData package consumer copied the new dual-RID source-build
property into its isolated local-only package feed and requested extra runtime
packs. The property is now conditional on the exact existing source-import path;
source checkouts retain both lock targets while packaged deployments retain the
SDK's selected-runtime behavior. Independent review accepted this narrow change.
The subsequent full locked restore and solution build pass with zero warnings or
errors; the actual previously failing package-consumer case passes 1/1 with CORE
variables cleared and the ordinary project profile. The original failed report
is retained alongside the 2,556 unchanged passing cases. No product/test C# or
package versions changed. Actual Linux service qualification remains pending.

The next [Linux run 45031970822](https://github.com/andrewspiteri/CISLIB/actions/runs/45031970822)
restored and built successfully, then passed 133 of 145 cases with zero skips.
Eleven RPC failures came from the clean solution build placing its unlisted child
project under Debug while the Release parent copied an empty Release glob. The
integration project now preserves parent configuration for unmapped references
and fails its build clearly if the required child DLL/runtimeconfig/deps are
absent. After the exact old child/copy outputs were preserved and removed, a
fresh solution build produced the Release child and passed with zero warnings or
errors.

The remaining safe-DLX case recovered three of four confirmed records and
observed the fourth retained in the source, with no transport failure. RabbitMQ
4.4.1's [native confirmation retry default](https://github.com/rabbitmq/rabbitmq-server/blob/v4.4.1/deps/rabbit/Makefile)
is 180 seconds. A capacity-one target can require two such rounds for two
confirmed source records. Its bounded observation is now 390 seconds; all exact
accepted-byte, recovered-set and source/target-empty assertions remain intact.
Broker defaults and the overall 20-minute service limit remain unchanged. This
test deadline is not a production delivery-latency guarantee. The original failed
TRX is retained; focused and subsequent Linux outcomes are recorded separately.

The resulting Windows focused RPC/DLX run passes 12/12, zero failures/skips,
in 5 minutes 11 seconds, with inherited CORE identity cleared. It includes all
11 real RPC child-process cases and the unchanged exact-record DLX recovery
assertions. Scoped formatting/verification and the affected build pass. The four
current source, Unit, Integration and actual child Core copies match SHA256
`838154abc0f9cad16162bf49ff6c884c5aab19129a69bf42c166a65d3e55e466`;
earlier immutable binary snapshots retain their historical hashes.

All four implementation tasks are Complete through the supported legacy CIS
lifecycle. The final publication continuation was admitted while WORK-003 was
active: only closeout metadata, its automatic unchanged-workflow qualification,
bounded report inspection and qualified merge/main synchronization remain.
Delegated redistribution moved 15 unused ordinary minutes from WORK-001 to
WORK-003 without changing the shared 12 hours/16 attempts, existing restoration
floors or storage limits. The pre-final-head checkpoint records 526 estimated
active source minutes, 85.51585 separately measured native/CIS/CI minutes and ten
external logical attempts. Automatic CIS counters remain separate. Counted local
storage is 5,697,018,479 / 6,442,450,944 bytes, including the conservative Docker
allowance, cache charges and protected 64 MiB. Final CI/publication costs remain
charged to the same allocation.

After the last affected native checks, exact postflight found zero owned children,
containers, volumes or images. Three freshly pulled unshared images and five
phase scratch directories / 3,545 bytes were removed, preserving reports and
binary snapshots. Twelve unrelated Docker volumes created before this task were
left untouched. The protected user launch settings retain their original hash.

## CIS tool-use snapshots

| Task ID | Check | Artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2139; failed=291; possibleTokenSavings=591; ledgerDigest=sha256:95a69fabeaad7170b4f628de6daad8970dcaffc0c5b106ff70c79d4185aaa82e |
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2140; failed=291; possibleTokenSavings=591; ledgerDigest=sha256:b84bffcedabc83c4719f6c0375982a8b96911e1b3e423fcc7475322267172dda |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2141; failed=291; possibleTokenSavings=591; ledgerDigest=sha256:902ab14b3a9565d64b1caf02801f748cc0d71881bd809d7e57557d8ee8c20c9c |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2142; failed=291; possibleTokenSavings=591; ledgerDigest=sha256:ae3bef1948a7f9c152dad8910d81211d3c119be50927a27e8c54683aa706e03b |
