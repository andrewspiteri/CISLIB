# R09 proposal — Save a JSON version, then Apply it

Historical, superseded proposal. The user's later version-led model is recorded
in [R09 versions and deployment](ux-r09-version-deployment.md). In particular,
that proposal replaces this draft's publication-only Saved meaning, Apply
vocabulary, preserved-targeting default and extra confirmation steps. This file
is retained as history; it is neither the active R09 contract nor an approval.

Status: source-only proposal for the next screenshot review. R09 screens are
not approved, and this document changes no product behavior. Existing R07/R08
approvals, native effects and failed/passing evidence remain intact. The live
review concerns include the new `test.andrew` Configuration; no live request,
build or mutation was executed while preparing this proposal.

## Current causes and actual click path

The editor has separate native publication, release and assignment effects, but
the operator must understand their implementation vocabulary to finish a normal
change. For a newly created binding there is no assigned default and no release.
Opening **Manage assignment** can therefore lead to an empty release selector
and a Canary-oriented page, while review requires an explicit default release.
An existing JSON publication is not sufficient to make that button useful.

The actual current path, when permissions and reads are available, is:

1. After creation, **Continue to publication**. Enter a reason and choose
   **Review Application JSON version**. With no assigned default, explicitly
   choose **Use no shared publication · Application only**, or an actual Shared
   publication, then **Continue to JSON version review**.
2. **Publish Application JSON version**. Once its original is known committed,
   choose **Review release with this publication**. Enter the release reason,
   **Review release**, then **Create release — not assigned**.
3. From the known release result, **Review assignment for this release**. In
   **Selected exact release**, choose **Use for application default**. Enter an
   assignment reason, **Review complete assignment**, then **Confirm assignment**.
   An initial successful assignment advances generation from unassigned to 1.

There is also a concrete navigation-lifetime mismatch to address. Creation keeps
its dirty parent owner registered while embedding the publication editor; it
clears that parent only after a matching publication commits. The child's Back
button clears only its own draft. Consequently another dirty owner can still
raise the document's before-unload prompt. Generic dirty anchor navigation also
confirms without settling those dirty owners, allowing a second unload prompt.
This is a source-supported explanation of the reported Back/Manage-assignment
problem, not a claim that the current live browser was inspected.

Relevant source: `components/new-configuration.tsx`,
`components/configuration-editor.tsx`, `components/configuration-assignment.tsx`,
`configuration-assignment-model.ts` and `components/protected-context.tsx` under
the existing Management portal. No replacement router or draft owner is needed.

## Proposed operator contract

The main editor has **Save version** and **Apply saved version**. Its status says
**Unsaved changes**, **Saved — not applied**, or **Default assignment updated —
instance acceptance pending**. Exact native identities and individual original
results remain available in Technical details/recovery. There is no autosave,
background apply, combined atomicity claim or new Save backend/API.

**Save version** reviews the exact Application JSON and reason, then confirms
one existing source-publication operation. The frozen bytes, SHA, actual source,
session, schema and source ETag must still match. Only its known committed
matching result establishes Saved. A cleanup warning does not erase a known
save. Local edits after that result become a new unsaved buffer; they do not
change the saved version or permit another write while an original is unresolved.

New names still require the existing explicit creation operation before native
source validation/publication can run: an unknown definition has no source or
binding IDs. Present this once as setup, retaining the exact JSON in memory; do
not pretend creation saves JSON or promise a side-effect-free native preview
before the target exists. The editor then follows the same Save/Apply contract.

**Apply saved version** uses one deliberately selected immutable publication.
Its review shows the exact Shared/Application pair, the whole current/proposed
default map and every retained cohort pin. With no default, Shared versus
Application-only is an explicit choice. Simple Apply changes the default only;
all unrelated cohort pins remain copied unchanged. Removing canary pins belongs
to an explicit advanced assignment review, never an implicit promotion.

After R09 approval, one explicit **Confirm apply** may authorize the finite
existing native stages: create the coherent release under its own original key,
then preview the whole map using that actual release ID, and submit assignment
under a different original key/current map ETag only if every reviewed fact
still matches. Display these stages as progress, not extra unexplained forms.
Any changed map, reference, session, permission or eligibility stops before the
next effect and requires deliberate review. No automatic mutation replay or
reload-time continuation is allowed. This is bounded component logic over
existing clients, not a new coordinator, journal or persisted workflow framework.

Assignment commitment establishes desired default generation, not a restart,
deployment, consumer validation or accepted runtime value. **View adoption**
remains the separate source of actual recipient observations.

## Required states

| State | Visible action and retained facts |
| --- | --- |
| Editing / Save review | Editable memory-only JSON; exact reviewed bytes; no effect before confirmation. Format/edit invalidates review. |
| Save outcome unknown | Keep the original source key and permitted buffer. Recover that key; disable a replacement Save and Apply. |
| Saved, not applied | Show the exact saved version and publication time. Offer Apply separately. Later unsaved text is clearly distinct. |
| Release committed, assignment not committed/unknown | Preserve Saved and the actual release separately. Explain that nothing is known assigned; recover the exact pending assignment or explicitly review the remaining step. |
| Assignment committed | Show actual committed desired generation/map and independent cleanup warning if any. Observe adoption separately. |
| Conflict / denied / unavailable | Preserve still-permitted draft and prior committed facts. No silent rebase, new key, replacement command or empty-value fallback. |
| Session/target/permission change | Clear affected protected values, cancel and join current work, retain only permitted payload-free original references. Late callbacks cannot navigate or expose another context. |

Receipts retain only their existing finite family/target/key/association/time.
They store no JSON, ETag, draft or aggregate outcome. Save/release/assignment
remain different Operations originals with their actual native transaction and
Audit semantics. Every later stage independently reauthorizes its real resource;
UI capability hints are advisory. Missing Apply permissions leaves a usable
permitted Save flow with an explicit explanation.

A saved but unassigned source has no current-value pointer. After reload, use an
exact saved publication ID from the explicit URL/selection or original recovery;
never choose the greatest UUID or assume the first history row is latest. A
saved-version URL still requires current exact source/content authorization and
does not reconstruct an unresolved command or restore a lost unsaved buffer.

## Navigation, layout and review package

Back/breadcrumb/Apply navigation must make one deliberate dirty decision across
the creation handoff and editor, settling both local draft owners on confirmed
discard while retaining original references. Cancel stays put. A known committed
save must not be described as unsaved merely because its original receipt exists.
Pending native work is canceled/joined and never converted into a rollback claim.

R09 previews should cover an unassigned new Application-only Configuration,
Shared override, Save review, Saved/not-applied, Apply review with preserved
cohorts, assigned/adoption-pending and partial/unknown recovery. Keep the accepted
shell, desktop palette and readable routes. Include the actual new-editor and
review surfaces at 900/1280 widths, long JSON/identifiers and error banners.
Contained instance-table tests do not prove these different screens resize
correctly; no layout fix is claimed by this source diagnosis.

After screenshot approval, meaningful checks should prove the new-name handoff,
single dirty decision, exact large-number/Unicode bytes, one original per native
effect, unchanged assignments after Save, whole-map pin preservation on Apply,
conflict/unknown recovery without replay, read-only grants and current session
isolation. Reuse the existing native fixture and qualified providers. Preserve
current evidence and its platform limits; no broad harness or Scheduler work.
