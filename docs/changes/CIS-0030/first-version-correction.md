# First Configuration version correction

The user reported that **Create first version** on Billing Worker's
`shared.settings` fails with “The saved JSON could not be admitted for this
exact source and session. Nothing was copied.”

The version page correctly offers first-version editing for an empty saved
history and assignment map. The editor incorrectly requires the Common catalog
stream to be absent before admitting a blank Application draft. Stream presence
does not mean an assigned Common publication exists. The existing Save contract
already creates the deliberately application-only first release, with explicit
Common absence, current native authorization and preview, two distinct original
publication/release operations, and no assignment after Save.

Continue WORK-190 under the current delegated outcome and original version-saving
contract. Fix only initial draft eligibility: require an actual empty assignment
map with the exact binding/application/Configuration/schema/environment identity,
regardless of Common stream presence. Preserve current source/session/defaults,
dirty-draft and native Save/ETag/permission/recovery guards. Do not guess a Common
publication or copy unauthorized source JSON. No visual arrangement changes;
the approved R09 screens and screenshot gate remain intact.

Add one distinct named case to the existing real host/browser/provider fixture:
preprovisioned Application and Common streams, no saved/deployed version, Create
first version, editable empty JSON, then exact formatted JSON saved as one
publication and one release with explicit Common absence and no assignment.
Retain existing auth/metadata checks, SQL/Operations/Audit assertions and joined
cleanup. Other fixture branches and prior regression expectations are unchanged.
Include the affected build, focused checks, documentation and independent review
in this same coherent implementation outcome, with root serializing native work.

The existing delegated allocation permits redistribution. Transfer two unused
hours from WORK-100-BACKOFFICE to WORK-190, leaving that pool25h and WORK-1907.25h.
At intake the former had 10,722,566ms unused; the transfer leaves 3,522,566ms for its
existing remaining work. Keep all attempts, historical costs, active review
envelopes, restoration reserves and shared120h/128 attempts/physical6GiB limits.
Conservatively add one shared source hour for all agents, investigation, fix,
test authoring, documentation and review: WORK-190 source estimate4.5h, plus exact
native/metadata costs. Logical attempt5/6 covers this correction. No completed
owner is reopened, no new framework/package/backend or broad campaign is added,
and no push/merge/release is included.

The55170 review used shared build outputs and remained untouched during source
work. The user then explicitly replied **Ready — replace it with fresh test
data** for this correction. The decision is retained in
`first-version-review-replacement-decision.json`. Its runner exited0, with actual
host/stream/IPC/issuer joins, separately confirmed fixture-owner disposal and
test-assembly cleanup before the build. The status file's placeholder
`fixtureCleanupComplete:false` is preserved; the later parent fixture-cleanup
and runner-complete records establish their own actual settlement. Continuity
had passed. No JSON restoration is claimed. All old results and costs remain.

The narrow editor and dedicated five-file fixture changes passed independent
source reviews. The production build, scoped C# whitespace format and
verify-no-changes passed. C# formatting changed no frozen bytes. The affected
test build passed with zero warnings/errors. Source authors did not execute
native work.

The first native execution passed both browser cases, including the exact
formatted JSON readback and unchanged assignment. The complete remaining auth
sequence also reached graceful-host-stop. The child host then did not exit
before the existing180-second deadline; the full test had0 passed/1 failed/0 skipped. Its C#
post-Node SQL/Audit checks did not run, so browser observations alone are not a
passing parent result. The failed TRX/log/phase and cost remain retained under
`WORK-190/rm09c-first-version-native`. The isolated Docker volumes were deleted
by actual assembly cleanup. Source review found no new detached owner in this
scenario; the unchanged shutdown await's precise cause is not established.

Use the final existing logical attempt6/6 for one fresh same-candidate execution
of this same named case after verifying native drainage. This checks whether
the shutdown timeout repeats; it is not permission for an open-ended runtime or
tooling campaign. Keep exact source bytes, runtime/SDK pins, deadlines, native
assertions and all prior results unchanged. Source/time/storage totals and
restoration reserves remain intact. A later pass cannot resolve the prior
timeout's cause or retroactively make it successful.

The same-candidate confirmation also reached graceful-host-stop with zero
recorded failures after both browser cases, then timed out at the unchanged
180-second boundary. Its full result is again0 passed/1 failed/0 skipped, retained
under `WORK-190/rm09c-first-version-native-confirmation`. Both isolated Docker
volumes were deleted by actual assembly cleanup. The SQL/Audit postconditions
after the child Node return did not execute in either run. A narrow independent
shutdown source review found no defensible change; no speculative ordering,
deadline, runtime pin or host implementation change was made. There is no full
native acceptance or resolved shutdown cause, and no third test/probe campaign.

## Current verification boundary

| Check | Actual result |
| --- | --- |
| Editor and dedicated fixture source reviews | No required findings; retained in `first-version-editor-source-review.md` and `first-version-native-fixture-source-review.md` under `.cis/local/testing/rm09-completion/` |
| Portal production build | Passed Next16.4.6 production build, TypeScript and routes, phase `rm09c-first-version-portal-build` |
| Scoped C# whitespace/verification | Both passed; frozen C# hashes unchanged |
| Affected native test build | Passed, zero warnings/errors,29.18s |
| Actual browser flow | Both executions passed the two named Create-first-version/Save checks, including exact formatted JSON with Unicode/large integer, two distinct native originals, explicit Common absence and unchanged full assignment map |
| Complete named native test | Both executions failed on child-host shutdown timeout; current C# SQL/Audit assertions and graceful native shutdown remain unverified |
| Source stability | All seven files match `.cis/local/testing/rm09-completion/first-version-candidate-manifest.json`, SHA-256 `83d07e0718fc799a7ea23b1a1b0b56767ae7a18f99b2fe6a91f3e72f214d11af`; scoped Git whitespace check passed |

The production editor is
`implementations/typescript/applications/management-portal/src/components/configuration-version-editor.tsx`,
SHA-256 `83be40459953699cba2ebca5175620dd3c97ce93a270015489d3475a7fb82e8f`.
The passing first browser screenshot is
`.artifacts/management-portal/CIS-0029/WORK-005/browser-f409f56f-257a-439a-b3f5-d633eb4e12d7/configuration-first-version-existing-common-stream-native.png`.
Root inspected it: one saved version, exact formatted Application JSON and
Not deployed, with closed comparison/diagnostics. Its real browser case result
does not supersede the parent failure. The second phase/child/browser evidence
and both failed TRXs remain independently retained.

WORK-190 remains InProgress for the unresolved full native check and actual
finite user-review drainage. The original passing R09 checkpoints remain their
historical proofs, not proof of this newly added first-version case. The manual
replacement uses the user's exact current readiness and fresh test data; it is
part of correction attempt6, not a third automated confirmation. Its ordinary
30-minute lease/40-minute envelope plus10-minute closure margin fit the remaining
5,566,627ms at launch. Readiness will be recorded separately after observation;
it cannot qualify either failed test or its postconditions. No push, merge or
release was performed.

## Fresh local review

Observed ready at2026-11-26T23:13:59.5281206Z with public `/healthz` HTTP200 and
the owned visible browser opened. Entry:
https://127.0.0.1:49291/applications. Select **Billing Worker → Configuration →
shared.settings → Create first version**. This has fresh synthetic data and new
resource IDs; the old55170 URL and its data are closed. No copied JSON or saved
data restoration is claimed.

Expiry:2026-11-26T23:43:22.660Z, **27 November00:43:22 Malta**. The actual control
directory is
`.artifacts/management-portal/CIS-0029/WORK-006/rm09c-first-version-review-20261127-000302/`;
execution/accounting belongs to CIS-0030/WORK-190. Current source identities still
match the seven-file candidate manifest, and the user's launch settings retain
SHA-256 `ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.

Independent disposition of both full failures and their passing browser checks:
`.cis/local/testing/rm09-completion/first-version-native-first-attempt-disposition.md`.
Readiness does not qualify the failed named test. The review's continuity probe,
final native result/cost and actual eventual cleanup remain pending. WORK-190
stays active; there is no full native acceptance or work-owner closure claim.
