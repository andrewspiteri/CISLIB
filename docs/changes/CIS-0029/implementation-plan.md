---
title: "RM09 bounded draft implementation plan"
type: implementation-plan
status: Draft
change_id: CIS-0029
---

# RM-09 draft implementation plan

**State: draft; not an approved executable plan.** The current phase is directly
user-authorized preparation of a visible design, capability reconciliation and
bounded proposal. CIS outcome authorization encountered the historical
product-wide no-UI approval. That retained approval remains unchanged; this file
does not override it, authorize production work or import approved implementation
tasks. The user has directly authorized synthetic design preparation and its
rendering checks.

The user delegates necessary work and asks for only the explicit UI preview
approval pause. Present one reviewable package containing the exact visual
revision, its screen/workflow map, this bounded plan and a narrow Management
backoffice scope/direction amendment. After the human decision, reconcile the
normal CIS product/outcome/plan/task records before production work. Do not run a
source-CIS workflow campaign, change unrelated approval policy, manually mark a
checkpoint approved, or implement production screens/write wiring while that
gate is pending.

The selected baseline is `6c70cee807f12caddf40cd1b443e4a20045d35aa`.
Use the [capability reconciliation](reconciliation.md), canonical
[Management contract](../../../contracts/management/v1/README.md) and
[preserved input](../../../planning/management/portal/sources/CISLIB_RM09_Management_Portal_Specification_v1.0.md).
Current RM-08 service/client delivery is an existing dependency, not new work to
reimplement or a claim of deployment qualification.

## Approval and execution dependencies

- **DRAFT:** This directly delegated design preparation may continue now. Mock
  data is visibly synthetic; a disposable prototype performs no real management
  mutation. Earlier no-UI authority does not become an approved UI direction.
- **VISUAL:** Explicit user approval binds the visible artifact's exact revision
  or content hash, principal journeys, target selection, permission presentation
  and narrow backoffice scope amendment. Retain the artifact, decision, approver,
  time, conditions and covered implementation scope through supported CIS
  checkpoints. Approval of prose alone is insufficient. Material workflow
  revisions require approval of the affected revision; routine spacing,
  accessibility and copy fixes preserving approved meaning do not.
- **GOVERNED:** Following that decision, record the accepted portal scope and
  design through CIS, import the implementation tasks and allocate their finite
  capacity. Production starts only when those records accept the approved scope
  and task dependencies. Retain and resolve any real blocker through that
  workflow; never manufacture an approval. No proposed task below is currently
  eligible for production execution.
- Every production task below depends on VISUAL and GOVERNED, in addition to its
  listed technical dependencies. This conservative ordering avoids using the
  blocked outcome to launch a separate prerequisite implementation. A retained
  approval for unchanged content survives restart; do not ask for it again.

## Shared finite allocation

Propose **48 active hours and 48 logical attempts total**, including all agents,
intake, design, implementation, documentation, reviews, verification, failures,
tool operations and restoration. The ten proposed leaves total 47 hours and 47
attempts; one hour and one attempt remain reserved for actual restoration.
These are shared maxima, not per-agent allowances or a fresh zero-cost grant.
Already incurred intake/design costs count against WORK-001 and the same totals;
the controller must retain them and check remaining capacity before importing or
launching work. Waiting does not erase active preparation or settlement costs.

The existing **6 GiB** counted storage ceiling remains, including retained
evidence, dependencies, generated output, existing fixed cache/Docker charges
and restoration reserves. Inspect actual headroom before installation/build or
fixture execution. Do not delete historical evidence/global caches to make room
or enlarge the ceiling. Coordinate shared output directories and native fixtures;
parallel agent reasoning does not authorize concurrent builds against them.

Each leaf is **medium or smaller**, has one bounded observable outcome and owns
its affected tests, documentation and review. Do not create separate generic
test/documentation/review campaigns. Split a leaf before execution if its actual
solution is high complexity, preserving these shared maxima and its dependencies.

| Proposed leaf | Outcome and technical dependency | Maximum active time / attempts |
| --- | --- | --- |
| WORK-001 | Source-routed intake and complete visible design package; DRAFT now, then VISUAL/GOVERNED handoff | 5 h / 5 |
| WORK-002 | Independently validated production human/delegated Management identity; WORK-001 | 6 h / 6 |
| WORK-003 | Authorized global application query and environment enumeration; WORK-001 | 4 h / 4 |
| WORK-004 | Scoped configuration eligibility and configured-cohort lookup; WORK-001 | 3 h / 3 |
| WORK-005 | Independent Next.js host and operator session boundary; WORK-002 | 6 h / 6 |
| WORK-006 | Approved application shell and truthful inspection; WORK-003, WORK-005 | 4 h / 4 |
| WORK-007 | Deliberate metadata change and original-result recovery; WORK-006 | 4 h / 4 |
| WORK-008 | Exact configuration authoring, publication and release journey; WORK-004, WORK-006, WORK-007 | 6 h / 6 |
| WORK-009 | Whole-map assignment/promotion/rollback and truthful adoption; WORK-008 | 5 h / 5 |
| WORK-010 | Runnable isolated portal/service reference and deployment handoff; WORK-007, WORK-009 | 4 h / 4 |
| Restoration reserve | Restore actually owned inputs/output/fixtures when needed; not an extra feature task | 1 h / 1 |

WORK-002, WORK-003 and WORK-004 can proceed independently after the common gates.
Coordinate service edits and serialize shared builds/fixtures. The later
dependencies deliberately reuse the established session, review and recovery
boundaries instead of introducing new identity, command, DI or test frameworks.

## Leaf outcomes and focused verification

**WORK-001 — inspectable design.** Preserve the input and refreshed baseline;
produce a navigation/screen map and navigable prototype or rendered screen set
for inspection, metadata confirmation/recovery, exact configuration authoring,
release, assignment, adoption and rollback. Include desktop/narrow layouts,
keyboard/focus behavior, target/version placement and loading/empty/denied/
unavailable/stale/validation/conflict/uncertainty/expired-session states. Use the
existing CIS shell/templates for disposable draft screens. Map every field/action
to a delivered API or named prerequisite and submit the exact revision with the
narrow backoffice direction amendment. Review synthetic-data labeling, workflow
coverage and approval persistence; do not represent the prototype as production.

**WORK-002 — native operator authority.** Implement the reference native
human/delegated token handler/projector through existing Security extension
points, with explicit management issuer/audience/subject/client relationships,
complete target grants and distinct human/calling-application attribution. Keep
current workload profiles strict and development credentials isolated. Extend
existing native signed-token/HTTPS and original-key/Audit fixtures for wrong
issuer/audience/client, forged delegation, missing grants, actor switching and
resource denial. Document reference Entra setup and the limit of controlled local
issuer proof; it is not a live Entra deployment.

**WORK-003 — application discovery.** Define bounded environment and application
identity/display-name matching, stable ordering and query/caller scope cursor
binding in OpenAPI/schema/conformance; preserve omitted-filter callers. Extend
typed C#/TypeScript clients and the existing selected backend/PostgreSQL reads,
plus authorized bounded environment enumeration. Filter before pages/cursors.
Verify matches beyond the first page, inaccessible data, filter/cursor mismatch,
scope changes, Unicode/limits, live paging and old callers using current native
fixtures. Document exact semantics. Do not fetch the whole fleet in Next.js.

**WORK-004 — actionable configuration permissions and cohorts.** Resolve actual
source/binding permissions through small scoped hints on existing authorized
reads, or an equally bounded explicit reference grant bundle, so a journey is
unavailable without its required recovery permission. Add only the authorized
binding-scoped cohort-ID lookup needed to choose a configured canary absent from
current recipients/pins; disclose no native mapping subjects. Preserve existing
server reauthorization and trusted cohort membership. Extend canonical contracts,
both clients and current native denial/cursor/unknown-cohort tests as needed.
Document that implementation discovery is not permission and reported labels do
not create cohorts. No general permission introspection/administration framework.

**WORK-005 — session and host.** Create the independent Next.js/TypeScript portal
host with maintained OIDC authorization-code/PKCE session integration, server-only
token handling, secure HttpOnly cookies, origin/CSRF controls, restricted redirects,
expiry/sign-out and per-request delegated credentials for the existing client.
Verify every protected read/action boundary, cross-user isolation, expiry,
revocation, callback validation, cancellation and trusted endpoint/prefix handling.
Keep portable packages free of Next/React/Node dependencies. Document independent
bootstrap/trust/session configuration, replica needs and health behavior.

The proposed production component approach is Radix Primitives, ordinary CSS
design tokens and typed Next.js components. Radix is MIT licensed; retain its
copyright and permission notice. Its accessible primitives support custom styling
without a paid component license. See the [official introduction](https://www.radix-ui.com/primitives/docs/overview/introduction)
and [license](https://github.com/radix-ui/primitives/blob/main/LICENSE).
Before adoption, verify the exact supported Next/React/auth/primitive versions,
package licenses and notices against official package sources and repository
policy, then pin them and record the decision in this leaf. No paid component
license or unverified dependency selection is assumed by this draft. Draft CIS
templates do not determine a new portable-package dependency.

**WORK-006 — inspection.** Implement the approved shell, visible authorized
environment selection, global search/paging, application detail, capabilities,
instances and complete module/category/job observations. Preserve source times,
reported/verified distinctions, safe bounded opaque details and unknown states.
Verify late-response target switching, user cache clearing, denied/unavailable
states, polling settlement, narrow reflow and keyboard/focus/contrast on principal
views. Use supported summaries only; no fleet totals, restart or admission
projection is invented. Update typed-screen maintainer guidance in this leaf.

**WORK-007 — metadata and recovery.** Deliver edit/review/confirm with exact
environment/application, before/after fields, omission-to-clear description,
strong ETag and original key retained before submission. Establish the reusable
payload-free receipt and outcome presentation boundary for later configuration
journeys. Verify no write retry, precondition conflict/new review, lost response,
NotFound, revoked lookup permission, reload/user switching and committed result
with later cleanup failure against current fixtures. Document original intent,
business decision, original commit and attempt cleanup distinctions.

**WORK-008 — author through release.** Deliver authorized definition/source
inspection, memory-only JSON drafts, unsaved-navigation feedback, exact-byte
validation/review, safe scoped diff, immutable publication and coherent release
creation. Preserve common/application absence, defaults redaction, source/map
ETags and original-key recovery; publication/release never implies activation.
Reuse WORK-007's result boundary. Verify large numeric lexemes, UTF-8/limits,
redaction, rejection/conflict, permission loss, uncertainty and no accidental
payload rewrite using portable tests plus focused native/portal integration.
Document that structural acceptance is not typed consumer acceptance.

**WORK-009 — assignment and adoption.** Deliver explicit whole-map review and
manual configured canary/default assignment, promotion showing retained/removed
overrides, and higher-generation rollback to an inspected known release ID.
Use `getRelease`; no convenience release-history API. Show current desired and
reported acceptance/local revision, status, freshness and timestamps separately.
Verify atomic map conflicts, unrelated override preservation, denied/revoked
actions, original recovery, PendingRestart/Faulted, unknown/no-instance/stale
states and explicit ratio scope. No automatic promotion/schedule or remote
restart. Document these operational meanings alongside the flow.

**WORK-010 — runnable reference.** Package an isolated portal plus real Management
service reference using the existing native HTTPS/provider/Testcontainers
fixtures and freshly provisioned owned resources. Validate the principal approved
journeys end to end, including denial, response loss/recovery, session expiry,
reconnect, narrow layout and WCAG 2.2 AA checks appropriate to those journeys.
Document clean setup, trusted URL/prefix, token audience/issuer, session secrets/
expiry, proxies/origins, runtime pins, health endpoint and replica requirements.
Compare actual screens/workflows with the exact approved revision and retain
material deviations, real results and remaining limits. This distinct outcome is
the runnable deployment reference; earlier leaves already own their affected
tests/docs/review. Local reference evidence does not qualify external deployment
or a release, and mocked configuration journeys do not complete RM-09.

## Proposed finite host/UI limits

These are initial finite choices to confirm against the selected provider before
implementation, without increasing service/client ceilings:

- Ordinary lists request 50 rows, permit at most 100 per page and retain at most
  two pages per active resource/identity/target. Complete inventory views request
  ten entries, at most twenty; retain only the current complete page. No eager
  all-fleet or all-inventory traversal for summaries.
- Exact editor source is at most 256 KiB UTF-8 and depth sixteen; configuration
  envelopes at most 1 MiB. Reuse native whole-inventory and regular response bounds;
  over-bound data fails clearly instead of being silently truncated.
- At most four upstream protected calls per active operator/request group and
  sixteen per host, with a bounded pending allowance of sixteen and explicit
  capacity refusal. Only one refresh per resource is in flight. Protected server
  responses use no-store unless an explicitly user/scope-aware bounded cache is
  justified. Target/identity changes discard obsolete protected view caches.
- Use the existing 30-second entry deadline and propagate cancellation through
  credential/transport/decode cleanup. At most one explicitly selected safe GET
  retry inside that deadline; mutation retries remain zero. Actual committed
  outcomes remain distinct from cancelled waiting.
- Visible inventory/adoption refresh is ten seconds; uncertain original-key
  lookup is two seconds for at most sixty seconds, then returns to ten seconds.
  Back off failures to twenty, forty and at most sixty seconds, pause hidden-view
  polling and offer manual refresh. Reconnect refreshes authorization,
  capabilities, target and preconditions before new writes; no click is queued.
- Retain at most twenty minimal payload-free operation receipts per operator
  session for at most 24 hours, clearing them on sign-out/user switch. Use
  operator-bound session storage only for those references. Payload drafts stay
  in memory. UI receipt expiry never changes server duplicate protection.
- Proposed sessions have thirty-minute idle and eight-hour absolute maxima,
  bounded further by the selected integration/provider validity. Passive polling
  must not indefinitely renew operator presence. Validate every route/action and
  remove protected content on session expiry/denial; an authenticated outage may
  retain a clearly stale already-displayed snapshot.

## Boundaries and completion

No existing/shared/user database access, external deployment, source transmission,
push, merge or release is included. Isolated newly provisioned fixtures become
eligible only through the reconciled task authority and remaining shared bounds.
No production UI begins before VISUAL/GOVERNED, and no historical completed owner
is reopened. Preserve original evidence and actual failed costs.

Completion requires the approved journeys running through the real authoritative
Management service, focused passing evidence for changed boundaries, documented
deployment/identity limitations, comparison with the approved design and normal
CIS closeout with settled accounting. Inventory-only delivery or mocked RM-08
writes is not full RM-09 completion.
