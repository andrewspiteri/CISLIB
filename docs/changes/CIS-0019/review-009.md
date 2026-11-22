---
title: "RM-04 public consumer, skill and final handoff review"
type: document
status: Draft
---

The existing outside-friend package fixture now compiles the same minimal public
CrashHost source against fresh locally packed Core and optional RabbitMQ packages,
with the retained exact test-only Npgsql 10.0.4 archive. It restores from a private
local feed/cache, checks the exact allowed closure and archive bytes, checks
deployed Core/provider/Npgsql bytes, and runs the actual PostgreSQL/TLS RabbitMQ
query plus durable original/replayed decision. Independent SQL reads verify one
effect and one journal entry. Application data sources outlive joined host/process
drain. No new package, tutorial, generic harness or RestPackage mutation was added.

The actual public packaged RPC case passes 1/1 on Windows. The refreshed passive
Core-only/optional applications and public CommonTesting helper consumer also pass
(two methods total). The first three-method run retained two genuine passes
and one failure before native startup: NuGet metadata content identity was wrongly
compared to raw signed archive SHA512. The correction separately binds assets to
retained metadata, archive bytes to their SHA512 sidecar, exact nuspec identity and
the archive DLL to the currently loaded test provider. The fresh feed/cache/deployed
checks remain strict; independent review found no blocker, and the actual RPC retry
passes. Both the failure and passing receipt remain preserved.

The existing current-Core approved runtime dependency closure check passes 1/1
after a zero-warning/error architecture build. Core retains its Microsoft-only
runtime closure and framework reference. CrashHost graph restore regenerated the
Integration test lock: Npgsql is direct at the same 10.0.4 test pin, and existing
Microsoft transitive IDs resolve to allowed 10.0.0/10.0.3 patch levels rather than
the earlier 10.0.6 test resolutions. Those are the actual tested assets; declared
CPM/SDK/runtime and production Core pins are unchanged. Newline-only lock churn is
excluded from the commit.

Public helpers pass the retained 19/19 units per runtime. They express bounded
publisher/delivery/request facts without claiming SQL, native authentication or
broker durability. Complete guides link the public request/ticket flow, current
attempt recovery, explicit schema-v2 installation, native provider ownership,
hosting/drain and actual fixture evidence. CountryData remains the sole maintained
database-free tutorial.

The messaging skill passes the installed structural validator at its initial
creation and after final routing changes. Its initial truly fresh usage review correctly found unavailable Part B
implementation and stale guidance; the current implementation/native proof and
corrected guidance resolve those findings. Current source-based consumer assessment,
fourteen local route/link checks and an independent final package/process/exporter
review find no required blocker. This is not a claim of a second independent fresh
skill execution. Actual automatic RPC export is established by the B06 native
case, separately from the earlier twelve collection cases.

Final restoration verifies zero Windows native children, zero Linux crash children,
zero broker connections and zero other PostgreSQL client sessions. It removes only
three exact labelled owned containers (including the stopped superseded broker),
two owned volumes, two newly pulled unshared pinned images and nine generated
connection/configuration/TLS/private-inspection inputs. Nothing is retained from
that owned resource set. Hashes and history remain in
`.cis/local/messaging-intake/final-restoration.json`; no engine prune, unknown cache
deletion, credential disclosure or accounting reset occurred.

Actual Ubuntu evidence uses the existing ELF runtime, not a Linux SDK package
build. Single-node recovery is not deployment or multi-node qualification. The
unrelated existing Heartbeat intermittent result is retained. No push, merge,
publication or release is included in this handoff.
