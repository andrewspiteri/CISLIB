# HC12 selected platform source preparation

`CIS.Verification.HealthcheckPlatformPreparation.RunAsync(string[] args)` is an
internal C# utility for the coordinator to dispatch. Arguments are exactly
`--repo-root <CisLib-root> --output-root <new-or-empty-directory>`.
It performs no restore, build, native tests, WSL operation or toolchain discovery.
Authoring has not executed this utility and is not native platform evidence.

Output contains `source/`, an immutable `source-inputs.json` and mutable
`preparation.json`. The input manifest lists repository-relative path, SHA-256 and
length in ordinal path order; it also carries a digest of path/hash LF records.
The utility rechecks source immediately around each copy, the complete selected
source inventory after copying, the exact destination inventory and the manifest
hash before marking preparation successful. A failed attempt retains its partial
output and failure record; retry with a new empty directory.

Selection includes the complete `implementations/csharp` tree after generated and
linked entry exclusions; Healthcheck contracts/conformance/current compatibility;
all `planning/healthcheck` frozen inputs/evidence; root README/AGENTS/editor/Git
attributes/ignore; root `eng/test.ps1`; the actual C# publication ledger; and
`.github/workflows` when present. The ledger is needed by initial compatibility
tests; the root script is the established focused native launcher. Logging
contracts/conformance are also explicitly copied as unchanged native build and
discovery prerequisites: IntegrationTests copies its configuration contract and
existing MSTest dynamic-data catalogs read conformance fixtures during discovery,
including when Healthcheck is the selected filter. No tracked source is rewritten
to make Linux checks pass.

Every linked/reparse entry and excluded output/metadata directory encountered is
recorded in omissions. The snapshot excludes `.git`, `.cis/local`, `.artifacts`,
`bin`, `obj`, `.vs`, `TestResults`, `StrykerOutput` and `.stryker-tmp`, other language
trees, unrelated planning/review snapshots, nonselected compatibility history,
external tools, caches and runtime/SDK installations. Required native fixture
inputs remain selected even where they are binary historical files under the
C# verification tree. The snapshot is explicitly **not the full repository or an
exact-package/release replay snapshot**.

The coordinator owns dispatcher integration, native execution, transfer into a
new Linux directory and exact SDK/runtime/PowerShell pins. The selection supports
the actual Healthcheck unit, integration, business, current compatibility and
architecture test paths. Those commands still need fresh native build/output and
nonempty passed native results before any platform claim.
