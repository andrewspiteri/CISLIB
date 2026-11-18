---
title: RestClient bounded implementation plan
type: delivery-plan
status: Approved
change_id: CIS-0010
cis:
  stable_id: cislib:change:cis-0010:implementation-plan
---

# RestClient bounded implementation plan

Prepare one portable REST module with native C# transport/content, real deadline
and resource ownership, explicit optional retry execution and automatic bounded
Metrics. The seven proposed leaves below include their tests, documentation and
review. [Reconciliation P01–P12](../../../planning/rest-client/reconciliation.md)
defines the profile to review with this plan. Source REST-01–REST-10 and A01–A22
remain linked through [the intake](../../../planning/rest-client/README.md) and
[acceptance map](acceptance.md).

Andrew Spiteri approved this plan and its shared allocation on 17 November 2026
and directed implementation. CIS recorded outcome authorization
`cislib:CIS-0010:outcome:23214d429eb5401485eba6e61987f44d`; RC1–RC7 now map to
WORK-001–WORK-007. WORK-001 is InProgress. The preparation history below describes
how the proposal reached that decision; the [verification record](verification.md)
records the actual authority, allocation and implementation checkpoints.

## Tasks and shared allocation proposal

Recommend one allocation of **12 active hours / 16 logical work attempts**, including
**1,200 seconds for restoration**. All agents, native preparation/execution/result
admission, authoring, tests, documentation and review share these totals. The
controller may organise tasks and redistribute unused capacity inside the same
approved total. These approved totals are now allocated through CIS across the
seven owners.

| Label | Owned outcome | Start dependencies | Active seconds | Logical attempts | Included restoration seconds |
| --- | --- | --- | ---: | ---: | ---: |
| RC1 | Portable contracts, guarded results, isolated defaults and shared attempt/body/policy seams | Source/profile decisions | 5,400 | 2 | 120 |
| RC2 | Native HTTP/content, bounded consumption and resource ownership | RC1 | 7,200 | 2 | 180 |
| RC3 | Ordered callbacks/handlers, finalized authentication and response mapping | RC1 | 5,400 | 2 | 120 |
| RC4 | Optional public upstream retry adapter and early delay/replay proof | RC1; optional dependency decision | 7,200 | 3 | 240 |
| RC5 | Logical deadline/dispatch/result owner and actual host shutdown drain | RC2, RC3; RC4 for retry integration | 7,200 | 3 | 240 |
| RC6 | REST metric binding and bounded exact-host outgoing selection | RC1; agreed operation seam | 5,400 | 2 | 120 |
| RC7 | Actual source/host/package consumers and composed contract delivery | RC2, RC3, RC4, RC5, RC6 | 5,400 | 2 | 180 |
| Total | One shared outcome | | 43,200 | 16 | 1,200 |

A logical work attempt is CIS execution accounting for an implementation owner,
potentially containing several governed commands. It is unrelated to the REST
request's AttemptCount or its maximum three HTTP dispatches under the preset.

RC6's collector work can proceed after RC1; its full operation-boundary proof
joins the real RC5 implementation before completion. RC5 can establish Core/None
behavior while the optional adapter is being proved; configured-retry integration
must wait for RC4. No task may claim composed acceptance before those real owners
work together. RC7 is an actual consumer integration outcome, not a final-sweep,
approval or test-harness campaign.

After a real implementation decision, the proposed route is one recorded outcome
authorization for scope and total limits, then delegated task organisation and
resource distribution. The draft seed can become RC1's concrete owner and the
other six can be added through that route. This does not approve the generated
product backlog or pretend that the four proposed impacts were accepted. They
remain visible and any disposition needs its applicable authority; legacy
import/approved-feature derivation cannot rely on the current unapproved backlog.

The inspected scopes are medium-sized targets: RC1 fixes a finite public/seam
surface; RC2 owns one native transport/content family; RC3 owns three ordered
extension responsibilities; RC4 adapts one upstream strategy through a bounded
proof; RC5 owns one fixed execution/lifetime path; RC6 changes one existing
collector plus one fixed module binding; RC7 consumes them through ordinary source
and package use. RC4 has the main uncertainty and a strict stop condition below.
Reassess a materially larger unresolved scope before starting it; do not disguise
a new framework as medium by splitting it into many infrastructure tasks.

## RC1 — contracts and shared seams

Own `contracts/rest-client`, `conformance/rest-client`, Core
`RestClient/Contracts`, related Unit/Architecture cases and the corresponding
contract guide. Freeze the specified verb surface, guarded five-property result,
HasValue/null/empty rules, typed bounded failure/context, headers/query semantics,
immutable effective options and explicit callback/retry disable choices. Settle
the one attempt/body-reader/policy-outcome seam, ownership acquisition, terminal
matrix P04, message-truncation flag and correlation mapping before other owners
edit against them. Do not ship a generic stage graph or native HTTP/SDK types in
ordinary consumer signatures. A01–A03/A05/A22 need literal portable cases plus
public construction/API-boundary tests, without a framework for fixture execution.

## RC2 — native transport and content

Own Core `RestClient/Http` and transport/content tests/docs. Use managed native
pooling, fresh per-attempt messages/content and native serialization. Implement
JSON/text/bytes/no-value, JSON/text/bytes/form/declared stream requests, correct
headers/query/URI resolution, consumed/decompressed response limits and bounded
error capture. One body reader coordinates decoder and mapper; body wrappers take
the agreed observation context without owning the Metrics binding. Preserve
borrowed versus transferred/factory ownership. Validate the supported native
profile, including timeout, redirects/cookies, credentials and meter provenance.
Keep a content-extension seam for native multipart use without creating a
separate portable multipart model.
A01/A06/A07/A14/A16 include real bytes/values, one-byte-over limits, pre-dispatch
rejection and actual cancellation/disposal, plus focused loopback HTTP.

## RC3 — callbacks, final authentication and mapping

Own Core `RestClient/Extensions`, bridge helpers and related tests/docs. Preserve
A/B/C outward and C/B/A inward order, at-most-once next, typed expected failure,
zero-dispatch short circuits and isolated per-call credentials. Finish all ordinary
mutations before final origin/TRACE validation and authentication; signing uses
the exact bounded representation to be sent. Freeze signed fields afterward.
Map HTTP/business failures through the single consumption owner; thrown defects
remain visible after owned cleanup. A03–A05/A08/A13/A16 include concurrent shared
options, async order, stable replay representation, redirects/origin/TRACE and
defect/cancellation cases. Do not write the outer orchestrator concurrently.

## RC4 — optional retry execution

Own `CIS.RestClient.Resilience`, retry tests and policy/dependency documentation.
Propose exact Polly.Core 8.9.1, its one exact NuGet mapping, central pin and normal
net10 lock; Core stays unchanged. The coordinator alone edits shared package and
project files. The first bounded part proves the public-API delay composition in
[the dependency proposal](../../../planning/rest-client/dependency-and-retry.md).
Use at most one logical attempt and 1,800 active seconds of RC4's own allocation
for this early gate. It is part of the real adapter implementation and tests,
not a separate prototype/tooling task or additional pool.

Keep upstream retry iteration, jitter and waiting; select D=max(local,server)
once, check that same D against the remaining entry budget, dispose before wait,
honor long Retry-After and settle cancellation. Establish inherit/None/whole
replacement, finite registered policies, fresh requests, replay safety and final
cause/stop reason. A11–A15 include 503/503/200, seconds/date/invalid/long server
delays, one-shot content, stable supported keys and held attempt cleanup.
Custom policies support finite status/error eligibility, backoff
bounds, optional attempt timeout and a cancellation-aware decision callback over
bounded outcome metadata; that callback never schedules or dispatches a retry.
If supported APIs cannot meet the rule inside the early bound, retain the concrete
gap and stop retry-dependent work; no hidden semantic relaxation, copied engine,
standard default strategy stack or extra package is authorised by this plan.

## RC5 — logical execution and owned shutdown

Own Core `RestClient/Runtime`, `RestClient/Hosting`, the outer implementation and
the narrow existing Startup/HB hosting cutoff changes. Capture public-entry budget
and one metric binding, snapshot effective options, invoke one execution-policy
owner, count dispatch immediately before primary send, settle all owned work and
commit one immutable result or propagate caller cancellation/defect. Timeout must
cancel and await the actual operation, including body/callback/cleanup; it never
restarts after an attempt or merely abandons an asynchronous task.

Close REST admission and signal cancellation before existing Startup/HB bridges
wait for HB/Configuration; join real drains before dependency teardown. Preserve
StartupCoordinator independence and sequential startup. Generic-host, standalone
and borrowed ownership need explicit behavior, not a new lifecycle registry.
A07–A10/A15/A21 include a held cooperative cleanup, caller/total/stopping/attempt
matrix, another unaffected call and HB awaiting REST during canceled-budget stop.
Every test releases and joins its barriers in finally. Keep optional-policy and
metric integration through the RC1 seam, without editing their owned algorithms.

## RC6 — logical and native Metrics

Own Core `RestClient/Metrics` and the existing optional collector's Options,
Settings, Context, StreamCatalog and registration selector, plus matching
contracts/conformance/docs/tests. Follow the [Metrics/hosting proposal](../../../planning/rest-client/metrics-and-hosting.md).
One host-owned four-handle bundle covers logical duration/active work and actual
payload bytes. Preserve one entry binding across retry/cleanup. The native
System.Net.Http meter must be the exact root-factory object; retain only the two
specified streams/method key on pinned .NET 10. No second provider or wildcard
selection. Conditional reservation preserves old Metrics-only small profiles.

Those four handles are reusable recording interfaces for four REST instruments,
shared across registered clients. The catalog's 23 Core streams includes those
four plus the existing nineteen; four separate native streams cover inbound and
outbound HTTP. Handles are not a new stream per client or per call.

The full default REST profile is 23 Core +4 native streams, configured capacity
4,416, within existing 96-definition / 128-stream / 16,384-capacity / four-MiB limits. A17–A19 require real values,
partial/retried bytes, overflow/disabled invariance, real export identity, two-host
isolation, foreign/global meter rejection, both registration orders and a held
body after headers. No new broad memory/benchmark campaign or placeholder transfer
instrument is included.

## RC7 — real consumers and delivery

Own `examples/rest-client`, actual source/package Business/Integration cases and
consumer/agent routing. Demonstrate typed JSON POST, explicit retried GET, async
auth, HTTP200 business mapping, empty success and real cancellation through the
implemented API. Run a genuine Core-only package consumer and a genuine optional
retry+Metrics consumer through ordinary build/folder publish on the pinned
runtime. Use existing Type Manager SDK/packaging and test helpers; introduce no
new build or test platform. Actual test/project-reference success is not package
consumption or another platform's result.

Complete A20–A22 and remaining composed acceptance, affected Architecture,
Compatibility, Unit/Integration/Business and applicable narrow Mutation checks.
The existing 95/90 coverage and 90% conservative mutation release obligations remain
unchanged; a focused pass is not release qualification. No full release, stress,
memory or mutation campaign is allocated by default. Document actual results,
platforms, failed observations, costs and limitations; update README, root AGENTS,
the existing Metrics guidance and accepted dependency decision only when the
implemented examples can be referenced. No new installable skill is needed.

## Coordination, resources and checkpoints

One coordinator owns shared contracts until frozen, solution/project references,
central pins/locks, NuGet.config, dependency policy and shared registration.
After RC1, transport/content, extensions, optional retry and collector-only work
can proceed independently by file ownership. Native builds/tests sharing outputs
remain serialized with a source freeze; no agent launches its own restore/build.
Runtime request stages and existing startup order remain sequential; independent
calls may run concurrently. Provide concise checkpoints after base cancellation
and cleanup work, after retry-delay proof, and after actual host/export/package use.

Retain physical storage group `CISLIB-CIS-0001-STORAGE-2026-11-10` and all existing
counted roots under 6 GiB, including 64 MiB restoration space. The intake sample was
5,661,509,307B, leaving 713,832,773 B after that reserve. All new package/cache,
example, test and scratch output shares this headroom. Use a small serial scratch
envelope (at most 256 MiB per operation initially), verify actual admission space,
and stop before the ceiling; this is not a new pool or permission to delete
historical/user data. No extra worktree or historical cleanup is proposed.

Focused validation uses ordinary small DTOs, a two-host/two-concurrent-call
isolation case and controlled streams at the 16 MiB/16 KiB boundaries plus one byte.
Avoid duplicating large files or asserting a whole-process memory bound. Retain
one new shared work ledger with all agents charged to the same approved total;
At initial planning no native REST restore/build/test/pack or experiment had run.
The approved implementation's actual results and limits are now recorded in
[verification](verification.md).
