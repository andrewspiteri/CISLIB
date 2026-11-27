# R09 manual review corrections

The user's 26 November screenshot reports three defects: resizing the actual
browser window leaves a fixed content area, the deployed comparison should be
collapsed by default, and entering Edit replaces the formatted JSON display with
compact text. These are corrections within active WORK-190's existing scope.
They do not add a new screen or change the approved saved-version/deployment
model. WORK-191's deployed-only comparison contract remains unchanged.

The visible review fixture explicitly created an emulated 1600×1000 viewport.
At display scaling this explains the fixed rendered area with blank space to
its right and bottom. Earlier `page.setViewportSize` checks verified CSS inside
an emulated viewport; they did not prove that the actual window could resize
it. Future manual contexts use native window dimensions. The existing owned
browser test will change actual window width and height before its ordinary
Save/Deploy checks. No CSS width workaround is needed for that fixture setting.

The comparison disclosure becomes controlled by its existing open state, so
context/selection/edit/save resets actually close the DOM disclosure. Opening
it remains deliberate; an already-open comparison can still refresh after
Deploy. Existing saved JSON is formatted once when admitted to a fresh editor,
after its exact source/reference/session checks. No formatter runs on each
keystroke or refresh, overwrites a local draft, or changes a new creation's
original input/autosave bytes. Existing token-preserving formatting is reused.

The current fixture was stopped only after the user's explicit reply **JSON
saved/copied — restart when ready**. Its runner exited0 and parent fixture and
assembly cleanup joined. The old URL must not be presented as updated; a new
finite review instance follows the rebuilt candidate and passing focused checks.

Under the existing delegated controller, 1.5 unused hours/one logical attempt
move from future WORK-140 to WORK-190: 140 retains0.5h/1 attempt; 190 now has
5.5h/6 attempts. All agents, intake, implementation, tests, documentation, review
and replacement lease share that owner. All other native allocations, costs,
shared120h/128 attempts, restoration3h/3 attempts, physical6GiB/64MiB reserve and
contained Docker allowance are preserved. The exact allocation input is
`task-inputs-supported/allocation-r09-window-corrections.json`. Historical source
and native results remain historical; verification of this correction is pending.
No dependency, framework, new test harness, Scheduler runtime, push, merge or
release is included.

## Current verification

The frozen five-file correction manifest is
`.cis/local/testing/rm09-completion/r09-window-editor-source-manifest.json`,
SHA-256 `3555a894f477ed46a5bc9b7682414acc98cc0d690b0736a2953b15e26e3aaf08`.
The disclosure peer review and root's independent editor/native source review
are retained separately. This snapshot predates the ordering-only fixture
continuation described below; earlier source hashes are not silently replaced.

| Phase | Actual result |
| --- | --- |
| `rm09c-r09-window-json-pure-tests` | Ten passed, zero failed/skipped; exact-token formatter and New Configuration handoff checks. |
| `rm09c-r09-window-editor-portal-build` | Current production build, TypeScript and route generation passed. |
| `rm09c-r09-native-window-editor-first` | Storage admission refused before any test launched, short by676196 bytes at the retained measurement. |
| `rm09c-r09-native-window-editor-cache-fit` | Test failed before UI verification: Windows Node host exit3221226505. Failure and actual joined resources retained. |
| `rm09c-r09-native-window-editor-host-retry` | Failed again before desktop, in existing `configuration-api-editor-repeat`, with the same host exit. This is not a passing window/format/disclosure result. |

Only regenerable invocation-owned caches were disposed after actual drainage:
the completed build's webpack packs and fourteen exact WORK-190 Node compile
cache folders (29130635 bytes), with per-file hashes and target manifests.
Source, production server build, reports, logs, results and recovery inputs were
retained. No quota or runtime pin/flag changed.

The next bounded continuation runs the same version UI block before the
unrelated legacy Configuration API checks, which target `shared.settings`.
The UI targets `shared.features` and one new definition. Every legacy check,
native security check, final SQL/Audit assertion and cleanup obligation remains;
other scenarios retain their previous ordering. This obtains direct evidence
for the requested UI sooner without treating a later failure as a test pass.
The resulting `rm09c-r09-native-window-editor-ui-first` execution passed one
native test with zero failures/skips, including every later legacy/security
check, exact SQL/Audit assertions and actual cleanup. Its browser record has
seven named cases, with the new actual-window check added to the existing six.

| Actual window | Actual content viewport | Editor height |
| --- | --- | --- |
| 1000×780 | 978×689 | 360 |
| 1440×1080 | 1418×989 | 474.875 |
| 1120×880 | 1098×789 | 360 |

The observed device-pixel ratio was1.5. All three points had document scroll
width equal to client width; rail and header reached the actual content edges.
The probe used no viewport emulation. It also checked that the comparison began
closed, a deliberately opened comparison closed on Edit, saved JSON entered the
editor formatted with exact tokens, and typed long JSON survived resizing.
The deliberately emulated width checks afterward remain a separate assertion.
Root inspected the actual 1000×780 screenshot; no clipping or formatting finding
remained. Earlier fixed-viewport acceptance does not become native-window proof.

The current ordering-only continuation manifest is
`.cis/local/testing/rm09-completion/r09-window-editor-ui-first-source-manifest.json`,
SHA-256 `540f407866f5da39900d94a135310affb6251d245ea82c545c8e64a31a56ec01`.
The production build is unchanged from the passing correction build. No test
or failure was discarded, skipped or converted into a pass. The final TRX guard
and fresh manual handoff are recorded below after observation.

The exact current TRX guard also passed: total1 / passed1 / failed0 / skipped0, full report elapsed52.266s. Independent inspection of the actual dimensions, 1000×780 PNG, five-file candidate hashes, native host joins and SQL/Audit assertions is retained at .cis/local/testing/rm09-completion/work190-window-editor-native-observation.md (SHA-256 9e51f81c52b528e4932bdc2ba07a67ff9465ea9d1c0ccfc23642e0038b37c52e). The prior two Windows Node failures remain failed historical results; the later pass does not resolve their intermittent cause.


## Corrected manual review handoff

Observed ready at 2026-11-26T21:45:53.7077786+00:00, public /healthz HTTP200, owned visible browser opened with viewport:null. Entry: https://127.0.0.1:61071/applications. Expiry: 2026-11-26T22:44:54.7810000+00:00 (2026-11-26 23:44:54 Malta). The control directory is rm09c-r09-window-20261126-224239 under the legacy fixture root; actual owner is CIS-0030/WORK-190. The old61739 instance stopped cleanly after the user's saved/copied confirmation. This replacement uses fresh synthetic data; no saved-data or browser-draft restoration is claimed.

Current functional acceptance is verified. WORK-190 remains active only for this finite user-review lease and actual cleanup; the new lease's final native result/cost and continuity observation remain pending. The existing19800000-ms owner includes its4200000-ms envelope, all source estimates and additive native/metadata costs. No whole-portal reliability qualification, push, merge or release is claimed.

The61071 review subsequently stopped normally with joined cleanup after the
user approved fresh data for the Instance status simplification. Its old URL is
closed. The current candidate and replacement review are recorded in
[Instance status verification](instance-status-verification.md); original
window/editor evidence above remains its historical checkpoint.
