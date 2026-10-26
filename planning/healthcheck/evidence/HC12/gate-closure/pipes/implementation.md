# Pipes gate-closure implementation and executed baseline

The current tranche adds real pipe/probe detectors and two bounded source
simplifications. It does not claim a mutation gate or release qualification.
The [historical inventory](historical-remaining.md) separately reviews all 114
retained survivors and 25 timeouts without rewriting their outcomes.

## Changes

Existing integration classes preserve the declared Stryker filters. Fifteen new
pipe rows cover constructor nulls, zero/negative clock frequency, stop before
start and repeated disposal, exact monotonic deadline equality, truncated request
headers/bodies, cancellation/format exceptions from the actual source, and disposal
of the real request-owned TimeProvider timer. Nine new actual probe rows cover
missing/partial response headers, default/upper valid CLI settings and exact
1023/1024/1025-byte response payload behavior under a 1024-byte limit. Existing wire
success cases now require complete snapshot bytes plus one newline. Error cases
require exit 2, empty stdout and bounded meaningful stderr, and reject supplied
private server detail. They deliberately accept a changed cosmetic program label.

The coordinator approved one later pipe case,
`DisposalWaitsForAnActuallyRunningSnapshotSource`. It starts an actual request,
waits for synchronous source entry, calls disposal before releasing the source,
and immediately checks that disposal is still pending. Finally cleanup releases
the source and joins the actual operations. This is actual settlement evidence,
not a sleep or caller-context assertion. It was added after snapshot 03 and is
**not covered by the 117-case baseline below**.

`HealthPipeServer.HandleClientAsync` now obtains its response from an extracted
`ReadResponseAsync` helper using direct returns. This removes the uninitialized
response variable across conditional/exception assignments that produced collateral
instrumentation compiler rollbacks. The selected-view error mapping remains:
framing failure→InvalidFrame, disabled details→DetailsDisabled, source failure→
ReportingUnavailable with its selected view. Prewrite and postwrite elapsed checks,
bounded frame write and actual client-drain read remain in the original handler.
This is a maintainability/definite-assignment simplification, not an exclusion or
a promise that the new native tool run has zero compiler rollbacks.

The private probe parser uses a nullable out result with
`NotNullWhen(true)` instead of allocating an unused default settings record before
validation. Valid settings/defaults still come from the same locals and final
successful assignment. Historical true-return parser mutants must remain tied to
their old initialized-default source; a changed-source null-settings crash does
not automatically become a credited kill. The coordinator is separately requiring
actual Core/selector/MVID/async-frame positive and negative attribution controls.

## Honest concurrency baseline history

The concurrency correctness case previously used a 1000 ms request budget. Actual
normal runs failed reading a response header:

| Native run | Actual result | Scope |
| --- | --- | --- |
| [Initial canonical TRX](../../../../../../.artifacts/csharp/focused/2944122fc4d54f81b36ced1fa9b0e52b/selected.trx) |176 passed/177 total,1 failed|HTTP+pipe+probe normal parallel integration selection;1s version|
| [Copied serialized TRX](../../../../../../.artifacts/hcg/pipe-inputs-01/source/.artifacts/csharp/focused/9bbeb1e42ab341429180e4079ed170c5/selected.trx) |115 passed/116 total,1 failed|Pipe+probe+scheduling selection under other separate native job load;1s version|
| [Updated copied TRX](../../../../../../.artifacts/hcg/inputs-03/source/.artifacts/csharp/focused/1773e1aaea2a44f697977f5239c4d37d/selected.trx) |117 passed/117 total,0 failed/0 skipped|Serialized pipe+probe+scheduling selection;5s concurrency version, timer case included; later disposal case absent|

Both actual failures are retained as failures. The failing method in each was
`ManyConcurrentClientsSettleAndStopIsTerminal`, with EndOfStream while awaiting the
four-byte response header. Overlapping process startup/normal native work could
consume the one-second deadline, but no retained per-stage server timestamps prove
that cause. This remains an inference, not a relabeling as a pass or dismissal as
flakiness.

The coordinator approved changing only that concurrency correctness case's
request budget to 5000 ms. The outer 15 s client bound and actual child deadlines are
unchanged. Dedicated short deadline and exact elapsed-boundary cases retain their
own budgets. The new 117-case execution succeeded with 0 build warnings/errors;
this demonstrates that selected current normal closure, not the entire release
or any mutation denominator.

## Input and receipt pins

Snapshot 03 preparation selected 1367 files. Its exact source digest is
`ff53f93fb83bb0f8b7321aeb8b435d7ef6a07e5f3b2b5bf346a7635fecde7eb1`;
the [source-input manifest](../../../../../../.artifacts/hcg/inputs-03/source-inputs.json)
SHA-256 is
`d530562e17eea79a10a4c5a69f52708c1fc71d6c637ac999056cafc018687a00`.
The later source/test note edits are not retroactively part of that snapshot.

| Snapshot 03 file, relative to repository | SHA-256 |
| --- | --- |
|implementations/csharp/tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/HealthcheckPipeTransportTests.cs|`b68e5a03e59d960c75ad0765a8f07c967bd6766291b37334b9635333c80f2547`|
|implementations/csharp/tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/HealthcheckProbeProcessTests.cs|`31c99407e9221fae40990509598046cf2d4b6ef17e20d9888d32122dbb6673f4`|
|implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthPipeServer.cs|`809ba1a493d6176e354f84209ea556ae21eb726e626b35eb7cc777956cdba12f`|
|implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthProbeClient.cs|`ad5afc9bf1f19fddcbd478efd88616607c87262061c75da6cf97407a2994a512`|

| Retained native receipt | SHA-256 |
| --- | --- |
|Initial canonical failed TRX|`7d4497d463549e10a3bbebec5d0cecae339e400370126f5eadfca51d105ce1ce`|
|Copied failed TRX|`7e5ed748bbd26636a3b227c69c1979968f35b1a4961c3db34fb1a8ae2bf941c6`|
|Copied failed baseline log|`7ea3d39e99a14292e4d326c67a199e5616dcb761f16fc20eb92947f555e6b726`|
|Updated passing TRX|`82e272175e19e46d9f42b2397a2daa58fa2f4af622061ab862890561bb754b48`|
|[Updated passing baseline log](../../../../../../.artifacts/hcg/pipes-baseline-02.log)|`887a3e9a8232e7313ebea0d7c0c94cffe596bbd0082b5c8e0b2f745bcb3b5fbc`|

The later pending-disposal test file, before coordinator formatting, hashes
`1ac7dfbd4e308bfb627ed7f74db8693bc81629d0f180d33587df6be5a9214848`.
Its next actual baseline/replay needs a new coherent input receipt.

## Remaining qualification

The new [diagnostic partition configs](../../../../qualification/gate-closure/pipes-parts/README.md)
have disjoint mutate arrays covering exactly the four original Pipes files, with
Complete/all kinds, coverage analysis off, strict 90% thresholds and no source
exclusions. Parent/child exact loaded Core, owner schema, selector, normal/managed
terminal attribution, forced-stop/timeout checks and copied negative audit controls
remain independent requirements. Each partition's exact declared test closure is
part of its evidence. Do not average percentages or reuse numeric IDs across
changed source hashes. Fresh compiler rollbacks, ignored records, survivors,
unknown child failures and timeouts must all remain visible, followed by the
full-scope gate or a validated complete partition-matrix reconciliation.

