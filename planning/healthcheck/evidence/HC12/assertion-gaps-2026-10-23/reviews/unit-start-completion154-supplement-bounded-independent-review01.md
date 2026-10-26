# Independent read of separate startup-completion removal supplement154

**CLEAR within the bounded private-control scope.** Read-only source/provenance review, 24 October 2026. No reviewer build, test, classifier invocation, receipt replay, native mutation campaign, SSH, canonical change or gate credit. This is separate from the already reviewed/adopted main154 fixture and its separate1055 control.

Reviewed directory: `.artifacts/hcg/unit-start-completion154-supplement01`.

## Independent source change and original baseline

Read the complete supplement `Program.cs`, SHA-256 **`0eea8645281a7c4168722a0ea28f07f83665384207a2caf095fbc7a4e7aa06d8`**, including its distinct prepare/build/row/review modes, project and SDK inputs, preparation record, actual compiler command/exit/log, raw row and receiving records. Source preparation SHA-256 **`4e3fe278e5f4c813d85c3fb93cff2b4f6eb94f4d1b978bae2d7d36de53e279fc`**.

The preparation starts from the immutable original full143 payload `unit-complete-partition143-payload01/snapshot/source`, requires a fresh destination, and hashes/copies/rechecks the complete original inventory before making this change. It does not copy or edit the1055 variant. The original and variant inventories both contain **1,807 profile files**, including **146 Core C# source files**. During this review, standard read-only file hashing checked all1,807 original files and all1,807 variant files against those inventories: **zero mismatches in either set**. Comparing those recorded inventories finds only Runner changed.

The actual original Runner hashes **`386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8`**. The sole edit removes `startCompletion.TrySetResult();` at original UTF-16 **`[3096,3127)`**, replacing it with the empty string. The variant hashes **`230befa103ed14f8316f755ab64e99ce4bf3d43a81bed267c32b44e1ae34204e`**. Reading both actual physical files independently confirmed that this one removal exactly reconstructs the whole variant and that original `[2424,2431)` remains **`started`** in both files. The earlier1055 condition negation is not stacked.

All project/props/SDK/nullable/warnings/analyzer/dependency profile inputs retain their original recorded identities. The recorded full Core project build uses SDK10.0.401 with `/nullable:enable`, `/warn:10`, `/warnaserror+`, ordinary original references/analyzers, and isolated artifacts/output paths. Its actual log ends **build succeeded, 0 warnings, 0 errors**, exit0. This reviewer read that record and did not rebuild it.

## Same fixture and meaningful actual assertion

The supplement copies the exact main154 source **`821527a59e10eca274d3a76dc8c80ebb3f6dc193e7be604c7761b540bddf08a8`**, fixture PE **`cf786dc53aca7237af1a9e3bb17b436a7607dae255048eb8fa204d679a16c9af`**, PDB **`3aa9f3fad47400ada5afd4ab82aced0d623a6f105587749dd3885ec4afb83d45`**. These were physically hashed. No fixture source/body/phase/clock/Scope change is introduced by the supplement.

The existing public row is `CIS.Tests.UnitTests.Healthcheck.HealthCollectorRunnerMutationQualificationTests.RepeatedStartCreatesOnlyOneInitiallyInertTimer`, original GUID **`1a404029-8976-8309-a4e8-3d2a905b5a79`**. The original actual one-pass TRX is copied unchanged (SHA **`d361bd95ab9efee61dcfd65edace8b6f80522169fb0f1453791c017029bd51f4`**), containing that GUID/name and exactly1 passed,0 failed/error/timeout/aborted/not-executed. The copied main154 receiving receipt **`5d0e7faaa98441d90a37dc2540f0b53a7c55009f9dc21c4128c6ef462ba7beae`** and four real phase-control record **`93b65ed3835684d35129cc12b2b98d0265e0b031ec75c1eb2d802c1203e58ded`** match the already independently reviewed main154 evidence; they are reused, not rerun by this supplement.

The actual variant row is invoked through that unchanged public fixture method. The fixture really makes both original uncanceled Start calls; its original callback-free forwarding timer delegates real API operations, actual Stop/runner disposal/service disposal are independently initiated and successfully joined, creation/disposal/terminal counters must balance, and its complete actual owner graph must die while both actual bare Start tasks remain strongly retained. Unknown startup, timer, terminal or ownership errors are checked unconditionally before and after owner observation. A live producer/owner or incomplete/faulted cleanup cannot reach the direct startup assertion. Actual faulted/canceled Start tasks are observed as such. The three original timer assertions remain after the promise assertions.

Raw row record **`986bf55d2e353af22450748724d9bae18c1a861491e35c785226dcbd7afb6fc1`** was written before validation. It records the actual same fixture, variant Core, full public method, elapsed **264.7392ms**, and exact **AssertFailedException**:

> An accepted startup promise must settle after all actual runner resources and startup owners have returned and died.

Its stack reaches `AssertActualStart`/`AssertRepeatedStart` and the original public row; the actual failed expression is `Assert.IsTrue(task.IsCompleted)`. The scalar guard path and unchanged fixture source establish why this assertion is after the real terminal/resource/whole-owner checks; the raw record is not an independently instrumented trace of each guard. The assertion halts before later original timer assertions in this negative execution; their continued presence is not a claim they executed on the variant.

The actual row receipt **`6c34c1dc12f090336bf1c93515048f882452f3266e66b2956e6d3bce92fbc18c`** also records the unchanged maintained direct-assertion classifier accepting the actual caught failure. Its temporary `NativeResult` is classifier input constructed from this manual exception, not a native Stryker/VSTest result. The original GUID is bound through the unchanged fixture and preserved ordinary TRX; the reflection control itself does not generate a failed native TRX or independently discover that GUID.

## PE/PDB/source closure

Read the complete source of the receiving verifier and its actual successful receipt **`26faaff365578db7a910744a8193d47ac41f859894ffbc416e9164e00d292d2c`**. Its checks cover all146 original and all146 variant physical source-to-portable-PDB document checksums, exact fixture/helper source documents, portable CodeView GUID/stamp/age identity, and full deterministic portable-PDB SHA256 checksum after clearing the PDB ID field. It checks the fresh profile inverse, original condition, strict compiler settings and exact raw/fixture/variant receipt joins. The verifier was not re-executed by this reviewer.

Critical physical PE/PDB hashes independently checked:

| Pair | PE SHA-256 | PDB SHA-256 |
| --- | --- | --- |
| Original Core baseline | `6c06cf587f546e3f83ae2118d3e5b7e3336c76f12b8902a2e1345f17a6b33cf6` | `7acc5a8233ba2aa3bc97ce29742c78cd4cd6d160361b480f93cd6529b81c0832` |
| Separate completion-removal Core | `d947e9f92ee2c60cc680dd8722d440947b71af843b849b33718f6cb82a47b4a4` | `1a44bb63685d37051f94fb9e4b76d3c7aee3bc7237c31a447f0751d268a279ef` |
| Supplement verifier/control | `0828addef84f7c6bf46868ddae984cf08c87c0305ff847d490d7c0f79ec2e4ae` | `6472d5081f3209ed72c1892794b00617f5957472136c0eb32cd6acf00d4f66d3` |

The fixture pair is pinned above. The supplemental PE/PDB closure is read from the source-pinned author verifier/receipt with these independently checked physical identities; this bounded review does not claim a second metadata-verifier execution.

## Limits

This is one separately compiled manual source perturbation and one actual public-row control, not a new campaign or completed runtime/union partition. The empty-string statement removal is this manual experiment's exact source edit; it does not rewrite any report replacement or transfer an old numeric1066 outcome. The actual raw exception, source and physical compiled evidence are retained. Native Stryker execution, instrumented child propagation, full current-host capture/restoration qualification, report adjustment and kill credit are not supplied here. All records explicitly preserve **appliedKillCredit0** and **old1066OutcomeTransferred=false**. No timeout, cancellation, fake task completion, field write or skipped cleanup supplies the assertion. No source/provenance blocker was found within that scope.
