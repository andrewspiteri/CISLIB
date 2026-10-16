# Native environment and configuration verification

16 October 2026, based on `61da450d`. This corrective Batch B slice changes test infrastructure only; shipping Core, dependencies and historical baselines are unchanged. Review is local, with no Claude transfer.

## Migration

The existing native integration classes now execute every current shared resolution row (26), combined-validation row (24) and applicable native path row (13 of the 14-row union). Five real static-construction scenarios remain, with stronger readonly/identity/metadata/UTC/default/override/failure assertions. A separate four-process cohort requires unique nonempty instance IDs. One catalog case and three supplemental tilde/marker cases bring the focused selection to **73 cases per lane** (51 additional native integration executions).

Each physical run binds the actual child PID, exact runtime 10.0.12 with roll-forward disabled, native OS, fixture/Core/input hashes checked before and after execution, complete parent environment and current directory, ordered recognized reads and the absence of logging filesystem outputs. Native TRX output retains commands, physical PIDs and original child JSON. Caller-supplied configuration transports remain test-only; they do not reintroduce a production startup framework or claim that controlled host flags describe the actual terminal/debugger.

Resolution checks preserve exact invalid code/key order, absent validated values on failure, all configured properties and independent output decisions. Combined validation preserves the complete issue multiset, nonempty guidance, safe exception text and diagnostic exclusions, expected value subsets and stable stored projections across real child directory changes. Path checks retain platform applicability, invalid-path key adaptation already used by current C# unit tests, safe errors and literal prepared paths after directory changes.

The Windows tilde harness calls the OS solely to confirm an existing 8.3 alias and its different expanded name; Core must preserve the alias in absolute, captured-base and private-use-marker inputs without creating the leaf. Linux exercises the same three input shapes under an actual literal-tilde directory; it makes no Windows alias claim. Existing unit tests continue to cover lexical normalization, Unicode markers and private-use-marker exhaustion.

## Retirement

Five current Python drivers are retired: `verify-constructor-options.py`, `verify-configuration.py`, `verify-validation.py`, `verify-paths.py` and `verify-path-tildes.py`. Their source hashes and the helper hash are retained in `.artifacts/corrective/configuration-readers/retirement-audit.json` and recoverable from the base revision. They contain no separate corruption-control matrices. `configuration_expectations.py` moves byte-for-byte into `historical/`, because the preserved historical startup qualification still imports it; no maintained check imports Python configuration code.

This leaves **70 nonhistorical Python files**: 61 DiscoveryHost readers/helpers, six engineering utilities and three Type Manager fixture utilities. Five historical Python files, including the moved helper, remain preserved. The original roadmap stays paused while authorized corrective migration continues. Full Ubuntu Developer, current coverage/mutation/compatibility, examples and release qualification remain open.

## Verification

Executed native run identifiers and the coherent Windows checkpoint are recorded below. Focused checks do not qualify unexecuted families or a shared release package.

Original source hashes (SHA-256):

- `verify-constructor-options.py`: `3285f6143003ba8ad41f766102101d8f2cd8f977807332330d0bb6beffa1e714`.
- `verify-configuration.py`: `edecd7469788c3fed73941166762b4b42f3a9f36da5a88f68970b0c6bfd5cd40`.
- `verify-validation.py`: `254d50e0b4c33c12cc9096e37d31b685dcf89e0ddb0de656f4f44e7bdd219d8a`.
- `verify-paths.py`: `c646308eb0916f8773b5736d3d60958ca27b2bb69b968180680fa03a07ab7355`.
- `verify-path-tildes.py`: `33e9ea6210bfa3066de91a2e77ec36cb4dee262f8ef400fd929d0d93c7d569e4`.
- `configuration_expectations.py`: `ebdaffbbd7c7c40a88671991ac5809665b8eb5da0cdcd57b444e9bac68ed81be`.

Windows focused run `d44fcd300c8d42048837d16ee3474288` passed 73 cases before final import/newline cleanup. Ubuntu final run `dd42c04423f94baaaefd2455ee02c2c0` rebuilt the final source and passed 73 cases, with zero failures/skips. An earlier Ubuntu run `b841b8f2168a43e29684dd8fefcff0b9` also passed 73. The case audit records only the two declared platform-specific path names and zero unexpected differences.

The first comprehensive Windows attempt (`49033f1bd08744908756a81227341539`) stopped at the import-order formatter; no native family from that attempt is credited. Its output is retained under `.artifacts/corrective/configuration-readers/format-attempt-49033f1bd08744908756a81227341539`. The using directives were ordered correctly before the final attempt.

## Final Windows checkpoint and graph

Run `1b1cffc4ed614e9ea0f33813f3d2312d`, **07:44:05–07:57:04 UTC**, passed **3,659 unit, 1,645 integration, 619 architecture and 24 business cases: 5,947 total**, zero failures/skips, exit 0 and zero validation issues. Restore, format, normal solution build, compiler consumers, package/dependency boundaries, discovery reconciliation, ledger validation and the configuration scaffold also passed. Mutation and compatibility were NotRun. This does not qualify the complete corrected release candidate or a full Ubuntu Developer lane.

Retained checkpoint: `.artifacts/corrective/configuration-readers/developer-1b1cffc4ed614e9ea0f33813f3d2312d`. Summary SHA-256: `82a80c16675e30bd3651584fd3fcaff06ed12ae2d9359b1146969dd27b6dc5e1`. Captured source-inventory SHA-256: `b5c0a753ac1c57a9ab9cef7a205015617c4198260c21bf2fdad4f2f069ed761b`. The observed candidate is the base revision plus this migration and preserved unrelated workspace edits; it is not a clean release checkout.

Final schema-2 graph refresh: **10,844 nodes, 51,668 edges, zero warnings/errors**, build `fc75f49ea40eeec219c3cee322fc79c32b7d39db00bdcd2dd827f2b2e45c6605`. The constructor class and new process helper resolve uniquely; the constructor's depth-one related query is fresh/untruncated with four nodes/four traversals, proposed edges excluded. Compiler/semantic extractors remain 6/3. These are bounded navigation observations, not complete semantic or test-coverage evidence.
