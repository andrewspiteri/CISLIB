---
title: RM-01 pre-merge review corrections
type: document
status: draft
---

# Pre-merge review corrections

The user's [review of74fce40b](../../../planning/security/reviews/74fce40b-review.txt)
requests two runtime corrections, an explicit Windows chain contract and focused
native verification. This follow-up leaves completed WORK-001–008 and all their
evidence intact. There is no push, merge, release or external deployment in scope.

## Bounded supporting outcomes

| Owner | Coherent correction and included evidence | Seconds / attempts |
| --- | --- | --- |
| WORK-009 | Recover required outbound Entra availability through the existing Heartbeat and SDK credential, independently of business readiness. Coalesce and drain acquisition, bound probes, distinguish observed expiry from acquisition failure; same-host startup-failure/idle-expiry regressions and guide. | 9,000 / 4 |
| WORK-010 | Observe eligible certificate retirement faults, preserve held-response ownership, publish terminal transport closure in status/readiness; immediate/deferred fault regressions. Document and test Windows intermediate-chain arrangements, preserve no-store-write policy, and execute affected certificate/transport tests on Linux if a bounded native lane can be established. | 9,000 / 5 |

The third delegated redistribution moves unused ordinary capacity into these
supporting tasks, retaining every consumed cost and protected reserve. Current
[work-allocation.json](work-allocation.json) distributes seconds/attempts across
WORK-001–010 as18,000/5;17,400/3;12,600/3;10,200/2;12,000/3;4,800/2;
12,600/4;2,400/1;9,000/4;9,000/5. Totals remain108,000seconds and32attempts,
including3,600restoration seconds. Manual estimates and measured native receipts
remain separate from CIS automatic counters. No cost or history is reset.

Entra and certificate source work may proceed independently; one owner controls
native commands and shared metadata. Runtime Startup remains sequential. Tests,
guide changes, formatting and independent review belong to their correction.

## Platform verification boundary

Existing Ubuntu24.04 WSL is available, but standard dotnet locations have no
runtime. The bounded alternative is one privately located official portable
ASP.NET/Core10.0.12 Linux runtime, SHA-512 checked against Microsoft release
metadata, with the already installed SDK's managed VSTest runner and existing
AnyCPU test outputs. No global installation, new SDK, package pins, replacement
test harness, certificate stores, remote host or additional storage pool is
introduced. Inspect archive sizes before extraction; charge every file and native
command to WORK-010 within the existing physical6GiB ceiling and64MiB reserve.
If that narrow route fails, retain the real limitation rather than expanding into
a tooling campaign or calling Windows dotnet execution a Linux pass.

Windows tests keep the approved W01 new-user-key custody and finite live-import
limits. Deliberate cleanup callback faults use private journals/scopes and retain
the original fault and exact-file absence evidence. They never poison the shared
assembly scope or access existing credentials. No automatic certificate-store
writes are permitted on either lane.

Actual AKS federation (AC-02), cert-manager issuance/mount/renewal (AC-24) and
release qualification remain outside this local correction.

## Second review: joining caller cancellation

The user's [review of 528739d](../../../planning/security/reviews/528739d-review.txt)
identifies a public Entra source-recheck caller that joins existing recovery but
loses its later cancellation. Supporting WORK-011 owns the narrow correction,
held-work regression, original-fault precedence, affected Windows/Linux Entra
tests, guide, formatting and independent review. Public cancellable calls await
the actual shared settlement before checking their own token. They neither
abandon settlement nor cancel the shared SDK operation on behalf of a joiner.
The original shared failure remains primary; calls without cancellable tokens
retain the shared task directly. SDK ownership and runtime Startup are unchanged.

The fourth delegated redistribution transfers one unused ordinary hour and two
unused attempts from completed WORK-009 to WORK-011. WORK-009 retains 5,400 seconds
and 2 attempts, above its recorded actual costs; WORK-011 has 3,600 seconds and 2 attempts.
The recorded allocation input includes the third distribution already applied
in the ledger for WORK-009/010 and this fourth transfer.
Every other allocation, completed contract, historical cost and protected reserve
is preserved. The shared totals remain 30 hours, 32 attempts and the physical 6 GiB
ceiling with 64 MiB reserve. This includes intake, all agents, native work, tests,
documentation and local closeout. The new work includes no push, merge, release
or external deployment.
