# Root retention inventory and minimal recipe181

Read-only inventory completed against existing private bytes while Developer181 is root-owned. No canonical files were copied, no subject/adapter/classifier was invoked, and no Core/shared project or new utility was built. Existing C# full archive readback receipts were inspected. The only extraction was the root-authorized operational `tar -tzf`/`tar -xzf` of the exact 3,504-byte actual ASP08 receipt archive into a new absent private sibling. No old source, archive, receipt or failure was edited. All paths below are relative to `C:/miscwork/portfolio/cislib`.

## Four primary packets and two independent peers

| File | Bytes | Physically observed SHA-256 |
| --- | ---: | --- |
| `.artifacts/hcg/unit-identifier147-current176-complete01.tar.gz` | 18,512,134 | `7c897af56fdf9b2d4cdf24d795a119dbd54738a732a188304d2df088b123a4b8` |
| `.artifacts/hcg/resume-capture88-proposal/unit-current176-independent-review01.md` | 4,953 | `da3c4eadaf6798164d13445591aae7299a5948b691d678f00fe28f1166121579` |
| `.artifacts/hcg/unit170-index08-source-seal01/unit170-index08-private01.tar.gz` | 692,866 | `0c5412b94d0bab9bfe662363cbccc10ef16d0a97aeb748461826fb12e7a8d24c` |
| `.artifacts/hcg/resume-pipes88-review/unit170-index08-independent-review01.md` | 5,608 | `652bcd1ad1c373aef8989d9ea78def9975c7f3135b577d3624510cdaad0db6b4` |
| `.artifacts/hcg/asp93-index08-regression170-actual01.tar.gz` | 3,504 | `9ae7cfa60b78814d09843d8b6bce56ddaed87f27fc76b3399e1ae74ad0f92599` |
| `.artifacts/hcg/developer179-failure-portable01.tar.gz` | 15,950,898 | `2b782bf0f93985430a4237365b09984971c35a6e332535377903e78724c60c62` |

The four primary archives total 35,159,402 bytes. These are ready for root's hash-checked canonical retention copy; this inventory performed no such copy. Keep the two independent peer files separately because they were written after the corresponding immutable packages.

## Existing readers, receipts and detached ledgers

The existing C# TarReader implementations verify every member's regular-file type, unique name, byte length and SHA-256, then recheck physical source pins. They are one-shot receiving/packing programs, not read-only replay entry points: do not invoke their original entry points against sealed outputs. Their full recorded readback receipts are sufficient for this retention copy; root can hash the unchanged archive after copying.

| Packet | Existing C# implementation / compiled closure | Recorded complete readback |
| --- | --- | --- |
| 176 | `.artifacts/hcg/unit-identifier147-current176-diagnostic01/Seal.cs`, source `1e634469a7d6528c2029c8e29bb3fef6fe3adbb0316fd7e8a6e52635632b300c`; `seal-build02/bin/Seal/release/Seal.dll` | 2,035 members, 2,034 pinned physical files. `package-receipt01.json` = `d3a73cf54ca08d0a94d9715400532642588dfbac0fdba05f39187a2b781b81d9`; `package-source-pins01.json` = `eb1658f51e36cd85504f026cb645504632b6277ae116971c44e29940d403c303`. |
| 170/08 | `.artifacts/hcg/unit170-index08-source-seal01/Pack.cs`, source `a77b9ac701513395dafbc98df780bfdd65e5b62c801a3663f354da4302bf4fbc`; `pack-bin01/Pack.dll` | 209 members, 208 pinned physical files. `package-receipt01.json` = `1c5a3a7c596e16228b93ddfeb3d4ac9a01aa42c31abbf2cd78e373a6677d9316`; `package-source-pins01.json` = `eb501a317a97b4f2f42cebce7bbf548d65856d99796e40562aa557701c6810fe`. |
| 179 failure | `.artifacts/hcg/developer179-failure-portable-seal01/Pack.cs`, source `bc6fb3e49f4291fd3007dcb1925b1c65018d8ca3ae7cf73df2d3c3adc5de7844`; `bin01/Pack.dll` | 1,791 members: every 1,754 frozen-evidence file, every 36 completed packer-tree file, plus manifest. Zero exclusions. Detached receipt and manifest listed below. |
| Actual ASP08 regression | No new C# reader needed; exact downloaded archive was listed/extracted operationally under root authorization. | 17 ordinary files in two directories; every extracted file was hashed. This is archived actual argv/exit/output inspection, not an additional receiving execution. |

For 176 and 170/08, retain each named `package-receipt01.json` and `package-source-pins01.json` alongside its archive under different group directories to avoid basename collisions. The packages already retain their complete receiving/source/PE-PDB/build/control histories. In particular 176 retains failed Freeze01/Ready labels and Seal01 ordering attempts; 170 retains its failed first receiving/count assumption and source preimage. Neither failed attempt is erased by the later successful package.

The following detached 179 files complete the package's own ending and wrapper history; they must accompany its archive. The completed receipt cannot be inside the archive it hashes.

| File under `.artifacts/hcg/` | Bytes | SHA-256 |
| --- | ---: | --- |
| `developer179-failure-portable-receipt01.json` | 1,027 | `0566334a7f9ff9075f3e826dabfc5fd2fd253411b02051a88c3742457c2ba94e` |
| `developer179-failure-portable-source-pins01.json` | 433,892 | `9bc18f93e4af03297f97254ad86dccfc4135fbd98d5bee0d3bace39c99bcba39` |
| `developer179-failure-portable-wrapper01.json` | 1,327 | `6a11e4dfb4eae6ad0634904064a25496a44ce5a127bfff4f0417b865f76c6f86` |
| `developer179-failure-portable-execute01.log` | 428 | `93e1bdf54b5b71a71bd2019c065cfeeae14556178d2083d575af1db58ea15982` |
| `developer179-failure-portable01.md` | 1,929 | `bf62c7d313dcb2f4cbb7f5e800f7b5074a1488777b07489ba22067034c0d70d9` |

The wrapper records the exact detached shell exit-file path error after packer completion, with no rerun. The full archive/readback and source inputs were unaffected. Do not invent a separately written exit-code file; the actual final shell exit and completed receipt are retained.

## Actual ASP08 regression closure

New private extraction: `.artifacts/hcg/asp93-index08-regression170-retention181-readonly01/`. Preserve the original 3,504-byte archive as the transport record; the extraction is a convenience copy. It contains all actual final derived outputs and root invocation ledgers, but does not contain the historical subject captures or adapter binary. Bind those existing dependencies rather than calling this tiny archive a standalone qualification proof.

Actual argv pins Index08 DLL `51e77f37f90ca1e75405ca5a305e6b0528940bb0a7015d3ee9ae17b7a40c4f89` / PDB `82f467725b78f4e2a02b7c74059a65435ca1456114ceac0fc00aa480de98ee79` from the sealed 170/08 archive, exact original input `c747fd66947d25ff96b2b8ba44552d40e881ee3a7642da6a2fd19715e5da1013`, runtime 10.0.12, and a new absent derived output. Start/finish are 2026-10-24 09:50:47.869119697 to 09:50:48.549248708 UTC. Actual entry exit, stable-index comparison and input-stability comparison are each 0. Before/after ledgers are byte-identical (495 bytes, `46ce87802e3aa59a771eeecc5b0e03abefb2cd88800e27667f57f823615be2f8`). Stderr is empty. No author replay occurred.

Key exact extracted files:

| Relative to the new extraction | Bytes | SHA-256 |
| --- | ---: | --- |
| `asp93-index08-regression170-root01/command.argv.txt` | 390 | `389b3c08934020c1a24675341e48bfcb9c1c03d3f7d4d1999c5fd81f47adaa07` |
| `asp93-index08-regression170-derived01/derived-native-address-index.sha256` | 2,035 | `40f4c294fa88f2212875af9fdc90bccb672564acf957435b091f0069a374048b` |
| `asp93-index08-regression170-derived01/provenance.json` | 9,357 | `90eed158b3594627ba49e157ed739e0fe5e3f22d2014442fc3a7297d773ea912` |
| `asp93-index08-regression170-derived01/restoration-manifest.json` | 4,286 | `5edf963fa6c22a529705330904ce08dcd13aa5028de9628736ddeed17243b8e2` |
| `asp93-index08-regression170-derived01/descriptor-fragment.json` | 654 | `67d5d3d5b2b40c7d128d48050438c2838748a64eceb3077a3196bb1236a08e79` |

The actual original input is already physically available in `.artifacts/hcg/asp93-independent134-review01/linux-receipts01/asp93-postclosure134-inputs01/index-input.json` (1,138 bytes, exact c747 hash above) and in the actual07 extraction. Its original backing evidence remains:

| Existing backing packet | Bytes | SHA-256 |
| --- | ---: | --- |
| `planning/healthcheck/evidence/HC12/assertion-gaps-2026-10-23/retained/aspnet-complete-acceptance93-original-complete03.tar.gz` | 239,310,007 | `2bbe7ece50d9f462d25b5b529fa43dff96a2593dec026559633c44fb25ca6c9e` |
| `.artifacts/hcg/asp93-postclosure134-receipts01.tar.gz` | 9,792,609 | `5046785ba0b9e5300efd96f705a838b0eccd8849e1b50be5acda5a5646dce735` |
| `.artifacts/hcg/index07-regression149-actual01.tar.gz` | 3,791 | `2595e26185457b600e7255896e0aab710ea0b89a2882af277fc743e978047d6e` |

Keep references to these existing packets, without re-embedding or rerunning them. The original 170 peer/recipe correctly said ASP08 was future when sealed; retain it unchanged and add the actual08 packet as a later result. The address projection still claims no new native/child/whole-scope or qualification proof.

## Developer179 source/timeline findings: remaining retention boundary

`.artifacts/hcg/developer179-concurrent-readonly01` currently contains 45 files / 2,106,547 bytes. It has no portable/readback yet, confirmed by its author. Preserve the entire folder, including source/project/obj/bin, original failed execution01, unexecuted input02, wrong-cwd dotnet-usage execute02, successful input03/execute03 and actual02. A hash-checked ordinary directory copy is sufficient for prompt retention; another tool/build/replay is unnecessary.

| Key file in that folder | Bytes | SHA-256 |
| --- | ---: | --- |
| `FINDINGS01.md` | 8,291 | `317e6ac28016b011f9c7c3e2a6a08312c289a92f80670c751246fac89f774ae3` |
| `actual02/projection.json` | 1,523,779 | `93f4d927322d1519c5002d999996adea563c264e6d9c2977bffd759878430710` |
| `actual02/input-pins.json` | 12,532 | `3b92e6941e15ea803cbf90830927a29d7ec8507f4d1f69b6b8ee59718f73ecb1` |
| `input03.json` | 6,832 | `8ed0b92a4c59e4b279caa141d2a655d54e9f791af59620c790cfe0d733348380` |

The 50-input manifest binds dependencies outside that folder. The self-contained retention set therefore also needs:

1. The sealed 179 failure archive plus detached files above. It supplies the exact actual179 profile, source inventory, observed fixture/Core pairs, seven frozen subject sources and full observed PE/PDB/source closure. The original canonical main has since changed for the next checkpoint; use the frozen source bytes, not today's canonical path or a rerun of the metadata reader. The actual179 frozen names remain Main0051, Concurrent0048, Clock0049, TransportSupport0074, TestAssembly0202, CoreServer0048 and CoreBudget0046.
2. Four original focused-run files under `.artifacts/csharp/focused/a9b27906b8cf4361846e1c6b387188c3/`: `selected.trx` (83,070 bytes, `50928c7a451e9b94229444652b28f2aaab9825c7baec264eee68da7128c88ca7`), `summary.json` (33,594 bytes, `8ce244d46585b6e0ec903380731a61969b07eec842093c238f87dc7b7895c13c`), `build-arguments.json` (119 bytes, `d735383c8931bb4ec3af1c8a665e5946343487ae9526a20a50c57e9ae91e0c52`) and `test-arguments.json` (340 bytes, `f1d6b156235623dd977ca627af98d779b5100669dc89b2bed1aff8e1882dcca8`). Keep this prior pass labelled without a historical postrun fixture freeze.
3. Existing `.artifacts/hcg/pipes-concurrent161-proposal/pipes-concurrent161-private01.tar.gz` (5,356,775 bytes, `e3f9137c090c05a2c6e408ee1ef69d46fefc454063ac427555e5963fbdfa6309`). This supplies actual private161 PilotCase, matching Concurrent/Clock helpers, DLL/PDB, ordinary TRX and receiving receipt. Its fixture compiled PilotCase, not the full proposed main. Existing recorded private161 archive readback is retained with that packet; no new readback or subject invocation was performed here.

The 179 original failure is self-contained with its archive and detached completion records. The source/timeline findings become self-contained as evidence only with the historical files/packet references just listed; the 45-file findings folder alone is insufficient. No measured per-connection timing proves the EOF's cause, and the isolation proposal is a separate later experiment. Keep the original 2,322 passes/one EOF hold, postrun-only identity limit, and all runtime/cleanup errors unchanged.

## Minimal root copy order

1. Hash-copy the four primary archives and two peers to the existing retained evidence root, preserving basenames. Confirm their hashes against the first table. Do not repack them or recursively embed their extraction trees.
2. Copy detached 176/170 receipts and manifests into separate `retention181/unit176` and `retention181/unit170-index08` directories; copy the five detached179 files into `retention181/developer179-failure`. This keeps ending receipts and wrapper failure intact without basename collision.
3. Copy the complete 45-file readonly findings folder under `retention181/developer179-findings`, plus the four prior focused files under its `historical-focused56` directory. Preserve/refer to the existing private161 archive and the already retained 179/ASP originals by exact hash. No new source snapshot or dependency build is required.
4. Retain this recipe as a later inventory note; preserve all earlier peers, future-tense recipes and failed attempts unchanged. Record the actual08 regression as later actual receiving success, not future170 completion. All mutation/whole-union/release credit remains zero from this retention work.
