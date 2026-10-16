# Native stop start, drain and join reader migration

16 October 2026, based on `6fb623ec`. Corrective Batch B changes test readers/fixtures and current documentation only. Shipping Core, dependencies, public behavior, original vectors and historical evidence remain unchanged. Local review applies; no Claude transfer.

Three Python readers are retired. Native C# retains all 32 modes (16 start, eight drain, eight join) and all 74 original report corruptions (30/21/23). Each corruption follows a validated physical child report and retains its original scenario/identity. Native result checks now include exact result shape, canonical decimal Int64 counters, UTC labels, complete output partitions/order, writer calls/order, original bounded UTF-8/LF records and producer PID, rejection snapshots, eventual dispositions/faults, shared identities, timer ownership and caller-only cancellation. The start reader also retains the exact output shape.

Physical checks use the existing process helper with explicit flat-header support: actual child PID/runtime/OS, before/after managed hashes, original command/report retention and full parent environment/CWD preservation. Mode catalogs are reconciled against the fixture mode sets; children retain 60-second bounds. The normal dual drain checks actual file bytes and exclusive lease release after the child completes.

## Current behavior reconciliation

The old Python drain check described an entirely abandoned queued tail as Completed, while current LogFlushResult records an expired overall deadline as TimedOut. The existing native expectation and current runtime are retained. The original drain guide and generic completion table are corrected to reflect the already implemented shared deadline/worker-cleanup behavior. No production behavior is changed by this migration.

The old reader also required an unstarted flush registration behind a still-active write to disappear at the deadline. The current fixture/kernel retains that registration until the active write settles: queued-only has zero controls; single/blocked writes retain one; cooperative cancellation may have settled it already. These concrete current expectations and eventual zero registrations replace the stale rule. Historical script bytes remain recoverable at the base revision.

Stronger unit observations exposed a fixture race: manual release could win against an asynchronously requested cooperative cancellation. The fixture now awaits actual cooperative completion/cancellation before manual release. This makes the intended cancellation scenarios deterministic without relaxing counts or changing Core. The frozen stop result is still checked after later I/O settlement.

## Verification

Windows final unit `1bc04a4134d14ff884884b137b5c0dc0` passed 28 cases. Final integration `95bd852facb749ba98a5fc5141d9f05e` passed 268: all 106 migration cases plus 162 shared-helper regressions (151 console and 11 bounded queue). Ubuntu unit `e41a1af429b44c3ba8973054e87d4db9` passed 28 and integration `56126fd5f4464efa8ff7f6dd1facb303` passed the same 268, zero failures/skips. Native audit verifies all 296 case identities across lanes. Linux uses scoped current source copies and isolated outputs; this is not a shared exact package or full lane qualification.

Earlier Windows integration `fbb20f82f91741869a4ba16562a9d8dd` passed the original 32 modes and `91dbeba5520241ad891e2bcd1d6a3807` passed 106 after adding controls. Unit attempt `fde130ceb40c414baa99ca87f79ecc53` stopped at build because the new shared report checks needed an explicit UnitTests Compile link. After that link, `5edce4fdba4040558159e72f19ccdc1d` passed 26 and exposed the two cooperative fixture races. The deterministic fixture correction precedes the final passing selections; no failed/empty selection receives test credit.

The latest full Windows Developer result remains the earlier 5,948-case value checkpoint. Current coverage/mutation/compatibility, full corrected native-lane qualification and release remain open. The original roadmap remains paused while separately authorized corrective work continues.

## Retirement

Guarded original SHA-256 values:

- `verify-stop-start.py`: `977e8bf802d256a990aaa1d3d230062284840e9dfb5dce1f03f3beac7f77b3ff`.
- `verify-stop-drain.py`: `f199d3ac103c5721d08e8b19d47eef78a02c8df93415561fbe9d674f08b639ff`.
- `verify-stop-join.py`: `c06ad6c145b8433a9eee41b27fd33dd5d78a4dc9dbd5d5961e496e71219e7664`.

No maintained importer remains. Remaining: 32 nonhistorical implementation Python files (23 DiscoveryHost readers, six engineering utilities and three Type Manager fixture utilities), plus one maintained documentation selector. Five historical implementation helpers and 106 frozen planning/evidence files are preserved. The shared Python flush helper remains until its remaining importers are migrated.

Final graph: schema 2, 10,711 nodes/51,289 edges, zero diagnostics, build `acf7505cc25b655d82b179b08f8c0372e5104658e39e9917a5a5d378d21019b0`. StopJoinExpectations resolves uniquely; the depth-one related query is fresh/untruncated (three nodes/three traversals, limit ten, proposed edges excluded). Compiler/semantic extractors remain 6/3. This supports bounded navigation, not semantic completeness.
