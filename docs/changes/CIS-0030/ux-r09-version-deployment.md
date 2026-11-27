# R09 — one Configuration page, saved versions and Deploy

Status: proposal awaiting the user's exact R09 screenshot approval. No product
implementation is authorized by this document. It supersedes the vocabulary and
interaction proposal in [the earlier Save/Apply draft](ux-r09-save-proposal.md),
which is retained as historical, unapproved design material. Existing R07/R08
decisions and execution evidence are not rewritten.

## One user-facing model

One Configuration page contains a bounded **Versions** list and the selected
version's JSON. Each immutable row shows its change note and saved date.
Selecting a row inspects its JSON; **Edit as new version** copies the selected
version into the editor, and **Deploy** selects a saved version for deployment. The exact current
desired version is marked **Deployed**; deploying an older row is rollback, using
the same Deploy action. There is no separate rollback, Canary or draft wizard.

Rows use their actual immutable change note/title, such as “Exports enabled”,
“Regional settings” or “Baseline”, with **Saved <date/time>** as the fallback.
There is no existing stable v1/v2/v3 ordinal. Do not invent a counter, API,
migration or page-order version number. Technical IDs remain collapsed.

The current native author field contains actor kind plus an opaque actor-scope
digest, not a human display name. There is no human-author column. Keep any known
actor kind and exact reference in Technical details; do not invent names or imply
that historical identities can be reconstructed. Adding personal provenance is
outside this bounded proposal.

**Edit as new version** opens an inline editor or side panel on this page. It creates only
a memory-held buffer; changing it cannot alter a saved or deployed version.
The **Save version** button on the editable form is the explicit intent to save
the currently visible JSON and change note. It validates and performs the native
stages directly, with no separate Save confirmation, publication/release pages
or wizard. Saving never deploys it. **Deploy this version** opens one confirmation dialog,
then updates desired deployment. Status and any partial/recovery action remain
on this same page rather than sending the operator through multiple screens.

## Exact native mapping

| User fact/action | Existing native meaning |
| --- | --- |
| Saved version | One immutable coherent release, pinning the exact Shared and Application publication references. |
| Save version | One explicit form action for the current JSON/note; validate, publish Application JSON, then create that release, using distinct original keys and actual preconditions. |
| New configuration + Save version | Name, JSON and change note stay in the same flow. Run existing setup/creation first only when needed, then publication and release; each remains separately authorized and journaled. |
| Deploy / deploy an older version | One existing whole-map assignment: default = the selected saved release, cohort overrides = empty. Generation increases; it is never decremented to the older version's generation. |

The page remains scoped to the actual application/environment/Configuration and
selected schema binding. New generic configurations use the existing fixed
schema 1. Different native schema bindings are not silently combined into a
global version history or one Deployed badge; this adds no migration authority.

Existing bounded release history supplies rows, dates and notes. The
exact assignment map supplies current desired deployment. A release not on the
current history page must be read by its exact authorized ID to show its
deployed fact; absence from a page is not “nothing deployed”. No fleet/history
scan or greatest-UUID/latest-publication inference is permitted.

The JSON pane labels its actual layer: Application JSON when present, otherwise
Shared JSON. Its pinned Shared base can be explained in a short secondary caption.
It does not fabricate merged/effective JSON in the browser. Editing as a new version retains
that selected version's exact Shared reference, including when editing an older
version; it does not silently substitute the latest Shared content. Shared-only
copying still requires independent ContentRead and preserves every JSON token.

## Save, partial save and recovery

Clicking Save freezes the exact JSON/SHA, note, selected Shared base,
application/source/binding/schema/session and relevant source/map preconditions.
Native validation/preview still runs before publication, with inline errors and
same-page progress. It is not an extra operator confirmation step. No typing,
refresh or navigation saves anything automatically, and no aggregate Save
backend/framework is introduced.

A new generic Configuration visibly uses **Application only · no Shared base**
by default. Its Save flow does not force an exact-source picker. Shared Override
and Edit as new version retain the selected immutable pair. An explicit Shared selection
is needed only when the operator actually asks to add or change a Shared base;
never infer it from provisioned history or silently choose its latest entry.

An unknown new name has no source/binding for native preview until creation
commits. One Save intent may perform setup first, but the page must show that
stage truthfully. If the derived target, permissions, Shared base or preconditions
do not match the reviewed intent, stop on this page as **Save incomplete** and
request deliberate review. Creation does not mean its JSON was saved.

| State | Same-page behavior |
| --- | --- |
| Unsaved changes | Editable local JSON and note; direct Save version button; explicit dirty navigation decision. |
| Saving | At most one original per required native stage, sequentially awaited with real cleanup custody. No double submit or replacement key. |
| Publication committed, release not committed | **Save incomplete**. Preserve actual publication identity/SHA and the permitted buffer. It is not a deployable version row. Explicit completion may reuse this exact publication without republishing it. |
| Saved | Show the actual committed release as a version. Deploy remains a separate explicit action. Later edits are a new unsaved buffer. |
| Outcome unknown / current cleanup failed | Preserve earlier known committed facts and distinct cleanup warnings. Recover the exact original key; never infer failure or replacement permission from timeout, denial or NotFound. |
| Reload / session or target change | Keep only permitted payload-free original references and exact authorized selected IDs. No persisted JSON/ETags, command reconstruction or automatic stage resumption. Clear affected protected values and suppress late callbacks. |

There is no all-or-nothing promise across setup, publication and release. A later
stage failure never erases an earlier committed fact or causes an automatic
rollback. Save is complete only when the exact intended release is known
committed. All native mutation/recovery capabilities remain independent.

## Deploy means all instances for this Configuration

The dialog names the actual application, environment, Configuration and selected
saved version. It explicitly says **Deploy to all instances**. Review the complete
current map/ETag and proposed map before confirming. Proposed default is the exact
selected release; proposed overrides are empty. No saved JSON is modified.

When actual existing cohort-specific assignments are present, show one relevant
warning: this deployment replaces those targeted assignments so every target
uses the selected default. Their removal must appear in the full reviewed map;
it is not silently preserved behind a claim that one version is deployed
everywhere. No new targeting controls or Canary screen are introduced.

With no cohort pins, Deployed compares the exact current default release ID.
If existing targeted assignments remain, do not falsely mark one uniform
all-instance deployment: show a compact **Existing targeted deployment** fact
until an explicitly confirmed all-instance Deploy replaces it. Unknown/protected
current state is unavailable, not undeployed. Deploying the already selected
uniform version is unnecessary; redeploying that default to remove real targeted
pins remains a reviewed change with the warning above.

Deploy preserves whole-map native preview, strong ETag and its own original key.
A concurrent decision stops with conflict; no automatic rebase/retry. Lost
delivery recovers that same assignment original on this page. Deploy remains
independently available to appropriately authorized operators even when JSON
content is protected; ContentRead is not a substitute for deployment authority.

**Deployed** denotes committed desired assignment. **Instance status** is an
inline summary/disclosure on this same page, not separate View adoption
navigation. It reports actual instance acceptance, pending restart, stale or
unknown observations and honest no-instance states. It never claims immediate
runtime adoption, a restart or successful consumer validation merely because
assignment committed.

## Screenshot and implementation boundary

R09 previews should show the single page with a Deployed row, the Save editor
panel, and the Deploy confirmation (including the conditional targeting warning),
plus desktop resizing. Partial-save/recovery messages belong to that same page.
Back/breadcrumb navigation makes one dirty decision across creation handoff and
editor, not layered prompts; original references survive confirmed local discard.

Only after exact screenshot approval may the finite existing-component/client
implementation proceed. Checks must cover new-name Save, exact numeric/Unicode
bytes, partial-stage recovery, Deploy older version, explicit removal of existing
targeted pins, map conflict, current native grants/session lifetime and actual
editor/table bounds. Reuse current qualified fixtures. No new runtime, package,
counter/store, generic command framework, Scheduler or broad test campaign.
