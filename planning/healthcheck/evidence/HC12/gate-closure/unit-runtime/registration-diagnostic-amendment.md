# Registration diagnostic amendment candidate

The user explicitly approved a separate bounded registration diagnostic on 21 October 2026. This candidate adds getter-only nullable `HealthMonitorSnapshot.RegistrationDiagnostic` and optional detailed monitor `registrationDiagnostic`, reusing `HealthDiagnostic` and the existing v1 diagnostic schema. Existing constructors, public type names, required wire fields and schema versions are preserved. The schema/catalog/execution-fact notes, common fixtures and module usage/transport documentation describe the addition. Historical API/wire receipts and fixtures are not rewritten.

An accepted registration-scoped IntegrationFault replaces one monitor-lifetime diagnostic in atomic state commit order. Each live binding retains only its own last bounded diagnostic, normalized UTC event instant and optional source phase. Repeating those normalized values returns Duplicate without another revision or overwriting a later diagnostic from another binding. A distinct event advances revision once. Successful health observations, Registered, no-reason retirement and external unbinding retain the monitor slot. Retired.IntegrationError publishes its reason atomically with retirement; late facts remain Retired. Owned withdrawal removes current monitor details, and captured snapshots remain immutable. This does not change policy authority, counters, observation timestamps, collection diagnostics or execution-level errors, and supplies no absent Heartbeat lifecycle.

`HealthRegistrationDiagnosticTests` contains 11 test methods / 25 expanded cases under `CIS.Tests.UnitTests.Healthcheck`. Its public binding/service tests exercise sanitized/truncated exceptions, independent health/execution/collection errors, per-binding duplicate identity (including changed code/message/type/truncation/time/origin and absent fields), interleaved bindings, commit order with backward wall clocks, retirement/ownership and immutable old snapshots. Actual formatter/protocol tests exercise HTTP-style/pipe-envelope details, compact omission, legacy omission compatibility, exact scalar limits with supplementary Unicode, present-null/wrong-type/missing-flag rejection and malformed escaped Unicode. New common fixtures are HC-WIRE-010 (valid separate diagnostic without a health failure) and HC-WIRE-108 (explicit-null rejection).

Native execution belongs to the coordinating parent. No build/test/Stryker execution was performed by this author. JSON files were parsed locally and the existing implementation/seams were reviewed read-only. Existing snapshot04/u1 results cover their frozen earlier sources and provide no credit to this addition. A fresh native baseline, architecture/compatibility checks, transport closure and applicable coverage/mutation gates remain required; no threshold or exclusion is waived.

Focused native filter: `FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck.HealthRegistrationDiagnosticTests` (expected 25 cases; not an observed result).

Authoring source SHA-256 pins before coordinator formatting/execution:

| Repo-relative file | SHA-256 |
| --- | --- |
| `implementations/csharp/src/CIS.Core/Healthcheck/Contracts/HealthSnapshot.cs` | `1ceebb13053b88dd4034282c494dbaae0d9f721f1f01f0a8b47ca09c338c263e` |
| `implementations/csharp/src/CIS.Core/Healthcheck/Contracts/Execution/HealthExecutionFact.cs` | `b24142cde21e9376f4f4813bfa50eea56f05d5d1b4e8ff36400b9ea27ebc8198` |
| `implementations/csharp/src/CIS.Core/Healthcheck/Internal/Contracts/HealthSnapshotData.cs` | `e17b1afa98f90757bccf2cf56dc0c49a58adec18e964bcd730e117cc396ce514` |
| `implementations/csharp/src/CIS.Core/Healthcheck/Internal/State/HealthMonitorEntry.cs` | `f2c997dc9449d4907c3d4c55590918c7ff6e17946c44f06b8095b479b0596603` |
| `implementations/csharp/src/CIS.Core/Healthcheck/Internal/State/HealthStateCoordinator.cs` | `3e3a89690b99a372eb4ca75b0e34ddea98551696794b8a43ca8823c26d1dbe04` |
| `implementations/csharp/src/CIS.Core/Healthcheck/Internal/State/HealthStateCoordinator.Execution.cs` | `52e25e34d5f3cfeb2bebf7facc820b3d2ce6a39a6bab179950b9f51298a66f6a` |
| `implementations/csharp/src/CIS.Core/Healthcheck/Reporting/HealthReportFormatter.cs` | `82e9b67bc17492fba6d445f63404ce82ac2ffe116fb5144c35342295dcd81665` |
| `implementations/csharp/src/CIS.Core/Healthcheck/Reporting/HealthReportProtocol.cs` | `7161117a6783177bdbd94173148902b4288b687ba80e6c78f5a009d861d5bebf` |
| `implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Execution/HealthRegistrationDiagnosticTests.cs` | `34168008443ac3bea4a949869047fbd2ad0e8afc4865a69d27b3edee418c8114` |
| `contracts/healthcheck/v1/report.schema.json` | `aa2928101d0c9f10b9fc4f1ae4caba54978bbfd74b14e9e7c2af83f82d348b09` |
| `conformance/healthcheck/common/fixtures.json` | `a1c67c7b59f83193eb7af145fe297e49eb9ee32e01fcf76ceb5b3d2c7b05f8dd` |
| `conformance/healthcheck/common/valid/registration-diagnostic-details.json` | `a61752a6aaffd09f25325fcb7f544b030141859ba486d2e62f7d8d39fbb61f6e` |
| `conformance/healthcheck/common/invalid/registration-diagnostic-null.json` | `8b902e15b7c15358c3d9e4fe621db0ad8ea25b7bc0dfc4797e491cb9435d3633` |
