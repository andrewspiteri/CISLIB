# Cancellation and option assertion repairs, 26 October

The final source passed a second complete **10,333-test Developer checkpoint**,
including the later ordering and guidance assertions below. Run
`618b7840fe2742729575705cba0faed1` completed at 05:43:24 UTC with zero failures,
skips or structured validation issues. Exact final records are in
[developer197](developer197/summary.json). The earlier checkpoint follows for
source chronology; no passing result is transferred between source versions.
Its final source inventory SHA is
`e51857afee1d702d317b804ba54b78b8858c51aa4a1a62dcabdb52b3207ef84e`.

The adopted fixture and assertion repairs passed **10,333 Developer tests**:
5,305 Unit, 2,300 Integration, 2,699 Architecture and 29 Business; zero
failures or skips. All configured stages and structured validation passed.
The [summary](summary.json), [validation](validation.json) and four adjacent
family execution files are exact copies from run
`24470fbdda1f4c1f9424a526afbf17a1`, 04:47:19–05:05:02 UTC. Its source inventory
is `deb85ef69d439489f017bab2adcbbe49d1db1a251f15cd9519ea59881577804f`.
The complete run remains in `.artifacts/hcg/current195-coherent-developer01`.

The caller-cancellation fixture now races the actual connection/request tasks
against the actual probe. When a mutant makes the probe return before a valid
request reaches the peer, the test records the valid-request assertion instead
of waiting 15 seconds for an impossible connection. Cleanup retains all three
original tasks, observes completed faults, and preserves timeouts, unexpected
cancellation and resource failures. The existing caller-cancellation and
request-content assertions remain.

The Options tests explicitly assert that documented valid inputs are accepted,
retaining the actual configuration rejection and existing boundary/value
checks. Only the expected configuration exception is translated to that
assertion. An existing exact-multiset assertion uses equivalent cardinality
and per-key scalar assertions supported by the unchanged audit classifier.
These later source changes do not qualify earlier failed rows retroactively.

The focused official checks passed all 48 selected Unit cases and 121 Pipes
Integration cases. Their retained run IDs are
`adee00254cdd4f66ab7eca45ab42d3e5` and
`8a69000775994bbc9384e28cab85fb6d`, respectively.

The Integration project's mutation opt-in now compiles the Healthcheck cohort
when both mutation properties are true. Its ordinary compile membership is
unchanged. Strict private compilation and actual discovery found 181 cases;
discovery alone supplies no passing-test or Linux performance claim. The
fixed private Unit cohort similarly retains all 1,396 discovered identities.
Its source differs from the earlier pilot by exactly the three repaired test
files and the Integration project; all shipping Core source files are unchanged.

The [Options pilot](../options-pilot01/README.md) separates the 11
caller-cancellation waits from 16 large-cohort deadline aborts. A targeted
Ubuntu healthy/active control checks the repaired caller fixture separately.
No control, ordinary pass or discovery updates the maintained mutation union:
**2,111/3,104 = 68.009021%, unqualified**. Fresh complete execution, capture,
child evidence and the source-checked three-report audit remain required.

## Subsequent targeted control and corrections

The first Ubuntu selector564 control retained the unchanged 13,585 ms native
session deadline. Its healthy phase passed normally in 8.993 seconds. The
active phase returned one normal Failed case in 8.236 seconds, with no abort,
forced stop or process/drain hold. It remains **unaccepted**: the intended
valid-request assertion was combined with a cleanup cancellation. The unchanged
maintained direct-assertion classifier returned false. Exact receipts are in
[the failed attempt](selected564-195-failed/active-receipt.json); no exception
text or original outcome was rewritten.

The full retained attempt, including both short temporary roots, passed
Windows receiving for all 1,242 files / 147,758,674 bytes, with no exclusions
or extras. Archive SHA is
`defcf0a9b6861931395db7a0fc7ea2d41e24acd310867d810b87ff104beb86b6`;
manifest SHA is
`44108e8eb7c62fc90f177b37bfad73b1bfe01670e0bb294fea7ee56e2f9925d0`.
Readback remains in `.artifacts/hcg/unit195-selected564-closed-receiving01`.

A later seven-line fixture correction observes an already completed original
probe before starting a connection wait. Faults and cancellation still propagate;
successful premature completion reaches the existing assertion without creating
an unnecessary pending wait. All strict cleanup filters remain. This final
ordering correction passed the three official focused cases, run
`96a0a8bb6ce741699355c18a912d9c75`, with zero strict warnings/errors. It is
later than the first Developer checkpoint above.

The separate [196 control](selected564-196-clean/receiving-review.md) is clean:
healthy returned one normal Passed case in 8.519 seconds; active564 returned
one normal assertion Failed case in 8.208 seconds. Both retained the exact
13,585 ms native deadline, original selector, cached case and instrumented
Core. Neither phase aborted, canceled, killed or left ownership/drain holds.
The unchanged maintained direct-assertion classifier returned **true** on the
actual new row. This resolves the demonstrated early-return wait; it does not
establish that every Unit execution problem is resolved.

The complete 196 archive, including both temporary roots, passed Windows
receiving for all 1,243 files / 147,752,846 bytes, with no exclusions or extras.
Manifest SHA is
`c7fb2c7efe89f2ecbca2d4a278570fa2a188e0b8411658590e1cb204c57b1b72`;
archive SHA is
`2adb522a04786564debf6a81b36aab939b03e73faf66e3e1c9fbf26ccf13d02a`.
Readback remains in `.artifacts/hcg/unit196-selected564-closed-receiving01`.
The original 195 false/held result remains unchanged; both controls grant zero
campaign credit.

The four surviving Options definitions in the pilot removed issue explanations.
Four existing test scenarios now require nonblank correction guidance for
invalid integers, booleans, invalid pipe names and a missing required pipe name.
These assertions retain existing code/key/privacy checks and do not fix literal
message wording. All 31 focused Options cases passed, run
`673b5145e2594a23a7169d887bfbe766`, with zero strict warnings/errors. The
first filter attempt selected zero cases and supplies no passing evidence;
its separate failed receipt is retained. These changes are later than the
checkpoint above and will be included
in a new source-pinned Options pilot. Its prospective profile uses four workers
and 10,000 ms additional session budget, retaining all 1,396 identities,
the 20-minute overall limit and all original qualification holds. The previous
16 large requests contained 811–850 cases; their actual emitted budgets must
be measured afresh. This is not a hard 15-second per-group limit.
