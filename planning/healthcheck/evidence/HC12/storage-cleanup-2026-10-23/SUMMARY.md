# Healthcheck handoff and local artifact cleanup

23 October 2026. The user explicitly requested a summary and deletion of everything **inside `C:\miscwork\portfolio\cislib\.artifacts` only**, because the laptop was running out of space. This supersedes earlier local artifact retention choices. Source changes, Git history, other repositories, and build/cache directories outside that exact folder are outside the deletion scope. No commit, push, release, or remote deletion is authorized by this cleanup.

The final deletion result and disk-space observations are in `cleanup-receipt.json`. The curated handoff contains 56 copied files totaling 4,515,326 bytes, before this summary and manifests. Every retained copy was SHA-256 checked against its original; see `preservation-manifest.json`. This is a small metadata/source handoff, **not a complete execution evidence bundle**.

**Cleanup completed at 09:52 Malta.** The `.artifacts` directory remains present and empty. Observed C: free space increased from 28,954,537,984 to 181,982,679,040 bytes: approximately **153.03 GB reclaimed and 181.98 GB free** (decimal GB; volume observations include concurrent system activity). All 59 protected dirty source/document file hashes were unchanged, and all 56 retained copies passed a second hash check. No remote files were deleted. Native deletion encountered two access errors; PowerShell fallback removed both remaining trees with zero recorded errors. The receipt corrects an initially inaccurate native-junction-probe success flag.

Two deleted scratch worktrees under `.artifacts/corrective/batch-a/clean` and `linux` have stale, prunable Git registrations. The main checkout and Git commits remain intact. These registrations were deliberately not pruned because `.git` is outside the requested deletion scope. The local CIS CLI under `.artifacts/cis/cli` was also deleted; graph refresh is deferred until implementation resumes, rather than rebuilding artifacts during cleanup.

## Current result

- The latest closed complete Healthcheck union remains **1,591 / 3,065 = 51.908646%**, unqualified against the 90% gate. The historical raw conservative result was 2,659 / 3,102 = 85.7189%. No new complete union has been applied.
- Canonical Healthcheck Unit verification passed **1,354 cases**, with zero failures or skips, after the retirement-observer correction and the manual/unactivated-interval case. Full solution formatting passed.
- Developer55 passed all sixteen stages and **10,013 native cases**: 5,262 Unit, 2,322 Integration, 2,400 Architecture, 29 Business. It predates the later retirement-observer assertion correction and the additional manual-interval case. Mutation and compatibility were not executed by that checkpoint.
- All working source changes remain in the checkout, based on HEAD `a884e2edee7008da06121e742f1b468f565e53cb`. They have not been committed by this cleanup.

## Source changes already made

The shipping Runner fix handles concurrent Stop while timer creation throws. Its current source is `implementations/csharp/src/CIS.Core/Healthcheck/HealthCollectorRunner.cs`, SHA-256 `386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8`.

Fixture changes improve actual owner/cleanup observation, release gates during cleanup, bound existing drains, and remove unsupported synchronous binding-disposal assertions. Genuine deadline failures remain failures. The retirement-admission test now awaits the observer's actual Stop task, without requiring synchronous successful completion; its pending-inside-registration assertion remains.

The additional manual-interval case verifies that a positive interval, without `ActivatePeriodic`, cannot retire subsequent manual collection through an unnecessary overflowing future due calculation. Its source SHA-256 is `662e5599ab62b94f5719f1a4502b0b1ba41bc6c7b43e2671df1c2ca7beb9d56a`.

The user approved individually reviewed completed assertions before a later host abort. Maintained accounting currently implements this narrowly for historical exact mutant296. It does not inflate normal-host counts or waive abort, unfinished-work, or release holds. The separate category and receiving tests are already in canonical source; the complete union was not recalculated.

## Finite observations, without automatic union credit

Linux trial65 closed with 16 Killed, four Survived, zero Timeout/NoCoverage, and twenty normal target hosts. Requested definitions 1166/1191/1248/353/371/383/396 have direct assertion witnesses. Surviving siblings 354/372/374/388 remain unresolved. Original/fresh six-case controls passed. The retained completion metadata identifies the remote originals.

Windows trial69 closed with **three Killed, one Survived, one NoCoverage, zero Timeout**. Requested old1511 and old1513 map by exact source definition to native1514 and native1516, both direct assertion kills. Sibling old1512 maps to native1515 Killed; old1514 to native1517 Survived; old1515 to native1518 NoCoverage and no execution chain. Original/fresh two-case controls passed; actual native backups, explicit PDB repair, and corrected/fresh role equality were reviewed. Native exit two is retained in the summary. No independent Windows kernel-exit claim or aggregate gate credit was made. Its selected local metadata survives under `retained/artifacts/hcg/unit-manual-interval-boundary-69/`.

## Ubuntu server and active work

Server: `192.168.88.161`, account `user`. Credentials were supplied in the chat and are deliberately omitted from this handoff. The user selected `/data` for execution. Task root:

`/data/cis-healthcheck-20261021-01a15a72`

Latest remote-only update, **09:54:55 Malta**: Unit55 had started 835/2,745 selectors; Pipes66 had started 149/250. Both outer exits were absent. Unit wire capture was 4,180,862,950 bytes with 169 sealed receipts; server free space was 7,517,691,904 bytes. These are progress observations, not completed or qualified results. Existing eight-hour, size, and reserve guards remain unchanged. Expected outer deadlines are approximately **12:38 Malta for Unit and 12:40 Malta for Pipes**. Do not change a sealed campaign's settings or modify its source.

| Work | Remote location | State at handoff |
| --- | --- | --- |
| Full Unit55 | `unit-full-nomix55` | Running, corrected staging67, 1,353-case original control passed; predates the new case69. 6 GB wire / 17 GB campaign / 2 GB reserve guards. |
| Full Pipes66 | `pipes-full-current66` | Running; 596-case original control passed. 5 GB campaign / 2 GB reserve guards. |
| Full ASP.NET62 | `aspnet-full-current62` | Closed; controller/native exit zero; 82 original and 82 fresh cases passed. |
| ASP compressed original | `aspnet-current62-collection-copy02/aspnet-current62-regular.tar.gz` | 183,445,271 bytes, SHA-256 `967c114b2015cf9b4532bdab968c3193ac2766c85100741a1876c07fed6a587d`. |
| Original wire pilot61 | `unit-wire-pilot61`, `unit-wire-pilot61-import01` | Closed original primary/archive remain remote. |
| Remote monitor | `current-monitor-tool70b/observed01.log` | Existing remote monitoring; no new local downloads. |

ASP.NET62 reports **72 Killed, eight Survived, nineteen Ignored**, raw 72/80 = 90%. Its read-only native audit observes 81/81 normal execution hosts and 72 direct assertion IDs. All 303 non-direct failure issues remain retained. The corrected strict child audit passed with 3,726 intents/launches. The first child audit rejected an output-directory collision; that failure was retained and a read-only audit used a separate path. Role/source restoration was checked, including explicit PDB repair. This partition has not been applied to a new union. Its retained index is `retained/artifacts/hcg/aspnet-full-capture-62/closed-metadata01/index.json`, original SHA-256 `2038100a4816fbb9ad7d21ae33b64f31965237a0a783f8962bf181613a5b9fdd`.

## Execution gaps to fix next

The latest frozen abort snapshot is an observation at **09:01 Malta**, not a final report: 51 Unit aborted hosts and 31 Pipes aborted hosts. Selector aliases are not transferable historical mutation identities. See the retained actual03 index and source-bound diagnosis files.

1. **Unit fixture cleanup:** several zero-result hosts last observed the metric-capacity fixture. Exact instrumented source binds phantom-operation and permanently-starting mutations to a fixture with bounded body waits followed by unbounded implicit registration/Runner/service disposal. Attempt every real drain under the existing bounded watchdog. Keep actual timeouts and unfinished work as failures.
2. **Manual-before-start assertion:** the scheduling fixture awaits `Assert.ThrowsAsync(() => CollectAsync())` without an outer bound. Source-bound current1195/1197 can make the returned task remain pending. Bound the assertion task externally. Putting the watchdog inside `ThrowsAsync` would wrongly convert a deadline into an assertion failure.
3. **Pipes startup/disposal:** source-bound 2784/2787 can leave Start pending; some fixtures await it unbounded. 2791 removes DisposeAsync's BeginStop and exposes unbounded implicit cleanup. Use actual bounded asynchronous waits and attempt real cleanup; do not invent a synchronous-success contract.
4. **Accumulated watchdog costs:** several Unit hosts consume their aggregate host budget through many bounded waits. Pipes2806/2808 show repeated fifteen-second connection cancellations totaling about 300 seconds. This differs from one permanently blocked case. Preserve the distinction and actual failures.

## Capture work preserved for continuation

The closed pilot61 socket probe reconstructed eight real typed VSTest messages (four each direction), with exact paired send/receive bytes and 34 controls. Whole FD-table/IPC acceptance remains incomplete, including an untraced `unshare` concern. It supplies no automatic qualification credit.

The full pilot61 kernel projection reversibly checked 552,913,184 original bytes/841,444 records, with 24 controls and the unchanged maintained kernel reader. Expanded views are disposable and are being deleted locally.

The more suitable **compact71** proposal streams and verifies primary bytes twice and emits only process records with exact single/dual raw origins. Its 25 controls and real closed61 control passed; the latter used about 1.17 MB of derived output. **Actual Unit55 mode has never run and still needs root source review plus closed producer/sink/capture hashes.** It is not a maintained input acceptance change.

Curated sources and the small portable binary survive under `retained/artifacts/hcg/unit55-compact-kernel-proposal-71/`. Required private `ProcessView.cs` and `ProjectionControls.cs` links survive under `retained/artifacts/hcg/vstest-wire-pilot-61/kernel-projection/`. A frozen copy of the canonical kernel reader and pinned `global.json` are also retained. Do not replace current canonical source with the frozen reader. The original relative `.artifacts` paths in receipts describe the pre-cleanup layout; restore only explicitly needed files, or stage the reviewed tool on Ubuntu. Avoid recreating large local source trees or evidence extractions.

## What deletion means

All contents of the local `.artifacts` folder are authorized for removal, including historical raw reports, compressed archives, private build trees, caches, failed attempts and generated binaries. Some historical evidence existed only on this laptop. Those deleted raw executions may no longer be exactly recoverable; do not manufacture their files from metadata or treat this summary as replacement release evidence. Historical path/hash references in earlier documentation are observations of files that once existed.

Current Ubuntu originals remain separate and are not deleted by this operation. For later qualification, inspect the remote physical originals and exact source/configuration/cohort/restoration/child/kernel evidence, or rerun missing gates. Keep aggregate 90% and every unresolved failure hold intact. Prefer remote analysis and compact metadata imports to prevent the laptop filling again.

## Resume safely

- Read this summary and retained manifests before using older `.artifacts` references.
- Check actual remote statuses and guards; do not claim completion from a progress count.
- Fix the concrete receiving/cleanup gaps above in parallel where useful, with no mutation of active snapshots.
- Complete and review the compact projection's explicit origin contract before maintained acceptance.
- Build/check the current checkout as needed. Preserved focused TRXs document earlier execution, not a new run after cleanup.
- Refresh derived graph context before implementation. The prior clean graph build was `4ad11d51090886db3e31fd70e09e21cb3191e7c0eb62a9ca0354406e196128ca`; this handoff is later documentation.
