# Verification of the 85711214 session corrections

The [exact supplied review](reviews/85711214.md) remains unchanged. The two
[bounded correction owners](review-85711214-corrections.md) extend approved R09
behavior without new screens, dependencies, runtime pins or a test framework.
All results below are local Windows evidence with private Node 24.16.0,
npm 11.12.1, SDK 10.0.401 and runtime 10.0.12. Actual Ubuntu CI remains
unverified; no new payload publication is included.

## Current checks

Paths are relative to the repository root under
`.artifacts/management-portal/CIS-0030/`.

| Check | Result | Evidence |
| --- | --- | --- |
| Scoped invalidation/coordinator/receipt/protected-owner tests | 48 passed; 0 failed, cancelled or skipped | `WORK-200/rm09c-review-857-session-pure/native.log` |
| Scoped C# formatting, affected Release project build and format verification | Passed; 0 warnings, 0 errors | `WORK-201/rm09c-review-857-csharp/native.log` |
| Production portal build, five actual CI bash blocks and readonly Next startup guard | Passed on the corrected retry | `WORK-201/rm09c-review-857-portal-build-retry/native.log` |
| Old-session notification versus a newer valid browser draft | 1 passed; 0 failed or skipped | `WORK-200/rm09c-review-857-session-native/results/scoped-session.trx` |
| Native Deploy modality, pointer and keyboard Retry, normal Save/Deploy/rollback | 1 passed; 0 failed or skipped | `WORK-201/rm09c-review-857-modal-native/results/modal-session.trx` |
| Complete affected portal pure selection | 215 passed; 0 failed, cancelled or skipped | `WORK-200/rm09c-review-857-final-checks/native.log` |
| Strict guards on both actual native reports, changed native-script syntax, startup guard and exact source/build/runtime/user-file hashes | Passed | `WORK-200/rm09c-review-857-final-checks/native.log` |

The pure checks use the production coordinator, protected owner and receipt
store. Old expiry and changed-session notifications retire matching peers while
preserving a newer valid session's draft and exact original-reference storage.
The closed 140-character notification carries only session/service associations;
legacy, malformed and foreign notifications cannot erase protected state.
Matching signout still aborts old work without certifying callback settlement.

The new native session continuation adds independently OIDC-authenticated old
and newer A sessions to the existing real signout/operator-switch journey. Two
old documents receive the retiring session's notification; a third document's
newer session must retain its draft/note and pass an authoritative current read.
The existing C# parent requires zero Configuration mutations and unchanged
publication/release/map/override/original/Audit fingerprints. Pure receipt
preservation is not presented as a native Operations execution.
The matching passing report is
`.artifacts/management-portal/CIS-0029/WORK-004/native-host-57c779db-2169-4394-9a4f-6fdb89d6684a/report.json`;
its browser proof records all five journeys, retaining the prior signout,
unavailable delivery, held callback and real operator-switch assertions.

The Deploy continuation is inserted before the version journey's actual
deployment. It uses actual headed focus, two held native BFF reads with
controlled unavailable delivery, ordinary pointer Retry and native Tab/Enter
Retry. It requires the dialog to be closed and outside native modality during
checking/unavailability, then to restore its exact immutable version and Unicode
note only for the same verified session. It observes every protected mutation
request, including preview, and permits none during these checks. The existing
normal nine-effect Save/Deploy/rollback/new-Configuration assertions remain.
The matching passing report is
`.artifacts/management-portal/CIS-0029/WORK-004/native-host-fda8d8da-f3a8-4622-b367-d2698a7a4c68/report.json`.
It records two joined unavailable checks, both actual Retry methods, zero
preview/deployment mutations for recovery, all restoration/cancel flags and the
unchanged nine normal effects/final generation three. Actual host stop/exit,
streams, IPC, sockets and owned provider cleanup completed.

## Retained failures and resources

The first production build caught an `exactOptionalPropertyTypes` error: an
optional terminal snapshot field was explicitly assigned undefined. The fix
omits that property when no retiring scope exists, preserving strict compiler
settings and unknown-context semantics. The failed build remains failed; the
successful retry is separate evidence. Conservative source/build correction
attempts are charged within the existing two-per-owner allowance.

Verified settled invocation caches were retired after actual approved Node/.NET
drainage to admit the conservative 128 MiB production-build forecast. Producing
build caches are retired separately with per-file path/size/hash manifests.
The first cleanup's aggregate counter reported zero because it measured ordered
dictionary objects incorrectly; its retained file records correctly total
106,813,483 bytes. The original manifest and separate correction record remain,
and the helper now sums the fields explicitly. The successful build's seven new
cache files total 106,821,362 bytes. Original cache paths, production output,
every failed/passing report, recovery inputs and historical costs are preserved.

All cleanup manifests and the correction record remain under
`.cis/local/testing/rm09-completion/review-857-*` and
`rm09c-review-857-portal-build*-cache-disposal.json`.
The physical 6 GiB ceiling, 64 MiB reserve, contained 2 GiB Docker allowance,
shared 120 hours/128 attempts and restoration 3 hours/3 attempts remain unchanged.
Only unused existing capacity was redistributed; WORK-050's historical overlay
discrepancy was preserved and supplied no headroom. WORK-199 remains
ReadyForReview with Ubuntu execution explicitly pending.

Independent source review found no required behavior/ownership findings in the
scoped invalidation or Deploy correction. The actual pinned compiler exposed
the optional-field issue above; source review and authored tests do not replace
the current native execution and final report guards. Independent review of
both matching actual native proofs found no required findings. Current exact
source, assembly, production BUILD_ID and report hashes are retained in
`.cis/local/testing/rm09-completion/review-857-final-evidence.json`.

Supported CIS transitions now mark WORK-200 and WORK-201 Complete. WORK-199
remains ReadyForReview with actual Ubuntu execution unverified. Earlier owners
and results retain their status; this closes the two review corrections, not
the remaining RM-09/RM-10 work. No new payload was pushed, merged or released.

Final CIS plan validation and diff whitespace checks passed. The sampled
`.cis/local/testing/rm09-completion/review-857-final-state.json` records
6,251,204,414 counted bytes, 124,137,666 bytes headroom after the unchanged reserve,
positive remaining shared/owner time and attempt capacity, unchanged prior task
statuses and qualified source hashes, and zero owned Node/.NET/testhost
processes. This is a final sample, not a continuous peak measurement.

After this local checkpoint, the user authorized pushing and merging these
reviewed corrections on 27 November 2026. The earlier unpublished checkpoint
and its exact evidence remain historical; publication does not establish an
Ubuntu CI result or close the separately deferred work.
