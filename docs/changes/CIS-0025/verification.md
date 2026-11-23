# RM-07 v1.1 correction verification

The correction uses the existing unmerged `codex/rm07-central-management` branch,
with baseline `38031b41b4d8ce4f7e991b71038abdd60689532c`. No push, merge or release
is included. The current necessary-changes request and earlier no-permission-pause
delegation authorize execution; Codex selected the finite profile. Its exact
numbers were not separately human-reviewed.

The imported [v1.1 requirements](../../../planning/management/sources/CISLIB_RM07_Central_Management_Service_and_Client_Specification_v1_1.md)
have SHA256 `5e3700b5df9dd95362e4774dbf565842bef3bc8f216a219f9b4caf14d366a2e2`.
The original v1.0 source and CIS-0024 history remain intact. See the
[gap review and bounded plan](implementation-plan.md).

## Resulting behavior

| Requirement gap | Implemented reconciliation | Evidence |
| --- | --- | --- |
| Thirteenth authoritative control API | Exact nonce/registration echo, persisted authority/revision, finite lifetime, no-store and no read retries | Shared schemas/vectors; both native clients; real PostgreSQL control tests |
| Optional initial receipt was insufficient | Selected managed execution starts closed and requires direct live registration/control and sequential module reconciliation | ManagedAdmission, registration/hosting tests and actual CISApplication delivery case |
| Connectivity did not control finite work | Freshness checked on each actual admission, request-start age through cleanup/reconciliation, first failure/expiry/prerequisite loss closes, late/old/stopping responses fenced | Windows and actual Ubuntu unit cases; native outage case below |
| Work and settlement needed distinct treatment | Jobs acquisition/occurrence/iteration and application-message/explicit effect HTTP admission close; admitted callbacks, valid lease rules, native replies, original results and outbox/Audit settlement retain owners | 15 focused Jobs cases; 6 focused Messaging cases, including held original-operation claims; actual HTTP authorization/readiness/held-request case |
| Health readiness affected liveness/global routes | Bounded readiness-only sources, current readiness metric, publication revision changes, compatible one-way reader safety rule, preserved default output and HTTP 200/500 readiness mapping | Six readiness-source cases, existing protocol/policy/metric cases, native status/effect HTTP case |
| Direct store coupling lacked a qualified backend profile | Typed application backend over the same original Transactions/Operations/Messaging/Audit graph; complete PostgreSQL Standalone/Shared modes | Twelve native backend cases plus existing persistence/metadata cases |
| Authority/recovery depended on process memory | Explicit schema-v2 migration, persisted namespace authority/revision/epoch/hold, qualified primary reads, no silent reinitialization; cursor-v2 authority binding | Upgrade/reconstruction, hold, unsupported profile and native cleanup cases |
| Client/public handoffs omitted control and gate ports | Native C# and Node control with separate per-call workload/operator identities; outside-friend public control/admission/reconciliation consumers | Existing real-service and exact-byte package consumers |

`RequireInitialRegistration` and `IsHealthCritical` remain source/binary-compatible
properties. They cannot make selected managed bootstrap optional or convert
ordinary management loss into process liveness failure. The existing public
client interface supplies a default unsupported control implementation for old
custom implementers; selecting managed execution requires actual control support.

Control-echo mismatch uses the existing per-call response mapper and its native
Business rejection `management.control.invalid_confirmation`. It does not invent
a result after REST returns. Malformed representations retain native decoding
facts. No per-request codec or new REST extension was added. Control and reporting
have independent due times within the same nonoverlapping owner, including an
explicit short reporting interval. Each control attempt conservatively closes
admission into Reconciling until actual exchange cleanup and reconciliation finish.

The provider retains original metadata operation scopes across migration. An
authority epoch is not appended to original keys or actor scopes. A recovery hold
blocks bootstrap/control/new mutation while permitted original-result inspection
and earlier durable outgoing obligations keep their existing policies. An older
restored database cannot prove its own lost history; trusted recovery hold/expected
epoch and operator reconciliation remain required.

Readiness-only sources extend the previous strict derived-readiness invariant.
Deploy the current Health reader/probe with this selected profile: an older
strict-equality reader can classify a Healthy/NotReady frame as unavailable.
Unselected/default reports retain their previous semantics. The API compatibility
pass does not establish old-reader interoperability for the new opt-in profile.

## Executed checks

Receipts are under `.artifacts/management/CIS-0025/`. The native journal retains
exact arguments, exit codes and measured durations; the separate metadata journal
retains CIS commands and failures. Source effort estimates and logical attempts
are in `.cis/local/management-v11/resources.json`. Direct native execution is
counted there even though it is not a native CIS test batch.

| Check | Passed / failed / skipped | Receipt |
| --- | --- | --- |
| Pinned Core/CommonTesting, unit, product, integration, architecture and compatibility builds | Zero warnings/errors on corrected candidate | Respective `native.log` and native-cost journal |
| Focused Windows Core regressions | 749 / 0 / 0 | `WORK-006/v11-corrected-unit-tests/results/v11-units.trx` |
| Same focused set on actual Ubuntu 24.04/.NET 10.0.12 | 749 / 0 / 0 | `WORK-006/v11-linux-focused-units/results/linux-native.trx` |
| Existing OpenAPI/schema/independent conformance consumers | 89 / 0 / 0 | `WORK-001/v11-management-contract-tests/results/v11-contracts.trx` |
| TypeScript build, browser boundary compilation | Passed | `WORK-001/v11-ts-lexical-build`, `v11-browser-typecheck` |
| Native Node portable/client/wire tests | 114 / 0 / 0 | `WORK-001/v11-ts-lexical-tests/native.log` |
| Real PostgreSQL complete-backend profile | 12 / 0 / 0 | `WORK-002/v11-corrected-backend-tests/results/v11-backend.trx` |
| Management product/security/metadata/persistence/delivery, native C#/Node, public packages and affected application HTTP composition | 45 / 0 / 0 | `WORK-006/v11-current-host-and-consumer-tests/results/v11-hosts-consumers.trx` |
| Public API compatibility and both original source/frozen consumers | 3 / 0 / 0 | `WORK-006/v11-public-api-original-consumers/results/v11-compatibility.trx` |
| One-replica backend isolation, actual API replica loss and held reconciliation through the healthy replica | 1 / 0 / 0 | `WORK-006/v11-availability-capacity-tests/results/v11-availability.trx` |
| Pinned SDK whitespace apply/verify on explicit changed C# files | Passed for frozen subsets | Format/verify phases in native journal |

The Linux lane runs the actual retained Linux ELF runtime and managed VSTest
18.7 with ReadyToRun disabled. It is not a Linux SDK build or a newly built v1.1
application-image proof. Source, integration, unit and compatibility copies of
Core agree on SHA256 `ab6c86f2de721572fde8b5820d0a8edbe750665e245e6cb22391b81d9eab311e`.
The public package fixture separately asserts exact deployed Core/CommonTesting
bytes and its unchanged dependency allowlist.

## Acceptance mapping

AC01–13 retain their original native authority, whole inventory, scoped resource,
ETag/concurrency, original-result/Audit atomicity, response-loss, actor attribution,
wire and both-client behavior. The current 45 native cases, 89 contract cases,
114 Node cases and affected unit set exercise those boundaries without repeating
the underlying modules' full qualification.

AC14–17 are covered by mandatory closed bootstrap, direct startup, finite callback
admission/continuation, monotonic age and timing-gap checks, cancellation/cleanup,
and generation/authority/reconciliation tests. AC18 adds the isolated-role API
replica case: actual SQL permission refusal cannot fall back to cached authority;
the other root retains the same primary state; actual listener stop and held local
reconciliation keep admission closed until the healthy-root exchange settles.
AC19–20 use the complete PostgreSQL modes, explicit upgrade/reconstruction,
native primary/durability qualification, recovery hold/epoch, atomic original
metadata/result/Audit cases and whole-snapshot size/capacity cases. No other
complete provider is advertised.

The outage case uses two actual typed native clients with explicitly selected
trusted origins. Its test delegator selects the healthy client; it does not claim
an automatic cross-origin redirect, a new production failover framework or an
actual database-primary election. CommonTesting controls the local scheduling
and held module hook, while actual SQL permissions and TLS/JWT establish the
provider and authentication facts.

## Retained failures and limits

The first TypeScript run passed 111 and failed three new lexical integer cases;
the existing scanner's bounded integer paths were corrected and all 114 passed.
The first Core compile found an unsupported per-request codec setting; it was
replaced with the actual mapper. The first combined unit build found a test-only
generic nullable capture, corrected with a bounded property pattern. The first
unit run passed 744 and failed four: URL/query-port assumptions, an invalid reply
fixture and an overwritten precise gate diagnostic. Corrections and independent
control/report cadence preservation then passed all 749.

The first backend run passed one and failed eleven before SQL because omitted
Npgsql session attributes were incorrectly refused. The guard now accepts the
default while preserving actual primary/durability checks, and all twelve passed.
After that pass, the post-execution storage guard hit PowerShell's case-insensitive
`kB`/`KB` double-match. Its case-sensitive correction measured 1,622,988,608 counted
Docker bytes and 6,049,501,309 total bytes, below both limits, with no running
containers. That guard fault is distinct from the twelve passing native tests.
The initial availability setup selected four typed management aliases and exceeded
the existing 32-operation REST bound. Its corrected test uses two typed workload
aliases and the existing native HTTP fixture for administration; production limits
were preserved.

No new packages, source mappings, SDK/runtime pins, existing database access,
external deployment, trust-store installation, machine keys, broad failure campaign
or image rebuild campaign is included. PostgreSQL is the sole complete provider;
Redis/Cosmos remain explicitly unsupported. Shared API replica availability over
one qualified durable primary does not establish asynchronous database failover,
snapshot-loss repair, AKS federation, cert-manager issuance, destination Audit
persistence or release qualification. All TLS backing remains within the existing
W01 owned current-user exception and joined fixture cleanup.

## Source and resource closeout

The final product source manifest contains 108 changed/new files, excluding CIS
closeout documents and the unrelated user launch settings:
`WORK-006/final-source-and-resource-snapshot/product-source-hashes.csv`, SHA256
`2b914c3a8aa00b7ea7093f98fb17b2ca31ea8348d4f94afb8542795542f6aca0`.
All 75 changed C# files passed the final explicit-file whitespace verification.
No project/package lock, source mapping or SDK/runtime pin changed.

At the 21:06 UTC snapshot, conservative source estimates plus actual native/CIS
elapsed work total 49,770,088 milliseconds (about 13.83 shared active hours), with
18 logical attempts, before final CIS closure. The shared maximum remains 48
hours/56 attempts, including 7,200 restoration seconds. Every existing task's
protected reserve remains intact. Two unused contract attempts moved to integrated
availability validation; no resource total increased.

Counted retention was 6,051,467,960 bytes against 6,442,450,944, including the fixed
2-GiB Docker charge, retained external native cache, owned TLS backing and 64-MiB
protected reserve. Additional headroom was 390,982,984 bytes. Actual retained
Docker usage was conservatively counted at 1,622,988,608 bytes inside its
2-GiB allowance; no running task container remained. The snapshot is retained
alongside the manifest. It is an observed retained footprint, not a continuously
measured physical peak claim.

The unrelated CountryData launch settings remain byte-identical, SHA256
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.
Native cases joined their owned cleanup, including the generated isolation role,
actual clients, product roots and container leases. No prior/foreign Docker
volume or credential was selected or deleted.

All seven CIS work items are Complete. Final plan validation is valid and work
accounting is known, with no recovery hold. The post-closure receipt records
50,280,361 milliseconds (about 13.97 shared active hours), 18 logical attempts,
6,051,633,738 counted bytes and 390,817,206 additional bytes of headroom. There are
no running task containers, the product manifest is unchanged, and Core/user-file
hashes still match. See `WORK-006/final-source-and-resource-snapshot/post-closure-summary.json`.
The branch and baseline are unchanged; no commit, push, merge or release occurred.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2350; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:95bf5fe37f9bfe8da3f6948a256930acd7d7a9b6b2f3860da7feed36710f99a8 |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2351; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:1692fcd7949516a67b12ee445bbff75d48ecb509aec5553279d83e6ab48f6f20 |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2352; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:a615d713cf231e701ca9a94fea387698b08dba1177b73c5bbc3a181901df882b |
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2353; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:7be3138ef0669d5f1c9801eab0529a7d508f3f75d667f72a7b3d205c5a108000 |
| WORK-005 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2354; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:0f78fe44abb5eab13214de2cf53b663c20fe94f08f8710e7f61ab872c986362c |
| WORK-007 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2355; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:0f1fc6acbbfa32376c9f7df31d649b217e3702201d15e886d8a96360697fb48e |
| WORK-006 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2356; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:528dabdd0f6607271537e93a7eeb0f100bdb06fffb541672e90c9731ee558d08 |
