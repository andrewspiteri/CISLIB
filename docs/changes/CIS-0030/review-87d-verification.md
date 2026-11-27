# Verification of the 87d18ba6 review corrections

The [supplied review](reviews/87d18ba6.md) is retained unchanged. This continuation
corrects its four findings within the approved desktop Configuration behavior.
No new screen, package, runtime pin, Scheduler implementation or general test
framework was introduced. Native tests extend the existing ordinary
MSTest/Playwright/PostgreSQL/RabbitMQ fixture; startup and teardown remain owned
and sequential.

## Current results

All paths below are relative to the repository root. These are current Windows
results using the approved private Node 24.16.0 executable, npm 11.12.1, SDK
10.0.401 and runtime 10.0.12. They do not prove an Ubuntu Actions run.

| Check | Actual result | Retained phase under `.artifacts/management-portal/CIS-0030/` |
| --- | --- | --- |
| Full affected portal pure selection | 212 passed; 0 failed, cancelled or skipped | `WORK-198/rm09c-review-87d-final-source-checks/native.log` |
| Production portal build and readonly pinned Next startup guard | Passed on the conservatively admitted repeat | `WORK-199/rm09c-review-87d-portal-build-bounded/native.log` |
| Scoped C# format and Release integration-project build | Passed; build 0 warnings, 0 errors | `WORK-198/rm09c-review-87d-csharp-format-restored/` and `rm09c-review-87d-csharp-build-restored/` |
| Final scoped format verification, five actual CI bash blocks, startup guard and frozen source/runtime/user-file hashes | Passed | `WORK-198/rm09c-review-87d-final-source-checks/native.log` |
| Twenty-row Configuration menus | 1 passed; 0 failed or skipped | `WORK-196/rm09c-review-87d-menu-retry/results/configuration-menu-review-retry.trx` |
| Failed editor-read retry and concurrent source/map Refresh | 1 passed; 0 failed or skipped | `WORK-198/rm09c-review-87d-refresh-native/results/editor-refresh-review.trx` |
| Existing headed Edge native-window resize, Save, Deploy and exact rollback | 1 passed; 0 failed or skipped | `WORK-199/rm09c-review-87d-version-regression/results/version-regression-review.trx` |
| Real two-tab invalidation, actual focus and operator switch | 1 passed; 0 failed or skipped | `WORK-197/rm09c-review-87d-tabs-exact-session-restored/results/session-tabs-exact-session.trx` |
| Existing strict TRX guard on all four native reports | All four accepted; each 1 passed, 0 failed/skipped | `WORK-198/rm09c-review-87d-strict-evidence/native.log` |
| Corrected Ubuntu service CI path | Not executed | Requires publication/run of this new payload; no push, merge or release is included here |

The menu browser proof requires twenty real catalog entries, zero action reads
for closed menus, forty open/switch cycles plus a filtered reopening, exactly
41 scoped GETs, no mutations, no browser errors and matching native fingerprints.
Its passing native host report is
`.artifacts/management-portal/CIS-0029/WORK-004/native-host-908a6e99-859c-4930-b687-01f4a3bf8cd5/report.json`.

The Refresh browser proof holds a real editor response while preserving exact
Unicode/large-integer JSON and its version note. Another independently granted
native operator publishes a source and changes deployment. Save uses the new
source/map ETags while retaining the originally selected Shared reference; it
does not deploy. The C# parent verifies exact publication bytes/digests,
immutable pair, unchanged deployment and four distinct Operations/Audit
originals with their two actual actors. Its passing report is
`.artifacts/management-portal/CIS-0029/WORK-004/native-host-e6dc2dbc-9c34-4681-aef4-a4fe48c435a9/report.json`.

The two-tab proof uses actual headed focus with Playwright's focus emulation
disabled through owned CDP sessions. A valid same-session check hides the inline
draft and portaled Instance details, then restores both. An unavailable delivery
stays masked until explicit Retry. Real sign-out clears the peer draft while an
old response is held; a real B cookie produces an authoritative 409 that clears
A's values. There are zero Configuration mutations, unchanged native
Configuration/original/Audit fingerprints, and actual response/CDP/host cleanup.
The matching passing report is
`.artifacts/management-portal/CIS-0029/WORK-004/native-host-42ef72e6-5556-46ed-83a5-96e87e62f7bb/report.json`;
its four browser cases are in
`.artifacts/management-portal/CIS-0029/WORK-005/browser-c0d9ad7f-c6c8-4a86-bcaa-02e34e36822d/browser-evidence.json`.

## Retained failures and storage

The first menu attempt rendered all twenty rows but its open popup covered the
next mouse target during the direct-switch pass. The corrected pass uses actual
Enter activation; twenty ordinary mouse/Escape cycles and every read/effect
assertion remain. `rm09c-review-87d-menu-native` remains a failed result.

The first two-tab attempt incorrectly expected a Configuration row to open the
former details panel; R09 correctly opens the version page. The test now uses
the actual Instance details panel beside the other tab's Configuration draft.
The second attempt timed out waiting for actual focus activation. Pinned
Playwright enables focus emulation even in a headed browser, so the test needs
owned CDP sessions that disable that emulation and observe real visibility/focus.
Neither `rm09c-review-87d-tabs-native` nor `rm09c-review-87d-tabs-retry` is passing
evidence. Two subsequent failed runs isolated a test cleanup race: interception
was removed before its held response callback finished. The corrected order
releases, joins fulfillment/disposal, then removes interception. Bounded nested
source coordinates established the failure without recording error messages,
URLs, JSON or credentials. All privacy and panel-restoration assertions remained.

The next browser execution passed all four journeys, but its shared later expiry
check still assumed one session per operator and updated two A sessions. The
fixture now scopes that update to main A's already owned opaque ticket as well
as provider/principal. It retains exactly-one-row, actual 401, reauthentication,
stale-context and separate account-wide revocation checks. That full native
continuation then passed. Every earlier failed result remains failed.

The first production build functionally passed but generated 106,710,194 bytes
of new webpack cache beyond its 32 MiB forecast. The next native phase was
refused before execution. After real approved Node/.NET drainage, exact new
compiler caches and earlier settled invocation caches were retired using
path/hash manifests; reports, production output, recovery inputs and old costs
were preserved. The repeat build admitted 128 MiB growth and passed. Its new
106,707,499-byte disposable compiler cache was also retired after drainage to
leave provider-test headroom. Manifests remain under
`.cis/local/testing/rm09-completion/review-87d-*cache*.json`.

Admission also refused the first exact-session launch before native execution.
Five exact settled invocation Node caches (17,897,489 bytes) were retired after
actual native drainage with a per-file hash manifest; failed reports and browser
proof remained. The unchanged prepared logical attempt was then admitted under
`rm09c-review-87d-tabs-exact-session-restored`. No provider-growth forecast was
reduced to force admission.

The physical 6 GiB ceiling, 64 MiB restoration reserve, contained 2 GiB Docker
allowance, shared 120 hours/128 attempts and restoration 3 hours/3 attempts remain
unchanged. Failed invocations and conservative retry attempts remain charged.
The protected CountryData launch settings and global Node installation are
unchanged. No scanner/report/recovery cleanup or new resource pool was used.

## Review and limits

Disjoint agents reviewed menu/session/Refresh source and shared native wiring.
An independent review caught a fixture actor error before execution: the
lookup-only actor must retain its negative application-read grants. A distinct
scenario-only Refresh operator now owns the concurrent mutations and Audit.
The original lookup and A/B authorization negatives remain intact. Final Refresh
and menu source/result review found no further required change. Independent
review also accepted the exact-session expiry correction; valid panel
restoration and accessible Retry are established by the real browser proof.

The CI correction builds the actual production portal with the owned npm CLI,
requires its BUILD_ID, exports the pinned absolute Node executable for selected
process tests, checks Edge/Xvfb/xauth and wraps the actual service test command
with Xvfb while retaining its exit and strict report guard. Source/shell/local
browser evidence does not establish Ubuntu runner success. WORK-199 must retain
that explicit execution limit until the changed remote path runs.

## CIS checkpoint

Supported transitions completed WORK-196 (menus), WORK-197 (session) and
WORK-198 (Refresh). WORK-199 is ReadyForReview with its actual Ubuntu CI proof
pending. This does not close RM-09/RM-10 or reopen completed prior work. All
earlier failures and resource accounting remain retained. No new payload was
pushed, merged or released.

Final ignored evidence manifests are
`.cis/local/testing/rm09-completion/review-87d-final-evidence.json` (four strict
native reports and exact source/build identities) and
`review-87d-final-integrity.json` (unchanged built production source and protected
runtime/user-file identities). Only the two new browser test continuations,
precise shared fixture expiry and CI preparation changed after the successful
production build; none requires rebuilding product output.

Final CIS plan validation and diff whitespace checks passed. The sampled
`review-87d-final-resource-state.json` records 6,247,710,156 counted bytes,
127,631,924 bytes headroom after the unchanged restoration reserve, positive
remaining shared time/attempt capacity and zero owned Node/.NET/testhost
processes. Every pre-existing task status matches the prior pushed plan.

After this local checkpoint, the user authorized pushing these reviewed fixes
to the existing remote feature branch on 27 November 2026, explicitly without
merging. That publication authority does not establish an Ubuntu CI pass or
authorize a merge/release.
