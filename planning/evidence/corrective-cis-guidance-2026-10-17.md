# CIS guidance correction — 17 October 2026

Review findings 17-F4/F5 are applied. The pre-existing graph-recovery guidance, graph-input exclusions and October 15 tooling receipt are retained together; the unrelated Type Manager Web launch settings are excluded from this batch.

The component profile uses a file-specific root for JsonlInputSelector.cs, preserving the development checker's directory ownership. It adds the Type Manager build helper, examples and fixtures, and discovery test logger. It removes the stale ten-project/Python description. The exception-task query is explicitly historical.

Executed against the resulting live profile:

- `eng/cis.ps1 graph build --workspace . --format agent`: exit 0, 11,134 nodes, 55,233 edges, zero errors/warnings.
- `graph status --repo . --format agent` and `graph validate --repo . --strict --format agent`: valid and fresh, exit 0, zero diagnostics. Build `sha256:b6048e87caf5f1a356ffac6f39fc0e595879f1a50e7441f3f61f19645d1aee7d`.
- Bounded component queries resolve all five corrected/new component roots uniquely, with no truncation. The compiler-bound `CIS.Verification.JsonlInputSelector.Select(string)` resolves uniquely to component `logging-wire-examples` and its canonical file, line 22.

An overlapping query during the rebuild observed the previous stale generation and then a busy writer; it supplied no validation credit. All results above were obtained after the writer completed. Graph validation establishes structural consistency, not complete call/test coverage or runtime/release qualification. Planning/reference-source exclusions remain explicit. This batch changes no library runtime behavior. No Claude service was used. The original roadmap goal remains paused.
