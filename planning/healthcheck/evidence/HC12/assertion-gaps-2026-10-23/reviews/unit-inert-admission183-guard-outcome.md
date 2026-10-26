# Unit183 actual guard results

Root executed each separate guard fixture once against the SAME sole-publication-false Core pair: DLL `319cab23c66d5d146a1a31661a79df089c35c39e2affd065ebafc409d6808eaf`, PDB `8fd6ba510c6978707497638c21e50c062235707393cf166ded336844631d2a7a`. Both retain the original public GUID `1b181a18-5aaf-8d5d-91d2-0fca7f4fe5de`, FQN and display. No author subject invocation occurred.

| Guard | Actual single-case result | Test duration | Preserved runtime cause |
| --- | --- | --- | --- |
| `retained-owner01` | Failed, Aggregate; native/root exit1 | 10.0440531s | The actual static runner remained reachable; the weak-owner guard rejected release. The original acceptance assertion and Register rejection remain in the Aggregate. |
| `recorded-producer01` | Failed, Aggregate; native/root exit1 | 29.6631ms | The actual fixture clock threw its recorded timer error; unconditional error history and the nonzero timer count remained runtime holds alongside the original assertion/rejection. |

Each TRX has exactly one executed/failed result, zero passed, timeout, aborted or not-executed results. Each supervisor reports actualExit1, reaped/drained/log-copy-completed, no expiry/tree-kill or supervisory holds. A clean supervisor is separate from the intended test-level Aggregate runtime hold. The retained-runner ten-second ownership deadline produced a hold, never completion credit. The independent300-second caller watchdog was NOT executed; both root exit records state this explicitly.

The `root-negative-stage01.json` records bind each complete143-file host closure: only Core DLL/PDB differ from its separate baseline, and staging occurred after the actual main original passed. All fixture/supervisor builds used SDK10.0.401, locked restore and audit, with zero warnings/errors. The main source4056cc9b and its frozen closures were not changed for these controls.

## Exact pins

Paths below are relative to `guard-controls/<guard>/`.

| File | retained-owner01 SHA256 | recorded-producer01 SHA256 |
| --- | --- | --- |
| `HealthCollectorInertRegistrationStopBoundaryTests.proposed.cs` | `c8c8e7d065a31d340def374c524b6b93d8ef9f6222789e7c099e79f9b903373f` | `34f5af368435d5869c99093b6210cb5384bce61cc7c1828018c88a4e149e22d3` |
| `baseline/bin01/CIS.Core.UnitTests.dll` | `52903327b3aea5c5e78dba6d47cd5e3bb5dcfe39c750528f303ed957f5b01b26` | `535bf113b21f91e2201c47404ce23f9653d990f512dbcd012146e08ee31e45ac` |
| `baseline/bin01/CIS.Core.UnitTests.pdb` | `4f68dd91169a36b667cfe5c620c578554501c7b1a7500d50b997b49529a40216` | `53c47fef635dca3b8b8eadae57205ce94716d1c230add941bb3660ff11270d0d` |
| `Supervisor.cs` | `a4c4fe3240d8b4c264683140c666869b834cefa64d1e900b6a1ba3c31137a978` | `3884c4a3d696608c273507ab8bcd1643a683e25a245253ba885ed619377f21db` |
| `supervisor/bin01/Supervisor.dll` | `76a95acb0cf1d30bb4152196268144db0aac44a17b94e2c023c43c4f37dea6b0` | `c5cb96d46c2b72d4a0d5e0721d9f5669facd1d41467147be337b186b0f63f13f` |
| `supervisor/bin01/Supervisor.pdb` | `71f8beaf273b3162941925f8b821de47267179db6dca4e6d1d66db235bb42814` | `5266caa52a98fd901a3725dd3ed6b00bc0825fd41083f12a01ed1b6cc3fc1a9a` |
| `guard01-root-exit.json` | `99bd6fe63947137060bcee0b0c592b1d32a74f8664f01ce77e0327997199aa4e` | `ada96d195575380af0ae0735eb618750532e57292af20e860d28bd03e3f2ae6e` |
| `root-negative-stage01.json` | `55acf89299ea6bb92022e95ac6edf5bb5ff28a69bab5d9ce1861b2f4bb142414` | `e771d65b07714fbdc12e6f94489c5738d0136e4c245202165ca6e25a51d2cc4d` |
| `runs/guard01/result.json` | `239322f518465a0b3b63e832199e0b04ca15b3b0aad81ef3172d7c078c596523` | `986b8ab5753a80fcf08be98050137edca5e4cb3b111b17fdfef8e6a042925782` |
| `runs/guard01/results/selected.trx` | `70e463bcdb356e846325f3abeda14e5aeda6c7160a5914986fdfec9aceb77e6a` | `987da3ad54c5defa8e1c1d4ce086f8b70f263eeaba0ad4694aff2993d777fcd6` |

These are fixture-guard instrumentation results. The timer hook is an actual call to the fixture producer, not evidence that the SUT invoked a timer. Neither control simulates every possible queued owner, resource fault or mutated path. No Stryker outcome, prior host result, qualification threshold or kill credit is inferred. The old Aggregate/timeout host record, all raw results, failed strict sandbox preparations and successful strict normal-host builds remain preserved. This private proposal is now frozen for root retention; no further build, invocation, helper or repack is planned.
