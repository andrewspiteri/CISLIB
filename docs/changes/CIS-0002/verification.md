---
title: "CIS-0002 verification evidence"
type: verification-record
status: Draft
change_id: CIS-0002
authority: human-reviewed
---

# Verification evidence

The four package-consumer cases passed on actual Windows 11 x64 and Ubuntu 24.04
x64 using the same development package. The existing package SDK/runtime needed
no production change. This is scoped integration evidence, not package publication
or release acceptance.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Windows native package path | `test incremental run --profile .cis/local/testing/profiles/type-manager-package-windows.json --layer unit --max-batches 1 --max-seconds 1800 --change CIS-0002 --task WORK-001 --authority . --actor Codex` | 4 passed / 0 failed / 0 skipped | Actual Windows x64; fixture asserts SDK 10.0.401 and .NET/ASP.NET Core 10.0.12. Console and Web each ran build, folder publish and matching no-build publish; other cases cover missing selected assembly and ordinary non-enabled package use. |
| WORK-001 | Current Windows report | `.cis/local/testing/incremental/type-manager-package-windows/reports/file-754f922ff7fc05e162f8-unit.xml` | 4 passed / 0 failed / 0 skipped | SHA-256 `c0f83c100e1312c91e8b29ab664152fdd26e1a4207ed999683b796d4763412a5`; native driver 100,368 ms, enclosing settled root 232,155 ms. Graph selection covers the four package cases. |
| WORK-001 | Actual Ubuntu execution | `.cis/local/testing/type-manager-package-integration/linux/type-manager-package-linux.trx` and `linux-outcome.json` | 4 passed / 0 failed / 0 skipped | Source `599b5c5e2c2c4da615629d3cd5b6165502a02090`, same pinned SDK/shared frameworks and candidate. Locked restore took 10 seconds; connected native build/test took 146 seconds. TRX SHA-256 `08c30e3f29a1ed30daf937fb8c47f409bdddc1c3ceaabef340a7ec6f691a2f61`. |
| WORK-001 | Shared development candidate | `.cis/local/testing/type-manager-package-integration/candidate/CIS.Core.0.1.0-dev.1.nupkg` | Built and packed once; reused on both platforms | 237,842 bytes; SHA-256 `de794bafcee21b8a667bb3558e45242fddbab3f8eb7ca3b04974c5037628d93e`. Packaged Core DLL SHA-256 `ffcaf5b367ae901e312dfcc019ca6846cb2f38f31db5cb615d14964f0ba2266a`. Final Windows validation consumes these retained bytes without repacking. |
| WORK-001 | Selection and currentness correction | Scoped fixture source, explicit shipped configuration/candidate inputs and test class | Initial diagnostics retained; no CIS repair | The first source selection ran zero cases (155,135 ms root; no pass inferred). An SDK-file fallback then passed all four cases (214,924 ms root) but also fingerprinted planning metadata. The final profile uses the actual fixture's C# caller closure. Explicit `System` and `System.Threading.Tasks` imports let raw-source graph analysis resolve the test calls; the SDK already generated these imports, so behavior/assertions and shipped package bytes are unchanged. The Ubuntu result remains applicable. |
| WORK-001 | Independent source review | Separate Codex `review_corrections` review of actual fixture/tests/docs | No required finding | Existing sample behavior supplies the assertions; one disposable native fixture is necessary to cross real package/restore/deployment boundaries. No generalized runner, replay/custody framework or expanded product responsibility was added. Runtime outcomes were not inferred from this review. |
| WORK-001 | Ubuntu bounds and cleanup | Existing pinned toolchain; disposable `/data/user/cislib-tm-20261112` | Completed and removed | Initial toolchain inspection took 119 seconds; the external lane through collection, confirmation wait and cleanup took 826 seconds, including the restore/build/test above. Maximum sampled regular-file size 233,372,732 bytes, below the approved 2 GiB cap. Exact non-symlink job path and stopped processes were checked before removal; reused toolchain remains. |

All three native Windows roots are finalized with no recovery hold: task/change
consumption is 602,214 ms, zero time reserved and one of three logical attempts
used. Diagnostic and successful runs remain on `WORK-001:1`; nothing was reset.
The controller closed that logical attempt through `agent work progress` at
ledger revision `:106`; no attempt remains open and no allowance was increased.
The external Ubuntu durations are separate from native Windows consumption and
remain within the shared 7,200-second ceiling. Remote time/storage was inspected
under the approved SSH limitation, without claiming cross-host CIS containment.

Disposable Windows consumers, feed and isolated cache were removed by the shared
fixture. The Ubuntu source/build/cache copies were also removed after local
collection. Retained artifacts are the candidate/identity, compact native state,
reports, per-platform outcomes and necessary diagnostic references. Completion
checks below record current reuse, final review and supported lifecycle closure.

| Task ID | Completion check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Current reuse after documentation changes | Scoped `test incremental plan`, `status` and `verify --layer unit` | Verified; zero pending unit batches | No native launch. Fingerprint `5cd2827522917acfb48915343d9fdebaa542f1da995e88580ed0a9a208f004d4`; current report bytes unchanged. The packed root README remains unchanged. |
| WORK-001 | Preserved adoption | CIS-0001 state, report, profile, plan and task hashes; read-only unit verification | Unchanged and verified | No Healthcheck tests ran. Its Complete task, accepted report, 433,787 ms consumption and two used attempts remain intact. Adding CIS-0002 necessarily changes the global ledger, not the original change's accounting. |
| WORK-001 | Focused final review | Independent `review_corrections` inspection of source, actual TRXs and final delta | No required finding | Confirmed candidate identity, equivalent explicit imports, valid Ubuntu reuse and proportionate four-case shared fixture. |
| WORK-001 | Documentation and plan | `docs validate --strict`; `plan validate CIS-0002`; `git diff --check` | Passed | Approved plan covers the accepted impact with zero errors or warnings. No broader engineering-quality policy was adopted. |
| WORK-001 | Task closure | `plan task transition CIS-0002 WORK-001 --status Complete --actor Codex` | Complete | Supported lifecycle transition succeeded after evidence and accounting closure. The one-task plan queues no successor. |
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=362; failed=49; possibleTokenSavings=123; ledgerDigest=sha256:2961851e19b156a2b999e4d5f9dbcc8d02c8e74ad4308f94e69e9cdbbd723924 |

Dossier-wide `verify validate` also inspects the generated visual-design template.
It reports that template as unapproved for this ad-hoc plan, which has no feature
frontend metadata. This package/startup task has no visual UI and reuses the
already accepted no-UI direction; no new visual design or human answer is needed.
The inactive template is not an applicable task acceptance gate. No dossier-level
verification acceptance, release acceptance or change closure is claimed here.
