---
title: "CIS-0002 verification evidence"
type: verification-record
status: Draft
change_id: CIS-0002
authority: human-reviewed
---

# Verification evidence

The Windows package-consumer checks passed; Ubuntu execution remains pending at
this checkpoint. The existing package SDK/runtime needed no production change.
This is scoped integration evidence, not package publication or release acceptance.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Windows native package path | `test incremental run --profile .cis/local/testing/profiles/type-manager-package-windows.json --layer unit --max-batches 1 --max-seconds 1800 --change CIS-0002 --task WORK-001 --authority . --actor Codex` | 4 passed / 0 failed / 0 skipped | Actual Windows x64; fixture asserts SDK 10.0.401 and .NET/ASP.NET Core 10.0.12. Console and Web each ran build, folder publish and matching no-build publish; other cases cover missing selected assembly and ordinary non-enabled package use. |
| WORK-001 | Current native report | `.cis/local/testing/incremental/type-manager-package-windows/reports/file-13a17f002d65f85b302d-unit.xml` | Accepted nonempty TRX | SHA-256 `be3a41e83ec106b3581abf483e16b9f5a966c57b7495ad7434bca23cff441501`. Native driver 72,825 ms; enclosing settled root 214,924 ms includes preparation/accounting overhead. |
| WORK-001 | Shared development candidate | `.cis/local/testing/type-manager-package-integration/candidate/CIS.Core.0.1.0-dev.1.nupkg` | Built and packed once for the four cases | 237,842 bytes; SHA-256 `de794bafcee21b8a667bb3558e45242fddbab3f8eb7ca3b04974c5037628d93e`. Packaged Core DLL SHA-256 `ffcaf5b367ae901e312dfcc019ca6846cb2f38f31db5cb615d14964f0ba2266a`. Reuse these exact bytes for Ubuntu; do not repack while shipped inputs are unchanged. |
| WORK-001 | Initial selection diagnostic | Old C# source-root selection; compact failure log and retained zero-case report under the Windows profile | No test pass inferred | IntegrationTests built, but the caller-graph predicate excluded process-based package tests. The profile now uses the actual packaged SDK target entry and exact four-case class filter. Initial root cost 155,135 ms is retained in the same logical attempt. No CIS repair or allowance reset was used. |
| WORK-001 | Independent source review | Separate Codex `review_corrections` review of actual fixture/tests/docs | No required finding | Existing sample behavior supplies the assertions; one disposable native fixture is necessary to cross real package/restore/deployment boundaries. No generalized runner, replay/custody framework or expanded product responsibility was added. Runtime outcomes were not inferred from this review. |
| WORK-001 | Ubuntu preparation | Existing pinned installation, inspected through bounded SSH | Ready for actual execution | Ubuntu 24.04 x64; SDK 10.0.401, NET/ASP.NET Core 10.0.12 already installed. No installation/copy was needed. External inspection measured 119 seconds plus local SSH setup; no product command ran during it. |

Native Windows roots are finalized with no recovery hold. After the controller's
first-result checkpoint, task/change native consumption is 370,059 ms, zero time
reserved and one of three logical attempts used. The original empty selection
and the successful run both remain on `WORK-001:1`. External Ubuntu observations
are reported separately under the approved SSH limitation, not invented as native
CIS containment/storage accounting.

Disposable Windows consumers, feed and isolated cache were removed by the shared
fixture. Only the current candidate/identity and compact native report/state and
necessary failure diagnostics remain. Remaining acceptance: execute the same
candidate and four cases on actual Ubuntu, inspect read-only Windows reuse,
record final review/settlement and complete the task through CIS.
