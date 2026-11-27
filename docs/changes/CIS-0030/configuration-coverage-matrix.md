# Portal Configuration coverage matrix — current acceptance and limits

Inventory for the user's request to automate Configuration combinations. This
file maps inspected tests to their actual bounded acceptance; it runs nothing,
authorizes no additional work and makes no exhaustive Cartesian coverage claim.
The original four Save/Deploy additions and existing version workflow each have
an accepted passing run across the separate batches below. Four later browser
boundary cases also passed on Node 24.16.0; their strict report guard, current
source checks and two abnormal-host-close requalifications passed separately.

The unit of coverage is a valid state transition and its authority/original-key
boundary. Invalid combinations are refusal tests, not additional happy paths.
The current user flow is **Save version → saved immutable pair → Deploy**, with
rollback by deploying an older saved version. Older publication/release and
canary screens are retained history/compatibility source, not proof that the
current version-led components exercise the same transitions.

## Reading this inventory

- **Pure source** means a meaningful `node:test` case was inspected. It proves
  the named helper/owner boundary when its current run passes, not React/BFF/native
  integration. The ordinary final pure batch `rm09c-configuration-final-pure`
  passed 131/131; it is distinct from every native batch below.
- **Native source** means the existing C# parent and browser continuation contain
  concrete assertions. Acceptance additionally requires the current build,
  actual named test, parent SQL/Operations/Audit verifier and joined cleanup.
- **Historical/partial** evidence cannot be relabeled current native acceptance.
  In particular, the first-version browser Save checks completed twice, but the
  parent tests timed out at shutdown and their post-Node SQL/Audit verifiers did
  not run. See the retained
  `.cis/local/testing/rm09-completion/first-version-native-first-attempt-disposition.md`.
  Those failures remain retained. Root subsequently reported the complete
  original first-version native pass in 35 seconds after host repair, including
  its parent SQL/Audit verifier and cleanup. This later pass does not rewrite the
  failed attempts or qualify every new fixture branch.
- The focused `rm09c-instance-status-registered-native` pass includes the
  registered-row refinement, exact row count and closed-target assertions,
  independent Configuration fingerprints and actual cleanup. Later new fixture
  flags do not change that component. Older whole-rollout failures remain
  failures.

Portal test paths below are relative to
`implementations/typescript/applications/management-portal/tests`.
C# test names are in `CIS.Core.IntegrationTests/Management`. These are references
to actual methods/assertions, not new test registrations.

## Current native acceptance across split runs

After the pinned Next response-close correction and current build, the final
mixed native batch `rm09c-configuration-native-final` recorded **three passed,
two failed**. N-SAVE, N-RIGHTS and the existing
`ActualVersionPageSavesWithoutDeployingComparesDeployedAndRollsBackExactPairs`
passed their complete parent SQL/Operations/Audit and cleanup checks. N-DEPLOY
and N-RELEASE were interrupted before their target journeys by Windows Node
exit code `3221226505`; those failed attempts remain retained.

The subsequent targeted `rm09c-configuration-interrupted-cases` batch passed
**both** N-DEPLOY and N-RELEASE, including their full parent checks and cleanup.
Thus all four new cases plus the existing version workflow have passing current
runs. This is **not one five-of-five suite pass**: the mixed batch remains 3/2,
and no claim that the Windows crash was fixed follows from the targeted passes.
`rm09c-configuration-completion-reports` subsequently passed all three remaining
Configuration cases in one successful report. The host/backend report passed6/6
and the current Instance status report passed1/1. Together with the targeted2/2,
the four successful reports contain 12 distinct native cases, zero skips and
actual cleanup. The existing C# report guard accepted all four; scoped formatting
verification passed. These successful reports coexist with all earlier mixed
failures; the Windows native crash remains an explicit platform limitation.

The later `WORK-193/rm09c-browser-boundaries-node-comparison` report contains
N-PRE, N-SHARED, N-SESSION and N-STATUS: four executed/passed, zero failed or
skipped under the verified private Node 24.16.0. Their native parent verifiers
and actual fixture/assembly cleanup completed. This separate runtime-comparison
run does not replace the old 131 pure checks, combine the earlier 12 cases into a
new suite result, or establish general runtime reliability. Current source checks
passed395 portable tests and131 focused portal tests, both abnormal-host-close
requalifications passed, and the strict guard accepted this report. See [Configuration testing](configuration-testing.md)
for the exact TRX and the separate Node transport-probe results.

| Reference | Exact C# named test | Current browser/API source | Concrete independent boundary |
| --- | --- | --- | --- |
| N-SAVE | `ActualNewConfigurationSaveRecoversEachOriginalWithoutRepeatingSetupPublicationOrRelease` | `native-config-save-consumer.mjs` → `verifyNewConfigurationSaveRecoveryBrowser` | Malformed/duplicate local Save refusal, oversized-input rejection preserving prior draft; actual setup/publication/release delivery loss and explicit same-key recovery; exact bytes/pair, three originals/Audits, no assignment. |
| N-RELEASE | `ActualVersionSaveReconcilesAClosedReleaseConflictWithoutRepublishingItsFrozenJson` | `native-config-save-consumer.mjs` → `verifyConfigurationReleaseConflictBrowser` | Real concurrent map original makes release ETag stale; known closed conflict is dismissed; fresh reference-only preview admits a distinct release key without republishing; target SQL and four originals/Audits retain correct outcomes. |
| N-DEPLOY | `ActualVersionDeployClearsPinsConflictsAndRecoversCommittedDeliveryLoss` | `native-config-deploy-consumer.mjs` → `verifyConfigurationDeployApi`, `verifyConfigurationDeployBrowser` | Real retained pair/pins, stale preview/map conflict, deliberate retry, actual committed delivery loss/reload/same-key lookup, all-pin removal and older-pair deployment; independent no-republication/map/Operations/Audit checks. |
| N-RIGHTS | `ActualMetadataOnlyVersionDeploysWhileReadonlyOwnSavedVersionCannot` | `native-config-deploy-consumer.mjs` → the same API/browser functions with `metadataOnly` | Independently authorized actual saved metadata and redacted preview allow Deploy without ContentRead; the readonly account has its own real saved release but no Deploy, and direct mutation is refused. |
| N-PRE | `ActualPortalCreatesSavesAndDeploysConfigurationBeforeAnyInstanceAndRefusesUnretainedOriginals` | `native-configuration-boundaries-checks.mjs` → `verifyConfigurationBoundaryBrowser` / Predeployment | Actual catalog-provisioned application stays at zero registrations and Configuration sessions through New/Save/Deploy; exact UTF-8/SHA, pair, generation and four distinct native originals/Audits; storage throw/readback failure causes no writes. |
| N-SHARED | `ActualVersionComparisonShowsChangedSharedReferencesWhenEveryDisplayedJsonByteIsIdentical` | `native-configuration-boundaries-checks.mjs` → SharedReferenceDiff | Two actual immutable Shared publications contain identical bytes; saved pairs share the same App publication but different Shared references. Current deployed-only UI shows unchanged JSON and the Shared-reference notice, with zero effects and unchanged native fingerprints. |
| N-SESSION | `ActualConfigurationCallersRefuseStorageFailuresAndRetireDraftsAcrossRealSessionChanges` | `native-configuration-boundaries-checks.mjs` → SessionStorage | Finite receipt quota/readback controls block current Save/Deploy effects; actual A-to-B browser credential change retires a held A preview/draft, and credential removal produces a real 401; no Configuration effects or Audit changes. |
| N-STATUS | `ActualInstanceStatusPreservesMixedObservationsAcrossPagingAndBrowserReadBoundaries` | `native-instance-status-boundaries-checks.mjs` → `verifyInstanceStatusBoundariesBrowser` | Real fresh mixed reports, naturally stale receipt/presence, native registration closure, no-recipient targets and two pages; actual desired/applied/history separation, held late read, controlled unavailable/401 delivery and unchanged Configuration/report/session fingerprints. |

Use the existing pinned Node executable with ordinary `--test` commands for the
Configuration tests, command receipts, session context, portal session and portal
response tests. No npm alias or new harness is required. Protected-state tests
remain separately named coverage in the ordinary broader suite; a focused batch
does not silently claim to have run them. Real provider/browser methods remain
separate native checks. See [Configuration testing](configuration-testing.md)
and [Next response-close correction](next-response-close-correction.md) for
root-owned execution/currentness records. The earlier
[host shutdown correction](host-shutdown-correction.md) retains its distinct
raw-TLS-peer and original first-version history.

## Provisioning and initial content

| ID | Valid starting state → transition | Existing exact cases/assertions | Current coverage boundary / concrete gap |
| --- | --- | --- | --- |
| C01 | Unknown generic Configuration → one Save performs setup, publication and coherent release; remains undeployed | `ActualVersionPageSavesWithoutDeployingComparesDeployedAndRollsBackExactPairs` → `verifyConfigurationVersionsBrowser`: new `portal.r09.versions`, exact JSON, three originals, Common absent and unassigned map. `configuration-new-version.test.mjs`: **one Save hands off exact unmodified JSON only after its own known setup and current session, without treating setup as publication**. New current source: N-SAVE. | Current one-form setup/publication/release loss and explicit continuation now have a dedicated native oracle, with three originals/Audits and no assignment. Native acceptance is recorded in the split runs above; the historical separate-Continue UI is no longer the only integration source. |
| C02 | Existing typed Application binding, available Common stream, empty release history and unassigned map → Create first version | `ActualFirstVersionSavesAnExistingApplicationBindingWithAnAvailableCommonStream` → `verifyFirstConfigurationVersionBrowser`: actual empty history/map and provisioned Common/Application slots; `{}` draft admitted; formatted Greeting/Enabled/Count JSON saved; exactly two originals and no assignment. | The original named first-version case now has a root-reported full native pass after host repair, including SQL/Audit and cleanup. Its two earlier browser-only successes/parent timeouts remain retained. Common-available empty binding is distinct from the Common-absent generic path. |
| C03 | Application-only binding with no Common stream → initial Save | Current new-generic native flow in C01; `configuration-editing.test.mjs`: **no assigned Shared layer starts an explicit empty draft while protected context never supplies defaults as payload**. | The generic flow supplies a real Common-absent case. A second identical browser fixture is unnecessary unless the existing-binding admission branch diverges. Pure first-version admission boundaries must cover unavailable/missing/foreign/nonempty maps if extracted by the implementation owner. |
| C04 | Predeployment catalog application with zero registrations → create/publish/assign without enrollment | Backend native `ZeroRegistrationApplicationCreatesPublishesAndAssignsWithSeparateNativeOriginalsAndNoAutomaticEnrollment`; portable creation context/review tests; N-PRE now exercises the current portal against an actual preallocated zero-registration application. | The new browser/native parent case passed through New, Save and Deploy. SQL independently verifies zero registrations/sessions before and after, exact source bytes/SHA/pair and generation1, and exactly four originals/Audits. Storage refusal adds no effects. This closes the specific missing portal journey without a new framework or UI control. |
| C05 | Reuse an existing compatible generic definition for another application; reject typed/Common/foreign targets | Backend native `ExplicitApplicationGrantsCannotAuthorOtherApplicationsTypedRowsCommonSourcesOrMixedTargetBundles`, and `ChangedTrustedCatalogPreservesUnclaimedGenericRowsAndOnlyExactGenericAdoptionCanClaimThem`. Creation helper captures native identity separately from JSON. | The current New form deliberately submits `createGeneric`; it does not expose a `reuseGeneric` selector. Keep this as backend/portable coverage and document the UI boundary. Do not add a new reuse screen just to increase combination counts. |

## Editing, inheritance and saving versions

| ID | State / transition | Existing exact cases/assertions | Current coverage boundary / concrete gap |
| --- | --- | --- | --- |
| C06 | Assigned Shared-only release → Edit as new version copies its exact authorized Shared JSON → new App layer retains that Shared reference | `verifyConfigurationVersionsBrowser` starts from real Shared-only `shared.features`, requires the exact initial release snapshot, saves two App versions and checks the original Common publication is retained. `configuration-editing.test.mjs`: **a new Application override copies the exact assigned Shared text once and protects its dirty intent**. | Current version-led native source covers this branch. Old repair continuation `verifyConfigurationRepairsBrowser` separately proves Shared bytes/map remain unchanged after an App publication, but belongs to the earlier component flow. |
| C07 | Assigned Application layer → edit copy keeps App JSON, not Shared or latest source history | `configuration-editing.test.mjs`: **existing assigned Application JSON wins over Shared and a different publication cannot masquerade as its baseline**. Current version browser edits the first saved App version and requires its raw JSON before saving the second. | Actual App copy is exercised. Preserving an older selected pair's Shared reference after a newer Shared publication exists is pure-covered; a specific current UI/native oracle for that changed-Shared-history situation remains a useful bounded addition. |
| C08 | Unassigned map with retained publication/release history → explicit saved-version selection; never infer latest/current | `verifyConfigurationApi`: **native-configuration-unassigned-is-not-latest-history** checks `content.state=unselected` despite history. `configuration-projection.test.mjs`: **unselected absence and protected content stay distinct without a guessed empty object or latest publication**. | API/pure source is concrete. Current version page selects saved rows for inspection without claiming deployment; verify an actual reload with unassigned retained versions in the current UI regression rather than relying on UUID order or a legacy editor assertion. |
| C09 | Local raw edit, Format, incomplete JSON, omit/null/false/zero and large/exponent/Unicode tokens | `configuration-draft.test.mjs`: **scalar token patches preserve every unrelated large number Unicode escape false zero and null byte**; **pretty formatting changes whitespace only and preserves exact numeric string and Unicode tokens**; **format refuses incomplete duplicate or oversized results without returning a replacement draft**. `verifyFirstConfigurationVersionBrowser` and version workflow assert exact native bytes. New current source: N-SAVE before any valid Save. | The accepted New form case has meaningful assertions for malformed and duplicate JSON Save refusal, unchanged receipt storage/zero effects, and oversized local input rejected whole while preserving the previous invalid draft. Native acceptance is recorded in the split runs above; this is not a claim that an oversized body was sent to or rejected by the service. |
| C10 | Save freezes exact current draft and note → native candidate preview → publication settles → distinct release; no assignment | `configuration-version-commands.test.mjs`: **native preview must echo the complete current map, exact references, source/preconditions and raw SHA before publication** and **one Save awaits preview then publication cleanup before the distinct release and never submits assignment**. Current native version flow requires two effects per Save and unchanged complete map. New current sources: N-SAVE and N-RELEASE. Pure additions: **currentness loss during held preview or release joins the callback without advancing or hiding known original facts**; **foreign native preview tuple or source/map preconditions prevent every publication and release port**. | Current direct Save staging and real stale-map release reconciliation now have named native oracles; currentness/tuple/precondition refusal is directly exercised by the added finite pure tables. Current native acceptance is recorded in the split runs above, distinct from the finite pure tables. |
| C11 | A retained unresolved original or outage → local text stays editable, every new native write stays blocked | `configuration-editing.test.mjs`: **local JSON remains editable with a retained unknown original or stale read while every new dispatch stays blocked**. `configuration-repairs` native continuation counts no second publication while a known original remains. | Helper and earlier UI source cover separation. Add one current version-editor scenario that visibly edits text while Save cannot dispatch with an unresolved original; verify the original key is unchanged and no payload is placed in sessionStorage. |

## Partial saves and original-key recovery

| ID | State / transition | Existing exact cases/assertions | Current coverage boundary / concrete gap |
| --- | --- | --- | --- |
| C12 | Setup is unknown/rejected or cleanup is unconfirmed → stop before publication | `configuration-new-version.test.mjs`: **unknown/rejected setup and failed current/native cleanup retain a partial Save instead of authorizing the next original**. Historical R08 native test drops creation delivery and recovers the same key. New current source: N-SAVE. | The current one-form New component now has actual setup-commit/dropped-delivery source, same-key lookup, no automatic publication, and explicit Complete incomplete Save with its original in-memory intent. Native acceptance is recorded in the split runs above. Reload cannot reconstruct or automatically resume a payload-free receipt. |
| C13 | Publication unknown or cleanup failure → no release; same-key lookup can confirm and explicitly continue | `configuration-version-commands.test.mjs`: **unknown publication, failed cleanup or changed session stops later stages and retains actual earlier facts** and **explicit Continue reuses only the exact known publication; a release fault preserves it without automatic replay**. `configuration-operation-facts.test.mjs`: **only actual matching Found or Replay facts can confirm a later clean stage while prior cleanup facts remain dated history**. New current source: N-SAVE. | The accepted current component case performs an actual publication commit followed by delivery loss, same-key recovery and deliberate Continue. It counts one publication, suppresses release until confirmation and keeps the exact body/session intent. Native acceptance is recorded in the split runs above; weak/denied/cleanup-warning variations remain independently covered by the existing pure tables. |
| C14 | Publication known; release unknown/cleanup warning → keep known saved JSON/original; recover release only | Pure **a known saved release survives its independent cleanup warning while a foreign release result is never Saved**; operation-facts **a matching business result with another cleanup warning stays committed but cannot release the next Save stage**. Legacy native release delivery-loss checks exist. New current source: N-SAVE. | The same current journey then drops actual release delivery and recovers that exact original, retaining the earlier publication and admitting no repeat setup/publication/release or assignment. The accepted parent verifier checks one saved pair and three finalized originals/Audits. Native acceptance is recorded in the split runs above; unknown release is never prematurely called a deployable saved version. |
| C15 | Known rejected/conflicted release explicitly dismissed → fresh same-session exact map/pair review → new release-only intent | Pure **explicit resolved-release reconciliation preserves the exact already-published version and requires fresh pair/map preview without re-publication** and **unknown unresolved or contradictory release outcomes never authorize a new release key or cross-session rebind**. New current source: N-RELEASE. | The current UI transition now has accepted native evidence: an actual concurrent map decision causes a known finalized release conflict; explicit dismissal and fresh map/reference preview admit a distinct release-only key. Exact old Shared/new App references and one publication are independently checked. Native acceptance is recorded in the split runs above; unknown originals still cannot authorize replacement. |
| C16 | Weaker or contradictory later lookup, permission loss, cleanup fault | Operation-facts **a weaker same-original lookup preserves committed business facts but exposes its real current outcome without granting advancement**; **conflicting known decision or actor scope is inert invalid response and cannot replace the established fact**. `configuration-recovery.test.mjs` tests actual owners under delayed/denied background reads. New current sources: N-SAVE and N-RELEASE; existing operation-facts/recovery tables remain the broader negative evidence. | Clean same-key current caller recovery and deliberate closed-conflict replacement now have accepted native witnesses. Denial, weak lookup, conflicting known facts and cleanup warnings are not mislabeled native passing cases: their existing finite pure/owner assertions remain distinct. Current native acceptance is recorded in the split runs above. |

## Deploy, rollback and comparison

| ID | State / transition | Existing exact cases/assertions | Current coverage boundary / concrete gap |
| --- | --- | --- | --- |
| C17 | Uniform map → Deploy selected saved pair → older saved pair for rollback | Current `verifyConfigurationVersionsBrowser` deploys second then first, requires generations 2/3, exact immutable pairs, two assignment originals and no extra publication. C# `ActualVersionPageSavesWithoutDeployingComparesDeployedAndRollsBackExactPairs` independently checks SQL and Audit. New current source: N-DEPLOY, in addition to the original version workflow. | The original uniform-map version test remains unchanged. A second current-component native source now rolls back an exact retained pair after conflict/delivery recovery, with no publication/release rewrite and independent parent map/Operations/Audit assertions. Accepted passing runs are recorded across the final mixed and targeted batches; the final mixed batch remains three passed/two failed. |
| C18 | Existing cohort pins → Deploy all instances explicitly clears every pin after truthful warning | Pure **Deploy older version deliberately removes every existing pin and accepts only its exact full-map native preview**. Legacy rollout browser proves canary/promotion/retained Finance pins under the old assignment UI. New current source: N-DEPLOY. | The native coverage gap is addressed: real canary/Finance pins reach the current Deploy dialog, its explicit removal warning is checked, actual commands require overrides=[], and final native map has no pins. The legacy pin-preserving wizard is no longer cited as the only UI proof. Native acceptance is recorded in the split runs above. |
| C19 | Concurrent map change / lost assignment delivery → conflict or same-key recovery; no automatic rollback/new key | Legacy native `verifyRolloutBrowser` performs a real concurrent map decision, conflict, explicit rebase and lost-delivery recovery. Pure deployment receipts test **unknown, refused, weaker recovery and independent cleanup failures never unblock a new deployment**. New current source: N-DEPLOY. | The accepted current simple dialog case creates a real concurrent map original after preview, observes the closed conflict, deliberately retries, drops an actual committed assignment response, reloads and recovers the same key before older-pair rollback. Publication/release counts and persisted bytes remain unchanged. Native acceptance is recorded in the split runs above. |
| C20 | Selected saved version vs actual fresh uniform deployed pair; deployment refreshes the baseline | Current version browser compares left JSON with the exact initial native deployed snapshot, right with selected JSON, preserves the large integer and requires no mutation; after Deploy it checks no changes. Pure diff **comparison baseline is only the fresh actual uniform deployed ID, with explicit no-deployment and targeted states**. | Current positive baseline oracle is concrete. No default, targeted map, protected baseline and changed map/session are covered by pure boundaries; one current UI late-baseline/context invalidation check is useful if that seam is otherwise unexercised. No arbitrary version picker belongs in the matrix. |
| C21 | Same displayed Application JSON but changed Shared reference/content | Pure **identical Application JSON cannot claim unchanged saved versions when Shared bases or references differ**; protected/foreign/oversize comparisons are also refused. N-SHARED adds two real immutable pairs differing only in Shared publication reference while Shared and App JSON bytes remain identical. | N-SHARED passed the current deployed-only comparison: all displayed rows remain unchanged, the Shared-reference notice is visible, no write is sent and parent SQL verifies exact source/pair identities and unchanged fingerprints. This closes the specific reference-only rendering gap; it does not claim an effective merged diff or a generic JSON/schema engine. |

## Observation, permissions and lifecycle

| ID | State / transition | Existing exact cases/assertions | Current coverage boundary / concrete gap |
| --- | --- | --- | --- |
| C22 | Fresh recognized accepted/restart/rejected/faulted vs stale/offline/missing/unknown observations | `configuration-instance-status-model.test.mjs` retains exact desired/report/applied identity, known-state/update-mode and freshness cross-products. N-STATUS now adds real native fresh Applied/Pending/PendingRestart/Rejected/Faulted reports, actual wall-clock stale reports, a natively closed registration and no-recipient target rows across two pages. | N-STATUS passed the approved three-column UI, bounded continuation, current-versus-historical version details, stale/offline Unknown status and collapsed target diagnostics. Read-only SQL fingerprints include registrations, sessions, reports, Configuration state, originals and Audit. Controlled unavailable/401 delivery checks UI retirement only; those parts do not establish a real service outage or expired session. |
| C23 | AdoptionRead-only operator; version metadata denied; target placeholders are not instances | `ActualInstanceStatusRemainsReadOnlyForHistoricalReportsAndAnAdoptionOnlyOperator` → `verifyInstanceStatusBrowser`: real three headers, exact registered-row count, denied App/release metadata, unavailable labels, collapsed target diagnostics, history/Copy/Escape/Close and unchanged map/SQL/configuration originals/Audit. | The registered-row native pass includes these assertions and parent fingerprints/cleanup. This does not qualify the old whole-rollout wizard test or imply full fleet/process identity. |
| C24 | Read-only / content-protected / cross-application / denied mutation and lookup | `verifyConfigurationApi` checks native readonly default redaction/write denial and same-actor recovery after read/write removal; version browser refuses B-to-A version JSON. Pure **Save admission refuses foreign scopes, borrowed action resources, malformed JSON and canceled capture before any transport**. Native `ExplicitApplicationGrantsCannotAuthorOtherApplicationsTypedRowsCommonSourcesOrMixedTargetBundles` supports the service boundary. New current source: N-RIGHTS plus verifyConfigurationDeployApi cross-application denial. | Dedicated finite grant variants now provide a real saved version for the readonly account and metadata-only Deploy authority without ContentRead for A. The accepted case asserts redacted native preview, hidden JSON and disabled Edit, disabled/directly refused readonly deployment and unchanged readonly map. Existing ordinary A/B negatives are preserved. Native acceptance is recorded in the split runs above. |
| C25 | Same URL/target/schema changes, sign-out/expiry/user/session change, delayed ready/denial, outage and recovery | Existing protected-state/portal-session tests retain exact selection, same-URL race, permission loss and renewal boundaries. N-DEPLOY covers held map/view retirement. N-SESSION adds actual A-to-B browser session credentials during a held native A preview, then removes browser credentials for an actual 401. N-STATUS adds held native read retirement after selecting a foreign application, same-session refresh and controlled unavailable/401 delivery. | The later four-case report passed these named current caller boundaries, with joined late callbacks, cleared drafts/details and no Configuration effects. Cookie replacement/removal is a real browser identity/unauthenticated boundary, not provider-token expiry or server-side revocation. N-STATUS's substituted unavailable/401 delivery is controlled presentation evidence, not a real outage/session-expiry proof. Every session, renewal and timing permutation remains outside this bounded claim. |
| C26 | Receipt storage/full/age/context mismatch, dirty leave and local draft custody | Existing command-receipt tables cover quota/unverified admission, age/context and occupied recovery. N-SAVE/N-DEPLOY retain original-before-write and payload-free recovery checks. N-PRE and N-SESSION now exercise actual New/Save/Deploy callers with synthetic Storage.setItem quota throws and silently lost writes that fail retention readback. | The browser cases passed local draft retention and zero native setup/publication/release/assignment effects when the exact original could not be retained. Read-only validation/preview traffic is permitted; blocked writes produce no extra claims or Audit. Controls are restored and parent fingerprints remain unchanged. Physical browser-storage exhaustion, every receipt age/context permutation and every quota implementation are not claimed native coverage. |

## Bounded acceptance and remaining limits

The original four coherent source groups and four later browser boundary cases
now exist and have the passing observations identified above; the earlier
recommendations to author those specific cases are superseded. They reuse the ordinary native fixture and small actual
delivery/race controls, preserving old expected effects, failed attempts and
ordinary A/B negatives. No new test framework, backend mode or command authority
was added to satisfy this inventory.

1. **Initial/version acceptance:** the existing first-version regression now has
   its root-reported complete SQL/Audit/cleanup pass after host qualification.
   N-SAVE adds the current New form's local invalid/duplicate/oversize refusal
   before the valid flow. The original full version/rollback/diff test passed in
   the final mixed batch; that batch's other interrupted cases remain failed in
   its original record.
2. **Partial Save:** N-SAVE and N-RELEASE implement the missing current
   caller-level recovery and closed-conflict paths. N-SAVE passed the final mixed
   batch; N-RELEASE passed the targeted interrupted-case batch after its retained
   pre-target Windows Node failure. Each passing case completed independent
   persisted-effect and cleanup checks.
3. **Simple Deploy:** N-DEPLOY implements real pins, actual map conflict,
   controlled committed delivery loss, same-key reload recovery and older-pair
   deployment in the current dialog. It preserves exact source publication and
   immutable release data. N-DEPLOY passed the targeted interrupted-case batch
   after its retained pre-target Windows Node failure in the mixed batch.
4. **Authority/lifetime:** N-RIGHTS supplies finite native metadata-only/readonly
   variants. N-DEPLOY supplies the joined late-map/view-retirement seam, while
   N-SESSION adds actual browser user/unauthenticated changes during current
   editing and controlled receipt-storage refusal. N-STATUS adds real mixed
   fresh/stale/offline/no-recipient reports and two bounded pages, plus explicitly
   controlled unavailable/401 delivery. Broader session/receipt/held-Save tables
   retain their own distinct helper evidence. These passing results are not a
   complete browser Cartesian session or capability matrix.
5. **Previously missing browser combinations:** N-PRE closes the zero-registration
   portal New/Save/Deploy gap; N-SHARED closes the identical-JSON/different-Shared-
   reference rendering gap. Their exact native state assertions passed with
   N-SESSION/N-STATUS in the later four-case report. Neither backend-only proof
   nor authored browser source is being substituted for these actual results.

Valid combinations remain bounded by real product transitions. Common-present
and Common-absent initial states, exact inherited versus App content, known
versus unknown original outcomes, current map/preconditions, native capabilities
and protected session lifetimes are represented independently. Invalid
cross-target/future/duplicate/malformed states are refusal rows; the portal does
not expose every backend-only command variant. Remaining limits include physical
browser-storage exhaustion, every receipt age/context permutation, provider-token
expiry/server revocation in the new specific journeys, actual infrastructure
outage, and every timing/session/capability combination. Applicable existing pure
or earlier native evidence remains separately attributed. The strict report
guard and current source/abnormal-host-close requalification passed separately;
see [actual verification](configuration-followup-verification.md). These are disclosed limits, not an instruction to start
another campaign or invent new controls.

For every mutating native continuation, acceptance requires exact original keys,
native decisions, independent persisted bytes/pairs/maps and payload-free Audit,
plus settled request/browser/host/provider ownership. Read-only checks require
zero mutation and unchanged native state. Test selection remains focused on
these affected portal paths; there is no unbounded full-suite/tooling campaign,
new backend, Scheduler runtime or promise to enumerate every mathematical
combination.
