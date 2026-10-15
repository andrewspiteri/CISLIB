# WP07-S02-T03 — ownership and output/package boundaries

Implementation, approved read-only Claude review and local resolution of all six findings are complete. Both final native Developer profiles pass. The [current guide](../../../../docs/architecture/csharp-ownership-boundaries.md) applies the user's static-container simplification and preserves mutable setup, runtime capture and the approved Type Manager package assets. No shipping source/API/dependency, Type Manager implementation or package layout changed.

| Native lane | Unit | Integration | Architecture | Business | Total | Stages |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Windows x64 | 3,415 | 1,439 | 595 | 24 | 5,473 | 16 passed |
| Ubuntu 24.04 x64 | 3,415 | 1,439 | 595 | 24 | 5,473 | 16 passed |

Both final runs exit 0 with no failures/skips, using the same frozen source snapshot `010e139bfcf2ba08d9de86d85370f1a52b67e3d3`. All 1,114 native source records agree across lanes; all 944 maintained verification inputs match canonical source. All 102 protected shipping/Type Manager/original-plan/publication inputs and the other chat's launch profile are unchanged. Final bookkeeping documents are updated after execution without changing tested code.

Forty-two new C# executions comprise one real native positive, twenty-six isolated compiled-fact violations and fifteen actual copied-package/observation controls. Every negative first proves its unchanged positive. A separate direct native TRX definition/execution-ID audit proves all 42 rows passed on each lane. Each final inspection reports 20 immutable types, 846 contract use sites, 3 approved mutable setup properties, 14 package entries and zero violations.

The guard checks captured field/accessor/return shapes and permitted value graphs, excludes inherited mutable state, binds readonly environment properties to readonly storage, checks nested runtime/logger/provider caller-state fields and rejects contract-to-output type uses. It inspects the actual ZIP, exact assets, duplicate observations, maintained source build scripts, README, built XML, both DLL hashes and version-only generated props. Readonly fields/interfaces/views do not establish copying; existing executed mutation, GC and frozen-runtime/result tests remain required. This does not restore the superseded environment publication framework.

The user directly approved the exact 25-file, 179,162-byte transfer to Anthropic Claude after automatic approval review rejected standing authorization. The payload SHA-256 is `d8a8516c8490c2742903ba03ebd9a634515248e2cc7c7d186cb269a39e07cea0`; exact approved bytes are retained in `claude-approved-payload.txt.gz`, with manifest and actual response alongside. Claude returned **CHANGES_REQUIRED**, not a final PASS. The [disposition](claude-disposition.md) records all six focused fixes and their native verification. No second external transfer or follow-up PASS is claimed. Earlier reviewed 33-control profiles remain under `pre-review/`, distinct from final results.

Each lane retains its full Developer report tree archive plus summary, validation, source inputs and discovery reconciliation. Absolute paths describe actual local execution checkouts. SDK 10.0.401/runtime 10.0.12 and native Ubuntu facts are retained. Available restore caches are used; no cold-cache/offline claim is made. CIS graph builds are partial and status still exits 4 with the existing readonly SQLite cache error; graph output is not full semantic assurance.

The separate Release negative control exits 2. Comprehensive isolated source/binary negative qualification remains T04; complete coverage, mutation, cumulative compatibility, examples/skills and full release obligations remain required. No publication, push, durable archive or complete offline replay is claimed. `evidence-index.json` hashes the retained task material.
