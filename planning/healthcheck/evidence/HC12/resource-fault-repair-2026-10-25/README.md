# Preserve collector resource failures with unavailable messages

An original-only regression exposed a shipping defect: the runner could acknowledge successful `StopAsync` after an actual timer-disposal failure when the exception's Message getter threw. The existing bounded diagnostic correctly recorded an unavailable message, but the runner stored only that nullable text and interpreted null as success.

The runner now retains the existing bounded, detached `HealthDiagnostic` in its resource failure slot. Its presence determines failure; optional Message text supplies the existing `InvalidOperationException` argument. The same three catch paths and all admission, dispatch, cancellation, retirement and disposal barriers remain. No producer exception or owner graph is retained, and no public terminal-message precedence contract is added.

Two regressions cover a sole unavailable-message disposal failure and a later unavailable-message error erasing an earlier real timer Change failure. Both retain every original public task, genuinely drain their System timer, and preserve unexpected errors. An independent fixture-owned timer fallback runs after the saved SUT facts and cannot repair those facts.

## Executed controls

The unchanged original shipping code produced the intended sole-fault assertion failure in **27.8145 ms**, with normal typed completion, genuine timer cleanup, host exit zero, process reap and log drain. This remains an honest failing bug reproduction, rather than a positive baseline or mutation kill.

The separate two-fault fixture passed the original code and failed the exact source-bound historical Unit191 definition1272 at its intended failure-presence assertion. This test and the preserved first-message-precedence HOLD have different oracles: the new test requires a failure to remain a failure and does not select terminal text. No old report was reclassified.

Against the private repaired Core, the unchanged two new tests and the complete existing ResourceBoundary and RunnerMutationQualification classes passed **37/37**, zero skips. All GUID/FQN/display identities joined the actual prior 35-case Developer cohort and two preserved controls. The host completed normally, with no abort, timeout, kill or ownership hold. Independent review reproduced five PE/PDB pairs, 169 source documents and the complete 144-file fixture inventory. All 146 original/fixed shipping bodies were checked before canonical adoption; only the reviewed runner source differed.

The fixed C# adoption checked every source hash and canonical preimage before writing the runner and both tests byte-for-byte. The official repository focused run `6e3d9c2d6431449386013049a1b96089` then passed **37 cases**, zero failures/skips, with zero strict warnings/errors.

Coherent Developer run `e6949522af094e3f8f2458863902fb71` completed zero at 2026-10-25T18:27:30.8602879Z: **10,342 passes**, zero failures/skips and zero strict warnings/errors. Actual source inventory `e019321529aa56aa874021ced92f6a2121e32fb738fe04e37a8eea55e4c703c3` includes this repair, both new tests and the separately verified ASP.NET route prerequisite. Unit passed 5,308; Integration 2,323; Architecture 2,682; Business 29. Developer does not qualify omitted mutation/release families.

Source identities are runner `1ea877509f2a9fcc4419cb3cfcc222f4144d923675d1793ee4de9ce2fe845dcd`, two-fault test `6ac693173e2d2bbfe7f051e8d1bb36e77ab2b03973511ecbb4cf0eff26e29786`, and sole-fault test `68c3754af761ef4a731dd32f58f723349272e7696971754b56e5a92299157186`. The original runner is `386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8`.

## Retained evidence and limits

The [complete private proof](proofs.tar.gz) is **38,795,255 bytes**, SHA256 `4a30d161fa025adb9cb75fc580f6fa09ae9f7c48789887a0e6825408f534b1bc`. The [external file manifest](proof-files.json), SHA256 `f6e9892885ac23008165462b7baf2bef60cdff7e38622ca972e84889d4bca65b`, binds all three frozen author roots and nine peer files: **5,218 files / 161,807,952 bytes**, without exclusions. Native archive/extraction and complete [C# receiving](proof-receiving.json) passed. A post-archive inventory reproduced both original manifest and member-list hashes exactly.

Raw original failure, exact-variant failure, fixed positive controls, source preparation, physical receipts and peer notes are retained together. Archive paths preserve repository-relative `.artifacts/...` locations. Separately immutable readers and historical reports keep their original recorded identities; packaging is not a relocated replay or proof of runtime-loaded images.

The [shared verification supplement](verification.tar.gz), **9,411,057 bytes**, SHA256 `7c92adb8f59a859e1fae2ee2a65a61c566ffc74fe63213615a76877d269e0ab4`, retains both canonical-adoption roots, both official focused native roots, the complete coherent Developer root, completed graph logs and bounded source-only triage. Its [file manifest](verification-files.json), SHA256 `68c08292e8e365e176d521ebc3fc80fa7ed79c3d1f3fd7d93d014eb98fce160c`, binds **1,064 files / 94,831,174 bytes**, without exclusions. Native archive/extraction and complete [C# receiving](verification-receiving.json) passed; the post-archive original inventory reproduced both manifest and member-list hashes exactly. This supplement also covers the separately retained ASP.NET route prerequisite.

The running192 campaigns retain their earlier immutable shipping source, so their eventual aggregate cannot qualify this new runner body. No retroactive mutation credit, equivalence, abort waiver or release qualification is claimed. The last fully audited historical union remains **2,111/3,104 = 68.009021%, unqualified**; the 90% gate remains open.
