# Native provider mapping and structured-state migration

16 October 2026, based on `75b4d34c`. Corrective Batch B under the user's request to complete remaining corrective work. D/E implementation is already in place; this change migrates verification only. Shipping Core, dependency policy, frozen historical fixtures and publication records remain unchanged. Review is local; no Claude payload is sent.

## Preserved observations

- Mapping retains all five modes and all 28 former corruption controls, including exact rejection reasons, formatter/exception identity, numeric EventId, severity/category/name/message, timestamp validity, typed structured values, trace/span and direct-Core equivalence.
- State retains all nine modes and all 34 former corruptions: formatter and state identity, no arbitrary ToString, bounded traversal, invalid/truncation accounting, stop shape/summary/boundary, typed scalar/collection normalization, scope precedence, full array/field prefixes, depth and captured Activity.
- Mapping adds eight corruption controls (blank/oversized records, formatter/PID/schema/EventId numeric kinds, and zero/nonhex span) plus catalog reconciliation and a positive property-order case.
- State adds eight corruption controls (blank/oversized records, formatter/PID kinds, node/depth bounds and middle-array/field values), plus catalog reconciliation.
- Each corruption first validates an unchanged fixture observation, changes one owned copy, then requires its specific assertion. JsonNode comparisons preserve Boolean/string kinds and the former readers' integer-versus-floating-number expectations while accepting object-property ordering.
- Existing native process cases now bind to the actual child PID, exact loaded fixture/Core hashes, native OS/runtime, physical log bytes and exclusive lease reacquisition. PID/resource corruptions are exercised against real child reports rather than just in-memory fixtures.
- The two legacy readers have no remaining maintained importers or invocation references. Historical snapshots/receipts retain their original content; the retired source is recoverable from the base commit.

## Executed checks

Pinned SDK 10.0.401 and runtime 10.0.12; all credited cases have zero failures/skips.

| Native selection | Windows | Ubuntu |
| --- | --- | --- |
| Mapping/state behavior and verifier unit cases | 95 passed, run `7b52fbf1db07480098e2bf2cf2de9175` | 95 passed, final formatted-source run `0249c785a02a4301a299b8597c7bc964` |
| Physical mapping/state processes | 14 passed, run `c64160016d5947118bb017b134942017` | 14 passed, final formatted-source run `9df14a456fa6417a913c9180fcdc1265` |

Windows reports are below `.artifacts/csharp/focused/<id>`. Ubuntu reports are below `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/<id>`. Linux uses an isolated checkout with scoped current source copies and separate build outputs, not a clean whole-repository replay or one shared cross-platform package. An initial WSL invocation lost the pipe in its filter through the default shell and exited 127; it is not credited. The corrected invocation uses WSL --exec and passes the complete filter argument directly.

## Retirement and remaining scope

- `verify-provider-mapping.py`: SHA-256 `1eb08d2824242aedd4dda55014ed19680d753d284e55621bfbe4178a76d90554`.
- `verify-provider-state.py`: SHA-256 `84e1ea8e619a5d2106159a9b2340ef2b6729a0ca2732458db137948f82b041d2`.

The broad migration remains open: 81 nonhistorical Python files remain to reconcile individually after this retirement, including other DiscoveryHost readers and Type Manager utilities, plus policy-bearing PowerShell. File presence is not proof of an active invocation. In particular, the operation-scope reader contains expectations predating approved scope corrections and must be reconciled against current behavior rather than copied literally.

Supported IDE acceptance, complete runnable examples, current full coverage/mutation/compatibility, exact-candidate two-lane qualification and release/bundle/publication remain open. The old mutation roadmap is not automatically restarted. The complete Windows Developer checkpoint is recorded below. Graph navigation remains development evidence and does not establish release qualification.

## Final combined Windows checkpoint

Developer run `aaf7dc1de1f84c35895f5dce13278b0f`, 06:00:05–06:12:50 UTC, passed **3,659 unit, 1,445 integration, 619 architecture and 24 business cases: 5,747 total**, with zero failures/skips, exit 0 and zero validation issues. Locked restore, dependency checks, formatting, solution build, compiler consumers, packaging/boundary checks, native discovery reconciliation, ledger validation and the configuration scaffold host also passed. Mutation and compatibility are not executed by Developer; no new pass for those families is claimed.

The exact checkpoint is retained at `.artifacts/corrective/provider-mapping-state/developer-aaf7dc1de1f84c35895f5dce13278b0f`. Summary SHA-256: `36542b7dd549774100f2a82d5bfcc3775d5a1fe7ce2f710d382d838e237c7ae0`. Captured source inventory SHA-256: `9412c16532cf992e64226d152845a33b007e51815cff0126e6d93f1a0a292220`. This is the observed working-tree candidate based on `75b4d34c`, including these corrections and preserved unrelated workspace edits; it is not a clean release checkout.

The final Ubuntu runs rebuilt the formatted source and passed 95 unit and 14 physical process cases. Earlier Ubuntu runs `fb1f4d0730fe41ff9176e716613fc689` / `d7174888096043a5aa903f9a169a4cd0` passed before a whitespace-only formatting change; the final IDs in the table supersede them. A source audit maps every old corruption ID/mode/reason to a native DataRow with none missing; native report comparison finds no differences in the 95 unit case identities across Windows/Ubuntu. Audits are retained as `control-audit.json` and `native-case-audit.json` in the corrective artifact directory.

## Final graph navigation

Graph refresh succeeded with **10,910 nodes, 51,431 edges and zero warnings/errors**, build `97c947e8ebba8c2c1ef80f811886f4656e6dc60ef7c02699b7845e9a33828e28`. Exact class searches resolved both new verifier classes uniquely without truncation. The depth-one mapping-class query returned four nodes/four traversals, fresh and untruncated, with proposed edges excluded. Compiler/semantic extractors remain 6/3. These observations support navigation only; no complete semantic relationship or test-coverage claim follows from graph counts.
