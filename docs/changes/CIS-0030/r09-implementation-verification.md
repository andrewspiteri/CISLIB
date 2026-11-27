# R09 implementation verification

The approved Configuration page presents immutable saved versions, exact JSON,
one deployed marker, direct Save and one Deploy confirmation. Deploying an older
saved version is rollback. The user's subsequent clarification fixes the bottom
comparison to the current uniform deployed version; there is no second picker.
See the [recorded design decision](r09-user-approval.json) and
[consumer guide](../../modules/management/portal-configuration-versions.md).

WORK-190 owns implementation, tests, documentation and review. WORK-191 supports
the deployed-baseline clarification without rewriting the started contract.
These are focused checks, not whole-portal or release qualification. Native
browser acceptance passed after the concrete rollback receipt correction below.

| Retained phase | Actual outcome |
| --- | --- |
| `rm09c-r09-version-model-tests` | 14 passed / 1 failed; the test changed a release reference without changing its matching publication identity. Retained. |
| `rm09c-r09-version-model-tests-current` | 15 passed / 0 failed / 0 skipped after that consistent test correction. |
| `rm09c-r09-focused-pure-current` | 66 passed / 0 failed / 0 skipped across the affected command, exact JSON, recovery, creation, draft, session and protected-state tests. |
| `rm09c-r09-native-fixture-format` and `...-verify` | Scoped ordinary formatting and verify passed for the two changed C# fixture files. |
| `rm09c-r09-native-fixture-build` | Release build passed with zero warnings/errors; pinned SDK/runtime, no restore. |
| `rm09c-r09-portal-build-first` | Webpack compilation passed; TypeScript correctly rejected nullable completion and adoption fields. Retained. |
| `rm09c-r09-portal-build-types-fixed` | Production Next build, TypeScript and route generation passed. |
| `rm09c-r09-versions-native-first` | Failed before browser: existing fixture expected one definition although this scenario deliberately seeds two. The exact expectation was corrected for this scenario. |
| `rm09c-r09-versions-native-seed-count-fixed` | Failed before browser: actual Windows Node host exited with `3221226505` (`0xC0000409`). Native process/streams were joined; this is not a product assertion pass. |
| `rm09c-r09-versions-native-host-crash-retry` | Reached the browser. Save twice without assignment, exact deployed Shared JSON comparison, token preservation, one-confirmation Deploy and refreshed baseline passed. Rollback stopped because the exact completed assignment receipt disabled further Deploy. The failed run and screenshots remain retained. |
| `rm09c-r09-completed-deploy-boundary-tests` | Nine passed / zero failed or skipped: three new completed-deployment boundaries plus six unchanged recovery-fact checks. |
| `rm09c-r09-portal-build-rollback-fixed` | Current production build, TypeScript and route generation passed after the receipt correction. |
| `rm09c-r09-versions-native-rollback-fixed` | One native test passed / zero failed or skipped. Six named real browser cases cover Save, deployed-only diff, Deploy, exact rollback, foreign access denial, and New Configuration plus clean/dirty navigation. Widths 900/1280/1920 are also asserted on the page and long-line editor. |
| `rm09c-r09-versions-trx-report-guard` | Actual TRX verified: total1 / passed1 / failed0 / skipped0, elapsed45.674s. |

The rollback correction acknowledges only an exact known applied, cleanly
settled deployment original, following a new explicit Deploy confirmation and a
fresh native preview. Unknown, incomplete, foreign or conflicting references
continue to block new effects. Reference storage failure blocks dispatch. This
does not replay an original, erase its durable journal/Audit record, or add a
normal recovery wizard. Its focused tests and current rebuilt browser result
passed as recorded above.

The passing C# parent independently queried PostgreSQL for exact UTF-8 payloads
and SHA-256, immutable Shared/Application pairs, generation3 rollback to the
first saved release, no assignment for the new Configuration, exactly nine
distinct finalized applied originals, and exactly one succeeded original Audit
per key with the real synthetic caller. These are actual assertions, not proof
booleans taken alone. The complete test returned after owned native/browser,
host, fixture and assembly cleanup. It does not establish external identity,
deployment or whole-portal runtime reliability.

The current 36-file source snapshot is
`.cis/local/testing/rm09-completion/r09-rollback-fixed-source-manifest.json`,
SHA-256 `be7a93875ad37e557b88d5b84f889ff183f1126e43ef86611afb49d3299938fd`.
The user's launch settings retain SHA-256
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.

Source-only independent reviews are retained at
`.cis/local/testing/rm09-completion/work190-independent-review.md`,
`work190-new-configuration-source.md` and `work190-native-fixture-review.md`.
The completed-deployment correction has its separate review at
`work190-next-deploy-review.md`. Independent inspection of the actual passing
TRX, native report, screenshots and SQL/Audit assertions is retained at
`work190-native-observation.md`, SHA-256
`7bf1338950c312f885fc57c208350ef493d6de6e879ab96dd52f030da828fb53`.
The first-build and later frozen manifests remain separate; native logs,
admissions, TRX and additive costs remain under the corresponding WORK-190
phase directories. Only completed compiler packs were disposed, with exact
hash manifests, to stay inside the unchanged physical ceiling. No source,
server build, report, native result or recovery input was disposed.

The previous manual review instance is closed; its
[lifetime evidence](r09-prior-review-lifetime-closeout.md) is separate. Neither
failed optional export preserved `test.andrew`. No live-instance URL or restored
user data is implied by this record. No push, merge or release was performed.

## Fresh finite local review

The R09 review instance was observed ready at 2026-11-26T21:15:54.6952923+00:00, public /healthz HTTP200, and its owned visible browser opened. Entry: https://127.0.0.1:61739/applications. Lease expiry: 2026-11-26T22:14:35.8090000+00:00 (23:14:35 Malta on 26 November). Its control directory is rm09c-r09-review-20261126-221248 under the existing WORK-006 fixture root; actual execution/accounting owner is CIS-0030/WORK-190. This uses fresh synthetic data. The old test.andrew export produced no backup and was not restored.

Functional R09 acceptance and WORK-191 are complete. WORK-190 stays active for this finite review lease and actual native/fixture/assembly drainage. The current lease's final native result/cost and continuity observation are pending; readiness does not claim later cleanup. The 4200000-ms envelope is retained inside its existing 4h owner, with source costs 2.5h plus actual additive native/metadata costs. No push, merge or release occurred.

The above handoff subsequently closed cleanly after the user confirmed JSON was
saved/copied for replacement. Their three further review findings and the
current corrected candidate are recorded in
[window/editor corrections](r09-window-editor-corrections.md). That record
supersedes the earlier fixed-viewport resize claim and old live URL; it preserves
the earlier successful Save/Deploy evidence and all subsequent failed runs.

The next user-approved presentation change and current finite local handoff are
recorded in [Instance status verification](instance-status-verification.md).
