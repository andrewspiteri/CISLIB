# Application Configuration JSON workflow

The current R09 surface is documented in [Configuration versions](portal-configuration-versions.md). This guide retains the earlier R08 design and historical qualification; it does not describe a second active authoring flow.

The R08 desktop flow keeps Configuration names prominent and moves exact IDs, schema versions, generations and preconditions into closed Technical details. The Configuration list reads one bounded native summary page. Source badges come from actual assigned release references; publication history and source existence do not establish assignment. Shared, Application override, Application only, declared-defaults-only, cohort differences, unassigned, protected and unavailable states remain distinct.

Opening a row reads that exact application, Configuration and schema. The JSON panel shows one labelled assigned layer: Application JSON when present, otherwise shared JSON. It does not manufacture merged or effective JSON. A release whose two references are absent has no layer JSON to display. Cohort differences are qualified as differences from the displayed default assignment. Protected and failed reads are not empty JSON.

Publication history is read only and lazy. Opening its disclosure shows explicit Application/Shared history choices only for actually disclosed source identities; the selected source then performs its own authorized bounded history read. Opening an immutable payload requires a further authorized exact-publication read. History supplies neither a current-value pointer nor an implicit editing selection, and a different session or source scope clears the open selection.

The row actions dropdown requests native source and binding action hints for only that opened row. Edit, Override, Publish and Assign navigation are available only when their corresponding native write and original-operation lookup bundle is reported allowed. Viewing adoption is a separate navigation; its target read independently authorizes adoption access. The panel and dropdown do not fetch every row's content or scan a fleet.

The editor changes only Application JSON. Assigned shared content remains a separate, read-only layer. A legacy Common editor link is read only and offers an explicit Application override link. The main editor contains a large JSON textarea rather than source-reference selectors or generated typed controls. Format JSON is a deliberate edit: it preserves member order and exact string, number and scalar tokens, changes only whitespace, and invalidates an earlier review. Invalid or oversized formatted output leaves the current draft unchanged.

Creating an Application override of a Shared-only default assignment starts by
copying that exact assigned Shared publication into the local draft. The BFF
independently authorizes Shared ContentRead and verifies the actual binding,
source and immutable reference before supplying it. Existing Application JSON
and retained drafts are never replaced by a Shared copy on refresh. The copy
preserves the exact JSON tokens and marks the new override intent dirty; it
does not edit Shared content. Protected or unavailable Shared content produces
an explicit explanation, never an inferred latest publication, declared
defaults or an empty substitute.

Local editing is separate from publication admission. A still-permitted local
draft can be changed or formatted while its original operation reference is
retained or a transient read is stale. New validation/review/publication remains
blocked until the required fresh state and original resolution are available.
Editing neither discards an unknown original nor changes a known committed
version. Session/permission loss still clears affected protected values; receipt
storage contains no JSON. No replacement command or new key is inferred.

**Publish Application JSON version** stores one immutable JSON layer.
**Create release — not assigned** pins the exact Shared and Application versions
as a pair. **Assign** remains a separate operation. Neither publication nor
release changes running applications, and later local edits are not part of an
already-published version. The two original keys, confirmations and recovery
results stay separate.

The list's **Modified** date is the publication time of the actual default-assigned
Application layer when present, otherwise its Shared layer. It is not the last
local edit, source-history maximum, assignment time or cohort maximum. Missing
or protected metadata says Not available. JSON details label the selected actual
layer's publication time independently. Precise native UTC timestamps are kept.
These WORK-188 source corrections have authored tests; their current native
verification is recorded separately, without changing earlier R08 evidence.

For an assigned binding, review carries forward the exact authorized assigned Common reference and the current assignment precondition. When no default release is assigned, pressing Review asks for an explicit first shared-layer choice: no shared publication, or one immutable publication from bounded history. Nothing is preselected from the latest history entry. Choosing a different first shared option invalidates prior review. Publication, coherent release, assignment and local adoption remain separate confirmations and facts.

New configuration is scoped to one actual application and environment, including before deployment. Its native context checks creation and original-operation lookup eligibility. The server supplies the fixed generic nonnullable object schema at version 1; the portal does not infer a schema from JSON. Creation sends the name, explicit create/reuse intent and reason. The JSON remains in memory and is not sent in the creation command or stored in its receipt. Native reuse is limited to an exact compatible generic definition and never replaces manifest ownership.

After an actual committed creation result, Continue to publication replaces the address with the exact created Configuration's readable editor URL and transfers the still-held JSON in memory without a reload. The editor first verifies the actual current application, environment, Configuration, schema, binding, source, host and session, then independently reads publishing permissions and preconditions. Only a known applied publication matching the current reviewed payload SHA and actual source releases the parent's retained draft. Creation or original-result lookup alone does not discard that JSON. Creation grants no source or binding rights. A rejected or unavailable publication leaves the in-memory JSON protected; a recovered creation reference contains no JSON and cannot restore a lost draft. The known creation result says that JSON still needs publication. No release or assignment occurs automatically.

An explicitly selected existing Application publication can enter release review without another publication. Editing or formatting its raw text clears that existing-reference intent and requires a fresh publication review. Completing the first shared-layer choice clears that setup when native review is admitted, so same-page publication-to-release advancement cannot reuse a stale candidate setup. Each release still pins explicit immutable references under its own current map precondition and original key.

Drafts and review intent use the existing protected state owners. Original references contain only their native family, exact target, original key and bounded lifetime metadata. They are retained before dispatch. Unknown delivery, timeout, denial and NotFound do not authorize replacement operations. Recovery reads the same original directly, without an editor or application-read prefetch, and joins the real callback cleanup. A known original commit remains distinct from the current read's cleanup result. Source publication and creation use separate originals.

The accepted R08 snapshot passed its fresh isolated desktop case in 52 seconds, and existing authoring separately passed in 53 seconds. Each saved native TRX guard and final parent SQL/Audit assertions passed. This verifies actual creation/delivery-loss/same-key recovery, exact memory-only JSON transfer, separate publication/release/explicit assignment, pinned-layer JSON and read-only denial. The earlier 51-second R08 pass and its 22 actual native / 6 controlled internal stories remain retained, alongside all failed batches. The 144 pure cases already include the 13 route/draft and 3 instance cases; a separate six-case permission-model recheck is not a final 145-case batch. Production/strict TypeScript passed. See the [R08 checkpoint](../../changes/CIS-0030/r08-implementation-checkpoint.md) and [verification](../../changes/CIS-0030/verification.md) for exact accepted inputs and proof limits.

Only the bounded R08 scope is accepted. The separate fresh rollout attempt failed with an unresolved Node fast-fail before rollout, so original rollout/adoption acceptance remains open. Runtime pins/flags are unchanged; no green combined batch, whole-portal completion, Scheduler runtime or external deployment qualification is claimed. The manual-only Shared dataset is built and source-reviewed, but its finite live session and handoff are still pending.
