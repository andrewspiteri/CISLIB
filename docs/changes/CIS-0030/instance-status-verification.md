# Instance status verification

The approved simplification is implemented in both the standalone page and the
Configuration version page. Configuration adoption is displayed as **Instance
status**, with **Instance**, **Configuration version in use**, and **Status**.
Existing `/adoption` URLs remain compatible. Only registered instances appear in
the table; unregistered assignment targets remain in closed Technical details.
Actual registrations without reports remain visible. Counts are page-local and
the native continuation cursor is unchanged.

The version is the reported applied release, optionally labelled by an exact,
independently authorized immutable saved note/date. Desired and historical
successful versions never replace a missing current applied version. Unknown,
stale and missing reports remain neutral. Recognized fresh observations support
Up to date, Needs restart and Failed. The existing panel keeps exact diagnostics,
history, resource identities and Copy in a closed section.

## Current checks

| Check | Observed result | Retained evidence |
| --- | --- | --- |
| Pure presentation and adoption cases | 16 passed, zero failed/skipped/cancelled; final model guard logic unchanged, subsequent model edit changed explanatory strings only | `WORK-168/rm09c-instance-status-model-tests` |
| Final portal production build | Next16.4.6 production build, TypeScript and route generation passed | `WORK-168/rm09c-instance-status-registered-final-build` |
| Changed C# fixture format and build | Scoped format and verify passed; Release build passed with zero warnings/errors; C# unchanged by final registered-row correction | `rm09c-instance-status-fixture-format`, `rm09c-instance-status-fixture-format-verify`, `rm09c-instance-status-fixture-build` |
| Current native browser/provider journey | One passed, zero failed/skipped; full test duration42s | `WORK-168/rm09c-instance-status-registered-native/results/instance-status.trx` |
| Existing TRX guard | Total1, passed1, failed0, skipped0; report elapsed43.849s | `WORK-168/rm09c-instance-status-trx-guard` |
| Independent final source/oracle review | No required finding; product and fixture identities bound to the frozen source manifest | `.cis/local/testing/rm09-completion/instance-status-final-source-review.md` |
| Independent current native observation | Both real screenshots, source identities, parent no-effect checks and joined resources reviewed without a required finding | `.cis/local/testing/rm09-completion/instance-status-native-observation-review.md` |

Native phase paths above are under
`.artifacts/management-portal/CIS-0030/`. The real browser evidence is under
`.artifacts/management-portal/CIS-0029/WORK-005/browser-9cf200bd-7bbc-4189-88f4-b3fd9f85b9ef/`.
Root inspected `instance-status-read-only-observations-native.png`: four actual
registrations, three columns, readable saved metadata, Pending/Failed/Unknown
observations and closed unregistered-target diagnostics. There is no synthetic
instance row for a target without a registration.

The named native test is
`ActualInstanceStatusRemainsReadOnlyForHistoricalReportsAndAnAdoptionOnlyOperator`.
It uses the existing actual Management/Auth.js/PostgreSQL fixture. It checks the
historical applied version differs from desired and does not become Up to date;
Faulted without an applied version remains Not reported; AdoptionRead-only
operators retain rows when version metadata is refused; diagnostics start closed;
Copy preserves the exact registration without touching the OS clipboard; and
Escape/Close restore focus. The browser sends zero mutations and the complete
native assignment map is unchanged. The parent compares complete bounded
Configuration publication/release/assignment/override rows and Configuration
Operations/Audit originals before and after. The metadata prelude remains a
separate verified operation. Normal host exit and owned cleanup are required by
the passing parent test. The retained aggregate ChildProcess diagnostic
`hostClosed:false` is not rewritten: exit0, process/stream/IPC joins and socket
drain were independently observed. This is no broader reliability guarantee.

## Preserved history and limits

The first new native attempt failed on an incorrect seed oracle, with normal
host exit and joined cleanup. The corrected oracle respects actual native
Faulted ordering and freshness captured in the page's own response; see
`.cis/local/testing/rm09-completion/instance-status-native-oracle-correction.md`.
The corrected run passed, then the screenshot identified target-only rows mixed
with instances. That small presentation correction was independently reviewed,
rebuilt and passed the current native test with explicit row-count assertions.
All attempts, reports and costs remain retained.

The earlier whole rollout/adoption attempts and intermittent Windows Node exits
remain failed historical evidence. This focused read-only pass does not rerun or
qualify that older whole authoring journey, external deployment, release,
Scheduler runtime or all original WORK-168 acceptance. WORK-168 remains active
with its original unresolved acceptance explicitly retained.

This continuation uses the existing WORK-150 pool:17 active hours/16 logical
attempts, source estimate13.75h including all agents, documentation and review,
plus additive native/metadata costs. Shared120h/128 attempts, restoration3h/3
attempts, physical6GiB/64MiB reserve and contained Docker2GiB remain unchanged.
Only newly generated, settled webpack packs and four successful invocations'
Node compile caches were disposed after exact-path, drainage and hash checks;
manifests are retained. The latter recovered13,586,702bytes after the initial
review launch was refused before native execution,3,171,463bytes short of its
reserved-growth admission. Source, server build, results, reports and recovery
inputs remain. No push, merge or release was performed.

## Local review

The user explicitly approved fresh test data. The old61071 fixture stopped
normally with joined cleanup before shared-output builds. A fresh finite review
instance was observed ready at2026-11-26T22:48:43.2616664Z, public `/healthz`
HTTP200, with its owned visible browser opened and real-window `viewport:null`.
Entry: https://127.0.0.1:55170/applications. Its25-minute lease expires at
2026-11-26T23:12:35.811Z, **27 November00:12:35 Malta**. It uses fresh synthetic
data; no previous saved data or browser draft restoration is claimed.

Control directory:
`.artifacts/management-portal/CIS-0029/WORK-006/rm09c-instance-status-ready-20261126-234618/`.
The legacy folder name does not change its actual WORK-190 owner. The finite
2,100,000ms envelope and600,000ms closure margin fit its remaining3,030,531ms
ordinary allocation at admission, with logical attempt4/6. Readiness and public
health are separate from the lease's later actual cleanup, continuity probe and
final native result/cost. WORK-190 remains active until those settle. The refused
earlier launch and its metadata costs remain retained, without a fabricated test
execution or active lease.

Product/test sources still match the final passing14-file v2 candidate; the v3
manifest records the subsequent guide-only verification update, SHA-256
`91a05ede60143041693a5bf11e08c6793a508c3ccd585b72ba64ef4fb4591ae1`.
The protected CountryData launch settings retain SHA-256
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.

That55170 review subsequently stopped normally with independently joined fixture
and assembly cleanup after the user's fresh readiness for a first-version bug
correction. Its URL is closed. The current candidate, replacement review and
separate native shutdown failure limits are recorded in
[the first-version correction](first-version-correction.md). The Instance status
passing checkpoint above remains its own historical evidence.
