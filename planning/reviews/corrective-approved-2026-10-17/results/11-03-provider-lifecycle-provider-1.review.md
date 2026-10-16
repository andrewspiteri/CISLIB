# Static review: subpass 03-provider-lifecycle-provider-1

**Scope.** This is a static, read-only review of the supplied current files, the six deleted Python readers and the bounded context. I executed nothing, so nothing here confirms that any test runs or passes. It is not a whole-change pass and not release qualification.

## Confirmed findings

### F1 — Medium: the foundation doc contradicts the current provider code

**Where:** `docs/modules/logging/microsoft-provider-foundation.md`, lines 3, 5 and 9, compared with `CoreLoggerProvider.cs` (around lines 6–16, 52–59 and 92–100) and `ProviderScopeCapture.Copy`.

The doc uses present tense, but the code has moved past it:

| Doc line | Doc says | Current code does |
| --- | --- | --- |
| 3 | The provider "implements ILoggerProvider". | The class is `ILoggerProvider, ISupportExternalScope`. |
| 5 | Numeric EventId, template and structured-state mapping "remain WP06-S01-T02/T03". | `Log` forwards `eventId.Id` and calls `ProviderStateCapture.Capture`, which preserves `{OriginalFormat}` and structured fields. The class XML doc describes this. |
| 9 | "Other Microsoft scope forms currently raise ArgumentException." | `ProviderScopeCapture.Copy` accepts any `IEnumerable<KeyValuePair<string, object?>>`. |
| 9 | ISupportExternalScope/SetScopeProvider "remain integration work". | `SetScopeProvider` and borrowed host scopes are implemented. |

Concrete consequence: the doc says typed scopes are copied at `BeginScope` and follow core LIFO rules. Because the provider now implements `ISupportExternalScope`, the real Microsoft `LoggerFactory` routes `ILogger.BeginScope` to its own external scope provider. It does not call `CategoryLogger.BeginScope`.

- So for factory-created loggers, scopes are copied when the event is captured.
- The host-story `async-external` expectation shows this: `phase: "changed"`.
- The doc's statement about copy timing and ownership is therefore true only for loggers created directly from the provider.

**Fix:** either mark this file as the frozen T01 record and point to the current surface, or update lines 3, 5 and 9.

### F2 — Medium: provider-operation expectations are not at parity with the deleted reader, and four control IDs now mean something different

**Where:** `ProviderOperationExpectations.cs` (around lines 13–29, 53–57 and 71) and `ProviderOperationVerifierTests.cs` (rows `omitted-loss`, `explicit-value`, `marker`, `omitted-parent`, `duplicate`), compared with the deleted `verify-provider-operation.py`.

The native expectations encode different behavior from the deleted reader:

| Mode | Deleted Python reader | Current C# |
| --- | --- | --- |
| `nonstring` | 2 rows; non-string `operation_id` kept as an attribute | 1 row `after`; `ownedErrors` = 2 |
| `duplicates` | rows `duplicate.0`–`duplicate.4`, varied operations | rows `rejected.0`, `rejected.1`, `duplicate.2`–`duplicate.4`, all `parent`; `ownedErrors` = 2 |
| `host-omitted` | `truncated` = 1; truncation marker; operation `parent` preserved | `invalid` = 1; no marker; operation null; `formatterCalls` = 2 |
| `bounds` | no `event_id`; 1022 and 1023 fields | `event_id` present; 1021 and 1022 fields |

Four control IDs are kept but now corrupt a different field or expect a different reason:

| Control | Python | C# |
| --- | --- | --- |
| `omitted-loss` | `truncated` → "truncation accounting" | `invalid` → "invalid accounting" |
| `explicit-value` | wire `operation_id` → "operation and attributes" | `ownedErrors` → "owned ID errors" |
| `marker` | removes the marker | adds a marker |
| `omitted-parent` | removes `operation_id` | sets `operation_id` to `parent` |

These changes match current production code (`CaptureScope` throws on a non-string `operation_id`; `ProviderScopeCapture.Capture` throws when depth is exceeded), and the contract supports them ("Invalid operation ID → ArgumentException"). But this is a behavior change, not reader parity. Keeping the same IDs with new meaning conflicts with the stable-intended-reason discipline in `corrections.md`.

**Needed:** a recorded disposition saying that historical Python-reader receipts for these modes describe superseded semantics. Any migration-receipt or catalog wording that claims these controls were "preserved" should be corrected. (The foundation doc makes that claim for its own family, where it does roughly hold.)

### F3 — Low: env/version protection checks no longer require the fields to be present

**Where:** `ProviderHostStoryExpectations.cs` (around lines 69–70) and `ProviderOperationExpectations.cs` (around lines 67–68).

- The Python readers indexed `row['env']` and `row['version']` directly. A missing field raised `KeyError`, so the reader failed.
- The C# check `(string?)row["env"] != "fake"` passes when the field is absent.
- `ProviderReportChecks.Identity` does not check `env` or `version` either.

Concrete failure: in the `factory-precedence` or `protected` modes, a record that drops both automatic identity fields passes "protected Activity and identity".

**Fix:** require both fields to be non-null strings, then check they are not `"fake"`.

### F4 — Low: per-case artifact evidence is inconsistent and weaker than the deleted readers

**Where:** `ProviderFoundationProcessTests.cs`, `ProviderMappingProcessTests.cs`, `ProviderStateProcessTests.cs`, `ProviderBehaviorProcessTests.cs`, compared with `ProviderReportProcess.cs`.

- The deleted readers persisted, per case: command, PID, raw stdout/stderr/file bytes, their SHA-256s, the verifier SHA-256 and the catalog SHA-256.
- `ProviderReportProcess` (five families) writes command, PID, file SHA-256 and the report to `TestContext`, and re-hashes the host and Core assemblies after the run.
- The four standalone process tests write no command, PID or hash evidence and take no pre-run hash.
- Host-filter console output is logged only as a decoded string. There is no raw stderr hash.
- `ProviderBehaviorProcessTests` relies on the self-reported `leaseReleased` for physical modes. It never opens `active.log.cis.lock` with `FileShare.None`, as the foundation, mapping and state tests do.

Concrete consequence: a receipt for those four families can show only pass/fail per case. It cannot identify the per-case physical bytes the way the replaced reader reports did.

**Fix:** route these four families through `ProviderReportProcess`, or log the equivalent identities.

## Questions and limits (not confirmed findings)

1. **Record byte bound.**
   - Every verifier hard-codes 16384 bytes per JSONL record, and the `oversized` and "byte-budget" controls rely on it.
   - Your decision sets the default entry cap at 64 KiB.
   - If the fixtures run with that default, these controls test an assumed bound rather than the configured one, and a legitimate 16–64 KiB record would be rejected as "bounded records".
   - Please confirm the fixture configures a 16 KiB cap, or derive the bound from configuration.
2. **Console BOM check.** Host-filter validates `Encoding.UTF8.GetBytes(consoleText)`. If `ProcessRunner` decodes stderr through a `StreamReader` with BOM detection (the `Process` default), a leading BOM is stripped before the "no BOM" check. Python checked the raw bytes. `ProcessRunner` was not supplied.
3. **Integer asymmetry in `ProviderStateCapture.Value`.**
   - Any `long` is accepted.
   - A `ulong` above 2^53−1 throws, which makes the whole event invalid.
   - Is that intended? It depends on how `LogValue.Integer(long)` projects large values, which I could not see.
4. **Contract wording on disposal.** The provider ownership text in `runtime-contract.json` says disposal "stops only provider-owned registrations/scopes". But ownership mode `shared` expects a provider-created scope to survive provider disposal (`direct.scoped` keeps `owned.operation`). The wording should say provider disposal leaves creator-owned scopes untouched.
5. **Unverifiable counts.** I cannot check the doc's "Nine ordinary unit cases" or "two compiled architecture checks", because those tests were not supplied.

## Missing context

- Readers I could not compare: there are no deleted Python readers for provider-scope, provider-ownership or provider-state.
- Fixture and test-support code: the `conformance/logging/*-cases.json` catalogs, the required-case binding inventory, the DiscoveryHost fixtures (`Provider*Fixture`), `ProcessRunner`/`TemporaryDirectory` and `verify-flush-results.py` (`number`, `decoded`).
- Core types: `CoreLogRuntime.Scopes`/`BeginCopiedScope`, `CapturedOperationContext`, `LogValue`/`LogValueProjection` limits.
- Provider unit and architecture tests, the dated migration receipt, `planning/decisions.md` and the plan amendments.

Within the supplied scope, the eleven previously fixed findings do not overlap these files.