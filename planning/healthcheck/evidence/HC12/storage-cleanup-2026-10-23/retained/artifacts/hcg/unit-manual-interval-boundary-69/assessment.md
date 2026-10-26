# Manual collection with an unactivated interval

This is a private proposed test. No canonical source, shared build outputs, subjects, or mutation reports were changed or executed by this owner.

Proposed canonical destination: `implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Collection/HealthCollectorManualIntervalBoundaryTests.cs`.

Proposed source SHA-256: `662e5599ab62b94f5719f1a4502b0b1ba41bc6c7b43e2671df1c2ca7beb9d56a`.

The test exercises C04's separate manual and periodic activation paths. Registration with a positive interval is inert; the owner never calls `ActivatePeriodic`. Two real manual callers must each acquire a lease, enter its callback, commit its successful result, and join cleanup. A configured interval alone cannot become periodic scheduling or retire the registration.

Registration occurs at monotonic timestamp zero with a one-tick interval. The first actual lease cleanup advances the injected monotonic timestamp to `long.MaxValue`. The frequency is exactly `TimeSpan.TicksPerSecond` and UTC remains fixed and available. This is a valid monotonic progression and a representable timestamp. Only adding an unnecessary future periodic due time would overflow.

The first caller is awaited after its actual cleanup is released. The second caller is raced against its actual held callback. The targeted mutation returns `Retired` instead of entering that callback, so the direct `Committed` disposition assertion fails promptly. A timeout is only an uncredited bounded wait failure. The fixture does not require synchronous successful task completion, private identity, or diagnostic prose. Every supplied callback/cleanup gate is released before terminal runner/service drain, including assertion failures.

## Reviewed contract and source pins

| Input | SHA-256 | Relevant behavior |
| --- | --- | --- |
| `planning/healthcheck/contract-catalog.md` | `28a8ae959d30b7e35e8fd3002e585b06d483db7a282bbf78f7528f8e19335ad9` | C04 registration has no invocation or periodic activation; `ActivatePeriodic` explicitly enables recurrence; `CollectAsync` starts or joins one operation. |
| `planning/healthcheck/technical-intent.md` | `b7e8fc3f8692ab6d5a28f1eb11d1e6582196afd669a9e27197c285ff8593dab8` | Line 117 makes manual collection available after runner start independently of periodic activation. |
| `implementations/csharp/src/CIS.Core/Healthcheck/HealthCollectorRunner.cs` | `386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8` | Line 524 updates completion-plus-interval eligibility only for a live periodic entry. Overflow retires the entry. |
| Existing `HealthCollectorResourceBoundaryTests.cs` | `0dd1af3011177c783e9b5b91fffb9aa4166ac9c081da88df56fe1b75eeb2d968` | Existing representable/overflow rows explicitly call `ActivatePeriodic`, so they do not cover this inactive interval. |

The expected old survivor targets are 1511 and 1513: the outer or inner conjunction in `!retire && operation.Entry.Periodic && !operation.Entry.Closed` changes to disjunction. With a live manual entry, these changes enter the future-due calculation despite no periodic activation, overflow, and withdraw the entry. IDs alone are not qualification identities: a finite campaign must bind the exact current source span, original/replacement text, actual native report and direct assertion witness.

## Graph context and validation limits

The repository graph-context skill was read and used. Initial status and exact `ExecuteAsync` lookup were fresh on build `9f35504ab0a3ab6a228c8468315884e3466e60dcb3856b30605d6f23ade663ec` (19,400 nodes, 106,925 edges, no status diagnostics). A broad initial name lookup was truncated and was superseded by the exact method lookup, which returned one node without truncation.

The bounded outgoing related query used that exact method, depth one and limit eight. It returned eight nodes and fourteen traversal observations, marked truncated. Observations used `cis.csharp.semantic-facts/3` and located `Withdraw` at Runner line 531 and `SignalProgress` at 542. That query reported the graph stale because the canonical `HealthCollectorRetirementAdmissionRaceQualificationTests.cs` changed concurrently. No graph rebuild or graph database write was attempted by this owner; the coordinator owns refresh after canonical adoption. The Runner source pin and contracts above were inspected directly and remain the behavioral basis.

No build, native execution, finite mutation result, gate improvement, or release qualification is claimed for this proposal. The coordinator must review it, adopt it when shared builds are available, run the actual native case, and obtain source-bound direct mutant evidence before credit.
