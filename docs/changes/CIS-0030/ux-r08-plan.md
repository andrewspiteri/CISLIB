# R08 — first application UX simplification

Status: R08 screens approved by Andrew Spiteri; bounded implementation accepted under
the exact decision in `r08-user-approval.json`. This bounded iteration follows Andrew's manual
review of the running portal on 26 November 2026. WORK-182 owns the five preview
frames and contract reconciliation. It has two shared hours/two logical attempts,
transferred from unstarted future Scheduler work; all agents, intake, source,
rendering and review share that allocation. Existing totals, restoration and
storage limits are unchanged. R07 source, PNGs and decisions remain preserved.
The separate WORK-181 manual session has expired and drained. Its retained
`manual-active-session.json` records `closed`/`expired`; the owned control
directory contains the final fixture and runner cleanup receipts. No manual
portal instance is kept live by this plan. That historical manual review does
not establish R08 browser acceptance.

## Requested result

- Tabs have readable, refreshable URLs beneath the selected application.
- Configuration rows expose their available actions directly through a named
  ellipsis menu. A click opens Configuration JSON, with identifiers last.
- Assigned shared JSON and application overrides are visibly distinct. A shared
  row offers **Override for this application**, never a disguised shared edit.
- Application authoring is application-only: one large formatted JSON editor,
  small inheritance caption, ordinary validation/review, Technical details last.
  The source/reference selector is removed from the main editing surface.
- **New configuration** accepts a name and JSON before first deployment. A real
  native creation boundary must support a genuinely unknown definition.
- Current and retained past instance sections are both expanded. A stale report
  is not proof that a process stopped; only explicit closed registrations count
  as past. Counts and pagination describe the data actually loaded.

## Five synthetic desktop frames

The new self-contained renderer is
`assets/render-rm09-ux-r08-screens.mjs`. It produces Configuration list/menu,
JSON-first quick details, application JSON editor, predeployment creation, and
current/past instances at 1600 × 1000. These are design previews, not production
screenshots, passing Playwright evidence or a successful backend creation.
The original R07 canonical renderer/wireframe/native decision is not rewritten
to pretend these new pixels were approved by the earlier decision. The exact
R08 source/PNG manifest is identified by [r08-user-approval.json](r08-user-approval.json):
Andrew approved these five frames. This supplemental user decision is not a new
global native design approval or production/provider proof.

## Contract reconciliation

**Readable routes.** Use `/applications/{app-slug}/instances`, `/configuration`,
`/capabilities` and `/jobs`, and
`/applications/{app-slug}/configuration/{configuration-slug}` with `/edit`,
`/assignment` or `/adoption` where needed. Keep exact application UUID,
Configuration ID and schema as explicit URL selectors; slugs are readable labels,
not a new identity registry. Duplicate/renamed labels remain safe. Existing UUID
and `?tab=` links continue to work, contradictory or duplicate selectors are
rejected, and browser Back/refresh retain the selected tab. New Jobs detail links
use `/jobs/details` to avoid colliding with the new Jobs list route. BFF resource
paths and original-operation/protected-state targets retain exact native IDs.

**Value and inheritance.** Definition discovery alone does not identify a
current value or an application override. Use the exact default assigned release
and its Common/Application references, with independent content authorization.
Label the assigned shared layer and application override separately. No source
history scan, browser merge, schema-default substitution or workload-snapshot
credential borrowing supplies an effective JSON value. Mixed cohort maps,
unassigned maps and protected/unavailable values retain their actual meaning.
Any bounded native list/read projection needed for rows belongs with these
screens and its provider/client tests; avoid per-instance or unbounded fan-out.

**Application editing.** Carry the frozen actual Common/Application references
and strong preconditions internally. Edit only the application layer; do not let
an app-scoped action mutate shared content. An unassigned target has no implicit
latest shared reference: the initial explicit absence/retained-reference choice
must remain a simple deliberate decision. Publication, coherent release and
assignment remain separate effects with independent original receipts; readable
copy must preserve partial/uncertain results without exposing their machinery
as the main editing task. The row's Publish action opens review/authoring; it
cannot pretend that an undisplayed server-side draft exists.

**Formatted JSON.** A whitespace-only formatter copies original scalar and
quoted-string tokens verbatim after existing lexical validation. It preserves
large numbers, exponent spelling, negative zero, escaped Unicode, member order,
null and unknown fields. Read-only formatting does not alter retained bytes.
Opening an editor may present a formatted initial buffer without silently
publishing it; only deliberate edits produce a new reviewed payload. Explicit
Format JSON invalidates review, retains the original on error/size overflow, and
does not use a JavaScript numeric parse/stringify round trip.

**Before deployment.** Existing catalog apply can provision an application,
definition, schema, source and binding before any registration, but only from its
server-trusted manifest. It accepts a manifest digest, not caller JSON. Existing
publication requires an already authorized source. Consequently a genuine
name-plus-JSON creation is implemented through a narrow application-scoped
native operation in the existing Management/Configuration store. Its server-owned
generic-v1 contract is a nonnullable object with no declared members and `{}`
defaults; caller JSON does not define a schema. Explicit create/reuse intent,
immutable fingerprint/provenance checks, current grants, strong ETag and original
key remain required. It does not relax CatalogApply or simulate a local save.
Creation, publication, coherent release and assignment remain separate. Source
implementation alone does not establish provider acceptance; the corrected15-case
provider pass, separate Audit rollback pass and corrected R08 desktop pass are
recorded in [the implementation checkpoint](r08-implementation-checkpoint.md). No new
Configuration framework/store or Scheduler runtime is part of this change.

**Instances.** Existing native presence is active/stale/closed registration
telemetry, not independent physical-process attestation. Current includes recent
registrations and clearly marked unconfirmed/not-recently-seen observations;
Past means retained explicitly closed registrations. Both sections are expanded.
Grouping the current authorized page is supported today. Independently complete
Current/Past pagination needs a bounded native presence filter and query-bound
cursor extension; omitted filters preserve existing clients. No closed-page
count becomes total history, and retention does not imply every old instance is
still stored.

## Coherent implementation and remaining proof

1. Readable navigation plus compact Configuration list, direct actions and
   JSON-first details. Include selector/legacy/rename/collision tests, exact
   authorized layer reads and bounded list projection/provider proof where
   required, and matching operator documentation. These source paths are authored;
   focused native results are separately recorded in the implementation checkpoint.
2. Application-only JSON editor and real predeployment creation. Keep the
   formatter and meaningful exact-token tests with the editor; keep native
   create authority, original transaction/result/Audit, migration/catalog
   compatibility and duplicate/conflict/provider tests with creation. Do not
   expose a working Create action without that backend. The optional native
   creation boundary and separate create-to-publication memory handoff are now
   authored and the corrected focused provider/current R08 browser observations
   passed. The later two-case regression and subsequent combined three-case
   regression remain retained failures. The later fresh R08 and existing
   authoring cases passed separately; fresh rollout failed before rollout.
3. Current/past instance presentation and bounded presence-filter paging if
   needed. Include native active/stale/closed/retention and cursor checks plus
   browser navigation/accessibility coverage and documentation. R08 implements
   grouping of the current authorized page with the existing bounded pager; it
   introduces no independent whole-history presence query.

The approved follow-up is organized as three implementation tasks, each owning
its tests, documentation and independent review. Native allocation records retain
the unchanged shared totals; no new resource approval is inferred from the design
decision. The exact R08 decision and native creation contract govern this work. Existing outstanding
rollout/adoption regression remains open after its two-case 0 passed / 2 failed
batch and subsequent combined three-case 0 passed / 3 failed batch. In the latter,
authoring/R08 had joined Node fast-fail exits; rollout exited 0 and reached
adoption before two nested scoped locators failed on correct Faulted content.
Only those inner locator roots changed; strict assertions are preserved.
Fresh ordinary C# phases later passed R08 (52 seconds) and existing authoring
(53 seconds) separately, including saved TRX guards and parent SQL/Audit. Fresh
rollout failed after 46 seconds with a joined Node fast-fail during metadata
editing, before rollout. Runtime pins/flags are unchanged, and no green combined
batch, original rollout/adoption acceptance or whole-portal completion is claimed.
WORK-183/WORK-184 reached native Complete/valid/zero blockers; WORK-185 closure
is parent-owned. The manual-only shared dataset has a passing build and source
review, with a finite 3,600-second lease request prepared but live handoff still
pending. Earlier failures
and the unknown Node fast-fail cause remain retained. New design pixels do not
resolve them. Preserve the current pins,
real native authorization and actual owner drainage throughout.
