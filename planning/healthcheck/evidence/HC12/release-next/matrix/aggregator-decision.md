# Existing validation reuse

No new release aggregator is implemented. The current `VerificationEvidence`
validator already checks profile/lane identity, confined hashed report references,
native discovery and execution IDs/methods/names/source paths, required case minima,
counter agreement, failures/skips and matching reconciliation run/source identity.
It deliberately refuses missing families, planned cases and declared release gates.
The module's current package, coverage, identity and mutation harnesses already
produce their own exact-input records. A second generic aggregator would not make
the missing global Release validator or external integrations exist.

The coordinator can retain actual Release refusals and append the applicable
current module receipts to the new HC12/HC13 integration record. Prior receipts
are historical inputs, not automatically accepted current-candidate passes.

If a later integration needs a thin Healthcheck evidence importer, its minimal
contract should be:

* Independent expected candidate package/Core/helper/source/harness/toolchain pins,
  actual lane identity, exact required gate list and source-case applicability.
* Existing completed validator/producer outputs and raw native reports, each pinned
  before reading, with source inventories rechecked after import.
* Explicit rejection of absent/changed/out-of-root/duplicate reports, mismatched
  run/source/package/lane/toolchain identities, preliminary/incomplete records,
  failed/skipped/zero native tests and failed strict thresholds. Native mutation
  statuses, compiler rollbacks and ignored filters remain source-bound observations.
* Initial-history `NotApplicableEmptyActualHistory` only after checking the actual
  pinned empty ledger. A nonempty ledger requires real cumulative preservation;
  N/A never supplies fabricated released-consumer or historical-runtime evidence.
* Separate fields for locally measured gates, inherited global Release blockers,
  unavailable external integration and complete release qualification. Any required
  pending/failing/unvalidated gate keeps `qualified=false`; no partial-gate count
  or passing native score is promoted into whole-release success.

That contract adds no new family, dependency, public library API or waiver. Its
implementation would need a concrete integration requirement and native negative
controls; it is not necessary merely to document the current truthful refusal.
