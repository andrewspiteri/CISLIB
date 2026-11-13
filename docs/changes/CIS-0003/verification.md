---
title: "CIS-0003 verification evidence"
type: verification-record
status: Draft
change_id: CIS-0003
authority: human-reviewed
---

# Verification evidence

The two package-consumer cases passed on actual Windows 11 x64 and Ubuntu 24.04
x64, using identical example source and the same retained package. The existing
production runtime and package are unchanged. This is focused example integration
evidence, not package publication or release qualification.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Windows native example | `test incremental run --profile .cis/local/testing/profiles/logging-example-package-windows.json --layer unit --max-batches 1 --max-seconds 1200 --change CIS-0003 --task WORK-001 --authority . --actor Codex` | 2 passed / 0 failed / 0 skipped | Actual Windows 11 x64; SDK 10.0.401, NET/ASP.NET Core 10.0.12. The exact inspected graph filter selected only ConsoleHumanPackageTests. Normal and folder-published output, human/file JSONL context, lifecycle/actual cleanup and startup-failure exit are covered. |
| WORK-001 | Retained Windows result | `.cis/local/testing/incremental/logging-example-package-windows/reports/file-a611b2eafa1ee6b730dd-unit.xml` | Accepted nonempty TRX | SHA-256 `fa393a8be5ab2273b3ec534cfd920365ee73dedf03725e14e3ba76834cd84871`. Native driver 62,065 ms; inclusive settled root 179,935 ms. These durations overlap and are not added. |
| WORK-001 | Actual Ubuntu execution | Ordinary filtered `dotnet test` of IntegrationTests from `4055720edfbe091e3227629118769ef020d5866c`; `.cis/local/testing/logging-example-package-integration/linux/logging-example-package-linux.trx` | 2 passed / 0 failed / 0 skipped | SDK 10.0.401 and NET/ASP.NET Core 10.0.12. Locked restore 7 seconds; connected native build/test 75 seconds, including approximately 12 seconds of test bodies. TRX SHA-256 `5e68a211730d704b8912fb9a275f72622acabaeed798f9cd31d4f405b3ae5b5e`. |
| WORK-001 | Shared candidate | Existing `CIS.Core.0.1.0-dev.1.nupkg`, 237,842 bytes | Reused without repacking | SHA-256 `de794bafcee21b8a667bb3558e45242fddbab3f8eb7ca3b04974c5037628d93e`; packaged/deployed Core DLL `ffcaf5b367ae901e312dfcc019ca6846cb2f38f31db5cb615d14964f0ba2266a`. Shipped inputs remain unchanged. |
| WORK-001 | Actual example source | Source copied into the ordinary exact-version PackageReference consumer | Compiled and executed on both platforms | Program SHA-256 `4c55f8c26275d9ac3d18da587d67c221d9d1b7ac5399e481b0435eeda688f10a`, independently present in both native reports. No probe application or source Core reference substitutes for this example. |
| WORK-001 | Independent source and closing review | Separate Codex `healthcheck_boundary_correction` inspection of actual example, tests, contracts, README, both raw native reports and closure records | No remaining required finding | One required correction separated bounded disposal from the actual Completion wait so a disposal failure cannot skip cleanup observation; it was fixed before native execution and confirmed. Existing runtime/legacy probe tests remain unchanged. One shared disposable fixture and two focused cases were assessed as proportionate; closing counts, hashes and accounting were independently checked. |
| WORK-001 | Ubuntu preparation and cleanup | Bounded external SSH; compact `linux-outcome.json` and `execution-summary.log` | Collected, verified and cleaned | Read-only preflight 62 seconds; complete execution lane 488 seconds including collection, confirmation and cleanup, below its 1,200-second bound. Peak sampled regular files 229,048,702 bytes, below 2 GiB. After local hash/count confirmation, exact non-symlink task path and zero active owned processes were verified, only `/data/user/cislib-logging-20261113` was removed, and its absence confirmed. Toolchain retained, SSH closed, final free space 1,943,887,872 bytes. |

The Windows root and child are finalized with no recovery hold, zero reserved
time and one of three logical attempts used. Native consumption is 179,935 ms.
The controller recorded the first result before expanding to Ubuntu, preserving
`WORK-001:1` and all actual cost. Ubuntu observations remain separate under the
authorized external SSH limitation; no cross-host CIS accounting/storage
enforcement or ledger copy is claimed. These observed operations fit within the
shared 3,600-second allowance without increasing any limit. Preparation and
overlapping command/body durations are distinguished from native consumption.
The controller closed the logical attempt through `agent work progress` at
ledger revision `:127`; no attempt remains open and no allowance was increased.

The fixture disposed its Windows consumer, feed, cache and outputs. The remote
source/build/cache directory was removed after collection. Retained artifacts
are normal current state/report, the already retained candidate and compact
platform outcomes. Windows OS inspection confirmed Windows 11 Home 10.0.26300,
64-bit; the remote lane independently observed Ubuntu 24.04 x64.

This no-UI task reuses accepted no-UI direction. As with CIS-0002, dossier-wide
`verify validate` also checks an inactive generated visual-design template because
this ad-hoc plan has no feature frontend metadata. The final command returned
`invalid` solely with `CIS-VERIFY-DESIGN` for that template. It is not an
applicable acceptance gate here. No visual approval, feature provenance,
dossier-level verification acceptance, change closure or release acceptance is
invented or claimed.

| Task ID | Completion check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Current result reuse | Scoped read-only unit plan/status/verification after source commit and metadata updates | Verified; zero pending batches | No native replay. Unit fingerprint `c36919f1a7fe4f3e95deb0855d7e8cfa230855826391486b238d1e3dee8dddf4`; profile SHA-256 `484e838b2758d3bd51b8f4a61d0b2df4af3018d1b3a1d65f2b16bac7e033f942`. Packed root README remains unchanged. |
| WORK-001 | Both completed owners | Read-only verification and ten state/report/profile/plan/task hashes for CIS-0001/CIS-0002 | Unchanged and verified | No accepted test was rerun. Both tasks remain Complete with original costs and used attempts. New allocation/execution necessarily changes the global ledger, not those original owners' accounting. |
| WORK-001 | Documentation and plan | `docs validate --strict`, `plan validate CIS-0003`, `git diff --check` | Passed | Approved scope and accepted impact remain covered with zero errors/warnings. Broader release obligations remain separate. |
| WORK-001 | Task closure | `plan task transition CIS-0003 WORK-001 --status Complete --actor Codex` | Complete | Supported transition succeeded after resolved checklists/evidence and accounting closure. The one-task plan queues no successor. |
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=413; failed=53; possibleTokenSavings=123; ledgerDigest=sha256:3fae0df95113845c966db20b3de7deab4e1ba49ced706686bb5700156ce3cc4c |
