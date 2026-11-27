# CISLIB RM09 Management Portal Specification

**Implementation specification v1.0**  
**Date:** 24 November 2026  
**Status:** Proposed implementation contract. UI design approval is required and has not yet been given.  
**Roadmap outcome:** Operators can view applications and use the management capabilities delivered so far.

## 1 Purpose and completion outcome

RM-09 delivers the operator-facing management portal in Next.js and TypeScript. It presents application and instance information, exposes authorized management actions, and provides the configuration publication and distribution experience defined by RM-08. Central Management remains authoritative for managed resources, permissions, mutations and outcomes.

CIS must generate a concrete UI design, present it to the user, and obtain explicit approval before production UI implementation starts. A prose specification, implementation plan, generated screenshot that cannot be related to a workflow, or an agent's own review does not satisfy this approval. This document specifies that requirement; it does not constitute approval of an unseen design.

Completion requires a running portal connected to the real management service, the approved principal user journeys, truthful failure and recovery behavior, and focused verification. An inventory-only increment may be delivered while RM-08 is pending, but it is not full completion of this specification. Configuration acceptance remains blocked until its real backend capabilities are available.

## 2 Baseline and dependency ownership

The inspected CISLIB main revision is abfbcafaa6ef2f3c3c18304244e4dafc1dd5c852. Its RM-07 module supplies application inventory, registrations, presence, complete reported inventories, capabilities, and one administrative mutation: replacing application display metadata. It already supplies the TypeScript client-core and management-client packages. Production portal login and configuration publication are separate work. [S1–S4]

The RM-08 specification defines publication, releases, assignment, adoption and recovery. It is a dependency contract, not evidence that those APIs are implemented on the inspected main revision. CIS must refresh this short baseline against the selected implementation revision before planning executable tasks. [S5]

| Owner | Responsibility used by RM-09 |
| --- | --- |
| RM-01 Security | Validate credentials and trusted human or delegated identity; enforce complete grants and resource scope. |
| RM-02 and RM-03 | Preserve original transactions, idempotent decisions and uncertain-result recovery. |
| RM-04 Messaging and RM-06 Audit | Deliver required administrative audit records through the existing durable path. |
| RM-05 Coordination | Own resource leases and execution authority. Reported owner information is only an observation. |
| RM-07 Central Management | Own application records, presence, inventories, capabilities, metadata mutations and managed admission. |
| RM-08 Configuration | Own immutable publication, coherent releases, assignments, validation, distribution and adoption. |
| RM-09 Portal | Own human sign-in integration, navigation, forms, presentation and its Next.js server integration. |
| CIS | Plan and execute work, retain the design approval, and enforce implementation dependencies. |

Existing API limitations must be reflected in the design. The current list contracts provide paging, without server search/filter parameters or fleet aggregates. Current operation recovery is by the original metadata operation key; it is not a global operation history API. There is no destination audit query API, operator-facing current admission projection, or remote restart command in this baseline. [S2–S4, S6]

RM-09 may introduce the small additive API changes explicitly identified below. It must not invent missing server facts in the browser or put domain decisions into Next.js to bypass a missing management contract.

## 3 Initial product scope

The initial product is an internal operator portal. It supports authorized application inspection, application metadata editing, and the RM-08 configuration workflows. Its layout must work well on a desktop and remain usable on a narrow screen.

| Area | Required experience |
| --- | --- |
| Sign-in and session | Sign in, sign out, session expiration, authentication failure and insufficient access. |
| Overview | Identify the current environment and connection state, reach applications and configuration, and show only summaries supported by authoritative APIs. |
| Applications | Page and find authorized applications; inspect identity, display metadata and available observations. |
| Application detail | Inspect instances, capabilities and complete reported module/category/job inventories; edit display metadata when permitted. |
| Configuration | Inspect definitions and publication history, author and validate content, preview differences, create releases, assign, promote, roll back and inspect adoption. |
| Operation detail | Explain a submitted mutation and recover its original outcome using the existing operation reference. |

Discovery determines which implemented and compatible capabilities are usable. The portal maps known capabilities to known views. It must not execute a server-supplied UI schema, script, HTML fragment or arbitrary command.

The initial release does not add scheduling controls, rule editing, process restart, arbitrary remote execution, a log viewer, a metrics time-series store, an audit archive, permission administration, or customer record synchronization. Modules and job definitions may be inspected without implying that their future control capabilities already exist. Do not fill navigation with inactive future-product placeholders.

## 4 Required design and approval workflow

### 4.1 Design generation

CIS must first inspect available contracts and create a concise capability map linking each proposed screen, field and action to an API or a named prerequisite. It then generates one coherent design package using realistic synthetic applications, instances and configuration examples. All mock data must be recognizable as demonstration data.

The package must contain:

1. A navigation and screen map, with the proposed application/environment selection behavior.
2. A navigable prototype or rendered screen set covering the principal workflows. The user must be able to see the proposed UI; prose alone is insufficient.
3. A small reusable visual system covering typography, spacing, tables, forms, status labels, navigation, dialogs and feedback. Choose one maintained component approach and declare any licensing implications in the plan.
4. The application list/detail and metadata-edit journey, from inspection through confirmation and recovered result.
5. The configuration journey, from source authoring and preview through release creation, assignment, adoption and rollback. Mark dependency-blocked functionality explicitly.
6. Loading, empty, denied, unavailable, stale, validation-error, conflicting-update and uncertain-result states, including expired operator sessions.
7. Representative desktop and narrow layouts, keyboard focus behavior, and the placement of critical target/version information.
8. The capability map, unresolved decisions affecting the design, and the exact design revision being submitted.

The review should emphasize complete operator journeys. A small number of useful screens and state variants is preferable to a large gallery without interaction semantics. The prototype may use local mock data and disposable UI code. It must not submit real administrative changes or be represented as production implementation.

### 4.2 Approval gate

Production UI stories must depend on an approved design checkpoint in CIS. While it is pending, CIS may investigate contracts, prepare the prototype, and complete independently authorized nonvisual prerequisites that do not fix the unapproved experience. It must not build production screens or wire production write flows and then ask for retroactive approval.

CIS presents the completed package and explicitly asks the user to approve the identified revision, describing any conditions or dependency gaps. The human decision is recorded using the existing CIS approval/checkpoint mechanism wherever possible. Retain the design reference, revision or content identifier, decision, approver, decision time, conditions, and the implementation scope covered.

Approving this specification or an implementation plan does not approve an unseen design. If both plan and design approval are pending, CIS may present the complete plan and reviewable design together and ask for one decision explicitly covering both. If plan approval already exists, only the design decision remains. There is one design approval for the module, not a separate approval for every screen or task.

If CIS cannot currently express or enforce this dependency, make the smallest necessary workflow change a prerequisite. An unenforced note or manually marked success is insufficient. Do not expand this work into a general design-management platform or the later full CIS integration roadmap item. Earlier no-UI instructions for other roadmap work do not apply to this explicitly UI-focused requirement.

### 4.3 Revision and resume behavior

A valid approval survives an agent or CIS restart. Resume from the retained checkpoint and approved artifact without requesting the same approval again. A changed artifact must not inherit an approval merely because its filename is unchanged.

Changes to navigation, target selection, important workflows, permission presentation, or administrative action meaning require approval of the affected revised design before that production work proceeds. Spacing corrections, accessibility fixes that preserve the approved workflow, text corrections and internal refactoring do not require another approval. Unaffected approved work can continue.

If a newly discovered backend limitation materially changes a flow, revise that part of the design. Do not silently replace the approved interaction with an unrelated workaround. Final review compares the implemented principal journeys against the approved revision and records any material deviations.

## 5 Architecture and client boundaries

The browser communicates with the Next.js portal through its normal authenticated origin. The Next.js server uses the existing TypeScript management client to call Central Management over authenticated HTTPS. The management service communicates with its authoritative store and the existing CISLIB integrations.

Keep four boundaries explicit: portable TypeScript client foundation, management API client, Next.js host integration, and portal UI. Reuse @cislib/client-core and @cislib/management-client; extend their typed contracts only where RM-08 or a named RM-09 addition requires it. React, Next.js, session handling and component dependencies belong in the host/UI layer. The portable entry remains usable without Next.js or Node-specific imports. Future Kotlin Multiplatform clients should implement the same wire and behavioral contract without depending on portal view models. [S3]

The Next.js server may adapt responses into minimal authorized display DTOs, coordinate requests, handle sessions and preserve cancellation. Central Management continues to authorize targets, validate commands, control concurrency, perform durable changes and recover results. The portal cannot directly access the management database or an application's configuration store.

Do not add a portal database copying application state. Current views can use bounded request/client caches. Operator sessions use the chosen authentication integration; any session storage required for multiple portal replicas is explicit deployment infrastructure, not a second management authority. Portal startup and sign-in configuration must be independently available rather than relying on the configuration API they need to reach.

Select and pin supported runtime and UI dependencies at implementation time, honoring the repository's current package policies. Preserve existing lightweight client package boundaries. This work does not require porting CIS or all of CISLIB to TypeScript.

## 6 Operator authentication and authorization

### 6.1 Human identity

RM-09 must deliver a real human sign-in path. The initial production reference integration is Microsoft Entra ID using a standard OpenID Connect authorization-code flow with PKCE and a maintained authentication/session integration. A clearly isolated local development operator identity may support examples and tests. Development credentials must not become a fallback when production sign-in or management authentication fails. [S10]

The reference management connection must carry a verifiable human or delegated identity accepted by the RM-01 integration. The current service requires an independently validating compiled native handler/projector for this identity category; supplying the portal login alone does not close that prerequisite. Define and implement the token audience, issuer, subject, trusted calling-client relationship where applicable, complete grants and exact resource mapping before enabling production writes. [S2]

Prefer a delegated operator access token for the management audience in the reference deployment. If a host uses an explicit trusted delegation mechanism, Central Management must independently verify both the caller and delegated operator; arbitrary actor headers are insufficient. Never authorize an operator simply because the portal's own application credential has a broad grant. Keep the trusted human initiator distinct from the calling application in administrative attribution.

Certificate authentication can protect a server connection where supported. It does not replace human sign-in or require an operator's browser to receive a service private key. Do not build a new identity provider, user directory or role editor in RM-09.

### 6.2 Enforcement and browser boundary

Every portal route handler/server action and every underlying management API must verify the applicable session and permission. Hiding a button is not enforcement. Recheck current permission and actual target at submission and recovery, including a permission change after a page was opened. Next.js explicitly treats server mutations as security boundaries that need their own authorization checks. [S7, S8]

Use secure, HttpOnly session cookies with appropriate SameSite behavior, expiry, sign-out and CSRF/origin protection. Apply the authentication integration's issuer/audience validation, state and nonce handling and redirect restrictions. Keep service credentials, access/refresh tokens and private keys in server-only handling; never expose them through client bundles, public environment variables, browser storage, URLs, errors or telemetry.

Resource access derives from server-validated identity and grants. A selected environment or application ID expresses the requested target, not permission. Preserve the backend's inaccessible-resource behavior and avoid leaking unauthorized names, counts, configuration fields or cursor data. Use user/scope-aware caching or no-store behavior for protected data; no shared static page may serve one operator's data to another.

Sign-out and user switching clear protected client caches and local operation references. Authorization loss removes affected protected content and blocks the corresponding actions. For the same signed-in operator, a minimal payload-free operation receipt may remain solely to recover an already submitted outcome if permission is later restored. A stale display is not a way to bypass a denied read. A network outage with a still-valid session may retain an already displayed snapshot as described below.

## 7 Application discovery and inspection

Show the trusted application/environment identity separately from editable display metadata. Use the opaque application ID for routing and commands. Environment selection must always be visible when inspecting or changing resources.

The initial application list requires bounded server-side filtering by environment and application identity/display name so an operator can find an application across pages. This is an additive RM-09 backend/client prerequisite because it is absent from the current PageRequest contract. Specify match semantics, input limits and stable ordering in the canonical contract; bind the query and current authorization scope into cursors. A changed filter resets pagination. Older callers omitting the new fields retain their behavior. A server that lacks the extension must not silently offer a loaded-page filter as global search. [S6]

Provide environment selection only from authorized server-derived information. Reuse a suitable delivered contract or add a bounded authorized environment enumeration as part of the same prerequisite. Do not derive the available environment set from only the first application page.

Use server paging and bounded client retention. Never fetch every inventory just to calculate dashboard cards. Full-fleet totals are not required for the first release; omit them unless a real authorized aggregate API exists. Any loaded-page count must state that scope. Preserve live-view paging semantics rather than promising a cross-page historical snapshot.

Application detail includes available identity/display fields, current instance registrations, presence, reported health/lifecycle and their timestamps, capability information, and reported module/category/job inventories. Show explicitly whether instance identity is reported or verified. Treat all names, descriptions and extension details as escaped data; display opaque detail as bounded text/JSON, never executable content.

Reported ownership is time-qualified information. It does not establish a fresh lease, release a duty, or authorize the portal to transfer ownership. The workload-self control-confirmation API must not be called as an operator to synthesize another application's admission status. If a later API exposes an authorized admission observation, label its provenance and time; otherwise omit that field or state that it is unavailable.

## 8 Metadata editing and original results

Provide an authorized edit form for the installed metadata command. It changes display name and optional description only. Explain clearing a description correctly; the current API uses omission to clear it and rejects explicit null. It must not imply that display editing renames the application identity or changes permissions, configuration, instances or jobs. [S4]

The reference operator grant bundle includes the target read, metadata write and original-result lookup permissions needed to complete this journey. Apply the same principle to each configuration mutation. Do not knowingly enable a portal write journey when its required recovery permission is absent, infer that write permission grants lookup access, or broaden backend authorization to compensate. Use existing capability hints where available and keep execution-time checks authoritative; this does not require a new permission-introspection framework.

Load the current strong metadata ETag. The confirmation identifies the exact environment, application and before/after fields. Create and retain an original idempotency key before the first submission so a lost response cannot lose the recovery reference. Send the original intent, ETag and key through the typed client without automatic mutation retries.

On a precondition conflict, keep a permitted draft where practical, retrieve current state and require an explicit new review. A revised submission is a new deliberate intent with a new key and current ETag. An uncertain prior intent must first remain recoverable; it must not be silently transformed into this new submission.

The current metadata API distinguishes the business decision, original persistence, result source and current attempt cleanup. Show an applied result as completed only with the backend's durable committed fact. A rejected decision is a rejection even if its record was durably committed. A recovered committed result remains committed if a later lookup's cleanup fails; expose that diagnostic separately. HTTP success or a completed browser animation is insufficient. [S4]

## 9 Configuration authoring and release visibility

Configuration screens depend on the usable RM-08 contract and preserve its meanings. Do not create replacement publication tables or configuration business logic inside the portal. [S5]

### 9.1 Definitions and sources

Show the portable configuration ID, exact schema version, authorized environment/application binding and declared update mode. Inspect common and application publications separately. Authoring may begin with a bounded JSON editor plus validation and a readable diff; a generic schema-driven form generator is not required.

Preserve the exact JSON payload supplied for publication. Client parsing for display must not silently rewrite the authoritative payload, change large numeric values or alter the exact content that the operator reviewed. Enforce documented size/depth limits and show server validation errors at useful locations when supplied.

Drafts are caller-owned. Keep them in memory by default, warn about unsaved changes on navigation, and do not add a server draft store or store sensitive draft content in browser persistence by default. A side-effect-free validation or preview is not publication. Server structural acceptance does not prove that every deployed application's typed validation will succeed.

### 9.2 Review and publication

The review shows common/application scope, schema, additions/removals/overrides, exact source references and available target impact. All preview information must come from authorized responses. Show captured-default information only where supplied by the contract; do not calculate runtime defaults in JavaScript or imply that all consumer builds have identical effective values.

Sensitive values must be removed or safely redacted before reaching an unauthorized browser, including diffs and error details. A redacted value is not an editable literal. When the API cannot preserve a hidden field safely, make that operation unavailable or require an authorized editor; never republish a mask as configuration. RM-09 is not a secret vault.

Publish immutable source content, then create an application-specific coherent release pinning exact common and application publication references or explicit absence. New publication does not activate itself. Removing an override or selecting defaults must use the explicit RM-08 release semantics and be shown in the review.

Present publication, assignment and application acceptance as separate facts. An ambiguous single Version column must not conceal which fact it describes. Release history is immutable; the operator cannot edit an existing publication in place.

## 10 Assignment promotion rollback and adoption

### 10.1 Assignment review

Show the current assignment map, its default and exact trusted cohort overrides, the proposed map, source/release references, current precondition and the intended target. Cohorts come from trusted management/deployment mapping. Display labels, arbitrary expressions or user-entered instance names cannot create an authorized rollout group.

The initial flow supports manual assignment to a configured canary cohort or the application default. Promotion updates the default and explicitly shows whether the canary override will be removed or retained. Apply the required map changes atomically through RM-08, not as multiple unguarded browser requests.

Rollback selects a compatible earlier release and makes a new assignment with a higher generation. Preview every default/cohort entry it changes and preserve unrelated deliberate overrides. Rolling back the default alone must not be described as rolling back an overriding cohort. A change requiring restart may also require restart on rollback; show that limitation without offering an unsupported restart action.

Preview is informational. Commit must reauthorize and enforce the actual precondition. A changed assignment or permission after preview requires renewed review rather than silent overwrite. The initial portal submits assignment changes for one application at a time. Any later multi-application workflow must show each application's outcome separately and must not imply one atomic fleet commit. Automated rollout schedules and health-based automatic promotion are outside this release.

### 10.2 Adoption presentation

Show each instance's current desired release/generation, last accepted release/generation, local revision, status and observation/receipt times where provided. Compare observations with the currently resolved assignment, not merely the instance's historical success label.

| RM-08 state | Required operator meaning |
| --- | --- |
| Pending | A desired assignment has no current matching acceptance observation. |
| Applied | The instance accepted the desired release as its baseline or with an effective change. |
| AppliedUnchanged | It accepted the new provenance and its effective values did not change. |
| Rejected | An identified desired release failed content/integrity/schema/validation checks; show the safe reason and previous acceptance separately. |
| PendingRestart | The candidate is valid, but live values have not changed because restart is required. |
| Faulted | The Configuration entry is disabled or its live-state guarantee failed; previous accepted provenance is historical only. |

Connectivity, staleness and health remain separate indicators. Unknown future states display as unknown. An empty cohort has no observed recipients; it is not a successful rollout. Stale instances must not inflate a fresh-convergence percentage. Any ratio must identify its denominator and separately expose excluded or unobserved recipients.

Acceptance confirms Configuration's local result. It does not prove that every ongoing business operation has abandoned previously captured values or that every subscriber's external side effect has completed. A Faulted entry must not be shown as safely running last-known-good values when the backend reports otherwise.

## 11 Connectivity freshness and refresh behavior

Distinguish browser-to-portal availability, portal-to-management availability, management-store authority, instance presence, application health observation, and configuration adoption. One failed portal request does not establish that all applications are unhealthy.

Use an explicit last-successful-fetch time and preserve source observation/receipt times. A freshly rendered page cannot make an old observation current. Do not derive managed-work admission from a UI timer. RM-07 owns live bootstrap and the authority window; existing accepted state can be retained during an outage while new managed work is blocked under its rules.

Use bounded polling as the initial UI refresh mechanism. Default proposals are a 10-second interval for a visible inventory/adoption view and a 2-second interval while resolving a known uncertain operation, with only one request per resource in flight, bounded backoff and a bounded fast-poll window before returning to the normal interval. Confirm these values against provider limits during implementation. Pause routine polling for hidden views; provide manual refresh. UI refresh intervals are not RM-07 authority leases or RM-08 distribution intervals.

Push updates are optional only when a delivered contract supports them. They do not replace authoritative rereads or require a new WebSocket platform for RM-09.

During a network or management outage, a still-authenticated open page may retain its last displayed snapshot, clearly marked stale/unavailable. Disable new mutations and do not queue commands for later automatic execution. Initial load without a snapshot shows unavailable or empty as appropriate, never fabricated zeros. Session expiry or denied access follows the stricter security behavior in section 6.

After reconnect, refresh session/permissions, capabilities, current target state and concurrency tokens before enabling writes. Do not replay an old click. Switching environment, application or identity invalidates prepared confirmations, cancels obsolete reads where possible, and prevents a late response from replacing the newly selected view. An already submitted operation retains its original target and recovery reference.

## 12 Operation recovery and cancellation

An operation view presents only states supplied by the owning API. The current synchronous metadata command must not be given an invented queued/running lifecycle. A future command that actually reports acceptance before completion must remain pending until an authoritative completion arrives.

A timeout, lost connection or cancelled browser wait can leave an unknown outcome. Retain the original operation name, key, target and actor scope, then use the existing typed recovery endpoint. Lookup NotFound does not prove rollback or authorize a new-key repeat. Do not automatically retry a mutation, its commit, or an uncertain command. An explicit same-key resubmission is permitted only under the backend's unchanged-intent recovery contract. [S4]

If result-lookup permission is revoked after submission, show that recovery is unavailable and the outcome remains unresolved. Preserve only the same-user minimal receipt permitted by section 6. Do not reveal a denied result, switch credentials, infer failure or submit a replacement operation. Permission restoration can resume lookup of the original intent.

Retain a bounded minimal receipt across a page reload, such as an operator-bound session-storage reference containing the target, operation and original key. It must contain no credentials or configuration payload. Validate current authorization again when reopening it. Clear it on sign-out/user switch and expire entries under a finite documented UI retention policy; server duplicate protection remains governed by RM-03 and does not expire merely because the UI forgets a receipt.

The initial operation area may show recent receipts from the current session and open a known operation reference. Label that list as incomplete. It is not a searchable audit trail or global management history. A recovered historical result can differ from the resource's current state after later edits; display both without overwriting the current view with an old decision.

Browser cancellation stops waiting or requesting work. It does not prove rollback or cancellation of an accepted administrative effect. Show a Cancel operation action only if a real backend contract supports it and supplies its result semantics.

## 13 Portable contracts and bounded behavior

Keep one canonical OpenAPI/schema contract for management APIs. Add the necessary human-identity integration and application-query extension without breaking existing clients. The configuration contract remains RM-08's responsibility. Every field/action in the approved design must map to an implemented contract or a recorded prerequisite.

Preserve canonical decimal strings for Int64 revisions, generations and sequences; do not convert them through JavaScript Number. Preserve exact timestamps for requests, recovery and diagnostic detail while formatting a copy with a visible timezone for display. ETags, UUIDs and cursors are opaque; use ETags for equality and preconditions, not ordering. Render unknown statuses explicitly and ignore compatible response additions safely. [S3, S5, S6]

Provide finite limits for pages, cached pages, active operation receipts, editor payloads, response bodies, concurrent calls, request deadlines and retry/backoff. Reuse service/client limits rather than increasing them silently in the UI. A response exceeding its supported bound fails clearly rather than truncating a configuration or complete inventory and presenting success.

Reads may use the client retry policy only when safe and within a deadline. The Next.js host propagates AbortSignal/cancellation and disposes request resources. Authentication changes do not trigger a fallback identity or automatic mutation replay. Endpoint origins and permitted path prefixes come from trusted deployment configuration, not arbitrary browser request values.

## 14 Usability accessibility and diagnostics

The approved design should make the selected environment, target, freshness and mutation result easy to recognize. Use text with status color, useful empty states, field-level validation and a stable place for actionable errors. Important results must remain accessible after a transient toast disappears.

Target WCAG 2.2 AA for the principal workflows. Include keyboard navigation, visible and unobscured focus, useful labels and error associations, readable contrast, non-color status cues, dialog focus return, and programmatically announced status updates. Verify zoom/reflow and a narrow viewport without hiding target identity or requiring horizontal scrolling for the whole page. Dense data tables may have a clearly usable local scroll region. [S9]

Use existing correlation/error fields for safe operator diagnostics. Record portal request failures, auth failures and aggregate latency without logging tokens, configuration bodies or unbounded identifiers as metric labels. Required administrative audit records are produced by the authoritative management mutation through RM-06. The portal may correlate them but must not emit a second success audit merely because it received an HTTP response.

Supply loading, no-authorized-data, no-results, unavailable and permission-denied states with their distinct meanings. Preserve useful drafts on ordinary recoverable validation/conflict errors; do not keep exposing sensitive data after authorization loss.

## 15 Deployment and maintainability

Deliver the portal as its own deployable application consuming the management service. Document trusted management URL/prefix, identity provider configuration, session secrets and expiry, trusted proxy/origin settings, supported runtime, dependency versions and a health endpoint suitable for the selected host.

Multiple portal replicas must share only the session/trust configuration or backing state required by the chosen auth integration. Administrative state and operation decisions remain in Central Management. A portal restart must not require reconstructing a local copy of the application fleet or losing the server's original operation result.

Provide an isolated local example with the real management service and test credentials, plus instructions for the reference Entra integration. Record what has actually been tested; locally exercised token validation or mocked identity responses do not establish a live Entra deployment. Use existing management deployment/provider fixtures and Testcontainers for affected external-service integration tests.

Add concise maintainer and agent guidance explaining package boundaries, adding a typed capability screen, handling unknown outcomes, redaction and the CIS design gate. Keep domain rules out of presentation components and preserve meaningful test substitution at the API boundary.

## 16 Acceptance scenarios

The following scenarios define observable completion. CIS maps them to focused tests or reviews; they are not a request for one framework or an exhaustive matrix per component.

| ID | Scenario and expected result |
| --- | --- |
| RM09-AC01 | CIS produces a visible, versioned design package with complete inspection/change journeys and API mappings. An unapproved or rejected design blocks production UI tasks. |
| RM09-AC02 | An approval survives restart for the unchanged design. A material changed workflow requires approval of that revision; routine presentation fixes do not prompt again. |
| RM09-AC03 | Production human sign-in reaches management through independently validated human/delegated identity. An operator without a grant cannot borrow the portal application's broader privilege. Development identity is rejected by production configuration. |
| RM09-AC04 | Altering an environment, application ID, cursor, operation reference or URL cannot disclose or change an unauthorized resource. Direct server-action/API calls receive the same protection as the visible UI. |
| RM09-AC05 | Missing or incompatible capability versions are unavailable truthfully. Unknown response states never default to success, and opaque inventory detail is rendered safely. |
| RM09-AC06 | Cross-page application filtering uses the documented server query. Cursors bind the query/scope; changing filters resets them. No loaded page is advertised as a fleet-wide count or complete environment list. |
| RM09-AC07 | Reported identity, instance presence, historical health and resource-owner observations retain their source/time distinctions. The portal neither calls another workload's self-control API nor grants/releases execution authority. |
| RM09-AC08 | Metadata editing affects only supported display fields. Concurrent edits preserve the strong ETag contract; the losing edit is reviewed against current state rather than silently overwriting it. |
| RM09-AC09 | A mutation commits but its response is lost, then the page reloads. Recovery uses the original key and validated actor/target, performs one effect and one required audit, and preserves committed/rejected/unknown and cleanup distinctions. |
| RM09-AC10 | Session expiry, sign-out, user switching or grant loss blocks pending writes and clears affected protected data. A revoked lookup cannot reveal the result or cause a replacement mutation; only a same-user minimal receipt may remain. Late responses cannot repopulate the previous user's or environment's view. |
| RM09-AC11 | Configuration validation/preview has no publication effect. Publication preserves the reviewed exact payload, creates immutable content, and release creation pins exact authorized sources without assigning them automatically. |
| RM09-AC12 | A state or permission change after preview is checked at commit. A conflict preserves a permitted draft for revised review and does not resubmit automatically. |
| RM09-AC13 | Canary promotion and rollback show every affected default/cohort entry. Promotion handles the override explicitly; rollback advances assignment generation and retains immutable publication history. |
| RM09-AC14 | Mixed Applied, AppliedUnchanged, Pending, Rejected, PendingRestart and Faulted observations remain distinct from stale/offline status. Zero observed recipients and outdated successful reports are not called converged. |
| RM09-AC15 | Management is unavailable while a valid session has a prior snapshot. The portal dates and marks the snapshot, disables new changes and queues nothing; reconnect refreshes state before a fresh explicit submission. |
| RM09-AC16 | Unauthorized sensitive fields are absent from browser responses, rendered diffs, logs and error messages. A redaction marker cannot be submitted as a preserved hidden value. |
| RM09-AC17 | Large Int64 strings, exact ETags, prefixed URLs, unknown fields/statuses and response limits retain existing cross-language behavior through the UI and Next.js integration. |
| RM09-AC18 | Keyboard-only and narrow/zoomed principal journeys preserve labels, target visibility, focus, error recovery and result access. Representative rendered screens match the approved design. |
| RM09-AC19 | Polling, pagination, caches, drafts and operation receipts remain bounded. Request cancellation and portal restart do not claim to undo an already committed backend operation. |
| RM09-AC20 | The portal runs against real delivered management and RM-08 APIs for final acceptance. Mock-only configuration screens or an inventory-only increment cannot close full RM-09. |

Use a small set of end-to-end browser journeys: inspect an application, change metadata with conflict/recovery, publish and assign configuration, observe partial adoption/rollback, and handle outage/session loss. Reuse backend/provider acceptance coverage and existing native fixtures. Keep representative visual evidence and failure diagnostics only; do not create large screenshot archives, mutation campaigns or tests that merely repeat component markup.

## 17 Suggested CIS work breakdown

These are story groups for CIS to refine into tasks no larger than medium, with direct acceptance criteria and dependencies. They are not a claim that a canonical CIS plan has already been imported or approved.

| Group | Deliverable and dependency |
| --- | --- |
| RM09-S01 | Inspect the selected baseline and record the capability map, required human-auth/query additions and RM-08 availability. |
| RM09-S02 | Generate the design package, prepare the implementation plan, and obtain the explicit design approval. Production UI groups depend on this checkpoint. |
| RM09-S03 | Deliver the human/delegated management integration and portal session boundary. Backend contract work may proceed before design approval where independently authorized. |
| RM09-S04 | Implement the approved shell, authorized application search/list and instance/inventory inspection, including the bounded query extension. |
| RM09-S05 | Implement metadata editing, result presentation and original-key recovery with the approved confirmation and error flows. |
| RM09-S06 | Implement configuration definition/source/release authoring and preview against delivered RM-08 APIs. |
| RM09-S07 | Implement assignment, promotion, rollback and adoption views against delivered RM-08 APIs. |
| RM09-S08 | Complete affected integration/browser verification, accessibility and visual review, deployment guidance and agent documentation. |

After approval, application and configuration work may run in parallel once their shared shell/client/auth contracts are agreed and their backend dependencies exist. Partition file ownership to avoid competing shell edits. Backend identity/query prerequisites and the UI design can be prepared in parallel without bypassing the approval gate.

CIS should retain a compact checkpoint containing the selected source revision, dependency status, approved design reference, completed tasks and current verification. On interruption, resume from it. Re-run only checks invalidated by changed dependencies or required by the affected acceptance gate.

## 18 Completion checklist and handoff instruction

RM-09 is complete when the approved design is implemented, operator identity is validated end to end, current authorized application and inventory data is usable, metadata and configuration workflows use their authoritative APIs, result recovery and outage behavior are truthful, the acceptance scenarios have appropriate evidence, and another developer can run and extend the portal from the documentation.

The implementation handoff to CIS is:

> Import this specification through the established CIS workflow. Refresh the repository/API baseline and identify concrete prerequisites. Produce a reviewable UI design with the principal workflows and state variants, retain it as a versioned artifact, and request explicit user approval of that revision before production UI implementation. Reuse existing approval/checkpoint mechanisms and preserve the approval across resume. Use the existing TypeScript clients and authoritative management APIs. Do not report RM-09 complete while RM-08-dependent production workflows remain mocked or blocked. Keep tasks, tests and retained evidence proportionate to the acceptance scenarios.

## 19 Source references

Repository references below are pinned to the inspected main revision. They establish the baseline, not a claim of testing or deployment performed for this specification. Proposed RM-09 requirements are identified in the body.

- **S1 Central Management module guide:** https://github.com/andrewspiteri/CISLIB/blob/abfbcafaa6ef2f3c3c18304244e4dafc1dd5c852/docs/modules/management/README.md
- **S2 Management service and security integration:** https://github.com/andrewspiteri/CISLIB/blob/abfbcafaa6ef2f3c3c18304244e4dafc1dd5c852/implementations/csharp/applications/CIS.Management.Service/README.md
- **S3 Existing TypeScript management client:** https://github.com/andrewspiteri/CISLIB/blob/abfbcafaa6ef2f3c3c18304244e4dafc1dd5c852/implementations/typescript/packages/management-client/README.md
- **S4 Metadata commands and original-result recovery:** https://github.com/andrewspiteri/CISLIB/blob/abfbcafaa6ef2f3c3c18304244e4dafc1dd5c852/implementations/csharp/applications/CIS.Management.Service/Metadata/README.md
- **S5 RM08 specification version 1.0:** https://chatgpt.com/library?file_id=libfile_fde3_f-5b2e621dcfcc8191ba323598b481d250_a96453786d9081918df59aa18e3ca617&account_id=personal
- **S6 Portable management DTOs and paging contract:** https://github.com/andrewspiteri/CISLIB/blob/abfbcafaa6ef2f3c3c18304244e4dafc1dd5c852/implementations/typescript/packages/management-client/src/models.ts
- **S7 Next.js authentication guidance:** https://nextjs.org/docs/app/guides/authentication
- **S8 Next.js data security guidance:** https://nextjs.org/docs/app/guides/data-security
- **S9 W3C Web Content Accessibility Guidelines 2.2:** https://www.w3.org/TR/WCAG22/
- **S10 Microsoft Entra authorization code flow:** https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow
