# Receiving saved incremental mutation results

The prospective receiver accepts current, source-bound results from shared CIS
without merging or rewriting native reports. Its command is
`healthcheck-shared-mutation` in `CIS.DependencyChecker`. This is an HC12 mutation
check, not whole-Core or release approval.

The command is still being validated. In particular, it deliberately holds Pipes
and ASP.NET results until child-process evidence receiving is implemented, and it
does not yet load and validate survivor-review files from disk. A development profile passing
90% therefore cannot qualify the module through this receiver yet.

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

The receiving checkout supplies the current maintained source inventory and
configuration. Relevant source, test and build inputs must still match that
checkout. A completed result from an older snapshot is pending when those inputs
changed. An unchanged source file alone does not establish a current result.

The first implementation compares the receipt's entire declared input inventory.
It therefore holds a whole profile if one of those inputs changed, including a
declared test outside a particular batch's selected closure. Reusing a subset
across different snapshots still needs a comparison with the current graph's
selection, including newly added tests. Checking only the old closure's members
would miss new dependencies and is not sufficient.

Each accepted original profile must cover complete files with Complete mutation
level and the maintained cohort filter. Reviewed execution settings may differ
within the bounded configuration validator; arbitrary configuration differences,
line-only diagnostics, native since/baseline selection and foreign scope cannot
qualify. A generated configuration template is compared structurally with the
maintained configuration rather than requiring that transient path in the current
checkout.

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
independently reach 90%. An original Timeout remains a miss unless an explicit,
current rerun verifies the exact definition as Killed; expiry itself receives no
credit. Current completed native failures follow the approved prospective outcome
policy. Genuine execution, cleanup and child-origin failures remain holds.

Every remaining Survived or NoCoverage definition needs a source-bound
disposition. Reviewed noncritical misses stay in the denominator; an equivalent
disposition needs its own reviewed justification. Supplied approval booleans are
not evidence. The pure arithmetic model tests these rules, but the physical
loading and validation of review files is still outstanding.

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

Retain the small receiving result, input manifest and saved native reports while
needed for the active gate or a failure investigation. Reverification also needs
the pinned input and tooling files to remain available. Intermediate build
products and transfer archives are transient. Use shared CIS storage limits and
cleanup rather than retaining another copy of every intermediate build.
