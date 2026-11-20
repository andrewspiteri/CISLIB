# WORK-005 independent public testing and guide review

Independent source review used eight minutes. No runtime/helper defect was found.
The sixteen-script/sixteen-record defaults, before-dequeue exhaustion, original
begin fault, first reason, cancellation/refusal precedence, immutable unknown and
cleanup scripts, normalized rollback/disposal and separate portable-only context
match the accepted testing boundary. Twelve unit cases and the existing isolated
RestPackage consumer cover the public shape without private/reflection bypass.
No SQL engine, production owner reuse, new dependency or test framework is added.

One required guide correction was identified: source section 15.2(1) requires the
repository skill to direct readers to root AGENTS.md and the approved dependency
policy. Both links were added and independently confirmed in a further half
minute. The bundled skill validator passed before and after that correction.

A separate fresh-context, read-only forward exercise used the skill and minimum
linked sources for a realistic three-store order service. Four active minutes
produced supported public APIs, explicit context/ownership, caught-error marking,
three-way persistence handling, non-replay of uncertainty and bounded portable
testing limits. No unsupported API or consequential ambiguity was reported. This
was instruction/source validation, not executable consumer or database evidence.

At the source-review checkpoint, package/native qualification remained pending. The first Core-only build
found a generic MSTest assertion inference error between owner and context
interfaces; the test now supplies object explicitly. That test-only compilation
correction changes no helper/runtime behavior. The actual rebuilt unit result,
outside-friend packages and integrated database/collector evidence still govern
completion of this outcome.

## Actual public-helper unit checkpoint

The rebuilt helper tests subsequently passed **12/12 on Windows and 12/12 on
actual Ubuntu**, with zero failures, errors, timeouts, aborts, inconclusive or
unexecuted cases. Both TRX files contain only
`CIS.Tests.UnitTests.CommonTesting.ScriptedLocalTransactionFactoryTests`.
The earlier current transaction run independently contains **73/73 on each lane**
across Admission, CleanupSafety, CompletionSafety, Contract, Foundation,
Registration and Telemetry classes, likewise with zero nonpasses. These are
unit/provider-double and recorder observations, not database or scrape evidence.

Evidence paths below start at `.artifacts/transactions/CIS-0017/`:

| Actual result | TRX | SHA-256 |
| --- | --- | --- |
| Windows helper 12/12 | `WORK-005/public-helper-test/results/public-helper-windows.trx` | `a93fbccb4232736d888a4f35cba1a4fc8ad62b4d677940dcb2c84ccdf91e5007` |
| Ubuntu helper 12/12 | `WORK-005/public-helper-linux/results/public-helper-linux.trx` | `b13fa2a05fbefe79de054856f35b2c9e2d214d3a8aec1a88101b653861783bcd` |
| Windows transactions 73/73 | `WORK-004/core-transactions-test/results/observations-windows.trx` | `717bb306d8d68486424a713a6f068ae4b6c9f5c0e46f7dd0271b803c7e9407aa` |
| Ubuntu transactions 73/73 | `WORK-004/core-transactions-linux/results/observations-linux.trx` | `98ef1f10794f3495db15544aeecd53d8351b9f10e55765fdab07abc40dd18f7f` |

At this checkpoint, independently hashed current Release source outputs and
their unit-bin copies match exactly:

- Core: `ea1eef108d6efcc07350388e9493d5bb0ca0a81b4309f8bfb7dcb3a7b29eb69b`.
- CommonTesting: `9071fa1c4b08f74691c7ce8163c6f98be21104e18cdfc37cab514b8c2f7b3174`.
- Current unit assembly: `fd79b05ef0c3d0826a075bce69b7434884b6d9463ff63bcb946c4150172ff059`.

Both retained native logs identify this unit output; Ubuntu executes the shared
checkout output through the actual Linux runner. Its runtime configuration pins
Microsoft.NETCore.App and Microsoft.AspNetCore.App to 10.0.12 with roll-forward
disabled. The independent snapshot is retained as
`WORK-005/public-unit-receipt.json`, explicitly **before the final integration
build**. This snapshot does not establish a later rebuilt candidate's identity.

Follow-up source/evidence review used four minutes, plus approximately three
minutes for this recorded checkpoint and hashes. No additional product execution
was launched by the reviewer. The public package test still requires its actual
private-feed restore/build/child execution and deployed-package hash/closure
assertions. Actual PostgreSQL and exporter/scrape checks remain pending their
own native execution. EF, real lost acknowledgements and broader release
qualification remain unexecuted; no completed outcome is inferred from these
unit results.

## Final actual package checkpoint

The pending Windows package observation above is now resolved. The existing
`CommonTestingPackageTests.ActualOptionalPackageContainsTheCurrentPublicHelpersAndOnlyCoreAsItsPackageDependency`
passed **1/1**, with zero failed, inconclusive, aborted or unexecuted cases.
Its native log and independently parsed TRX are retained at
`WORK-005/public-package-windows/`; the TRX SHA-256 is
`680e9d5796282ec704ef1d51a3ff23a31bfc2550e92fa7e4885f5debcdf2939b`.

This actual test packed the current Core/CommonTesting outputs, restored through
a fresh private cache and local-only feed, built the outside-friend public
consumer and executed its native child. Its assertions verified public context
propagation, refusal/Unknown/cleanup facts, portable native-capability rejection,
bounded recordings, the restricted package closure and unchanged current,
packaged and deployed bytes. It copied no library source, used no ProjectReference
or private bypass, and introduced no new consumer harness. TestContext retained
these actual `0.1.0-dev.1` package archive SHA-256 values:

- Core package: `13fe6ab348e3bd2126af9211de6feeef2447d32a0772624d80e1a0949401db43`.
- CommonTesting package: `804dafc95e59fbfc948222ee19c5c7e7994072e04133c0d1ca35ba5e0b20df06`.

The final source-output, unit-copy and integration-copy hashes were independently
rechecked and all agree with the retained pre-integration unit snapshot:
Core `ea1eef108d6efcc07350388e9493d5bb0ca0a81b4309f8bfb7dcb3a7b29eb69b`;
CommonTesting `9071fa1c4b08f74691c7ce8163c6f98be21104e18cdfc37cab514b8c2f7b3174`.
The root `integrated-receipt.json` records this final frozen-source build under
SDK 10.0.401 and runtime 10.0.12 with roll-forward disabled. It is a working-source
candidate observation, not a clean-commit or release qualification claim.

The integrated receipt also records 11 real PostgreSQL cases and 33 collector
cases on each actual Windows/Ubuntu runtime lane, alongside the retained
73 transaction and 12 helper unit cases per lane. Its totals are 130 Windows and
129 Ubuntu focused cases, including the single Windows package case. Detailed
database/collector qualification belongs to those outcomes and their receipts;
this public-helper review does not substitute package simulation for either.

**Full Linux package consumption remains unverified.** Ubuntu executed the
current built assemblies through the existing Linux runtime; no Linux SDK was
installed and no Linux package restore/build/public-child qualification ran.
Do not reinterpret those 129 runtime cases as a Linux package-consumption pass.
EF, real network lost acknowledgements and broader release qualification remain
unexecuted. No required public-helper finding remains. Final read-only evidence
review and this append used approximately three additional active minutes, with
no reviewer product execution, CIS or Git mutation.
