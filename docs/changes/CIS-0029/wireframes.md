---
title: "CIS-0029 textual wireframes"
type: textual-wireframes
status: Draft
change_id: CIS-0029
approval_status: NotReviewed
authority: human-reviewed
---

# Textual wireframes

Draft behavior for one RM-09 backoffice design package. No production UI,
administrative submission or approval is represented by these wireframes. The
human decision must cover the portal UI direction and exact rendered design;
existing product-wide authority remains unchanged until that explicit decision.
See [design brief](design-brief.md) and [source reconciliation](reconciliation.md).

## Screen inventory

| Screen ID | Frontend type | Name | Route/path | Platform | Actors/access | Entry points | Purpose |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-SIGNIN | backoffice | Operator sign-in and session recovery | /sign-in | Web desktop and narrow | Anonymous or expired operator session | Entry, sign-out, session expiry | Establish a real human session without granting Management authority by portal identity alone. |
| RM09-APPLICATIONS | backoffice | Authorized applications | /applications | Web desktop and narrow | Current operator with target read access | Sign-in, navigation, detail return | Find and page authorized application identities in a visible environment. |
| RM09-APPLICATION | backoffice | Application detail and reported inventory | /applications/11111111-1111-1111-1111-111111111111 | Web desktop and narrow | Current operator with exact target read access | Application list, metadata result | Inspect identity, metadata, instances, capabilities and complete reported inventories. |
| RM09-METADATA | backoffice | Metadata edit and exact change review | /applications/11111111-1111-1111-1111-111111111111/metadata | Web desktop and narrow | Target read, metadata write and original lookup eligibility | Application detail | Deliberately change only display name/description, with conflict review and original-key receipt. |
| RM09-OPERATION | backoffice | Session operation receipts and recovery | /operations | Web desktop and narrow | Same current operator, freshly authorized for original target/lookup | Submitted changes, navigation, known receipt | Explain original decision/persistence and recover uncertain outcomes without duplicate effects. |
| RM09-CONFIGURATION | backoffice | Configuration sources, publications and releases | /configuration | Web desktop and narrow | Authorized source/binding discovery; content and mutation grants separately checked | Navigation, application context, operation result | Browse source pages, validate exact JSON, publish immutable content and create an inactive coherent release. |
| RM09-ASSIGNMENT | backoffice | Complete assignment map review | /applications/11111111-1111-1111-1111-111111111111/configuration/country.settings/assignment | Web desktop and narrow | Exact binding read/write/preview and original lookup eligibility | Created release, promotion, rollback | Review default and every cohort override before one guarded assignment decision. |
| RM09-ADOPTION | backoffice | Adoption, promotion and rollback | /applications/11111111-1111-1111-1111-111111111111/configuration/adoption | Web desktop and narrow | Exact application adoption read; mutation journeys separately checked | Assignment result, application context | Compare desired and reported versions with independent freshness; select verified known rollback content. |

## Shared interaction and data rules

All concrete UUIDs, keys, identities, observations and grants in the review are
synthetic. Every plate says **DEMONSTRATION - synthetic data**. The exact sample
paths below are proposed portal navigation, not Management API routes or evidence
of implemented portal behavior. Production dynamic segments must resolve and
authorize the requested resource. Query panels/states reuse these eight templates;
they are not disconnected screens. No renderer makes real administrative calls.

The main target is `Demo.CountryData`, display name `Country data service`, in
`Demo staging`, application ID `11111111-1111-1111-1111-111111111111`. Use
`country.settings`, schema `"1"`; common source
`22222222-2222-2222-2222-222222222222`, application source
`33333333-3333-3333-3333-333333333333`; known earlier release
`44444444-4444-4444-4444-444444444444`, proposed release
`55555555-5555-5555-5555-555555555555`. Metadata revision `"12"`, assignment
generation `"41"` then `"42"`, and a fixed dated CET display link the journey.
Keep exact seven-fraction UTC timestamps in detail and Int64 values as strings.

Required prerequisite labels are **P1 human/delegated identity**, **P2 application
search and authorized environments**, **P3 scoped configuration journey
eligibility**, and **P4 configured-cohort discovery**. They are proposed additions,
not delivered APIs. Current native workload Entra support is not human login;
current paging is not server search; foundation caller hints do not cover all
configuration source/binding grants; observed cohorts/current pins do not enumerate
an absent configured canary. Other delivered APIs remain capability/version gated.

Protected screens share environment/target context, dated fetch feedback,
Applications/Configuration/Operation receipts navigation and sign-out. Shell
navigation must not silently discard a permitted in-memory draft. Any target or
identity change invalidates prepared confirmations; late obsolete responses cannot
replace the new view. Expiry or denied read removes protected content; a valid
session's network outage may retain a clearly dated snapshot with writes disabled.

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-N01 | Applications | Current session; permitted draft departure resolved | Activate navigation | Open authorized application query; discard no unresolved draft silently | /applications | RM09-APPLICATIONS | No session routes through sign-in; unavailable state contains no fabricated rows. |
| RM09-N02 | Configuration | Current session; permitted draft departure resolved | Activate navigation | Open authorized source directory, not a complete app-specific catalog | /configuration | RM09-CONFIGURATION | Denied content is removed; no source payload fallback. |
| RM09-N03 | Operation receipts | Current session | Activate navigation | Open this operator's bounded, incomplete session references | /operations | RM09-OPERATION | A denied lookup reveals no result; no global history is implied. |
| RM09-N04 | Sign out / session expired | Protected screen; manual sign-out or authoritative expiry | Sign out or follow expiry transition | Clear protected caches, drafts and user-switch/sign-out receipts; cancel obsolete reads | /sign-in?reason=session-ended | RM09-SIGNIN | No fallback identity, old response repopulation or automatic mutation replay. |

## Screen definitions

### RM09-SIGNIN: Operator sign-in and session recovery

#### Description

Read in order: demo marker, portal name, sign-in/session explanation, reference
Entra sign-in control and persistent error region. Production P1 requires OIDC
authorization code/PKCE and server-only sessions plus independently validated
human/delegated Management identity. The synthetic review identifies its session as
demonstration data, never as a real administrator. Protected application/config
content is absent. Return destinations are restricted to trusted portal paths.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-S01 | Sign in with Microsoft Entra | Trusted production P1 configured; synthetic equivalent in review | Complete sign-in | Establish verified session, then request authorized application data | /applications | RM09-APPLICATIONS | Authentication failure remains on sign-in; no development fallback or borrowed portal grant. |
| RM09-S02 | Try sign-in again | Recoverable authentication/session error | Explicitly retry authentication | Start a fresh supported auth exchange; replay no administrative intent | /sign-in?reason=retry | RM09-SIGNIN | Keep safe error/correlation information without tokens or protected data. |

#### States

- Primary: reference sign-in proposal with P1 prerequisite badge.
- Loading: sign-in progress disables duplicate initiation, with announced status.
- Error/denied: safe authentication error versus authenticated no-access explanation.
- Session expired: protected content cleared; original unresolved same-user receipt
  may survive only under the finite payload-free policy, never after user switch.
- Narrow/keyboard: one-column card; labeled control, visible focus and no hover-only help.

### RM09-APPLICATIONS: Authorized applications

#### Description

Header shows selected authorized environment, connection/fetch time and page
purpose. P2 supplies a server environment selector and bounded identity/display-name
search. Table columns are display name, trusted application identity, environment,
metadata revision and Open. A count says only how many rows this page contains;
there are no invented fleet totals, presence/health columns requiring fleet scans,
or loaded-page filters advertised as global search. Search changes reset cursors;
live-view pagination never promises a historical cross-page snapshot.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-L01 | Apply environment/search | P2 implemented and current session | Choose authorized Demo staging and enter Country | Issue bounded server query with reset cursor and current scope | /applications?environment=Demo%20staging&q=Country | RM09-APPLICATIONS | Missing P2 is explicitly unavailable; no first-page/global-search substitution. |
| RM09-L02 | Next page | Authorized response supplies opaque next cursor | Activate Next | Request next page for unchanged query/scope; finite retained page cache | /applications?environment=Demo%20staging&q=Country&cursor=demo-page-2 | RM09-APPLICATIONS | Invalid/changed-scope cursor resets to a fresh query after explanation; expose no unauthorized data. |
| RM09-L03 | Open Country data service | Authorized row present | Activate row link | Read exact target and implemented capabilities | /applications/11111111-1111-1111-1111-111111111111 | RM09-APPLICATION | Inaccessible target uses backend absence/denial behavior; never reveal cached protected details. |
| RM09-L04 | Refresh applications | Valid session and no identical request in flight | Activate Refresh | Reread query without mutation or authority inference | /applications?environment=Demo%20staging&q=Country | RM09-APPLICATIONS | Preserve dated prior snapshot only for valid-session outage; initial unavailable has no fabricated zeros. |

#### States

- Primary: six synthetic authorized examples across two demo environments; only
  current page rows/count are shown.
- Loading, no authorized applications, no query results and unavailable are distinct.
- Stale: retain fetch time and disable changes; session/grant loss clears protected rows.
- Prerequisite: P2 badge explains search/environment selection is proposed.
- Narrow/keyboard: reflow search; table may scroll in its own labeled region;
  environment stays visible and row links have descriptive accessible names.

### RM09-APPLICATION: Application detail and reported inventory

#### Description

Show environment, display name, immutable identity/UUID, description and metadata
revision before actions. Sections expose instances, inventory and capabilities
through `getApplication`, `listInstances`, `listInventory`, `getCapabilities`.
Instances separate reported labels from NotProvided/Reported/Verified evidence,
presence, lifecycle/health observations and original observation/receipt times.
Inventory selects a registration and groups complete Modules, Categories and Jobs;
unknown details are bounded escaped text/JSON. Reported owner/generation/expiry is
an observation, never current lease or admission proof. Configuration access links
the paged source directory or exact authorized adoption context; it does not invent
a complete per-application definition list or definition-wide update mode.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-D01 | Instances | Target read allowed | Select Instances | Read bounded instance page and retain source/time distinctions | /applications/11111111-1111-1111-1111-111111111111?tab=instances | RM09-APPLICATION | Unavailable is not proof that all instances are unhealthy. |
| RM09-D02 | Inventory | Target read allowed | Select Inventory and registration | Read complete reported snapshot; render opaque data safely | /applications/11111111-1111-1111-1111-111111111111?tab=inventory | RM09-APPLICATION | Reject unsupported/oversized content clearly; do not present truncated inventory as complete. |
| RM09-D03 | Capabilities | Target read allowed | Select Capabilities | Show implemented version and caller hints as hints | /applications/11111111-1111-1111-1111-111111111111?tab=capabilities | RM09-APPLICATION | Missing/incompatible features unavailable; unknown versions/statuses never imply success. |
| RM09-D04 | Edit display metadata | Exact target read/write/lookup eligibility and fresh state | Activate Edit | Capture current strong ETag and open permitted draft | /applications/11111111-1111-1111-1111-111111111111/metadata | RM09-METADATA | Explain missing eligibility; direct server calls still independently authorize. |
| RM09-D05 | Browse configuration sources | Authorized discovery | Activate configuration link | Open paged authorized source directory with target context | /configuration?applicationId=11111111-1111-1111-1111-111111111111 | RM09-CONFIGURATION | Context is not an invented server filter; index labels its actual page scope. |
| RM09-D06 | View adoption | Exact application adoption read allowed | Activate Adoption | Request current target relationships and reported acceptance | /applications/11111111-1111-1111-1111-111111111111/configuration/adoption | RM09-ADOPTION | Denied data absent; unsupported capability has truthful unavailable explanation. |

#### States

- Primary, no registrations, no inventory received, loading and capability unavailable.
- Historical unhealthy observation, stale presence and unknown health are separate.
- Denied/expired clears protected sections; valid-session outage preserves dated
  observations and blocks edits without claiming new managed-work admission facts.
- No restart, job execution, owner transfer, self-control or release-lease controls.
- Narrow/keyboard: identity wraps above actions; sections retain labels, focus and
  local table scrolling; expanded JSON stays escaped and bounded.

### RM09-METADATA: Metadata edit and exact change review

#### Description

Same target header throughout Edit -> Review. Only display name and optional
description are editable. Empty description is shown as an explicit clear action;
the command omits it rather than sending null. Review names exact environment,
application identity, before/after fields and original precondition. Draft and
prepared review remain in permitted memory. Create the original key before the
first submission, retain a minimal same-user receipt and send unchanged intent,
ETag and key once through `replaceMetadata`. Display editing changes no identity,
permissions, instances, jobs or configuration.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-M01 | Review changes | Permitted valid draft and fresh target | Activate Review | Freeze exact proposed fields and current ETag for review; no mutation | /applications/11111111-1111-1111-1111-111111111111/metadata?panel=review | RM09-METADATA | Associate validation errors to fields; retain permitted draft. |
| RM09-M02 | Back to edit | Review not submitted | Activate Back | Return to permitted draft, invalidating prepared review if changed | /applications/11111111-1111-1111-1111-111111111111/metadata?panel=edit | RM09-METADATA | Authorization loss removes draft and sensitive state. |
| RM09-M03 | Confirm metadata change | Current target/session and read/write/lookup rechecked | Confirm exact intent | One typed conditional mutation; retain original receipt before waiting | /operations?receipt=demo.metadata.001 | RM09-OPERATION | Lost response is unresolved; rejection/commit/cleanup kept separate; no automatic mutation retry. |
| RM09-M04 | Review current values | Authoritative precondition conflict; draft still permitted | Explicitly fetch current metadata | Show current versus retained draft; new deliberate review uses current ETag/new key | /applications/11111111-1111-1111-1111-111111111111/metadata?panel=conflict | RM09-METADATA | Uncertain earlier intent stays separately recoverable; no silent overwrite or resubmission. |
| RM09-M05 | Cancel editing | No submission in flight | Discard after explicit unsaved-draft decision | Leave without creating a mutation | /applications/11111111-1111-1111-1111-111111111111 | RM09-APPLICATION | Cancelling an already submitted wait is handled by receipt semantics, not rollback. |

#### States

- Edit, review, field validation, concurrent-update review and denied/expired.
- Preparing/submitting prevents duplicate clicks; no invented backend queued/running lifecycle.
- Conflict preserves only permitted draft; committed rejection remains rejected.
- Narrow/keyboard: stacked before/after fields, target above confirmation, persistent
  error summary links to associated inputs; focus returns correctly on discard dialog.

### RM09-OPERATION: Session operation receipts and recovery

#### Description

List bounded minimal receipts from the current operator session and label the list
incomplete. A selected receipt shows original operation, exact target/key, result
source, business decision, original persistence and current-attempt cleanup as
distinct facts. Lost connection/timeout/cancelled wait says **Outcome unresolved**.
Recovery selects the typed metadata/publication/release/assignment lookup for that
original actor/target. NotFound/expiry never proves rollback or permits a new key.
Recovered historical results cannot replace the resource's current state.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-O01 | Open known receipt | Same operator reference and current authorization | Open demo.metadata.001 | Resolve retained operation/target/key and fresh lookup eligibility | /operations?receipt=demo.metadata.001 | RM09-OPERATION | Denied lookup reveals no result; show unresolved with minimal same-user receipt only. |
| RM09-O02 | Check original result | Original unresolved receipt and current lookup allowed | Activate Check | Typed original-key read; bounded polling may follow | /operations?receipt=demo.metadata.001&panel=recovery | RM09-OPERATION | NotFound remains unresolved; lookup cleanup warning does not erase known original commitment. |
| RM09-O03 | Stop checking | An owned lookup/wait exists | Cancel browser waiting | Abort/join browser request; retain original reference and outcome facts | /operations?receipt=demo.metadata.001&panel=unresolved | RM09-OPERATION | Explicitly states that stopping waiting does not cancel/undo the administrative effect. |
| RM09-O04 | View current application | Current target read allowed | Activate current-state link | Authoritative reread separate from recovered historical metadata | /applications/11111111-1111-1111-1111-111111111111 | RM09-APPLICATION | Read denial removes current data; never substitute old receipt as current metadata. |
| RM09-O05 | Continue source/release review | Relevant original publication/release known committed and current reads allowed | Open configuration workspace | Read current source/map state; activation still requires separate review | /configuration?sourceId=33333333-3333-3333-3333-333333333333&panel=release | RM09-CONFIGURATION | Unresolved publication/release cannot be treated as completed or replaced automatically. |
| RM09-O06 | Observe assignment adoption | Original assignment known committed; adoption read allowed | Open Adoption | Read current desired/report relationships | /applications/11111111-1111-1111-1111-111111111111/configuration/adoption | RM09-ADOPTION | Commit is not consumer acceptance; stale/missing reports remain explicit. |

#### States

- Empty session list, submitting browser wait, applied committed, committed rejection,
  notCommitted, unknown, recovered found/replayed and current cleanup warning.
- Lookup revoked: unresolved with no denied result or alternate credentials.
- Receipt survives reload only under finite same-user payload-free policy; no
  configuration payload/credential persists. Sign-out/user switch clears references.
- Narrow/keyboard: facts stack with labels, result persists beyond toast and updates
  are announced; no unsupported Cancel operation action.

### RM09-CONFIGURATION: Configuration sources, publications and releases

#### Description

One workspace switches directory, source editor/history and release panels. Paged
`getDefinitions` exposes authorized common/application source metadata; source
history uses `getSource`, content uses separately authorized `getPublication`.
Show exact definition/schema/source/binding scope and publication IDs. Omitted
defaults mean redacted, not empty. Mode comes only from actual instance reports.

Edit exact bounded JSON as text. The sample common text is
`{"RefreshInterval":"01:00:00","RequestTimeout":"00:00:10"}`; the application
sample uses the maintained SourceUrl and nullable RetryPolicyName fields. Preserve
reviewed UTF-8 text, including numeric lexemes, without normalization or rewrite.
`validate` is structural and non-mutating. Source review can show a local text diff
only from authorized original content; it is distinct from server pair preview.

Publication is immutable and inactive. After publication, choose exact existing
common/application publication references or explicit absence, run `preview`, then
create a coherent release with the current assignment-map ETag. Pair preview gives
paths/kinds/issues, redaction and recipient compatibility observations; it supplies
no before/after values or proof of deployed typed acceptance. Release creation
neither changes assignment nor activates content. P3 must establish complete
scoped read/write/recovery eligibility; implemented feature discovery alone cannot.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-C01 | Open application source | Authorized descriptor from current directory page | Open exact source | Read source precondition/history and separately authorized content | /configuration?sourceId=33333333-3333-3333-3333-333333333333&panel=source | RM09-CONFIGURATION | No source/default values are revealed from metadata-only permission. |
| RM09-C02 | Next source/history page | Opaque cursor returned for same authorized endpoint/scope | Activate Next | Read bounded authorized next page, not all definitions or history | /configuration?sourceId=33333333-3333-3333-3333-333333333333&panel=history&cursor=demo-history-2 | RM09-CONFIGURATION | Cursor/scope change requires fresh read; no hidden totals or silent truncation. |
| RM09-C03 | Validate draft | Source validate authority; bounded exact draft | Activate Validate | Non-mutating validation returns safe issue codes/paths | /configuration?sourceId=33333333-3333-3333-3333-333333333333&panel=validation | RM09-CONFIGURATION | Preserve permitted draft and field/path feedback; no publication/result receipt fabricated. |
| RM09-C04 | Review publication | Valid permitted exact draft; P3 satisfied | Activate Review | Freeze source text/reason/precondition and show exact source scope | /configuration?sourceId=33333333-3333-3333-3333-333333333333&panel=publication-review | RM09-CONFIGURATION | Redacted hidden fields cannot be masked/preserved by republishing; unavailable without safe authorized content. |
| RM09-C05 | Publish exact source | Session/source grant/precondition rechecked | Confirm immutable publication | One original-key `publishSource`; receipt retains original target | /operations?receipt=demo.publication.001 | RM09-OPERATION | Conflict returns to renewed review; uncertain outcome retains key and never auto-retries. |
| RM09-C06 | Preview exact release pair | Authorized existing references or explicit absence; binding preview allowed | Select pair and Preview | Non-mutating server preview of exact pair/current map and bounded recipients | /configuration?applicationId=11111111-1111-1111-1111-111111111111&configurationId=country.settings&schemaVersion=1&panel=release-preview | RM09-CONFIGURATION | Redacted changes labeled; invalid/incompatible references block creation; no guessed effective runtime defaults. |
| RM09-C07 | Create inactive release | Exact reviewed pair/current map ETag and P3 rechecked | Confirm Create release | One original-key immutable release creation; assignment unchanged | /operations?receipt=demo.release.001 | RM09-OPERATION | State/grant change invalidates review; uncertainty requires original release lookup. |
| RM09-C08 | Review canary assignment | Known committed release; P3/P4 and binding reads available | Activate Assign release | Load current complete map and trusted configured cohort IDs | /applications/11111111-1111-1111-1111-111111111111/configuration/country.settings/assignment?intent=replace&releaseId=55555555-5555-5555-5555-555555555555 | RM09-ASSIGNMENT | Missing P4 is explicit; never create a rollout group from arbitrary text or reported labels. |

#### States

- Directory/source/history/editor, validation issues, publication review and release
  pair review reuse one template with clear current step and back path.
- Loading, no authorized sources, no publications, denied content and unavailable differ.
- Redacted preview/defaults, precondition conflict, invalid JSON, payload/depth/response
  bounds and unknown compatibility remain explicit; preserve permitted memory drafts.
- Warn before unsaved navigation; session/grant loss removes protected content.
- Narrow/keyboard: JSON editor, errors and review stack; exact scope stays visible;
  local editor scrolling does not force whole-page horizontal scrolling.

### RM09-ASSIGNMENT: Complete assignment map review

#### Description

Header fixes application/environment, configuration/schema and intent: replace,
promote or rollback. Show current map ETag/generation and proposed default plus
every exact cohort override in one table, including retained rows. The demo map
begins at generation `"41"` with an earlier default and canary override. Canary IDs
come from P4 trusted binding-scoped discovery, never editable membership expressions.
Promotion explicitly chooses retention/removal of canary; rollback uses a verified
known compatible earlier release and preserves unrelated deliberate overrides.
Preview uses the full proposed map; commit reauthorizes and submits one whole-map
`replaceAssignments`, not several unguarded effects. A new accepted decision
advances generation; live adoption remains separate and restart may still be needed.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-A01 | Preview complete map | Exact release/binding and complete draft valid; preview allowed | Activate Preview | Non-mutating preview with current ETag, all map changes and recipient page | /applications/11111111-1111-1111-1111-111111111111/configuration/country.settings/assignment?panel=review | RM09-ASSIGNMENT | Changed map/grants require fresh review; never silently add/drop a cohort pin. |
| RM09-A02 | Edit proposed map | Review not submitted and still permitted | Activate Edit | Return to proposed default/override choices, preserving exact intent | /applications/11111111-1111-1111-1111-111111111111/configuration/country.settings/assignment?panel=edit | RM09-ASSIGNMENT | Target switch invalidates confirmation; unauthorized options/content removed. |
| RM09-A03 | Confirm assignment decision | Current map/session/complete P3 permissions rechecked | Confirm exact replace/promote/rollback map and reason | One original-key guarded whole-map mutation; retain assignment receipt | /operations?receipt=demo.assignment.001 | RM09-OPERATION | Rejected decision/commit/cleanup distinct; conflict retains permitted draft, uncertainty retains original key. |
| RM09-A04 | Cancel map review | No submission in flight | Leave reviewed proposal | Create no assignment or queued command | /applications/11111111-1111-1111-1111-111111111111/configuration/adoption | RM09-ADOPTION | A previously submitted uncertain intent remains in receipts; leaving does not cancel it. |

#### States

- Replace-canary review, promotion with explicit canary retention/removal, rollback
  showing every changed/default/cohort entry and unaffected retained overrides.
- Unassigned map is explicit; no default/generation is fabricated before assignment.
- Missing P3/P4, denied, conflict, stale/unavailable and original-result uncertainty.
- PendingRestart limitation has explanation and no unsupported restart control.
- Narrow/keyboard: each map row stacks current/proposed values with cohort label;
  environment/target precedes Confirm; focus and validation associations persist.

### RM09-ADOPTION: Adoption, promotion and rollback

#### Description

Show desired release/generation alongside reported applied provenance/local
revision, last-successful history where supplied, update mode/state, independent
registration/adoption freshness and observation/receipt time. `getStatus` supplies
current relationships; no payload/effective checksum is invented. Show six demo
rows spanning Pending, Applied, AppliedUnchanged, Rejected, PendingRestart and
Faulted, plus stale/offline and noInstances variants. Historical applied state
for an old target or stale report does not become fresh convergence. No ratio or
fleet count is derived from an incomplete page.

Promotion opens the complete map review. Rollback opens an exact known earlier
release via `getRelease`, verifies definition/schema/environment/binding, then
reviews a new map decision. The selector says **Open known release**; no full
release-history API is implied. Faulted shows historical provenance separately
and never claims safe live last-known-good values. Acceptance does not prove all
in-flight business operations or subscriber external effects changed together.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-R01 | Refresh adoption | Current adoption read allowed; one request per resource | Activate Refresh | Read current desired/report relationships with original observation times | /applications/11111111-1111-1111-1111-111111111111/configuration/adoption | RM09-ADOPTION | Mark valid-session prior snapshot stale; initial failure is unavailable, not zero recipients. |
| RM09-R02 | Promote selected release | Compatible known release; P3/P4 and fresh reads available | Choose promotion | Open full-map review showing canary retention/removal explicitly | /applications/11111111-1111-1111-1111-111111111111/configuration/country.settings/assignment?intent=promote&releaseId=55555555-5555-5555-5555-555555555555 | RM09-ASSIGNMENT | No automatic health-based promotion; denied/missing contract blocks this action. |
| RM09-R03 | Open known earlier release | Exact known ID and authorized metadata lookup | Enter earlier release ID and Open | `getRelease` verifies original references and actual target compatibility | /applications/11111111-1111-1111-1111-111111111111/configuration/adoption?releaseId=44444444-4444-4444-4444-444444444444&panel=rollback-candidate | RM09-ADOPTION | Missing, incompatible or inaccessible release cannot be selected; no guessed history or UUID ordering. |
| RM09-R04 | Review rollback | Verified earlier release and current binding/map authority | Activate Review rollback | Open explicit rollback map preserving unrelated deliberate overrides | /applications/11111111-1111-1111-1111-111111111111/configuration/country.settings/assignment?intent=rollback&releaseId=44444444-4444-4444-4444-444444444444 | RM09-ASSIGNMENT | Default-only rollback is not described as rolling back overriding cohorts; restart limitation stays visible. |
| RM09-R05 | Next recipient page | Authorized opaque continuation returned | Activate Next | Read bounded current recipient page under unchanged scope | /applications/11111111-1111-1111-1111-111111111111/configuration/adoption?cursor=demo-recipient-2 | RM09-ADOPTION | Invalid/changed cursor requires fresh query; never call partial pages fleet convergence. |

#### States

- All six known adoption states, unknown future state/mode, old desired provenance,
  independent stale/offline facets and no observed recipients are separate.
- Rejected exposes safe candidate code and previous acceptance; notification failure
  is separate from content acceptance; Faulted cannot assert live-state validity.
- Session/grant loss removes protected rows; management outage dates retained rows,
  disables new changes and queues nothing; reconnect revalidates before fresh action.
- Desktop table and narrow labeled row cards/local scroll preserve target, source
  timestamps, focus, text status and access to persistent results.

## Journey and requirement coverage

| Requirement/exclusion | Screen IDs | Action IDs | States | Notes |
| --- | --- | --- | --- | --- |
| RM09-AC01 | All eight | No runtime approval action | Draft, pending review | Versioned linked rendered package plus exact API map required; production remains gated. |
| RM09-AC02 | All eight | No runtime approval action | Unchanged approval/resume; material revision | CIS records exact design approval atomically and retains it; this draft grants none. |
| RM09-AC03 | RM09-SIGNIN | RM09-S01, RM09-S02 | Human sign-in, auth failure | P1 independently validates operator/delegation; no broad portal identity privilege. |
| RM09-AC04 | All protected screens | RM09-L03, RM09-M03, RM09-O02, RM09-A03 | Unauthorized target/cursor/reference | Server and underlying API independently authorize exact resolved resource. |
| RM09-AC05 | RM09-APPLICATION, RM09-CONFIGURATION, RM09-ADOPTION | RM09-D03, RM09-C06, RM09-R01 | Missing/incompatible/unknown | No executable server UI schema or unknown-state success fallback. |
| RM09-AC06 | RM09-APPLICATIONS | RM09-L01, RM09-L02 | Search/page/query reset | P2 required; authorized environments and global search cannot be inferred from first page. |
| RM09-AC07 | RM09-APPLICATION | RM09-D01, RM09-D02 | Reported/verified, stale, historical | No workload-self control, lease transfer or admission synthesis. |
| RM09-AC08 | RM09-METADATA | RM09-M01, RM09-M03, RM09-M04 | Review, conflict, new intent | Only display metadata; omitted clear semantics and strong ETag preserved. |
| RM09-AC09 | RM09-METADATA, RM09-OPERATION | RM09-M03, RM09-O01, RM09-O02 | Response lost/reload, recovered commitment | Original key/actor/target and separate original decision/commit/current cleanup. |
| RM09-AC10 | RM09-SIGNIN, RM09-OPERATION, all protected screens | RM09-N04, RM09-O02 | Expired, switched user, revoked lookup | Clear protected data; only permitted same-user minimal unresolved receipt remains. |
| RM09-AC11 | RM09-CONFIGURATION | RM09-C03, RM09-C05, RM09-C06, RM09-C07 | Validation, publish, inactive release | Exact JSON preserved; immutable publication and release creation do not assign. |
| RM09-AC12 | RM09-METADATA, RM09-CONFIGURATION, RM09-ASSIGNMENT | RM09-M04, RM09-C05, RM09-A01, RM09-A03 | Post-preview conflict/denial | Renew review against current precondition; no automatic resubmission. |
| RM09-AC13 | RM09-ASSIGNMENT, RM09-ADOPTION | RM09-A01, RM09-A03, RM09-R02, RM09-R04 | Promote, retained/removed canary, rollback | Whole-map effects explicit; rollback generation increases and immutable content stays historical. |
| RM09-AC14 | RM09-ADOPTION | RM09-R01, RM09-R05 | Six states, stale/offline, noInstances | No zero-recipient success or incomplete/stale fleet convergence ratio. |
| RM09-AC15 | RM09-APPLICATIONS, RM09-APPLICATION, RM09-ADOPTION | RM09-L04, RM09-R01 | Dated outage/reconnect | New changes disabled; no offline queue or replayed click. |
| RM09-AC16 | RM09-CONFIGURATION, RM09-OPERATION | RM09-C01, RM09-C04, RM09-C06 | Metadata-only/redacted/denied | Sensitive content absent before browser delivery; mask never republished. |
| RM09-AC17 | RM09-APPLICATION, RM09-CONFIGURATION, RM09-OPERATION | RM09-D02, RM09-C05, RM09-O02 | Large Int64, unknown additions, response bound | Exact strings, ETags, seven-fraction timestamps and trusted prefixed server URL retained. |
| RM09-AC18 | All eight | All declared user controls | Desktop/narrow/zoom, keyboard/focus/errors | Pictures express intent; actual behavior still needs implementation verification. |
| RM09-AC19 | RM09-APPLICATIONS, RM09-OPERATION, RM09-CONFIGURATION, RM09-ADOPTION | RM09-L02, RM09-O03, RM09-C02, RM09-R01 | Bounds, hidden view, cancel wait | Finite caches/drafts/receipts/polling; cancellation or restart never claims effect rollback. |
| RM09-AC20 | All eight | All principal journey actions | Real delivered API acceptance | Synthetic review does not close RM09; final real-service/browser acceptance still required. |
| Initial exclusions | All eight | No excluded action exists | Inspection only where control absent | No restart, scheduling, arbitrary execution, log/metric store, Audit archive, permission editor, automatic rollout or fleet transaction. |

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Pending Approval | No human decision recorded | Not decided | Not sealed; CIS will bind the exact review revision | Draft only. One explicit decision must cover RM09 portal UI direction and exact rendered design before production UI/task import. No existing authority is changed by this record. |
