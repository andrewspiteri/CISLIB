# Runtime review closure checkpoint

14 October 2026. This supersedes the runtime-promotion checkpoint for current
runtime correctness. It is still development evidence, not release qualification.

Core SHA-256: `53619d263b200fa3d53bb5d9455dfa599110ce83ad30b83448b480bd542e1cb3`.
The complete Core source was copied into an isolated local snapshot to avoid
concurrent logging build outputs. Windows and Ubuntu executed the same DLL.
The snapshot and full source hash inventory remain under
`.artifacts/type-manager/final-source-snapshot*`. No source commit or published
package identity is claimed for the combined uncommitted Core snapshot.

- Both OS profiles passed **72** focused cases: 63 unit and 9 runtime architecture.
- Both passed **14** fresh process/generator cases: 12 runtime and 2 generator.
- Replay validates every fixture file hash plus Core and the helper, before use.
- ApiCompat, historical source rebuild/run and unchanged compiled consumer passed.
  Their logs are separate; the three report names are asserted and retained.
- Ordinary generated-inventory discovery and mid-load cancellation passed on
  Windows and Ubuntu against this Core DLL; Windows output is retained here.
- The earlier canonical Web SDK Debug inventory matrix has nine passes per OS.

The final read-only Claude review returned PASS for V1/R1/R2 and no remaining
mandatory finding in that scope. V1 fixed a PowerShell case-insensitive variable
collision that had overwritten replay log names. R1 excludes dynamic assemblies
from the initial report snapshot. R2 rejects a host resolver's foreign-context
assembly even if its identity matches. Original review findings F1–F5 remain fixed.

## Qualification correction

The raw final review incorrectly reasoned that a CLR cross-context rejection
would escape unwrapped. In fact, the loader maps FileLoadException to its failure
contract. A negative run exposed that the original **collectible** foreign-context
fixture also passed against the old DLL, so it did not qualify the new check.

The fixture now returns a **noncollectible** foreign-context assembly and requires
Type Manager's own rejection message. It passes against the final DLL and fails
with 'Preparation unexpectedly succeeded' against the prior e5ac candidate.
The complete 14-case Windows/Linux suite was rerun with this strengthened fixture.
The separate dynamic-report regression also fails against that prior DLL.
These controls establish those specific regressions, not a complete mutation suite.

Whole-Developer verification is tracked separately. Package RID restore/setup,
DI examples, explicit enumeration instrumentation, final-boundary cancellation,
full mutation/compatibility qualification, performance and the release bundle
remain open roadmap work.

The final unit reports use stable string DataRow IDs. The initial Type-valued
DynamicData produced nine duplicate native test IDs even though every case ran
successfully. The repository's strict TRX validator caught this during combined
verification. The nine cases retain their original coverage/count, and the
isolated harness now uses that same strict validator. Both final 72-case native
reports pass identity/counter validation; earlier runtime-promotion native reports
are historical execution records and are superseded for qualification.
