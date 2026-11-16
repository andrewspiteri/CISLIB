---
title: CIS-0004 verification
type: verification-record
status: Draft
change_id: CIS-0004
---

# Verification

This closes the implementation/developer-package integration milestone against
proposal AC-1–AC-5 and accepted `CIS-BR-003`–`CIS-BR-007`. Release qualification
remains separate. The one user instruction supplies the outcome authority;
controller task/allocation records claim no additional human approval.

## Three-feature outcome

| Feature | What a developer can do | Applicable evidence |
| --- | --- | --- |
| Logging | Use the ConsoleHuman package example for contextual output and application-owned flush/shutdown. Defaults and ownership are unchanged. | Reuse [CIS-0003](../CIS-0003/verification.md): 2 passed / 0 failed / 0 skipped on each actual Windows/Ubuntu lane. Logging example, fixture and assertions remain unchanged; no replay. |
| Type Manager | Use the Console/Web package examples with automatic inventory, preparation before container construction, fixed cached membership and the accepted missing-assembly failure. | Reuse [CIS-0002](../CIS-0002/verification.md): 4 passed / 0 failed / 0 skipped on each actual Windows/Ubuntu lane. Shipped inputs and accepted defaults remain unchanged; no replay. |
| Healthcheck | Restore, build and folder-publish the actual CountryList source against the exact Core package; inspect HTTP reporting and use the matching existing same-identity pipe probe. | New native results: **6 package cases plus 4 affected source stories passed on each actual platform**, with zero failures/skips in the final runs. |

The maintained [CountryList guide](../../modules/healthcheck/country-list.md)
documents source and isolated package routes, explicit settings, fresh cache/feed,
startup failures and shutdown ownership. Existing Logging and Type Manager guides
remain applicable. No new probe distribution, scheduler or startup framework was
created. Execution-fact binding is application-owned; it is not integration with
an absent Heartbeat implementation.

## Exact candidate and native checks

The retained `CIS.Core.0.1.0-dev.1.nupkg` was reused on both platforms:

- Package SHA-256: `de794bafcee21b8a667bb3558e45242fddbab3f8eb7ca3b04974c5037628d93e`.
- Packaged Core SHA-256: `ffcaf5b367ae901e312dfcc019ca6846cb2f38f31db5cb615d14964f0ba2266a`.
- Shipped Core, build-helper machinery and shipping configuration remain unchanged
  from candidate source `599b5c5e`; no new pack or publication was needed.
- Actual Windows 11 x64 and Ubuntu 24.04.5 x64 used SDK **10.0.401** and
  .NET/ASP.NET Core **10.0.12**, framework-dependent and untrimmed build/folder
  publish. Ubuntu's exact SDK was restored from Microsoft's checksum-verified
  archive; its prior default installation was not used for these checks.

| Native profile | Actual result | Retained TRX SHA-256 |
| --- | --- | --- |
| `healthcheck-package-windows` | 6 / 0 / 0 | `df7cf12e66f43b430c1e43ae3301772fcfc1887c1086845ae0511f3c62ebbfc8` |
| `healthcheck-source-windows` | 4 / 0 / 0 | `56ebf1fb4ac75090d9ce3ad15cf3fc0a6324d2856ba7c8f133b9f637f5d3cb87` |
| `healthcheck-package-linux` | 6 / 0 / 0 | `660af2e2ad9f42eb25ce6f4be1efe3c69358d8caadbaea50a7b128ac94cefab7` |
| `healthcheck-source-linux` | 4 / 0 / 0 | `6bde7c525caf4880038c2e3cd528b12ba4930c0816458eb539343f399ebad92a` |

The six package rows run both build and published application output. Four reuse
the existing successful startup, failed prime, failed refresh registration and
failed later-startup assertions. They inspect actual HTTP snapshots/business
responses, pending startup, readiness, retained usable data, critical
failure/recovery and shutdown waiting for real acquired work. Two run normal
healthy/unhealthy hosts with the matching probe, checking snapshot fields and
health/readiness exit `0`/`1`, plus missing-endpoint transport exit `2`.
Normal probe-host cleanup kills/reaps its owned child; graceful owned-work drain
is demonstrated by the finite startup stories.

The original source assertions are shared, rather than copied into another
behavior suite. Package tests independently hash the reported loaded Core against
the package payload and check restored/deployed dependency closure. The diagnostic
source/package label alone is not accepted as provenance. Production Core and its
public contracts/defaults/dependencies are unchanged.

Each CIS-admitted invocation first required a nonempty native listing of exactly
6 or 4 matching rows, then ran ordinary MSTest/VSTest with
`PerformanceObservation` and `ExtendedStress` excluded. Shared outputs were
coordinated. The affected Windows source project ran from a hash-checked isolated
copy under existing owned `.cis/local`, preserving the shared storage policy.
Ubuntu execution was supervised through bounded SSH inside the same Windows CIS
owner; no authoritative ledger/allocation was copied to the server. Remote
containment/storage is externally checked, not enforced by CIS.

## Saved results and currentness

The requested historical diagnosis stopped after about 5½ minutes. The three
earlier reports, candidate and shipped inputs remain intact. Their saved states
retain combined fingerprints, without the original per-input/ambient-environment
inventory; the pre-existing installation-time difference cannot be identified or
safely restored from that evidence. The new shared assertion link also genuinely
changes the integration-test project input, independently of that earlier hold.
No old fingerprint, report, profile, completed task or accounting was rewritten.

Before final closeout, the new Linux package/source receipts verified current
with zero pending batches in their recorded SSH invocation environment. The
Windows source plan also reported zero pending after its temporary source/build
copy was removed. The Windows package report was a genuine settled six-case pass,
but a later aggregate fingerprint differed (`b2a99b6d…` saved, `8c98f268…`
observed). Bounded inspection found no specific input that could honestly be
restored.

After ordinary closeout metadata and commit `601b3d21`, read-only checks also held
Windows source and Linux package cache reuse. Linux source's last verified receipt
predates that commit; the fail-fast post-commit check stopped at Linux package,
so no newer source-currentness claim is made. The precise individual changed basis
remains unidentified. Treat these as retained scoped native passes, not a promise
of current cache reuse. All report bytes and settlement remain intact. No force,
fingerprint edit, revalidation or demonstration rerun was used. This explicit
tooling limitation does not become another recovery project under this outcome.

## Corrections, review and retained evidence

The first six package rows failed during fixture preparation because .NET 10
pruned framework-provided packages from the deliberate three-package closure.
The isolated consumer now explicitly disables pruning, preserving the strict
closure/hash checks. Explicit build-server controls accompany the corrected
passing run. A separate Windows source-preparation invocation lacked the
PowerShell hashing cmdlet; built-in .NET SHA-256 replaced it before any source
test ran. Failed preparation and consumed effort remain in the same logical
attempt and ledger; no product defect or fresh allowance was inferred.

One focused independent source review checked the actual consumer route,
preserved assertions/state, meaningful failure and legitimate continuation.
Required review findings are resolved. This was source/assertion review; actual
native outcomes are established by the reports above. Source review and final
Git inspection cover the full change from `d84e4638`, including preparation
already present when CIS recorded the dossier's creation inputs.

Retain local profile/state/report records under `.cis/local/testing/`, the exact
candidate, source-input identity and compact
`three-feature-integration/platform-summary.json`. Temporary consumers/caches and
the Windows source copy were removed. Ubuntu task output fell from 872,704,022
to 640,148,071 bytes, below 2 GiB; only the reusable selected toolchain remains.
No owned remote native process remained. Other server contents were preserved.

## Accounting and completion boundary

The sole `CIS-0004/WORK-001` owns this work under one 7,200-second / three-attempt
maximum, including 180 seconds protected restoration. It uses the unchanged
6,442,450,944-byte shared ceiling and 67,108,864-byte reserve. Native work consumed
**1,216,113 ms**, with zero active reservations/recovery holds and one logical
attempt. Controller preparation/review and bounded external tool preparation are
reported honestly; they are not claimed as separately metered native intervals.
The Ubuntu product checks are included in that native total. Separate external
tool/source preparation was observed over about 8¼ minutes, overlapping other
work; its wall-clock window is not represented as a second native execution or
as zero-cost preparation.

Completed owners retain exactly 433,787 ms / 2 attempts (CIS-0001), 602,214 ms /
1 attempt (CIS-0002), and 179,935 ms / 1 attempt (CIS-0003). Their original limits,
contracts, approvals, reports and completion remain intact. The adoption's two
diagnostic cases alone are not full Healthcheck acceptance.

Full native release qualification, 95% line / 90% conservative branch coverage,
90% conservative mutation, compatibility, performance and supported-platform
obligations remain open. No coverage/mutation/stress campaign, main merge,
package publication or new deployment profile was performed. Formal human release
acceptance is not inferred from this checked integration milestone.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=526; failed=79; possibleTokenSavings=123; ledgerDigest=sha256:49899702f6aa6b686148b7e58b6407f3395b0e82acb29021cf76d0fd5b233109 |
