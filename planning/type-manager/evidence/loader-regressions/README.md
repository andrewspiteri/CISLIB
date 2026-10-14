# Loader and diagnostic regression checkpoint

14 October 2026. Ten new unit cases strengthen Type Manager's existing implementation;
production source and runtime dependencies are unchanged. **TM-027 and the release
gate remain open.**

## Executed scope

The final tests pass 90/90 on Windows and native Ubuntu against the frozen actual
Core `d70fcfdc41b6d15829227f1cee60130ddad3f7f82cc11c7a2fcbf8d5f700fb09`:
81 unit cases plus nine runtime architecture cases, with no failures or skips.
Ubuntu executes the identical Windows-built test/Core/dependency bytes on the pinned
10.0.12 runtime. SDK selection remains 10.0.401.

Eight cases emit unique physical assemblies to exercise fresh default-context
loading, reuse, private constructors without activation, case-insensitive loaded-name
conflicts, file identity mismatches, resolver substitution, both reference-attribute
constructor metadata forms, and two similar attribute names that must remain valid.
Two cases verify diagnostic context, owned/read-only loader errors, and absent
optional context. The runtime verifier registers all ten; the mutation preparer now
registers 100 cases: 81 unit, nine architecture and ten existing business stories.

The fixture's first local-attribute emitter produced a zero constructor token. That
was a fixture defect, not a production failure. The final emitter assigns member
tokens before adding the assembly attribute and independently reads back its kind
and declaring type. Positive similar-name cases also have to load successfully.
Default-context files remain mapped on Windows, so generated assets stay in test
output; remove `cis-type-manager-fresh` only after every host using it has exited.

## Mutation results and independent controls

A fresh native Stryker 4.16 run with the initial reviewed 100-case test revision gives:

| Native status | Previous 90-case run | New 100-case run |
| --- | ---: | ---: |
| Killed | 103 | 109 |
| Survived | 45 | 42 |
| NoCoverage | 40 | 37 |
| CompileError | 238 | 238 |
| Ignored | 57 | 57 |
| Total | 483 | 483 |

The score is 109 / (109 + 42 + 37) = **57.98%**, up from 54.79%; native exit is 2
because the unchanged threshold is 90%. IDs 290, 292, 444, 446, 456 and 458 move
from Survived to Killed. IDs 350, 352 and 353 move from NoCoverage to Survived.
No native status is rewritten and no compiler-error/ignored entry receives kill
credit. Every remaining non-killed entry still requires disposition.

The native run predates final review hardening of the tests (canonical paths,
stronger fixture metadata assertions, message assertion diagnostics and LF endings).
Its exact input snapshot is retained separately; the score is not claimed as a
native run of the final committed test bytes. Final-source qualification uses the
frozen-Core checks and separate source-module controls described below.

On Windows, with the **final** test sources, independent strict source-module builds exercise syntax-qualified equivalents of native mutations
282 (remove post-load identity validation), 290 (`FirstOrDefault` to `First`) and
335 (invert local constructor-kind recognition). Each fails its intended new cases;
282 and 335 each fail exactly one case. Both unmutated controls pass 100/100,
and restored source-module Core/test DLL hashes equal their control baseline. This rebuilt module is not the frozen shipping Core binary. The audited runner pins the prior syntax manifest/native report, checks the nine TypeManagement source files and listed test files against final canonical files, requires fresh output directories and one TRX per run, and validates exact failures for 282/335. These controls do not revise
native CompileError classifications for 282/335 or imply the full mutation gate passes.

Read-only Claude review returned PASS, small hardening suggestions were addressed,
and the final **test-source** follow-up returned PASS with no actionable defects. A separate evidence review identified runner provenance/output checks; these were tightened and its results are retained independently. The evidence follow-up returned PASS with a provenance wording correction, applied here; the source-module project contains no logging implementation. The initial
Developer attempt stopped on new-file line endings; its failed evidence is retained
separately from final verification. The final Developer run `4e7f7902892948348da768b9feaa673a` passes all 14 stages and 5,041 tests (3,286 unit, 1,292 integration, 463 architecture), with zero failures/skips. Combined Developer evidence includes logging's
uncommitted T03 work; it is a working-tree check, not a clean-commit release replay.

`checkpoint.json` records native counts from the initial reviewed revision, hashes and the combined run.
`native-evidence.zip` retains reports, reviewed input snapshots, source/control
provenance and logs. `archive-files.json` lists every archived file's SHA-256.
Graph context remains partial navigation evidence, separate from native verification.
