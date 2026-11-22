---
title: CIS-0020 verification
type: verification-record
status: Draft
change_id: CIS-0020
---

# Verification

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Current affected units, Windows / Ubuntu | `correction-affected-units-windows-retry` / `correction-affected-units-linux` | Passed | 542/542 each; zero failures/skips; all 15 new unit cases included. |
| WORK-001 | Current SQL/TLS Messaging integration, Windows / Ubuntu | `correction-current-messaging-integration-windows` / `correction-current-messaging-integration-linux` | Passed | 84/84 each; all five new native cases included. |
| WORK-001 | Actual public packaged RPC, Windows | `correction-current-package-rpc-windows` | Passed | 1/1; fresh outside-friend package consumer commits typed query/durable replies and replays one original effect. |
| WORK-001 | Current Core dependency closure, Windows | `correction-current-core-closure-windows` | Passed | 1/1 after a zero-warning/error architecture build; approved Microsoft/framework closure unchanged. |
| WORK-001 | Affected builds and scoped format | `.artifacts/messaging/CIS-0020/native-costs.jsonl` | Passed | Pinned SDK 10.0.401, NET/ASP 10.0.12; final Unit/Integration builds have zero warnings/errors; scoped whitespace format and verification pass. |
| WORK-001 | Independent source and evidence review | [Review](review.md), immutable receipt below | Passed | Cross-author review has no required/optional finding; root independently checked all 97 source, 17 binary and six passing TRX hashes. |
| WORK-001 | Exact owned restoration | `.cis/local/messaging-review-intake/final-restoration.json`, `restoration-history-confirmation.json` | Passed | Actual child/connection drain; all current and earlier failed owned fixtures/private inputs restored; zero retained owned resources. |

## Current candidate and promised continuation

The ignored `.artifacts/messaging/CIS-0020/final-integrated-receipt.json` binds
97 source files, 17 immutable binary/configuration snapshots and six passing TRX
reports. Core SHA256 is
`2a3d514d94248269c1cf9f141ea1a15db56758b6d67995231786cceaee4e95e7`,
identical in the Unit, Integration, actual crash child, Architecture and source
outputs. The root's independent hash check found zero mismatches. Closing CIS
documents are outside this frozen source manifest; the four listed module guides
and implementation/test sources remain unchanged.

Each native 84-case lane contains 42 PostgreSQL store cases, 12 RPC cases and
30 host/publication/dispatch/disposition/exporter/fixed-footprint cases. The five
new cases prove atomic pressure admission followed by both requests and a third
delivery, exact canonical-free quarantine followed by a valid Event with the same
MessageId through the same consumer, the SQL rejection-purpose/correlation boundary,
and both readiness-policy values after actual native transport disconnection.
Each unit lane contains all 15 new cases. The earlier 502-case filter selected one
CommonTesting class explicitly; the current filter selects all CommonTesting,
adding 25 pre-existing cases as well as the 15 new cases, for 542.

The optional RabbitMQ implementation is unchanged. Its earlier 21-case provider
results remain historical component evidence; they were not rerun for this
correction. The current 84-case lanes exercise the actual SQL/broker integration
affected by these fixes. The readiness rows close the selected root's real native
connection, with its host/store alive. They establish disconnection policy, not
automatic broker outage recovery. No general fixture ownership check was weakened
to restart a differently owned container.

## Retained failures and corrections

The first current Windows unit run retains **540 passed / 2 failed / 0 skipped**.
Both new pressure tests waited for a handler that correctly could not run: their
TargetOnly grant did not authorize the resolved `messaging.reply` resource. The
fixture now grants the exact legitimate reply resource to the intended producer
or native service subject; production Security is unchanged. The first-handler
wait also observes actual dispatch failure/SafeCode rather than hiding it behind
a timeout. Independent review accepted this narrow three-test-file correction.
Both current 542-case lanes then passed.

Initial broker setup first created a root-owned native cookie through an early
root probe, causing EACCES. That exact stopped fixture, its unshared volume and
five private inputs were restored with a retained hash manifest. Probes now run
as the native RabbitMQ user. The replacement then exposed node ping preceding
application readiness; guarded continuation uses `check_running`, verifies the
fresh principals and preserves the same owned container. A list-users header
initially triggered its conservative identity guard without changing a principal;
the exact header was separated from account rows before continuation. Setup failures,
redacted diagnostics and actual costs remain preserved.

The formatter normalized six source files, so affected projects were rebuilt.
An ordered-dictionary property projection failed after format/verify had passed;
reading the retained hash JSON corrected that observation without repeating the
successful formatter. The later test-fixture correction was formatted, verified
and rebuilt before the final native runs. No failed result, cost or logical
attempt was reset.

## Restoration, accounting and limits

Final restoration observed zero Windows native children, zero Linux crash children,
zero broker connections and zero other PostgreSQL client sessions. It removed two
current owned containers, two volumes, two new unshared images and eight private
inputs. Independent postflight also confirmed absence of the earlier failed
fixture: aggregate restoration is three containers, three volumes, two images
and thirteen private inputs, with zero retained owned resources. History, hash
manifests, caches and accounting remain; no engine prune occurred.

The shared correction allocation is six active hours and eight logical attempts,
including fifteen protected restoration minutes, under the existing physical
6 GiB ceiling and 64 MiB reserve. Conservative source estimates total 199 minutes;
measured native elapsed including failures/restoration is 14.9768 minutes: total
**213.9768 / 360 minutes**, one external logical attempt out of eight. Automatic
CIS consumption/attempt counters remain zero and are not substituted for those
external costs. Post-restoration counted storage is 5,477,367,377 / 6,442,450,944
bytes, including the fixed 2 GiB Docker allowance and protected reserve; small
closing metadata adds bounded bytes. The prior CIS-0019 history remains intact.

Actual Ubuntu evidence uses the retained ELF runtime with the managed SDK runner,
not a Linux SDK package build. Single-node recovery is not deployment or multi-node
failover proof. The unchanged, unrelated Heartbeat 119/120 Ubuntu cohort and
isolated 1/1 pass remain documented in [CIS-0019](../CIS-0019/verification.md);
Heartbeat was neither changed nor rerun. No all-scope release qualification,
publication, push or merge is claimed by this correction.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2081; failed=286; possibleTokenSavings=591; ledgerDigest=sha256:37aae9b39afdbfa0515714096acc7bd3b759f69177b0476e4c91f1e35cffd8cd |
