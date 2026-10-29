# Receiving saved incremental mutation results

The prospective receiver accepts current, source-bound results from shared CIS
without merging or rewriting native reports. Its command is
`healthcheck-shared-mutation` in `CIS.DependencyChecker`. This is an HC12 mutation
check, not whole-Core or release approval.

The command has opt-in child-process evidence receiving through `childEvidence`.
Component checks pass; an actual captured native trial and adoption remain pending.
Missing or invalid child evidence holds Pipes and ASP.NET results. Optional
disposition references use the existing strict maintained review reader; supplying
a reference does not approve a new review. A development profile passing 90%
alone cannot qualify the module.

## Current inputs and saved work

The input manifest selects `cis.healthcheck.shared-mutation-inputs/v1` and
`cis-native-completed/v1`, names one OS lane, and pins the installed .NET host,
shared CIS host, Testing assembly and process-helper assembly by path, SHA-256 and
byte count. Each profile descriptor pins its original profile, snapshot root and
any explicit refinement profiles. Run a lane on its matching native host.

For each descriptor, the checker invokes the pinned
`cis test incremental verify --repo <snapshot> --profile <profile>` command.
Explicit refinements use repeated `--refinement-profile` arguments. This is a
read-only verification of saved work; it does not schedule tests or mutations.
The original native reports remain the outcome authority. The shared receipt
binds their commands, inputs, test identities, recovery and postflight results.

The `--repo-root` checkout supplies the current maintained source inventory and
configuration. Relevant source, test and build inputs must still match that
checkout. A completed result from an older snapshot is pending when those inputs
changed. An unchanged source file alone does not establish a current result.

Without `currentProfile`, the receiver compares the receipt's entire declared
input inventory and holds a whole profile when any relevant input changed. A
descriptor may instead pin a `currentProfile` file under `--repo-root`, using the
same path/SHA-256/byte-count object as its original `profile` pin. That file must
be byte-identical to the original profile. Stage it and its unchanged template
explicitly before verification; the receiver creates neither file.

This optional path invokes the pinned current shared CIS planner against the
current target. Both graphs must be complete and fresh. The receiver pins their
databases and manifests, checks their build identities, and checks every current
and original maintained test-project C# source against its graph inventory.
The complete test-file path inventories must agree. A new or deleted test file
therefore holds reuse, even if it is absent from the old selected closure.

Each batch must reproduce its exact unit and mutation fingerprints, selected
tests, dependency paths, native filter and current source/test/dependency hashes.
Those fingerprints also bind current edges, graph schema/extractors, profile,
configuration, tools, producers and environment. Configuration and non-C# build
dependencies retain the global currency check. A changed C# file outside a
freshly reproduced batch closure may differ; checking only old closure members
cannot establish this independence. Older selector fingerprints are never
rewritten or relabeled as current. Failed comparisons remain explicit per-source
pending entries, and missing sources still prevent a qualification aggregate.

Select the frozen qualification candidate with `--repo-root`; later verification
tool edits in another checkout do not redefine that candidate.

Each accepted original profile must cover complete files with Complete mutation
level and the maintained cohort filter. Reviewed execution settings may differ
within the bounded configuration validator; arbitrary configuration differences,
line-only diagnostics, native since/baseline selection and foreign scope cannot
qualify. A generated configuration template is compared structurally with the
maintained configuration rather than requiring that transient path in the current
checkout.

An original qualification profile cannot set a nonnull
`rerunAdditionalTimeoutMilliseconds`. Explicit refinement profiles may use the
shared verifier's bounded rerun-only override. This cannot change the original
native Timeout or award credit to a timed-out or incomplete rerun.

Historical reports and their scores retain their original policy. A producer
revalidation records a reviewed equivalent tooling change; it does not make
changed source or test inputs current, or approve a different selection.

## The mutation gate

All 46 current Healthcheck source files must be present exactly once across the
41 Unit/runtime, four Pipes and one ASP.NET files. Files with no generated
definitions remain part of that inventory. Incomplete scope has no qualification
aggregate. Native IDs are local to each report; the result retains every original
report hash, native ID and source-bound definition identity while assigning
separate in-memory composite IDs for arithmetic.

At complete scope, the conservative union must reach 90%. Each partition need not
independently reach 90%. An original Timeout stays in the denominator and receives
no kill credit. An explicit, current rerun that verifies the exact definition as
Killed can supply separate credit. The maintained model supports source-bound
confirmed `NonTermination` dispositions without kill credit or denominator removal,
but this shared receiver does not yet bind those physical review records. Timeouts
therefore remain unresolved here unless an eligible exact-definition rerun closes
them. Current completed
native failures follow the approved prospective outcome policy. Genuine execution,
cleanup and child-origin failures remain holds under either disposition.

Every remaining Survived or NoCoverage definition needs a source-bound
disposition. Reviewed noncritical misses stay in the denominator; an equivalent
disposition needs its own reviewed justification. Supplied approval booleans are
not evidence. Optional manifest `dispositions` references currently accept only
`Equivalent` or `ReviewedNoncritical` records for Survived or NoCoverage definitions;
they reject `NonTermination`. References must match existing maintained records
and their current physical evidence closure.
The adapter then binds each record to the exact accepted original report,
source, native ID, status and full definition. It cannot approve an unknown
record, turn a review into kill credit, or remove execution and child holds.
No review is approved by this document or by a supplied flag.

## Running and retaining a check

```text
dotnet CIS.DependencyChecker.dll healthcheck-shared-mutation \
  --repo-root <current-checkout> --manifest <pinned-inputs.json> \
  --output-root <new-empty-output>
```

The command writes `results.json`. Exit zero means its mutation qualification
rules passed; exit one includes incomplete scope or an unresolved hold. Invalid
inputs fail rather than producing a passing result. Coverage, applicable test
families, package/compatibility checks and platform qualification remain separate.
`currentComparisons` records the bounded plan identity and each source's reuse
decision. `dispositions` records accepted or held review bindings. An absent
current comparison or review does not imply success.

Retain the small receiving result, input manifest and saved native reports while
needed for the active gate or a failure investigation. Reverification also needs
the pinned input and tooling files to remain available. Intermediate build
products and transfer archives are transient. Use shared CIS storage limits and
cleanup rather than retaining another copy of every intermediate build.
