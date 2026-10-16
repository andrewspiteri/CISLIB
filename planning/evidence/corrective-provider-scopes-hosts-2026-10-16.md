# Native provider scope, operation, ownership and host readers

16 October 2026, based on `5ae3c893`. Corrective Batch B continues under the user's instruction to migrate the remaining Python checks. This slice changes verification only; shipping Core, package dependencies, publication records and historical snapshots are unchanged. Review is local, with no Claude review or transfer.

## Native migration

| Former reader | Reconciled legacy controls | Native corruption cases | Physical modes |
| --- | ---: | ---: | ---: |
| Provider scopes | 25 | 34 | 13 |
| Operation identities | 25 | 34 | 12 |
| Provider ownership | 13 | 23 | 4 |
| Host filtering | 9 | 19 | 7 |
| Host scenarios | 19 | 29 | 7 |
| Total | 91 | 139 | 43 |

Five catalog cases and five positive attribute-property-order cases bring the focused integration selection to **192 cases**. Existing scope/operation unit selections contain **26 cases**. Every corruption first validates an unchanged physical child report, mutates an owned copy, then requires its specific rejection reason. The source audit retains every legacy ID/mode, with the three reason changes below explicitly recorded. Additional cases reject wrong PID/runtime, floating numeric kinds, blank/oversized records, extra result fields and altered physical resource identity.

Small shared C# reader checks provide strict JSON comparisons, framing, identity and drain observations. Existing process cases now use a shared native process helper that checks actual child PID, native OS/runtime, exact loaded fixture/Core/factory hashes, real file bytes and exclusive lease reacquisition. Commands, physical PID/file hash, full original report and host console output are retained in native TRX output. The helper permits the genuinely absent file in the all-disabled host-filter mode.

## Reconciliation with approved corrections

The [15 October behavior addendum](../../docs/modules/logging/review-contract-2026-10-15.md) supersedes the old readers:

| Old expectation | Current native observation |
| --- | --- |
| Invalid/incompatible borrowed scopes omit a frame and emit truncated events | Reject each invalid event once, with InvalidEventCount and no truncation; the later valid after-event remains usable. The incompatible-fields control now requires incompatible frame rejected; incompatible-marker injects an erroneous marker instead of removing one from a nonexistent event. |
| Non-string operation IDs become attributes or a later duplicate repairs an earlier invalid value | Owned scope creation rejects invalid IDs. The duplicates/nonstring cases retain rejection counts, parent identity and the actual surviving event sequence. The explicit-value control now checks owned ID errors. |
| An invalid nested host operation ID retains its parent's event with truncation | The invalid event is rejected; a later after-event has no inherited operation. omitted-loss now checks invalid accounting, and omitted-parent injects a leaked parent identity into the later event. |
| Full scope/operation attribute budgets can omit the provider EventId | Reserve numeric event_id before remaining fields. Native comparers check complete retained field prefixes, including the correct event/scoped precedence and late unvisited operation ID. |

No production behavior is changed to satisfy outdated scripts. Ownership, host interest, factory lifetime, callback identity, concurrency/order, Activity and shutdown assertions are retained and strengthened.

## Executed checks

Pinned SDK **10.0.401**, runtime **10.0.12**. All credited results have zero failures/skips.

| Selection | Windows | Ubuntu |
| --- | --- | --- |
| Integration behavior, corruption, catalog and property-order cases | 192 passed; `b48822d40fe343a8925790593abb4ea0` | 192 passed; `85ea98d1b6f34389accba7dcb566f02d` |
| Existing unit scope/operation cases | 26 passed; `838a0f9390484a56955b0859717d3447` | 26 passed; `30b6730c074043c6a7a7eadbe0896813` |

Windows focused artifacts are below `.artifacts/csharp/focused/<id>`; Ubuntu uses `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/<id>`. Windows focused checks preceded whitespace formatting; Ubuntu rebuilt the formatted source. The comprehensive final Windows checkpoint below includes the formatted source. Linux uses scoped current copies in a separate existing checkout with isolated outputs, not a full clean candidate/package replay. The source reconciliation audit is retained at `.artifacts/corrective/provider-readers/control-audit.json`.

## Retirement and remaining work

The five retired readers are recoverable from the base revision; their original SHA-256 values are:

- `verify-provider-scope.py`: `0de0e0a0db0f31f177f58d5929e729fb26c7e9974a0cac696fd389c8f0701c16`.
- `verify-provider-operation.py`: `dc1eab8b8e29b826ce57dd74c44b1a79f59c8b4c8618d1f1e1e7cdcf853f4697`.
- `verify-provider-ownership.py`: `5b28d1d2a7785db770d49cfef4dc7e3f516e5da7b2e7da99fc4b546377873948`.
- `verify-provider-host-filter.py`: `6f133ad7402152145ff923fddf8cd7404ae1ac842f4615547f95088d6aee9f62`.
- `verify-provider-host-story.py`: `db8b6b630dd4d499c7442b6204d3c65133880fe0adaa6760f9c93206cdcca3b5`.

No maintained reader imports these entry points. All maintained provider readers are now native C#. **76 nonhistorical Python files remain**: 67 other DiscoveryHost readers/helpers, six engineering utilities and three Type Manager fixture utilities. Four additional historical readers remain preserved. Substantive PowerShell policy migration, supported IDE acceptance, full coverage/mutation/compatibility, runnable examples and final release qualification remain open. This does not restart the original mutation queue or claim publication.

## Final Windows Developer checkpoint

Run `d00f7cc9ffbd4a7e95138a0eff10af30`, **06:48:13–06:59:07 UTC**, passed **3,659 unit, 1,594 integration, 619 architecture and 24 business cases: 5,896 total**, zero failures/skips, exit 0 and zero validation issues. Restore, formatting, solution build, compiler consumers, package/dependency boundaries, native discovery reconciliation, publication-ledger validation and the existing configuration scaffold also passed. This Developer run does not execute mutation or compatibility; no new pass for those families or complete release qualification is claimed.

Retained checkpoint: `.artifacts/corrective/provider-readers/developer-d00f7cc9ffbd4a7e95138a0eff10af30`. Summary SHA-256: `6c5e69719e03118bd90392af77d9066a28f6afd9224e39a9dfb87d8c15bc7eaa`. Captured source-inventory SHA-256: `49aa3dbefd506368a1fe3bafe117b8a20a07a3f229ee3827faf589fcc7ca254e`. The observed candidate is `5ae3c893` plus this migration and preserved unrelated workspace edits, not a clean release checkout.

The Windows/Ubuntu focused integration reports contain the same 192 native case identities, with no differences, recorded in `.artifacts/corrective/provider-readers/native-case-audit.json`. All 43 former physical modes remain required. The new 139 corruption cases plus ten catalog/property-order cases account for the 149 additional full integration executions.

## Graph navigation

Final refresh: **10,862 nodes, 51,609 edges, zero warnings/errors**, build `b94bd52e00211ee9fd8cf5a1b285a4d406453d5591ecf8b714ae8628cdf0dd3f`. Exact class queries resolve the scope verifier and shared reader helper uniquely; the depth-one scope-verifier query is fresh/untruncated with four nodes/four traversals and proposed edges excluded. Compiler/semantic extractors remain 6/3. Derived navigation facts do not establish complete semantic relationships or test coverage.
