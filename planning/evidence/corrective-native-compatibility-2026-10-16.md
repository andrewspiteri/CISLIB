# Native Type Manager compatibility and provider foundation migration

16 October 2026. Corrective Batch B, continued at the user's request. Logging's shared build/package/index window was reserved with its chat; the older mutation goal remains paused. Local review only, following the user's instruction to stop Claude reviews. Shipping Core, dependency policy and frozen baselines are unchanged.

## Preserved observations

`CIS.Core.CompatibilityTests` now contains native Type Manager compatibility cases. The 86 checks in the retired behavior verifier map directly to native cases:

| Former observation | Native method | Executions |
| --- | --- | ---: |
| 22 frozen behavior cases against candidate, rebuilt source consumer and positive isolated module | `FrozenBehaviorIsPreserved` | 66 |
| Nine deliberately changed behavior implementations | `DeliberateBehaviorRegressionsAreDetected` | 9 |
| Changed failure enum also observed through a thrown operational failure | `ChangedFailureValueIsAlsoDetectedWhenThrown` | 1 |
| Original source rebuilt against the frozen API produces identical consumer DLL bytes | `OriginalApiRebuildMatchesFrozenConsumerBytes` | 1 |
| Original source/binary consumer against candidate and positive isolated module | `OriginalConsumersRemainCompatible` | 4 |
| Candidate and positive module pass pinned API comparison | `PublicApiRemainsCompatible` | 2 |
| Renamed public property fails API/source/binary boundaries for the specified diagnostics | `RenamedPropertyIsRejectedAtEveryBoundary` | 3 |

One additional case proves that the original frozen consumer requires deployment of Core's approved Microsoft dependency closure. Its old dependency manifest predates that closure. The harness copies the exact two dependency DLLs and supplies an external `--additional-deps` manifest; it never rewrites the frozen consumer, its dependency manifest or expected behavior. Omitting the supplemental manifest must fail for missing logging abstractions; the unchanged consumer passes with it. This corrects a dormant deployment assumption in the previous script, not a production behavior change.

Twenty-four native guard cases cover strict JSON kinds/shape, allowed property ordering, mutation anchors, producer completion/candidate/source identity, baseline hash/inventory/content/traversal, and altered PDB identity/content or stale source. Default project candidates bind the portable PDB to the PE using both CodeView identity and the specified PDB checksum, then compare every Type Manager document checksum with the selected source. Explicit candidates retain the completed producer-report/hash contract. The PDB checksum follows the [Microsoft PE-COFF specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PE-COFF.md#portable-pdb-checksum), which excludes the PDB ID bytes. Candidate, dependencies, verifier source and frozen inputs are rechecked after each successful case and at class cleanup.

SDK 10.0.401, consumer runtime 10.0.12, ApiCompat 10.0.401 and tool runtime 8.0.31 remain pinned. All child processes have a 180-second bound, private package/home/cache paths and an explicit inherited-environment allowlist. Native TRX, not a partial custom report, establishes selected case completion. Unpublished development replay does not qualify a released history or an exact package.

The provider foundation migration strengthens the existing native fixture expectations and process cases. Eight modes preserve formatter laziness/error identity, category/event/message mapping, copied scopes, borrowed runtime lifetime, stop outcomes/counters, physical child/runtime/assembly identities, exact file bytes and exclusive lease release. All 24 former corruption controls remain represented; the physical PID mutation executes against actual physical reports. Four additional controls reject blank lines, oversized wire records and wrong formatter/PID numeric kinds. A separate native case reconciles fixture/expectation/shared-catalog membership. The conformance inventory requires 28 corruption cases, one catalog case and eight physical cases on both lanes.

## Executed evidence

Windows pinned Debug native runs have zero failures or skips:

| Selection | Passed | Focused run ID |
| --- | ---: | --- |
| Complete compatibility project after formatting | 111 | `de5d8e8bf47d420b98d8c6588e374cf1` |
| Provider foundation unit behavior/controls/catalog | 38 | `35f33145b7b44f4a8f64b5a3ffd72097` |
| Provider foundation physical processes | 8 | `0e01aa92c2dd48f5819fc681a712219f` |

Reports are retained under `.artifacts/csharp/focused/<id>`. Detailed compatibility child logs, observations, hashes, source producer and control mutations are retained under `.artifacts/csharp/type-manager-compatibility/<id>`; the native TRX contains that directory's identity. The selected Windows .NET 8 runtime archive was downloaded from Microsoft's release metadata and checked against its SHA-512, with the source record under `.artifacts/corrective/native-compat/api-runtime-source.json`.

The initial Windows PDB comparison used a whole-file checksum and correctly failed before consumers ran; the implementation now follows the portable-PDB checksum specification. A second harness failure used mixed Windows path separators in a diagnostic substring comparison; normalization of the owned root fixed it. Both initial full platform runs then passed 109/110, exposing the original consumer's missing deployment manifest. Those failed runs remain evidence; the corrected Windows run passes all 111 including the new negative control.

Final Ubuntu 24.04 native Debug runs also have zero failures or skips, in the isolated checkout at `.artifacts/corrective/batch-a/linux`:

| Selection | Passed | Focused run ID |
| --- | ---: | --- |
| Complete compatibility project | 111 | `be4426605ed14a21a0cc850cc6189e3e` |
| Provider foundation unit behavior/controls/catalog | 38 | `e30d1a0a08294a8ab1f6f31109b8bbb8` |
| Provider foundation physical processes | 8 | `512ba969b5f1400db52be95ee68fa525` |

Each Linux focused directory is below that checkout's `.artifacts/csharp/focused`. The complete compatibility TRX hashes are Windows `ca909db281456d8c5f323ec3c33f5f19fb1bc49d8d80ee6fe784aee9acdfc6e9` and Ubuntu `a97668bd3b4d1c034964bcc1ce2f158ea61c615fb67a20190a26efb29e7ef099`. Detailed compatibility roots are Windows `077372f128c743e79498be7ded8d29fc` and Ubuntu `a5890bd60bc945cf95e0696720b8da87`. Their `results.json` hashes are respectively `794be5292dd54ada895cfa26c10a274f7da2458a1848a4e3f67703264369f7d6` and `dcc798a1debc83f8bc456c9eeaf748acbc0a6f058c879e0287dc0157ee55db2f`.

These are separate native project candidates: Windows Core SHA-256 `e75624f9b048bfc2af7c4b58e90c4c661c5d7fcee618d99d02b3d629209c1c53`, Ubuntu `37af3f54cdd103fd575ca8bbe6204ab8ea900484e6694c004e4cd08e1e4d3ed9`. Each is bound to its actual PDB and source; this is not a claim of one shared package replayed across platforms.

The Windows Release-configuration Developer checkpoint `3b3f964e333545a88ab1fbeed2d7a947` ran 04:32:55–04:46:53 UTC and exited 0 with no validation issues. It passed 3,578 unit, 1,445 integration, 619 architecture and 24 business cases: **5,666**, with zero failures/skips. Locked restore, 27-project dependency checks, format, build, compiler consumers, packaging, packed/compiled boundary checks, discovery reconciliation, publication-ledger validation and the existing configuration scaffold host also passed. The 111 compatibility cases above are separately executed, not credited to this profile. No new full coverage/mutation, full Ubuntu Developer profile, IDE or release qualification is claimed.

The retained checkpoint is `.artifacts/corrective/native-compat/developer-3b3f964e333545a88ab1fbeed2d7a947`. Summary SHA-256: `366fb45c5cc45e8f996c4d2aa45ce587f5bcec247a4d03874003fd72c203cec2`; captured source inventory SHA-256: `f4dc24bc18c50dc78ace45d96a1c0e4e2b9332e3642d8b51f4d74c5582d218c6`. The run used `bc1fe2c9` plus these changes and existing unrelated workspace edits, before finalizing this receipt; it is not a clean release checkout.

Graph refresh succeeded with 10,864 nodes, 51,192 edges and zero diagnostics, build `d6bf63a21256a80b03c135f3997d8445d9e22fb0a0848b1dc1da0157cf5072f1`. Exact class lookup and depth-one related query for `CIS.Tests.CompatibilityTests.TypeManagerCompatibilityTests` were fresh and untruncated (five related nodes/edges; proposed edges excluded). Compiler extractor 6 and semantic-facts extractor 3 establish navigation bindings only; this bounded query does not claim complete call/test coverage. Derived graph state remains local.

## Retirement and remaining scope

Retired maintained entry points (historical source snapshots and receipts remain intact):

- `implementations/csharp/eng/verify-type-manager-behavior.py`, SHA-256 `3916ed74544e3d8a95d5b15dfa7c17fa36e706f2f08978bf3db214f365732640`.
- `implementations/csharp/tests/fixtures/DiscoveryHost/verify-provider-foundation.py`, SHA-256 `bab56d168e6899ae06103655d2779331852f86451f83fa7efe320da06413a3c5`.

Neither entry point is imported by the other maintained readers. Reproduction commands, explicit candidate options and tool prerequisites are in `implementations/csharp/tests/README.md`. Compatibility remains a separately invoked family and is not credited by the ordinary Developer profile.

Batch B remains open for other DiscoveryHost readers, Type Manager runtime/inventory/package/examples/instrumentation/performance/mutation utilities and policy-bearing PowerShell. There are 87 Python files under the C# tree after this retirement, including four explicitly historical readers: 83 nonhistorical files remain to reconcile individually. Presence alone does not establish an active invocation. IDE acceptance, final examples and full release qualification also remain open. No thresholds, historical expectations, release gates or publication records were changed.
