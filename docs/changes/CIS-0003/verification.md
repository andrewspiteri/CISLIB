---
title: "CIS-0003 verification evidence"
type: verification-record
status: Draft
change_id: CIS-0003
authority: human-reviewed
---

# Verification evidence

The actual Windows package-consumer checks passed. Ubuntu preflight passed;
its execution remains pending at this checkpoint. The existing production
runtime and package are unchanged. This is focused example integration evidence,
not package publication or release qualification.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Windows native example | `test incremental run --profile .cis/local/testing/profiles/logging-example-package-windows.json --layer unit --max-batches 1 --max-seconds 1200 --change CIS-0003 --task WORK-001 --authority . --actor Codex` | 2 passed / 0 failed / 0 skipped | Actual Windows 11 x64; SDK 10.0.401, NET/ASP.NET Core 10.0.12. The exact inspected graph filter selected only ConsoleHumanPackageTests. Normal and folder-published output, human/file JSONL context, lifecycle/actual cleanup and startup-failure exit are covered. |
| WORK-001 | Retained Windows result | `.cis/local/testing/incremental/logging-example-package-windows/reports/file-a611b2eafa1ee6b730dd-unit.xml` | Accepted nonempty TRX | SHA-256 `fa393a8be5ab2273b3ec534cfd920365ee73dedf03725e14e3ba76834cd84871`. Native driver 62,065 ms; inclusive settled root 179,935 ms. These durations overlap and are not added. |
| WORK-001 | Shared candidate | Existing `CIS.Core.0.1.0-dev.1.nupkg`, 237,842 bytes | Reused without repacking | SHA-256 `de794bafcee21b8a667bb3558e45242fddbab3f8eb7ca3b04974c5037628d93e`; packaged/deployed Core DLL `ffcaf5b367ae901e312dfcc019ca6846cb2f38f31db5cb615d14964f0ba2266a`. Shipped inputs remain unchanged. |
| WORK-001 | Actual example source | Source copied into the ordinary exact-version PackageReference consumer | Compiled and executed | Program SHA-256 `4c55f8c26275d9ac3d18da587d67c221d9d1b7ac5399e481b0435eeda688f10a`, also recorded in native test output. No probe application or source Core reference substitutes for this example. |
| WORK-001 | Independent source review | Separate Codex `healthcheck_boundary_correction` inspection of actual example, tests, contracts and README | No remaining required finding | One required correction separated bounded disposal from the actual Completion wait so a disposal failure cannot skip cleanup observation; it was fixed before native execution and confirmed. Existing runtime/legacy probe tests remain unchanged. One shared disposable fixture and two focused cases were assessed as proportionate. |
| WORK-001 | Ubuntu preparation | Read-only bounded SSH, existing pinned toolchain and fresh prospective task path | Ready for actual execution | Actual Ubuntu 24.04 x64, SDK 10.0.401 and NET/ASP.NET Core 10.0.12; `/data/user/cislib-logging-20261113` absent with no symlink components, 1,943,887,872 bytes available. Preflight wall time 62 seconds; no build/test/source transfer occurred during it. |

The Windows root and child are finalized with no recovery hold, zero reserved
time and one of three logical attempts used. Native consumption is 179,935 ms.
The controller recorded the first result and next bounded action through
`agent work progress`, keeping `WORK-001:1` open for the same approved outcome.
Ubuntu observations remain separate under the authorized external SSH limitation;
no cross-host CIS accounting/storage enforcement or ledger copy is claimed.

The fixture disposed its Windows consumer, feed, cache and outputs. Retain only
normal current state/report, the already retained candidate and compact platform
outcomes. Remaining acceptance: execute the unchanged source/candidate on Ubuntu,
collect/clean, inspect read-only currentness after completion-record updates,
settle/close the attempt and transition the task to Complete.
