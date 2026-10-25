# Closed ASP191 run01 bootstrap: independent bounded review

CLEAR as retained **failed bootstrap evidence only**. Read the immutable failure result, root stdout/stderr/PID/exit/timestamps, source-copy/policy metadata and all seven receiving JSON/log/exit triples in `bootstrap191-readback01`. Compared the failure site to exact original writer `bd4611d38e77e3c097d944d7c4c3ddffddf9497a91a51a48f3df24e444f1fc19` and the already reviewed original/retry staging contract. No build, tool entry, subject, SSH, source/policy edit or qualification credit occurred in this review.

The closed archive `bootstrap191-closed01.tar.gz` is **9,985,945 bytes**, SHA256 `ee4d327598f1be89b3aada1cf8b28bffa9e5ecdf80741ca754844cb7a59588e9`. Successful recorded readback02 (`5f63d8699490fce13eba0d4cc182999275b8fc852365d90d09b67c810aa7ba40`) verifies **2,054 files / 57,068,604 bytes**, zero extra files, against retention manifest `3f1ec37f74aa93707362939ce71b3cf54c47a43d65d3ab6e141583312f8041c4`. This peer hashes critical originals and reads that executed receipt; it is not a second full 2,054-file receiver.

## Actual failure and process scope

`aspnet-complete-current191-run01/results.json` is **1,998 bytes**, SHA256 `5525ef918a281369d3cb8116f0c0bba045b6b8190f28a351d1730e99a9d9ab39`. It preserves `completeExecution=false`, `processes=[]`, nativeExit/childAuditExit/actualInitialCohort/actualEmittedBudgetMs null, preflight/scope/shutdown-capture/native-restoration false, `qualificationClaim=false` and zero applied credit. `completePartitionInvocation=true` identifies the intended invocation; it does not establish completed source scope or execution. There is no report, initial/fresh baseline or native process phase in this run's recorded output.

The exact exception is `InvalidDataException: No current C# source/config may be omitted from the frozen origin comparison.` Both result and stderr place it at original `BindExpectedFamily` Program line460, reached from initial bootstrap line71. Stderr is `060c64379924d1f8ca58fe60f6756d42002224169e7a1e3c426ac9c8d61752f5`; stdout `44e15ae77408d64e908829231c6cbf2b8ba66a2ad3b071217688730990d26b3f` reports the same result SHA. Recorded wrapper PID is **2041884**, controller PID **2041886**, wrapper exit **1**, start `2026-10-25T00:10:13.595537728Z`, finish `2026-10-25T00:10:22.846537341Z`; controller elapsed is **8.9073338s**. These PID files are operational records, not independent kernel parent/reap or loaded-image proof.

Source ordering places `ValidateAndCopySnapshot(); BindExpectedFamily();` before the first process Run call. Together with the empty process array and absent phase directories, this supports bootstrap failure before controller-launched preflight/build/test/native/child work. It supplies no test result, mutant outcome or score, and is not pooled with retry02.

## Source cause and retry consistency

The retained copied `source-inputs.json` physically matches `04d4cec455a252665927c5cddbb801c4c4519249369fbd7233445412b7073fdb` /449,591 bytes. Payload-before and source-before records each state all **2,018** physical snapshot pins matched, before the family comparison. Policy-before/after-copy objects agree on the exact compact profile,333 archived omissions/5,264,846,764 bytes and the original policy/omission hashes; no omission repair is inferred.

Read-only metadata set comparison finds **1,222** snapshot entries under `implementations/csharp/` versus **1,171** entries in the corresponding subtree of complete origin `e794653c3c435628d69c278aeb497e1f0769298d115ca0cf3c6313a40eafa46e`. Both have **972 `.cs` files**. Every origin-subtree path/SHA matches a unique snapshot row; zero origin entries are missing or changed. The **51** extra paths are exactly four `eng/__pycache__` and47 `tests/fixtures/DiscoveryHost/__pycache__` entries. Their path/SHA/byte tuples equal the already reviewed retry manifest `481bf5973a036dc28dca218873807462a52d951140fc82e5f1bc9f2fbb8175cb`, with no extra/mismatched tuple. The complete origin file also retains its non-C#tree rows; this comparison does not describe the whole repository origin as only1,171 rows.

Original line460 compares those subtree counts after its loop has checked every origin row and physical body. The actual exception and retained sets therefore support the documented count-premise defect, not a missing or changed C# source. Retry02's explicit disjoint51-artifact comparison and current82 family use the same source/config/input; it is a separate reviewed writer/stage, not retroactive success for run01. This note does not claim retry subjects or any current full report have completed.

## Seven receiving checks

All seven retained Linux receiving JSON records have `verified=true`, `extraFiles=0`, `sourceOrNativeQualificationClaim=false`; their corresponding recorded exits are0 and logs report the same counts. These are **physical transfer checks**, not subject or campaign success.

| Receipt stem | Files | Bytes | Receipt SHA256 |
|---|---:|---:|---|
| unit191-linux-receiving01 |2048|58288508|aa9c1fa146136a41f5077b1897c07f9e62737f5243f4571af385c4330f331fa1|
| pipes191-linux-receiving01 |2053|74985752|00a71da4fd02c83eb11bbbbea0fc04b72fce1ee22ab8b84435931e217e0486de|
| aspnet191-linux-receiving01 |2048|71046582|247212b7df947dc242e2645d2551bbb03341792ced4f68c026b8cdaf4ae26254|
| aspnet191b-linux-receiving02 |2050|71073095|fbabc5ca5c1cdcafb4de459ae6c6f13b6ba37fd721abb6a5d83e4cdd3e033e78|
| current191-index10-linux-receiving01 |4|282708|a1ddefff5ad41fa813db4d4a3f6c7823f9c019d5b862fa13ad199bd8f79f2d75|
| current191-glue-linux-receiving01 |72|572896|08371fa7c2bf524c945d87590c3d70264815cf29a866f4c98ef38ca282c5f7d7|
| current191-templates-linux-receiving01 |7|18251|b08fe80c050c133a1336e012ff8f85668d5867fdcafecc98f8b6cbed8f6806ff|

The Root readback01 expected-hash typo remains explicitly retained in `bootstrap191-readback-attempt01-note.txt` (`6abe35a78c4e773e53e1425d0cdb0bd24cf9f2ac54bd778e19e436465e40226b`): the manually transcribed expected manifest hash had65 characters, so that attempt rejected before inventory verification. The corrected externally checked64-character manifest hash is the `3f1ec37f...8041c4` value above; readback02 passed. This correction does not erase the failed metadata attempt or relabel ASP run01. All current three-report closures, source-checked diagnostic aggregate and mandatory joined HC12 gate remain pending.
