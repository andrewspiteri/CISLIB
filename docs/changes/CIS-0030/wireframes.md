---
title: "CIS-0030 textual wireframes — R07"
type: textual-wireframes
status: Approved
change_id: CIS-0030
design_revision: R07
approval_status: Approved
authority: current-user-scope
review_authority: pending-exact-screenshot-review
frontend_type: backoffice
audience: Desktop operators only
---

# Textual wireframes — R07

R07 completes the original RM09 operator journeys and the required RM10 v1.1
integration, correcting Capabilities semantics and the prominence of technical
identifiers. All screens are **backoffice**, for **desktop browser operators
only**, at the approved 1600 × 1000 reference viewport. There is no mobile scope.
Thirteen primary frames and four critical supporting frames cover the changed
workflows. No picture, textual entry or prerequisite label claims a delivered
backend or passing native proof.

The governing sources are the preserved
[RM09 specification](../../../planning/management/portal/sources/CISLIB_RM09_Management_Portal_Specification_v1.0.md)
(SHA-256 `1574ec2624247a95945b68f318e67d0ea4595b8fbd8e116eff149059ad502633`),
[RM10 v1.1 specification](../../../planning/scheduler/sources/CISLIB_RM10_Application_Scheduler_and_Triggers_Specification_v1.1.md)
(SHA-256 `cbb75ceec89564cefeebb003ab51eed565c2bbcad4e18235954accad97c36510`),
the current explicit user corrections and the unchanged
[accepted R06 wireframes](../CIS-0029/wireframes.md). Source imperatives are
requirements, not independent execution permission. R02–R06 decisions/assets
remain historical evidence and must not be edited by this revision.

The current derived CIS-0030 allocation is 120 shared active hours and 128 logical
attempts, including three restoration hours, under the existing physical 6 GiB
pool and 64 MiB reserve. The earlier 96-hour draft is superseded without resetting
costs; no independent human numeric approval is claimed. This document grants
no native execution. Finite eligible owners, the source plan and the applicable
design decision remain prerequisites. Only uncovered visual choices require the
user's screenshot approval; unchanged accepted layout/backend work is not a
reason to ask the same approval again.

## Screen inventory

Paths below are proposed UI navigation contracts. Braced values bind exact
authorized opaque identifiers, never display names. Typed native HTTP routes are
reconciled separately; a screen route does not establish a backend capability.

| Screen ID | Frontend type | Name | Route/path | Platform | Actors/access | Entry points | Purpose |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-APPLICATIONS | backoffice | Applications reported to Management | /applications | Web desktop only | Operator with bounded authorized application reads | Sign-in, global Applications, Back | Preserve accepted server-filtered list; names/environment first, identifiers under Technical details. |
| R07-INSTANCES | backoffice | Application instances | /applications/{applicationId}/instances | Web desktop only | Exact application and instance reads | Application selection, Instances tab | Preserve default Instances, per-registration versions and dated presence. |
| R07-CAPABILITIES | backoffice | Application capability catalog | /applications/{applicationId}/capabilities | Web desktop only | Exact logical feature/definition/instance reads | Capabilities tab, direct authorized link | Registered schedulable jobs, RuleEngine instances and trigger providers; API diagnostics secondary. |
| R07-CONFIGURATION-LIST | backoffice | Configuration definitions | /applications/{applicationId}/configurations | Web desktop only | Exact application/environment definition reads | Configuration tab, editor return | Preserve scoped list; open permitted content/edit/adoption flows by exact definition. |
| R07-CONFIGURATION-EDITOR | backoffice | Edit configuration | /applications/{applicationId}/configurations/{configurationId}/edit | Web desktop only | Scoped values/defaults/edit and recovery permissions | Definition panel, supported Job/Trigger configuration | Known typed controls, inheritance, bounded supported JSON and safe in-memory draft. |
| R07-CONFIGURATION-REVIEW | backoffice | Review publication and release | /applications/{applicationId}/configurations/{configurationId}/review | Web desktop only | Actual preview/publication/release and original-result permissions | Review changes, return from conflict | Exact reviewed content, separate immutable publication and coherent source-pinned release. |
| R07-ASSIGNMENT | backoffice | Review assignment map | /applications/{applicationId}/configurations/{configurationId}/assignment | Web desktop only | Exact assignment/cohort/release reads, map mutation and recovery | Published release, compatible prior release | Canary, promotion or rollback as one guarded application map decision. |
| R07-ADOPTION | backoffice | Configuration adoption | /applications/{applicationId}/configurations/{configurationId}/adoption | Web desktop only | Exact current assignment and authorized instance adoption reads | Assignment completion, configuration details | Desired/accepted provenance, local revision and distinct adoption/freshness facts. |
| R07-SCHEDULED-JOBS | backoffice | Scheduled tasks and registered jobs | /applications/{applicationId}/jobs | Web desktop only | Authorized task catalog and reported-job reads; commands independent | Scheduled jobs tab, Task Back | Named task discovery and retained registered-job inspection. |
| R07-TASK-FULL | backoffice | Scheduled Task editor | /applications/{applicationId}/tasks/{taskId} | Web desktop only | Exact task/configuration/definition reads; changes/recovery independent | Task panel, related Job/Event, catalog creation | Main association/resource/input mapping above exactly Trigger and Job tabs; History separate. |
| R07-TASK-PREVIEW | backoffice | Whole-task preview | /applications/{applicationId}/tasks/{taskId}/preview | Web desktop only | Full draft preview/impact and independently permitted mutations | Preview whole task from either contextual tab | Side-effect-free coherent review, shared impact and actual apply/adoption milestones. |
| R07-RUN-HISTORY | backoffice | Scoped logical runs and attempts | /applications/{applicationId}/runs | Web desktop only | Exact task/job/event/resource history reads; detail authorization independent | Task History, Job Run history, Event runs | One durable run history with expanded attempts and original-result/build attribution. |
| R07-JOB-FULL | backoffice | Job details and configuration | /applications/{applicationId}/jobs/{executionScope}/{jobId} | Web desktop only | Exact Job/definition/report reads; defaults/history permissions independent | Job panel, task/catalog/history link | Preserve Overview/Run history/Configuration; separate defaults from task invocation input. |
| R07-EVENT-FULL | backoffice | Immutable Event details | /applications/{applicationId}/events/{eventId} | Web desktop only | Exact event/captured-content reads; produced runs independently authorized | History Event link, scoped direct link | Captured occurrence/configuration references and original produced runs, never current-setting substitution. |
| R07-METADATA | backoffice | Edit display metadata | /applications/{applicationId}/metadata/edit | Web desktop only | Exact metadata read/write and original-result recovery | Supported application Edit display metadata | Display name and optional description only, strong ETag and omission-to-clear. |
| R07-OPERATION | backoffice | Original operation result | /applications/{applicationId}/operations/{receiptId} | Web desktop only | Original actor/target/operation lookup permissions | Submitted mutation receipt, reload/reconnect | Actual partial/unknown/committed/rejected facts and unchanged-intent recovery. |
| R07-CONFLICT | backoffice | Reconcile concurrent edit | /applications/{applicationId}/changes/{draftId}/reconcile | Web desktop only | Current target read and original draft content permissions | Strong precondition conflict | Preserve permitted draft, compare current authority, explicitly review deliberate new intent. |

## Shared interaction and data rules

Retain the exact R06 200 px dark left rail with CIS/Management and its sole
Applications destination, together with the Management header, white/#F8FAFC
surfaces, Inter, existing blue controls, 8 px rhythm and text-labelled statuses.
Application detail keeps exactly **Instances / Configuration / Capabilities /
Scheduled jobs**. Entity breadcrumbs sit beneath this context. Do not add
multi-area rail destinations, replace the accepted shell or add inactive areas.

Names, environment and meaningful configuration/build versions lead. The example
heading is **Country data service**, subtitle **Demo staging · Demo.CountryData**.
UUIDs, exact registration IDs, hashes and operation keys are in collapsed
**Technical details**, with explicitly named Copy buttons and announced feedback.
Names remain editable display data; exact keys continue selecting/authorizing all
targets. Duplicate names receive an owner/scope disambiguator. A missing original
build remains Not reported, not the recovering worker's build.

Every preview says **DEMONSTRATION — synthetic data**. Snapshot:
`2026-11-25T10:00:00.0000000Z` (11:00 CET). Example: Country data service /
Demo.CountryData / Demo staging; Customer dormancy; Daily customer check; Daily
calendar owned by Central Management; Europe partition. Active Task revision 7
and desired revision 8 are different facts. Production must have real typed
backends: no mock fallback, placeholder history or manufactured success.

Keep application/environment/query/cursor/scroll/focus and exact selected report
on Back. Filter changes clear query-bound cursors. Superseded requests are
cancelled and joined; late data cannot cross targets/users. Each read/page/lookup,
content/default, input/result and write/recovery action reauthorizes its actual
resource. Advisory capability hints are not authority. A write journey is not
enabled when its required original-result lookup permission is absent.

Drafts remain caller-owned in memory by default. Dirty navigation warns; browser
persistence receives no sensitive draft by default. Redacted content is never an
editable literal or submitted mask. Preserve unrelated members/inheritance only
under the actual owner contract; if hidden content cannot safely be preserved,
editing is unavailable or requires an authorized editor. Escaped opaque data is
bounded text/JSON, never HTML, script, CLR selectors or arbitrary lookup URLs.

Publication, coherent release, assignment, owner adoption and executable Task
activation remain distinct milestones. Every underlying mutation retains its
own original intent/key/target/actor and exact preconditions before submission.
There are no automatic mutation retries, offline command queues, hidden commits
or distributed-transaction claims across owners. NotFound, timeout, cancelled
wait or absent optional history do not establish rollback or permit a new key.
An original committed result remains committed when a later lookup's cleanup
fails; expose the independent diagnostic without rewriting the outcome.

### Native source prerequisites

| Prerequisite | Required native source / owner | Screens affected | Completion limit |
| --- | --- | --- | --- |
| R07-B01 | Existing real Auth.js/session boundary and independently validated RM01 human/delegated Management identity | All protected screens | Controlled local proof is not live Entra/deployment qualification; no production demo login. |
| R07-B02 | Additive trusted logical feature/form catalogs plus separate authorized per-instance support/version observations in Management and C#/TypeScript clients | Capabilities, Task, Job, Configuration | Registration/presence/opaque inventory cannot establish schema authority; no new RuleEngine evaluator or DSL. |
| R07-B03 | Existing RM08 getSource/getPublication/publication/release/map/adoption authority and needed bounded typed lookup/impact/cohort companions | Configuration, assignment, adoption, Task preview | Page exact sources through GET configuration/sources/{sourceId}; authorized immutable content through GET configuration/publications/{publicationId}; no second store or browser merge authority. |
| R07-B04 | RM10 authoritative PostgreSQL trigger/task/event/run operations, exact revision/precondition/recovery/Audit integration | Task, preview, Event, operations | Real schema migration, calendar evaluator, durable admission and existing Jobs dispatch are required; a screen is not their implementation. |
| R07-B05 | RM10 durable scoped history and actual worker build/handler/execution/recovery compatibility evidence | History, Job, Task Job tab | No history reconstructed from logs/metrics; no replacement lease or generic checkpoint engine. |
| R07-B06 | RM10 UI01–06 owner descriptions with finite known components and registered bounded deterministic mappers/lookups | Editors and previews | No executable schema, arbitrary service URL, custom scripting or browser date/default authority. |
| R07-B07 | Existing native owner cancellation, sequential startup, original transaction/Operations/Messaging/Audit and provider drain | Every real command and runtime workflow | No new DI/auth/transaction/test framework; native callback/scope/resource settlement is required. |

### Shared actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-N01 | Applications / Back to applications | Safe navigation context | Navigate | Restore permitted list query/cursor/focus; no mutation | /applications | R07-APPLICATIONS | Recheck session/read permission; clear denied content. |
| R07-N02 | Instances tab | Exact selected application | Select | Default per-registration inspection | /applications/{applicationId}/instances | R07-INSTANCES | Loading/empty/unavailable remain distinct. |
| R07-N03 | Configuration tab | Exact selected application | Select | Scoped definition list | /applications/{applicationId}/configurations | R07-CONFIGURATION-LIST | No inferred common membership or content access. |
| R07-N04 | Capabilities tab | Exact selected application | Select | Authorized logical feature catalog | /applications/{applicationId}/capabilities | R07-CAPABILITIES | Missing catalog is unavailable, not an empty successful capability set. |
| R07-N05 | Scheduled jobs tab | Exact selected application | Select | Tasks and separately labelled registered/reporting jobs | /applications/{applicationId}/jobs | R07-SCHEDULED-JOBS | No current schedule inferred from telemetry. |
| R07-G03 | Sign in / Sign out | Existing maintained authentication routes | Authenticate or sign out | Ordinary library CSRF/OIDC/session flow; clear previous protected state | /api/auth/signin | External existing maintained authentication entry | No token injection, workload fallback, account linking or cross-user retained draft. |

The following in-place path map defines the shared Technical details/Copy and
read-only-panel Close/Escape controls. Copy requires the exact field's disclosure
permission, changes no target and announces feedback. Close preserves
query/report/scroll and restores origin focus; a removed row uses the heading.
Denied content clears immediately and is never copied. Each row applies only
to controls actually present on that screen; it creates no new details read.

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-G01-L | Technical details / Copy / Close details | Permitted selected list record | Inspect/copy/dismiss | In-place only; preserve list/focus | /applications | R07-APPLICATIONS | Shared disclosure/close rules above. |
| R07-G01-I | Technical details / Copy / Close details | Permitted selected registration | Inspect/copy/dismiss | In-place only; preserve exact registration | /applications/{applicationId}/instances | R07-INSTANCES | Shared disclosure/close rules above. |
| R07-G01-C | Technical details / Copy / Close details | Permitted definition | Inspect/copy/dismiss | In-place only; preserve exact definition | /applications/{applicationId}/configurations | R07-CONFIGURATION-LIST | Shared disclosure/close rules above. |
| R07-G01-K | Technical details / Copy / Close details | Permitted logical feature | Inspect/copy/dismiss | In-place only; preserve exact feature/version | /applications/{applicationId}/capabilities | R07-CAPABILITIES | Shared disclosure/close rules above. |
| R07-G01-CE | Technical details / Copy | Permitted source/draft field | Inspect/copy | In-place only; preserve exact draft | /applications/{applicationId}/configurations/{configurationId}/edit | R07-CONFIGURATION-EDITOR | Never copy protected/redacted value. |
| R07-G01-CR | Technical details / Copy | Permitted reviewed field | Inspect/copy | In-place only; preserve exact reviewed intent | /applications/{applicationId}/configurations/{configurationId}/review | R07-CONFIGURATION-REVIEW | Never copy protected/redacted value. |
| R07-G01-A | Technical details / Copy | Permitted map/reference | Inspect/copy | In-place only; preserve exact application map | /applications/{applicationId}/configurations/{configurationId}/assignment | R07-ASSIGNMENT | Shared disclosure rules above. |
| R07-G01-D | Technical details / Copy / Close details | Permitted adoption observation | Inspect/copy/dismiss | In-place only; preserve exact instance/observation | /applications/{applicationId}/configurations/{configurationId}/adoption | R07-ADOPTION | Shared disclosure/close rules above. |
| R07-G01-SJ | Technical details / Copy / Close details | Permitted task/job row | Inspect/copy/dismiss | In-place only; preserve exact selection | /applications/{applicationId}/jobs | R07-SCHEDULED-JOBS | Shared disclosure/close rules above. |
| R07-G01-T | Technical details / Copy | Permitted task/reference | Inspect/copy | In-place only; preserve Task draft | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | Shared disclosure rules above. |
| R07-G01-TP | Technical details / Copy | Permitted preview/reference | Inspect/copy | In-place only; preserve whole review | /applications/{applicationId}/tasks/{taskId}/preview | R07-TASK-PREVIEW | Shared disclosure rules above. |
| R07-G01-H | Technical details / Copy / Close details | Permitted run/attempt detail | Inspect/copy/dismiss | In-place only; preserve exact run/attempt | /applications/{applicationId}/runs | R07-RUN-HISTORY | Shared disclosure/close rules above. |
| R07-G01-J | Technical details / Copy | Permitted Job/reference | Inspect/copy | In-place only; preserve exact scoped Job | /applications/{applicationId}/jobs/{executionScope}/{jobId} | R07-JOB-FULL | Shared disclosure rules above. |
| R07-G01-E | Technical details / Copy | Permitted Event/reference | Inspect/copy | In-place only; preserve immutable Event | /applications/{applicationId}/events/{eventId} | R07-EVENT-FULL | Shared disclosure rules above. |
| R07-G01-M | Technical details / Copy | Permitted metadata field/reference | Inspect/copy | In-place only; preserve metadata draft | /applications/{applicationId}/metadata/edit | R07-METADATA | Shared disclosure rules above. |
| R07-G01-O | Technical details / Copy | Permitted minimal original receipt | Inspect/copy | In-place only; preserve original operation | /applications/{applicationId}/operations/{receiptId} | R07-OPERATION | Lookup loss preserves only permitted same-user minimal receipt. |
| R07-G01-X | Technical details / Copy | Permitted current/draft reference | Inspect/copy | In-place only; preserve reconciliation | /applications/{applicationId}/changes/{draftId}/reconcile | R07-CONFLICT | Shared disclosure rules above. |

### Shared states and lifetime

The unchanged human authentication entry is an external existing UI dependency,
not an R07 screen to redesign or re-approve. Reuse the delivered maintained
library/session integration and native validation: fixed provider/tenant/client,
CSRF, state/nonce/PKCE and opaque database session. Server tokens remain server
side; production rejects fixture/development identities. Session expiry, sign-out
or identity switching clears old protected data and permits ordinary sign-in.

The following states apply to every screen in the inventory, in addition to its
specific state table. Opening a named modal moves/traps focus and makes its
background inert; Close/Escape restores focus. Semantic headings/tables, labels,
keyboard activation, zoom, contrast, non-color status and persistent announced
feedback remain requirements. Pictures do not establish accessibility.

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-G-LOADING | Initial read pending | Named scoped loading; no old target data, fabricated zeros or optimistic success. |
| R07-G-EMPTY | Authoritatively empty | Clear scoped empty meaning and only actually supported next action. |
| R07-G-STALE | Refresh failed with permitted same-scope snapshot | Retain original dated observation/receipt and last successful fetch; disable new mutations. |
| R07-G-UNAVAILABLE | Initial service/authority unavailable | Safe unavailable/Retry; no implied application health or successful empty result. |
| R07-G-DENIED | Session/permission lost | Clear affected values, panels, confirmations and requests; retain only the permitted same-user minimal receipt. |
| R07-G-INCOMPATIBLE | Unsupported contract or unknown state | Safe incompatible/unknown text; never default to Allowed, Succeeded or Applied. |
| R07-G-RECONNECT | Connection restored | Refresh session, capabilities, current target and preconditions before enabling a new write; no queued click replay. |
| R07-G-STOPPING | UI/request/native owner closes | Abort and join owned work, preserve submitted original receipt, distinguish cancelled wait from actual command/callback settlement. |

Polling stays bounded, one request per resource, with hidden-view pause/backoff
and finite fast lookup polling; it is not a managed-admission lease. Runtime
startup stays sequential. Cancellation/lost lease/timeout is an observation or
request, never proof that code stopped or business effects rolled back.

## Screen definitions

### R07-APPLICATIONS: Applications reported to Management

#### Description

Preserve the accepted filterable list and server-side name/identity/environment
query, authorized environment paging and exact selection. Names/environment lead;
exact keys are available under Technical details. Do not imply verified live
deployments, fleet totals, a shared current version or global loaded-page search.
No additional R07 primary picture re-approves the unchanged layout.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-L01 | Apply/Clear filters; Previous/Next; Refresh | Authorized bounded query | Filter/page/refresh | Reset incompatible cursor; query server, not loaded-page scan | /applications | R07-APPLICATIONS | Preserve scoped error/empty; stale responses cannot replace new query. |
| R07-L02 | Application name | Exact authorized row | Open application | Retain return context; default Instances | /applications/{applicationId}/instances | R07-INSTANCES | No selection by display name; denied target remains denied. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-L-PRIMARY | Populated list | Accepted bounded Applications list; exact keys remain accessible. |
| R07-L-NOMATCH | No matches | Clear filters action; no fabricated deployment result. |

### R07-INSTANCES: Application instances and reported versions

#### Description

Preserve default Instances and the accepted selected-row panel. Display each
reported instance name, exact reported application version, presence, lifecycle,
health and source times without turning labels into verified identity. Required
version and optional Not reported facts remain distinct. Use shared tab actions.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-I01 | Instance name; bounded paging/Refresh | Authorized exact registration row/query | Inspect or refresh | Open retained-row panel; keep registration/version/source time | /applications/{applicationId}/instances | R07-INSTANCES | Never rebind by row index/name; access loss clears details. |
| R07-I02 | Edit display metadata | Actual write plus original-result permissions | Edit | Load current strong metadata ETag | /applications/{applicationId}/metadata/edit | R07-METADATA | Read permission or advisory hint alone cannot enable write. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-I-PRIMARY | Observed registrations | Per-registration versions and independent freshness. |
| R07-I-DETAILS | Exact row selected | Existing right-side panel, identifiers collapsed, Close/Escape/focus return. |
| R07-I-EMPTY | No retained registrations | Honest empty; not successful rollout or verified no deployment. |

### R07-CONFIGURATION-LIST: Application configuration definitions

#### Description

Keep the existing scoped list and panel. Show names, schema, owner/update mode,
available source/configuration/adoption operations; never infer content/default
access from discovery. Common and application sources remain explicitly distinct.
Supporting S04 shows the names-first list with **Publication history** in the
selected definition's existing side panel. Choose only the independently
authorized common/application source actually bound to that definition. The
existing typed `getSource(sourceId, page)` reads bounded immutable publication
metadata pages; `getPublication(publicationId)` reads selected authorized content.
The actual routes are `GET configuration/sources/{sourceId}` and
`GET configuration/publications/{publicationId}` under the configured Management
prefix. History is not another store or global screen. Each source has its own
cursor. Names/scope/schema, created time, safe actor and reason lead; immutable
IDs and digest are in Technical details. Opening a publication never edits it.
Hidden/unavailable content is not empty JSON, absence or a default reset.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-C01 | Definition name; Refresh/paging | Exact authorized definition/query | Inspect | Retained metadata panel and bounded scoped reads | /applications/{applicationId}/configurations | R07-CONFIGURATION-LIST | Unavailable content is not absence/default; scoped empty remains explicit. |
| R07-C02 | Edit configuration | Actual values/edit/recovery permission and supported schema | Edit | Start memory draft from exact source/revision | /applications/{applicationId}/configurations/{configurationId}/edit | R07-CONFIGURATION-EDITOR | Hidden unsafe-to-preserve content prevents edit, never becomes a mask. |
| R07-C03 | View adoption | Actual assignment/adoption read | Inspect | Resolve current desired assignment and observations | /applications/{applicationId}/configurations/{configurationId}/adoption | R07-ADOPTION | No inferred acceptance from publication or old success label. |
| R07-C04 | Publication history / Common or Application source / Previous/Next | Exact independently authorized binding/source history read | Inspect/page | Existing getSource returns bounded immutable metadata for that exact source | /applications/{applicationId}/configurations | R07-CONFIGURATION-LIST | No global scan, inferred common binding or unauthorized actor/content disclosure. |
| R07-C05 | View immutable publication content | Exact selected publication/source/schema content read | Inspect | Existing getPublication returns authorized exact immutable payload/references in same panel | /applications/{applicationId}/configurations | R07-CONFIGURATION-LIST | Redacted/unavailable is not editable literal or empty content; no new detail store. |
| R07-C06 | Use this exact publication reference | Publication belongs to exact authorized binding/scope/schema; release review permitted | Select for review | Retain immutable common or application reference in current release draft, without publishing or assigning | /applications/{applicationId}/configurations/{configurationId}/review | R07-CONFIGURATION-REVIEW | Mismatched kind/application/environment/schema rejected; no latest-value substitution. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-C-PRIMARY | Scoped definitions | Existing names-first list with independent allowed operations. |
| R07-C-READONLY | Discovery only | Metadata readable; values/edit controls unavailable truthfully. |
| R07-C-HISTORY | Selected source publication history | Existing panel shows scoped Common/Application choice, bounded metadata pages and authorized immutable content/reference selection. |

### R07-CAPABILITIES: Application feature catalog

#### Description

Frame F01. Main heading Capabilities, description Registered features available
to this application. Three bounded sections: **Schedulable job types** (Customer
dormancy, Country reference refresh), **Registered RuleEngine instances**
(Country eligibility rules), **Trigger providers** (configurable Calendar owned
by Central Management; fixed Customer events source owned by the application).
Rows lead with Name, Owner, supported version, configuration and actual operations.
Logical registered definitions and observed participating instances are distinct.
Registration cannot create executable handlers or schema/authorization authority.

RuleEngine entries are typed reported/registered feature/configuration inspection,
not a rule designer, new evaluator or scripting language. Providers declare
Create/Edit/Inspect support and emitted contract. Configuring Calendar creates a
named supported source, not a provider implementation. Collapsed **Service API
details** retains existing Management API descriptors and advisory caller hints;
they are secondary diagnostics, not the application capability catalog itself.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-K01 | Feature name; Refresh/paging | Actual scoped logical catalog/instance reads | Inspect | Open typed retained-row panel with declared support and configuration | /applications/{applicationId}/capabilities | R07-CAPABILITIES | Missing/unknown data remains unavailable/unknown, never fabricated from opaque inventory. |
| R07-K02 | Job View full details | Exact authorized Job relationship | Navigate | Same job identity and return context | /applications/{applicationId}/jobs/{executionScope}/{jobId} | R07-JOB-FULL | No name-based target inference. |
| R07-K03 | Create Scheduled Task / Configure supported provider | Actual supported operation, write and recovery permissions | Start association | Open Task draft with registered identity preselected; supported named trigger creation remains in that context | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | Fixed/inspect-only providers have no create control; executable handlers are never cloned. |
| R07-K04 | Service API details | API diagnostic read permitted | Expand | Inspect original descriptors/hints only | /applications/{applicationId}/capabilities | R07-CAPABILITIES | Not evaluated differs from Allowed/Denied; no generic API invocation. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-K-CATALOG | Three feature groups | Names/configuration/version first; APIs and exact IDs collapsed. |
| R07-K-CONFLICT | Conflicting registered definition/version | Explicit conflict; later instance does not replace existing schema/editor. |
| R07-K-READONLY | Inspect-only fixed feature | Permitted metadata/configuration only; unsupported action absent. |

### R07-CONFIGURATION-EDITOR: Typed configuration and supported JSON

#### Description

Frame F02. Customer settings heading, application/environment, schema, owner,
update mode and exact authorized Common source or Application override selection.
Known controls cover text, number, Boolean, enumeration, duration and grouped
fields; examples batch size 250, inactivity period 90 days, Summary mode, Enabled.
Inherited rows identify their actual source. Reset uses real omission semantics.
Only supplied authorized captured defaults appear. Supported bounded JSON fallback
shows actual byte/depth limits and field errors; it executes no server metadata.
Exact reviewed bytes/large numbers are retained, not rewritten by display parsing.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-CE01 | Source scope; typed fields; JSON; reset override | Actual target/schema/content permission | Edit | Memory draft bound to selected definition/schema/source; preserve unrelated members | /applications/{applicationId}/configurations/{configurationId}/edit | R07-CONFIGURATION-EDITOR | No hidden-field deletion, mask publication, unsafe source switching or stale schema carryover. |
| R07-CE02 | Validate | Side-effect-free supported validation | Validate | Browser declared rules then Management structural/owner validation where available | /applications/{applicationId}/configurations/{configurationId}/edit | R07-CONFIGURATION-EDITOR | Error locations; unavailable owner validation is pending/refused, not semantic success. |
| R07-CE03 | Review changes | Required draft rules resolved; preview read allowed | Review | Capture exact payload/intent and authorized impact without publication | /applications/{applicationId}/configurations/{configurationId}/review | R07-CONFIGURATION-REVIEW | Missing permission/validation blocks review/commit appropriately. |
| R07-CE04 | Discard / Back | Draft exists | Confirm dirty navigation | Discard only after explicit choice; no backend mutation | /applications/{applicationId}/configurations | R07-CONFIGURATION-LIST | Remain editing if discard declined. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-CE-EDIT | Supported editable values | Names/scope/inheritance and finite typed controls before JSON. |
| R07-CE-INVALID | Declared validation failure | Announced inline fields; keep draft; publish disabled. |
| R07-CE-PROTECTED | Hidden/redacted or unsafe-to-preserve values | Read-only/unavailable operation or authorized-editor requirement, never editable mask. |
| R07-CE-DIRTY | Leaving with unsaved changes | Confirm discard or stay; no default persistent draft store. |

### R07-CONFIGURATION-REVIEW: Immutable publication and coherent release

#### Description

Frame F03. Before/after additions/removals/overrides, exact scope/schema, safe
validation and authorized impact lead. Expand exact payload without rewriting.
Summary separates new immutable source publication, release pinning exact common
and application references/explicit absence, and **Not assigned**. Names and
meaningful revisions lead; IDs/digests are in Technical details. Publication and
release are separate original operations, never an activation or distributed
transaction. Immutable history remains read-only.
Exact common/application references can be selected from the authorized source
history panel. Each choice remains pinned to its source/kind/schema/environment
and application binding; opening history never silently substitutes latest.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-CR01 | Back to draft / exact payload | Same permitted draft | Inspect/revise | Preserve content and target; new review after changes | /applications/{applicationId}/configurations/{configurationId}/edit | R07-CONFIGURATION-EDITOR | Recheck content permission; no hidden data leakage. |
| R07-CR02 | Publish and create release | Actual publication/release plus recovery permissions; exact review/current preconditions | Confirm target and reviewed intent | Retain distinct original keys before separate immutable native operations | /applications/{applicationId}/operations/{receiptId} | R07-OPERATION | No automatic mutation retry; rejection/unknown/partial completion stay separate. |
| R07-CR03 | Review assignment | Exact coherent release known committed and readable | Continue | Review application-specific whole map; no implicit assignment | /applications/{applicationId}/configurations/{configurationId}/assignment | R07-ASSIGNMENT | Unknown release outcome remains recoverable first. |
| R07-CR04 | Choose exact Common/Application publication | Actual bound source/publication read permitted | Open scoped history | Select immutable reference in existing Configuration panel and return to same review | /applications/{applicationId}/configurations | R07-CONFIGURATION-LIST | Failed read is not absence; incompatible or hidden reference cannot be selected. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-CR-REVIEW | Exact reviewed payload/references | Publication, release and activation are separate labelled facts. |
| R07-CR-PARTIAL | Publication committed, release outstanding | Retain original operations and actual completed stage; no all-or-nothing claim. |
| R07-CR-ABSENCE | Deliberate explicit missing source reference | Explain override/default semantics; failed read is not absence. |

### R07-ASSIGNMENT: Canary, promotion and rollback

#### Description

Frame F04 shows **Promote** selected beside Canary and Rollback modes. Current
versus proposed map rows contain Default and exact trusted cohorts, current and
proposed release and change. Promote Customer settings 8 to default, explicitly
remove the canary override and retain unrelated Finance override. Show increasing
generation, current precondition, authorized impact and RestartRequired limits.
Canary changes only its trusted cohort; rollback chooses an earlier compatible
release and a higher generation while preserving unrelated deliberate overrides.
No free-form cohort expression, auto-rollout, remote restart or fleet atomicity.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-A01 | Canary / Promote / Rollback; authorized release/cohort | Actual binding/map/release/cohort reads | Prepare | Show complete proposed map and explicit override retention/removal | /applications/{applicationId}/configurations/{configurationId}/assignment | R07-ASSIGNMENT | No target inferred from reported instance labels; invalid choice blocked. |
| R07-A02 | Confirm assignment | Actual map mutation/recovery permission and freshly reviewed precondition | Confirm | One guarded whole-map native command for one application | /applications/{applicationId}/operations/{receiptId} | R07-OPERATION | Conflict preserves permitted draft and enters reconciliation; unknown retains original key. |
| R07-A03 | View adoption | Current assignment and adoption reads | Inspect | Resolve desired map independently of acceptance | /applications/{applicationId}/configurations/{configurationId}/adoption | R07-ADOPTION | Assignment success never becomes consumer success. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-A-PROMOTION | Reviewed default/cohort map | Explicit remove/retain canary and unchanged unrelated entries. |
| R07-A-CANARY | Trusted cohort assignment | Only exact selected entry changes; no typed rollout group. |
| R07-A-ROLLBACK | Compatible earlier release | New higher generation, every affected entry and restart consequence visible. |
| R07-A-CONFLICT | Map changed after preview | No overwrite; current map and local intent enter R07-CONFLICT. |

### R07-ADOPTION: Current desired versus accepted provenance

#### Description

Frame F05. Table: Instance name, reported app version, desired release/generation,
last accepted release/generation, local revision, adoption and separate freshness.
Examples include Applied, AppliedUnchanged, Pending, PendingRestart, Rejected and
Faulted. Detail panel contains safe reason and observation/receipt times. Compare
with the currently resolved default/cohort assignment, not historical success.
PendingRestart says valid candidate/live values unchanged. Faulted does not claim
safe last-known-good execution. Empty cohort means no observed recipients; ratios
name their denominator and exclude stale/unobserved recipients explicitly.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-D01 | Refresh / bounded instance paging/filter | Exact current map and observation read | Read | Retain authoritative source times and current desired comparison | /applications/{applicationId}/configurations/{configurationId}/adoption | R07-ADOPTION | Date permitted snapshot; unavailable/unknown is not convergence. |
| R07-D02 | Instance adoption row | Exact authorized row | Inspect | Panel separates desired, historical acceptance, safe failure and freshness | /applications/{applicationId}/configurations/{configurationId}/adoption | R07-ADOPTION | Access loss clears values/panel; unknown enum remains unknown. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-D-MIXED | Different real adoption/freshness observations | Six precise adoption meanings, desired/accepted facts and independent stale/offline. |
| R07-D-NORECIPIENT | No observed recipients | No successful rollout ratio or fabricated denominator. |
| R07-D-FAULTED | Configuration guarantee failed | Historical acceptance labelled historical; no safe live-value assertion. |

### R07-SCHEDULED-JOBS: Scheduled tasks and registered jobs

#### Description

Frame F06 keeps the fourth application tab label Scheduled jobs. Primary
**Scheduled tasks** table has name/status/job/trigger filters, bounded paging and
Task, Trigger, Job, resource, active and desired revision/state. Daily customer
check shows Active 7 / Pending adoption 8; other examples are Active and Disabled.
A secondary Registered jobs section retains exact selected report/registration
inspection. Catalog definitions, configured tasks and instance telemetry remain
different facts. UUIDs do not dominate rows. The quick panel opens full details.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-SJ01 | Task name / View full details; Refresh/paging | Exact authorized task/query | Inspect | Existing quick panel then exact task and preserved return context | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | No row/name rebinding; unknown active state is not enabled. |
| R07-SJ02 | New Scheduled Task | Actual supported create plus recovery permissions | Create draft | Registered trigger/job only; no executable handler creation | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | Unsupported provider/job cannot be invented from a label. |
| R07-SJ03 | Registered Job View full details | Exact job/report read | Navigate | Preserve selected job/registration/report context | /applications/{applicationId}/jobs/{executionScope}/{jobId} | R07-JOB-FULL | Missing observations remain Not reported; no inferred schedule. |
| R07-SJ04 | Supported Run now / Disable / Retire / Cancel | Actual supported command, exact target and recovery permissions | Review operational confirmation | Submit only supported original command after exact effect review | /applications/{applicationId}/operations/{receiptId} | R07-OPERATION | Distinguish new admission, retained pending work and entered callbacks; cancel is not physical stop. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-SJ-TASKS | Named tasks and separate job reports | Active/desired revisions and per-target truth. |
| R07-SJ-CONFIRM | Operational command confirmation | Run now is not a temporary config edit; pause/retire/cancel consequences explicit. |
| R07-SJ-RETAINED | Disabled/retired task with protected work | Pending/unknown responsibility remains visible and durable. |

### R07-TASK-FULL: Accepted task-centred editor

#### Description

Frames F07 Trigger, F08 Job and S03 blocked. Preserve the accepted task-level
panel **above exactly two contextual tabs, Trigger and Job**. Main controls:
Name, Enabled, selected Trigger, selected Job, target Resource/resolver, inherited
Execution identity and typed invocation mapping. History is a separate header
action, never a third configuration tab. Default workload identity is a readable
inherited value; selection exists only for actually supported authorized
alternatives. Map PartitionId to explicit Europe partition and InactiveBefore
to explicit 2026-08-27. Runtime Event/Run/Attempt IDs cannot be supplied by mapping.
Registered typed transformations/authorized named lookups only; no expression box.
Show **Configured by** on the actual captured active Task revision (example:
Demo operations, revision 7), distinct from the current draft editor or executing
workload. A new draft does not invent persisted attribution before commit.

Trigger tab derives from the main selected configured source, owner and schema.
Daily calendar is shared by three authorized tasks: Daily, 02:00, Europe/Malta,
RunLatestOnce. FixedDelay exposes its required single-task association and failure
policy; external sources say Event-driven instead of a next firing time. A
supported Create named trigger action stays in task context and uses registered
provider permissions; fixed inspect-only sources cannot be cloned. Shared edits
label scope and authorized affected tasks.

Job tab derives from that same draft and separates **Application job defaults**,
**Supported task overrides** and **Invocation input — mapped above**. Show exact
execution/input/recovery contract versions and authorized observed worker
compatibility: releases 2.4.1 and 2.4.2 compatible, 2.5.0 incompatible captured
contract. Full handler/build hashes are in Technical details, not metric labels.
Changing job updates owner/schema/identity and revalidates mappings immediately;
incompatible values cannot silently migrate to another definition. Preview from
either tab covers the whole task, with no preview side effects.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-T01 | Main association / Trigger and Job selectors / mapping | Exact permitted task draft and registered definitions | Edit | Bind values to selected definition/schema; revalidate dependent context | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | Preserve stable task name/key; unresolved required mappings block publication. |
| R07-T02 | Trigger / Job tabs | Main selected identities readable | Switch | Show contextual owner/settings from same task draft; no separate selector | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | Unknown or unavailable settings remain explicit; no stale prior job values. |
| R07-T03 | Shared settings / Create named trigger | Actual provider-declared support and scoped edit/create/recovery permission | Edit/create supported source | Explicit shared intent; no implicit trigger/job mutation from task disable/edit | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | Inspect-only provider has no create; unauthorized shared values remain protected. |
| R07-T04 | Preview whole task | Required draft selection/mapping resolved; real preview supported | Preview | Side-effect-free task/resource/identity/settings/calendar validation | /applications/{applicationId}/tasks/{taskId}/preview | R07-TASK-PREVIEW | Unsupported/unavailable preview pending/refused, never fabricated success. |
| R07-T05 | History | Exact task history permission | Navigate | Same durable records scoped to Task across its historical jobs/revisions | /applications/{applicationId}/runs | R07-RUN-HISTORY | Preserve task scope; permission loss clears history. |
| R07-T06 | Selected Job full details | Exact job relationship permitted | Navigate | Preserve task return context and selected scoped definition | /applications/{applicationId}/jobs/{executionScope}/{jobId} | R07-JOB-FULL | No target inferred from name; historic revision remains distinct from current. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-T-TRIGGER | Trigger tab active | Persistent task association/mapping above two tabs, shared schedule and precise owner scope. |
| R07-T-JOB | Job tab active | Same association; defaults/input distinct; execution/recovery contract and worker evidence. |
| R07-T-BLOCKED | Job/profile/contract changed | Required mappings unresolved, unsupported identity or incompatible worker stated; no broader fallback or apply. |
| R07-T-PENDING | Required adoption/validation unresolved | Desired revision visible; prior active revision remains authoritative. |
| R07-T-READONLY | Task readable, shared object not editable | Permitted context visible; no unauthorized shared edits. |

### R07-TASK-PREVIEW: Whole-task review and staged application

#### Description

Frame F09. Separate review page, not another Task tab. Summarize complete selected
trigger/job/owner/environment/resource, mapping, workload identity and execution/
recovery contracts. Separate task-only and shared trigger/job changes with
authorized affected task names/current/pending revisions. Calendar preview uses
the actual authoritative evaluator and displays local zone and UTC instants;
event-driven behavior has no invented next time. Preview creates no event/run.

Publish and apply displays separate real milestones: publication, coherent
release, assignment, owner adoption/validation, executable Task revision activation.
Each native mutation retains its own key/precondition. Previous active revision
7 remains authoritative until coherent successful promotion/explicit disablement;
desired 8 alone is not active. Shared changes expose each authorized affected
task's pending/rejected/active references. Existing accepted runs retain their
captured capsule. No distributed transaction or simultaneous fleet adoption claim.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-TP01 | Back to draft | Same permitted draft | Revise | Retain exact association/settings, invalidate old confirmation | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | Changed target/schema cannot reuse prepared values. |
| R07-TP02 | Publish and apply | Actual required operation/recovery grants and reviewed compatible draft | Confirm whole intent | Separate native original operations with visible completed/outstanding stages | /applications/{applicationId}/operations/{receiptId} | R07-OPERATION | Partial commitment/unknown retains original receipts; no silent retry or mixed activation. |
| R07-TP03 | Affected Task name | Independently authorized related task | Inspect | Exact related active/desired references | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | No names/counts from unauthorized tasks. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-TP-REVIEW | Coherent whole draft | Task versus shared scope, calendar/identity/mapping and explicit activation boundary. |
| R07-TP-REFUSED | Actual validation/adoption/contract refused | Precise safe reasons; no publish/activation success. |
| R07-TP-PARTIAL | Publication complete, later owner outstanding | Separate completed/outstanding facts lead to original-result recovery. |

### R07-RUN-HISTORY: Logical runs and actual attempts

#### Description

Frame F10. One bounded shared history with Task, Job or Event scope and preserved
parent; filters date, status, resource and authorized worker release/contract.
Rows are logical runs, not polling/Busy/capacity deferrals. Counts identify runs
versus attempts. Select Daily customer check / 25 Nov 02:00 for the existing
right-side pattern: **Succeeded — original result recovered**, resource, captured
Task revision 7, safe business summary and original operation (Technical details).
Transaction completion, business outcome and callback/scope cleanup are separate.

Two attempts show release 2.4.1/build 2026.11.24.1 execution/ownership loss with
unknown business outcome, then 2.4.2/build 2026.11.25.2 compatible recovery of the
original success. Recovery did not execute a second business operation. Preserve
original executor/build separately from reporter/recoverer, workload/profile and
effective policy. Unknown original provenance says Not reported. Other rows show
Outcome unknown and Pending — no compatible worker. Delayed observations retain
age and happened/received times; no fabricated cross-host duration. Protected
input/result/detail links require independent permission; optional expiry is
explicit and never authorizes replay. Logs/metrics are not history authority.
The selected run also labels **Configured by — captured Task revision 7**,
**Trigger producer — Central Management calendar workload**, and **Requested by —
None (automatic calendar)**. Only an actually established manual/trusted upstream
request shows a requester. Each attempt has separate **Executed as**, selected
**Profile**, effective **Security policy version**, and **Reported execution
instance** (an operational label, not independently verified replica identity).
Issuer-qualified stable subjects remain authoritative and appear under Technical
details; human-readable names are diagnostic. Missing evidence says Not reported.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-H01 | Task/Job/Event scope; filters; paging/Refresh | Exact authorized scoped query | Read | Query the same underlying durable records, bounded before disclosure | /applications/{applicationId}/runs | R07-RUN-HISTORY | No copied histories, unauthorized totals or global scan. |
| R07-H02 | Logical run / expand attempts | Exact run/attempt read | Inspect | Panel with captured identities, worker builds, actual roles and outcome/cleanup facts | /applications/{applicationId}/runs | R07-RUN-HISTORY | Unknown/stale/expired detail stays explicit; no recovery build substituted for original. |
| R07-H03-E | Recorded Event | Actual authorized immutable relationship | Navigate | Exact recorded Event and return context | /applications/{applicationId}/events/{eventId} | R07-EVENT-FULL | No lookup by label/current association. |
| R07-H03-T | Recorded Scheduled Task | Actual authorized immutable relationship | Navigate | Exact recorded Task and return context | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | Captured revision remains historical; no label-based lookup. |
| R07-H03-J | Recorded Job | Actual authorized immutable relationship | Navigate | Exact scoped Job and return context | /applications/{applicationId}/jobs/{executionScope}/{jobId} | R07-JOB-FULL | Captured contract remains historical; no label-based lookup. |
| R07-H04 | Supported request cancellation / original recovery | Actual supported command/lookup and exact resource permissions | Confirm request or inspect original result | Preserve original RunId/operation/capsule; no replacement business identity | /applications/{applicationId}/operations/{receiptId} | R07-OPERATION | Cancel/lost lease/timeout is not physical stop, rollback or known failure. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-H-ATTEMPTS | Selected recovered logical run | Actual two build/role observations; original success and separate cleanup. |
| R07-H-UNKNOWN | OutcomeUnknown | Original operation/capsule retained; no unsafe replay. |
| R07-H-WAITING | Pending/RetryPending blocked reason | Missing authority/worker/version/resource/capacity is not business failure. |
| R07-H-EXPIRED | Optional detail expired or protected | History fact preserved; detail unavailability explicitly bounded. |

### R07-JOB-FULL: Job details and persistent defaults

#### Description

Frame F11 Configuration. Preserve Overview / Run history / Configuration. Exact
registered owner/schema/business defaults and supported finite typed controls
lead, with actual desired/accepted provenance and related Tasks. Persistent job
settings, explicit supported task overrides and individual invocation input are
separate contracts. Editing shared defaults follows Configuration review/adoption
and affected Task revalidation; never invent values from runtime telemetry.
Overview shows actual worker/execution/recovery compatibility; Run history uses
the same underlying task runs, not a second history store.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-J01 | Overview / Configuration | Exact job/report/default reads | Inspect | Retain selected job/source/version context; no inferred current values | /applications/{applicationId}/jobs/{executionScope}/{jobId} | R07-JOB-FULL | Unknown/missing fields Not reported; protected defaults stay protected. |
| R07-J02 | Edit shared defaults | Actual supported owner config/edit/recovery permissions | Edit | Same typed configuration flow with shared scope/affected tasks | /applications/{applicationId}/configurations/{configurationId}/edit | R07-CONFIGURATION-EDITOR | Cannot treat task invocation input as defaults or silently mutate related tasks. |
| R07-J03 | Run history | Exact Job-scoped history permission | Navigate | Aggregate same runs across Tasks with preserved job scope | /applications/{applicationId}/runs | R07-RUN-HISTORY | No count inflation from recovery attempts. |
| R07-J04 | Related Scheduled Task | Exact recorded/current relationship and task read | Navigate | Preserve Job return context and captured/current distinction | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | Unauthorized/missing relationship is not guessed. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-J-CONFIGURATION | Registered persistent defaults | Typed owner fields separate from Task input; actual provenance. |
| R07-J-OVERVIEW | Definition/report inspection | Versioned contract and compatibility facts, no handler creation. |
| R07-J-READONLY | Defaults inspect-only | No unsupported override/edit operation. |

### R07-EVENT-FULL: Immutable captured Event

#### Description

Frame F12 Configuration, with Overview retained. Heading uses trigger name and
occurrence time; Event ID under Technical details. **Captured at event admission**
shows recorded trigger/task revisions, job/input/execution/recovery references and
immutable configuration/input references, never today's values. References do
not imply a captured full payload. Distinguish occurred/scheduled/received times,
producer/requester from executor, and exact original source occurrence. Authorized
produced logical runs link to history; supplied zero-run reason is shown where
authoritative. An unavailable relationship is not zero successful runs.
Label **Configured by** from the captured Task revision, **Trigger producer**
from authenticated event ingress/validated retained provenance, and optional
**Requested by** only when the source actually establishes one. The automatic
calendar example explicitly has no human requester. These never substitute for
each produced run attempt's actual Executed as/profile/policy or reported instance.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-E01 | Overview / Configuration | Exact Event/captured-content permission | Inspect | Immutable event/capsule values and original references | /applications/{applicationId}/events/{eventId} | R07-EVENT-FULL | Never substitute current settings; protected/expired detail explicit. |
| R07-E02 | Produced logical runs | Actual Event→Run relationship permission | Navigate | Same durable history with exact Event scope | /applications/{applicationId}/runs | R07-RUN-HISTORY | No unauthorized target names/counts; unavailable is not zero runs. |
| R07-E03-T | Captured Scheduled Task | Exact related target permission | Navigate | Recorded relationship and preserved Event return | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | Historical revision does not become current association. |
| R07-E03-J | Captured Job | Exact related target permission | Navigate | Exact scoped Job and preserved Event return | /applications/{applicationId}/jobs/{executionScope}/{jobId} | R07-JOB-FULL | Captured contract remains historical; no current-setting substitution. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-E-CONFIGURATION | Immutable captured configuration | Captured references/content only, no Event edit/replay control. |
| R07-E-OVERVIEW | Original occurrence/provenance | Original source identity and actual produced logical runs. |
| R07-E-ZERORUNS | Authoritative zero admitted runs | Supplied bounded reason or Reason not reported; no inferred success/failure. |

### R07-METADATA: Display name and description only

#### Description

Frame F13. Name/environment context, **Display name**, optional **Description**,
**Clear description** and Review changes. Canonical application identity is
read-only context/Technical details. Explain that display editing changes no
identity, permissions, Configuration, instances or jobs. Clear previews empty
description and uses native omission-to-clear semantics, never explicit null.
Strong ETag and original key are retained before first submission. The native
synchronous command receives no invented queued/running lifecycle.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-M01 | Display name / Description / Clear description | Actual metadata read/write/recovery permission | Edit and review | Memory draft and exact-target before/after confirmation; strong current ETag | /applications/{applicationId}/metadata/edit | R07-METADATA | Bound text/escape values; no canonical-identity field or explicit-null clearing. |
| R07-M02 | Save display metadata | Reviewed fields, actual grant and current strong precondition | Confirm | Original-key native command; durable decision distinct from current cleanup | /applications/{applicationId}/operations/{receiptId} | R07-OPERATION | Conflict retains permitted draft; unknown retains receipt; no automatic retry. |
| R07-M03 | Cancel / Back | Safe parent context | Navigate | Confirm dirty draft disposal; no mutation | /applications/{applicationId}/instances | R07-INSTANCES | Declining discard stays in form; access loss clears protected values. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-M-EDIT | Current authorized display fields | Name/optional description only, exact target identity read-only. |
| R07-M-REVIEW | Before/after confirmation | Clearing semantics, original target, ETag and intent preserved. |
| R07-M-CONFLICT | Strong ETag changed | Keep permitted draft and reconcile current authorized state. |

### R07-OPERATION: Unknown, partial and original committed outcome

#### Description

Supporting S01. Exact application/environment and safe intent summary lead.
Example milestone table: publication Committed, release Committed, assignment
Outcome unknown, Task activation Not started. Retain distinct original operation
names/keys/actor/target/preconditions; IDs in Technical details. Recovery uses
typed original-result reads. Known rejection remains rejected even when its
record committed. Known original success remains committed if lookup cleanup
fails, with separate diagnostic. Minimal reload receipt has no payload/token and
is same-user scoped. Permission loss leaves unresolved outcome, not false failure.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-O01 | Check original result / bounded lookup refresh | Same actor/target original recovery permitted | Read | Recover exact original decision/source/commit and current cleanup separately | /applications/{applicationId}/operations/{receiptId} | R07-OPERATION | NotFound/expired/unavailable remains unresolved; revoked lookup reveals no denied result. |
| R07-O02 | Supported same-key unchanged-intent resubmission | Backend explicitly permits and actual original intent/precondition retained | Explicit confirm | Same original command only, never duplicate successful effect | /applications/{applicationId}/operations/{receiptId} | R07-OPERATION | No automatic mutation retry/new key; different intent requires separate deliberate review. |
| R07-O03 | Review assignment | Coherent release known committed and current map reads permitted | Navigate | Review actual outstanding assignment with fresh precondition | /applications/{applicationId}/configurations/{configurationId}/assignment | R07-ASSIGNMENT | Publication alone cannot skip release/assignment. |
| R07-O04 | View adoption | Assignment known committed and current adoption reads permitted | Navigate | Resolve desired versus actual accepted state | /applications/{applicationId}/configurations/{configurationId}/adoption | R07-ADOPTION | Assignment commitment does not prove acceptance. |
| R07-O05 | View Scheduled Task | Relevant task operation known committed and exact Task readable | Navigate | Read actual active/desired revisions | /applications/{applicationId}/tasks/{taskId} | R07-TASK-FULL | Desired publication does not prove active revision. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-O-UNKNOWN | Submitted outcome unknown/partial | Actual completed/outstanding stages and original receipts; no rollback claim. |
| R07-O-COMMITTED | Original durable commitment established | Original outcome preserved; lookup cleanup diagnostic separate. |
| R07-O-REJECTED | Known business rejection/noncommit | Native distinctions visible without fabricated lifecycle. |
| R07-O-LOOKUPDENIED | Result read no longer authorized | Unresolved minimal permitted receipt only; never switch credentials or submit replacement. |

### R07-CONFLICT: Reconcile without overwrite

#### Description

Supporting S02. Keep the permitted local draft beside freshly authorized current
revision/map/metadata fields and safe diff, owner scope, current precondition and
affected tasks. New review is a deliberate intent; an uncertain submitted intent
must remain recoverable first. No hidden-field deletion, silent overwrite,
automatic rebase of submitted command or auto-apply after reconnect.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R07-X01 | Refresh current state / compare permitted fields | Exact current/draft read permissions | Read/compare | Safe current precondition and retained intended changes | /applications/{applicationId}/changes/{draftId}/reconcile | R07-CONFLICT | Clear fields no longer permitted; do not infer absent values. |
| R07-X02-C | Review reconciled Configuration | Required values resolved; uncertain old intent still recoverable | Deliberate new review | Current revision and new intent/key after actual confirmation | /applications/{applicationId}/configurations/{configurationId}/review | R07-CONFIGURATION-REVIEW | Never transform old submitted intent. |
| R07-X02-M | Review reconciled metadata | Required metadata resolved; uncertain old intent still recoverable | Deliberate new review | Current strong ETag and new reviewed intent | /applications/{applicationId}/metadata/edit | R07-METADATA | No silent overwrite or new key for unresolved old intent. |
| R07-X02-T | Preview reconciled Task | Required Task fields resolved; uncertain old intent still recoverable | Deliberate new review | Current Task revision and coherent whole-task preview | /applications/{applicationId}/tasks/{taskId}/preview | R07-TASK-PREVIEW | No mixed capsule or hidden shared edit. |
| R07-X02-A | Review reconciled assignment | Complete proposed map resolved; uncertain old intent still recoverable | Deliberate new review | Current whole-map precondition and explicit new intent | /applications/{applicationId}/configurations/{configurationId}/assignment | R07-ASSIGNMENT | Preserve unrelated current overrides; no automatic apply. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| R07-X-CONFLICT | Concurrent authoritative change | Draft/current comparison and explicit renewed review. |
| R07-X-PROTECTED | Scope/content permission reduced | Protected fields removed; unsafe reconcile unavailable. |

## Desktop review frame inventory

All newly rendered frames are backoffice, 1600 × 1000, synthetic and unapproved
until the exact screenshot package receives its required decision. R06 reference
assets remain byte-for-byte unchanged. Non-rendered states above still require
implementation and meaningful tests; an extra picture is not needed for every
state. The Task page has two primary states, not two independent editors.

| Frame ID | Screen ID | State | Review purpose |
| --- | --- | --- | --- |
| R07-F01 | R07-CAPABILITIES | R07-K-CATALOG | Correct logical application feature catalog and diagnostic hierarchy. |
| R07-F02 | R07-CONFIGURATION-EDITOR | R07-CE-EDIT | Known typed controls, scoped/inherited values, supported JSON and validation. |
| R07-F03 | R07-CONFIGURATION-REVIEW | R07-CR-REVIEW | Exact reviewed payload and separate publication/coherent release. |
| R07-F04 | R07-ASSIGNMENT | R07-A-PROMOTION | Whole-map promotion, explicit cohort retention/removal and guarded assignment. |
| R07-F05 | R07-ADOPTION | R07-D-MIXED | Desired/accepted/local revision and six truthful adoption observations. |
| R07-F06 | R07-SCHEDULED-JOBS | R07-SJ-TASKS | Named task list plus distinct registered/reporting Job inspection. |
| R07-F07 | R07-TASK-FULL | R07-T-TRIGGER | Accepted main settings/mapping above exactly Trigger/Job tabs; shared calendar. |
| R07-F08 | R07-TASK-FULL | R07-T-JOB | Same task draft, business defaults/input separation and execution/build compatibility. |
| R07-F09 | R07-TASK-PREVIEW | R07-TP-REVIEW | Whole-task/shared impact preview with real separate activation milestones. |
| R07-F10 | R07-RUN-HISTORY | R07-H-ATTEMPTS | Logical run and original outcome, different build/recovery attempts and cleanup facts. |
| R07-F11 | R07-JOB-FULL | R07-J-CONFIGURATION | Persistent Job defaults with provenance and affected Task inputs distinct. |
| R07-F12 | R07-EVENT-FULL | R07-E-CONFIGURATION | Immutable captured references, occurrence facts and produced original runs. |
| R07-F13 | R07-METADATA | R07-M-EDIT | Display name/description only and explicit clear-description semantics. |
| R07-S01 | R07-OPERATION | R07-O-UNKNOWN | Partial commitment and unknown original operation recovery without replay. |
| R07-S02 | R07-CONFLICT | R07-X-CONFLICT | Preserve draft, compare current authority, deliberate renewed review. |
| R07-S03 | R07-TASK-FULL | R07-T-BLOCKED | Job change leaves mapping/profile/version unresolved; safe repair and no activation. |
| R07-S04 | R07-CONFIGURATION-LIST | R07-C-HISTORY | Names-first Configuration list and existing panel with exact Common/Application immutable publication history/content/reference selection. |

### Exact approved reference reuse

Applications and Instances retain their accepted layout/interaction. Their exact
approved PNGs are reference reuse, not newly rendered R07 pixels or evidence that
new code already implements the shared names-first/Technical details rules. The
new Configuration-list S04 frame replaces its former GUID-heavy presentation and
adds the material publication-history flow. Existing sign-in remains external,
with no inventory screen or new screenshot-approval request.

| R07 target screen | Source change / screen / state | Exact approved PNG | Use and limit |
| --- | --- | --- | --- |
| R07-APPLICATIONS | CIS-0029 / RM09-APPLICATIONS / primary | ../CIS-0029/assets/rm09-applications--primary--desktop.png | Existing filter/list/navigation layout only; preserve original bytes and approval, with global identifier-subordination rules explicit in R07. |
| R07-INSTANCES | CIS-0029 / RM09-INSTANCES / primary | ../CIS-0029/assets/rm09-instances--primary--desktop.png | Existing per-registration version/presence/list layout only; preserve original bytes and approval, with R07 shared details rules explicit. |

## Journey and requirement coverage

| Requirement/exclusion | Screen IDs | Action IDs | States | Notes |
| --- | --- | --- | --- | --- |
| RM09-AC01–02; RM10-AC35 | All R07 screens | All declared actions | Exact R07 frame inventory; approval pending | Preserve R06; require new screenshots only for uncovered choices, not repeated backend/routine layout approval. |
| RM09-AC03–05, AC10, AC17 | All protected R07 screens; external existing authentication | R07-G03 and scoped page actions | Shared denied/incompatible/reconnect | Real independent human identity, target/cursor/resource/lookup authorization and no stale cross-user data; no new sign-in design. |
| RM09-AC06–07 | R07-APPLICATIONS, R07-INSTANCES, R07-SCHEDULED-JOBS | R07-L01–02, R07-I01, R07-SJ03 | List/query/report states | Server search/cursor scope, per-registration versions and independently dated observations. |
| Current Capabilities and GUID corrections; RM10-AC26–27 | R07-CAPABILITIES; shared details | R07-K01–04, R07-G01 | R07-K-CATALOG/CONFLICT/READONLY | Logical job/RuleEngine/provider catalog, trusted schemas distinct from observations; names before IDs; no RuleEngine DSL. |
| RM09-AC08–09, AC12 | R07-METADATA, R07-CONFLICT, R07-OPERATION | R07-M01–03, R07-X01–02, R07-O01–02 | Metadata/conflict/original-result states | Display fields only, exact ETag/original key and native commitment/rejection/cleanup distinctions. |
| RM09-AC11–12, AC16; RM10-AC26–30 | R07-CONFIGURATION-LIST, R07-CONFIGURATION-EDITOR, R07-CONFIGURATION-REVIEW, R07-JOB-FULL | R07-C04–06, R07-CE01–04, R07-CR01–04, R07-J01–02 | Source history/content/reference selection; edit/invalid/protected/review/partial | Known forms plus bounded supported JSON, exact bytes, scoped paged immutable common/application history through existing getSource/getPublication, coherent source selection and independent owner validation. |
| RM09-AC13–14 | R07-ASSIGNMENT, R07-ADOPTION | R07-A01–03, R07-D01–02 | Canary/promotion/rollback and mixed/empty/faulted adoption | Guarded complete map, trusted cohorts, increasing generation and honest desired/accepted/restart/freshness. |
| RM09-AC15; RM10-AC30 | R07-OPERATION, R07-CONFLICT; shared lifetime | R07-O01–03, R07-X01–02 | Unknown/lookup-denied/committed/stale | No command queued offline or new-key retry after timeout/NotFound; original result and cleanup remain separate. |
| RM10-AC01–25, AC31–34 | R07-TASK-FULL, R07-TASK-PREVIEW, R07-SCHEDULED-JOBS, R07-EVENT-FULL, R07-RUN-HISTORY | R07-T01–06, R07-TP01–03, R07-SJ01–04, R07-E01–03, R07-H01–04 | Task/preview/retained/waiting/unknown/event states | UI exposes required model; real calendar/admission/Jobs/durability/identity/retention and focused native proofs are required backend work, not established by pictures. |
| RM10-AC28–29, AC35–36 | R07-TASK-FULL, R07-TASK-PREVIEW, R07-JOB-FULL | R07-T01–06, R07-TP01–03, R07-J01–04 | Trigger/Job/blocked/coherent preview | Exactly two contextual tabs, main association/input mapping, History separate and explicit workload identity/contracts. |
| RM10-AC36–40; HIST01–18; VER01–09; ID02 | R07-RUN-HISTORY, R07-TASK-FULL, R07-JOB-FULL, R07-EVENT-FULL | R07-H01–04, R07-T02/T05, R07-J03, R07-E01–03 | Recovered attempts, incompatible waiting, unknown provenance | Captured-revision ConfiguredBy, authenticated TriggerProducer, actually present RequestedBy, actual-attempt ExecutedAs/profile/policy and separate reported instance; original executor/build distinct from reporter/recoverer; no deployment orchestrator. |
| RM09-AC18–19 | All R07 screens | Shared navigation/detail/copy and page actions | Shared desktop/keyboard/zoom/lifetime states | Desktop only, bounded reads/forms/pages, accessible controls/focus and no new UI/auth/test framework. |
| RM09-AC20 and complete RM10 acceptance | All R07 screens plus actual runtime owners | All required production operations | Meaningful real-service/provider/browser evidence still required | Design is not external deployment, production readiness, release qualification or native passing evidence. |
| Explicit exclusions | All R07 screens | No excluded control | Scope boundary | No mobile, rule language/new evaluator, generic webhook/command/workflow engine, cron language, automatic rollout, existing database/credential/trust-store access, new broker/lease/DI/auth/test framework or push/merge/release. |

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Approved | Andrew Spiteri | 2026-11-26T03:53:54Z | `sha256:97de2b070f51078b2dfed5b865fe394bff851ced319bb3cac7a91a6c45d5ef65` | Explicit user reply Approve R07 screens to the exact17newdesktopreviewpackage and renderer84167b628b10729927e5d0643035721befe53d22a39cf887e3c1d66851d5fc37. This records combined wireframe/renderer/PNGapproval and preserves7R06reference states; it does not claim runtimeimplementation or providerproof. |

## R09 supplement — one Configuration page with versions

Classification: **backoffice**. Owner: WORK-189. Status: awaiting the exact
synthetic screenshot decision; no self-approval or production change. The user's
26 November clarification supersedes the unapproved Save/Apply proposal: use a
version list, clearly mark the deployed version and deploy any saved version,
including an older one for rollback. Keep earlier approved frames/decisions at
their original digests.

| Frame | Proposed behavior |
| --- | --- |
| R09-VERSIONS | One Configuration page with saved change notes/dates, the exact Deployed row, selected saved JSON, Edit copy and Deploy actions. |
| R09-EDIT | Edit locally in a panel on that page; Save version creates an immutable saved version without changing the deployed version. |
| R09-DEPLOY | One confirmation names the selected older/newer version, current deployed version, application/environment and all-instance target. |
| R09-NARROW | The same version list and selected JSON adapt to a narrower desktop canvas; no mobile redesign. |

The technical contract is [R09 version deployment](ux-r09-version-deployment.md).
The exact preview package will identify renderer, inputs and PNG hashes. Normal
navigation does not expose separate source publication/release/canary/assignment
screens. Native setup/publication/release/assignment originals remain distinct
behind these actions, with truthful same-page partial/recovery states. Existing
targeting is replaced only through the explicit Deploy review. Desired deployment
does not imply immediate recipient adoption. Production geometry, navigation,
native effects and cleanup require later focused implementation verification.

### R09 approval and deployed-comparison amendment

The awaiting-decision wording above is retained as the original checkpoint.
[Andrew's exact R09 reply](r09-user-approval.json), “much better and cleaner.
maybe if possible add option for diff at the bottom against another version ?”,
approves the four-frame version-list/Save/Deploy direction with a small read-only
addition. The existing PNGs, renderer, preview manifest and original technical
proposal remain at their reviewed digests; this supplement neither rerenders
them nor extends the older R07/R08 approvals to unseen pixels.

The later instruction, “the diff should be against the current deplyed
version.”, supplies the exact baseline. On the same Configuration page, a
collapsed **Differences from deployed version** disclosure compares the selected
saved version against the actual current uniform default release from the fresh
exact assignment map, without another version selector. The left column is
**Deployed version** and the right is **Selected saved version**. Independently
authorized immutable layer reads preserve exact numeric/string tokens. A changed
Shared base is explicitly identified; the comparison is not an effective merged
configuration. A missing default, targeted overrides, stale/unknown/protected
content or cross-context selection remains explicit and unavailable for a
complete comparison, rather than empty or unchanged JSON.
Map/deployed identity, selection, session and permission changes invalidate the
comparison. Opening it never saves or deploys anything.

Saved notes/dates identify rows; no sequential version counter or human author is
invented. Instance status remains an inline disclosure on the same page. WORK-190
and supporting WORK-191 implement these semantics within the original shared
5h/6 implementation pool. Actual build, navigation, effects, browser geometry and
joined-cleanup acceptance remain pending root verification.

R09 verification checkpoint, 26 November: current production build and focused native/browser acceptance passed after the actual next-Deploy receipt correction. See [implementation verification](r09-implementation-verification.md) for exact candidate identity, six browser cases, SQL/Audit assertions, cleanup and preserved earlier failures. This supersedes the pending implementation status above; it does not expand the screenshot approval or claim whole-portal qualification. The finite manual-review lease remains a separate active ownership obligation.


Instance status: the proposed three-column standalone/inline presentation and existing row panel are detailed in [the focused wireframe supplement](instance-status-wireframe-note.md). Two synthetic frames are rendered and reviewed; exact user approval is pending. Earlier wireframe approvals and their exact manifests remain historical and unchanged.

