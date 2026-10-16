# Known Claude findings — verified corrective checkpoint

16 October 2026. All eleven supported findings from the partial review are fixed in four bounded corrective commits: `3b1417a5`, `4f6144d3`, `aa1fcc3f` and `7a606607`. [Dispositions and targeted results](../reviews/corrective-2026-10-16/corrections.md) distinguish each fix, the failed first combined attempt and its correction. Raw review responses and historical evidence are unchanged.

Windows Developer run `ac73d96d939c4f2d819463df71ba9f36` passes **6,438 native cases: 3,710 unit, 2,085 integration, 619 architecture and 24 business**, zero failures/skips, exit zero and zero validation issues. It ran from 2026-10-16T17:28:37.0017958Z to 2026-10-16T17:43:31.0532754Z against frozen source `7a606607b6477e91ca7506406f9b7824f198566c` plus the five declared pre-existing uncommitted inputs. Restore, format, solution build, startup/consumer/compiler controls, dependency/package inspection, discovery/execution and required-case reconciliation, publication-ledger validation and the scaffold host pass. Developer uses Release build configuration; it is not the full Release profile.

| Checkpoint identity | SHA-256 |
| --- | --- |
| Summary | `32383dfb3aea46570360e79c55c6632101bc8493611b70e2797295929b9c68c6` |
| Source-input inventory | `44855e8b064e9313fb8aaca6a29e3ccdb0b47df1869707bc935603b62b571a21` |
| Tested development Core DLL | `d84efb8926dbfed1c9e4f5e758aa3f214774a3e957e668ae5c57200399d5f56c` |
| Exact development package | `43d14bbbfa25ea57eada6f34f37fd334a4f0a0e913d84e564969d47187397930` |

The complete output, developer log, Core DLL/PDB and exact package are retained in the unique ignored archive `.artifacts/corrective/claude-fixes/passed-ac73d96d939c4f2d819463df71ba9f36`. Counts were checked against each native family execution report and the source/summary pins checked before archiving. Subsequent report-only edits do not change the tested implementation.

The first Developer run, `0127538b333f4c458ebbd307ae471370`, failed the diagnostics IL guard because a record copy called a generated setter. Direct readonly construction fixes it without changing that guard. Its failed output remains separately archived and is not presented as a pass. All twelve diagnostic checks and all 38 incoming/processing cases passed again on Windows and Ubuntu before the final checkpoint. The final full run also executes that unchanged guard.

Additional native development checks remain separate measures: Windows/Ubuntu file selections passed 152/153 cases, with the Windows symlink prerequisite explicitly unexecuted; selected compatibility cases passed five per lane. Both lanes passed 17 runtime producer/replay checks, four mutation-preparation provenance controls, a 100-case isolated baseline, seven measurement controls and 66 source/exact-package example checks. Frozen measurements ran 52 fresh processes per lane, including four warmups, without a threshold or performance qualification. [Utility report pins](../reviews/corrective-2026-10-16/fix-verification-index.json) identify their earlier exact candidates and compiled verifier; they do not attest this new package or add unique VSTest cases to the 6,438 total.

All five pre-existing user inputs match their earlier hashes and the new source inventory. Their files remain unstaged. The Web launch profile remains `1509170e5a2d67d067d16e8a3c69db9e67755f5761bc0b5413fe11558054b6be`; this input is not reproducible from Git alone. The separate Type Manager chat remains completed/not loaded at revision two and was not messaged.

The refreshed CIS graph has schema 2, 10,976 nodes, 54,123 edges and zero diagnostics, build `b1e9dc7927dcf33d62fff73906d40b5e3e0e93d8dc32ae4d32df23a7ffd778d7`, with compiler/semantic extractors 6/3. Exact roots and bounded related queries guided canonical source inspection. The final CIS.Logging.CoreLogRuntime related query is fresh at depth one, limit eight, with four nodes/four traversals, no truncation and no proposed edges. Canonical source supplies the behavior evidence; navigation does not establish complete semantic or test coverage.

The original logging roadmap goal remains **paused**, with its task accounting unchanged. All eleven supported findings are closed by implementation and executed development evidence. Three original Claude scopes (325 assigned paths) and a review of the new source delta remain pending; no whole-change Claude PASS is claimed. Full Ubuntu Developer, fresh complete coverage/mutation, substantive PowerShell policy assessment, supported IDE acceptance, released compatibility, final-package performance/examples, skills, release bundle and publication remain separate work.
