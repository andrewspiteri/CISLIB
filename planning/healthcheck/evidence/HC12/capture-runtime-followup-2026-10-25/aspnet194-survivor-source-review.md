# ASP194 three-survivor source and disposition boundary

This source-only review covers the three actual Survived definitions in the immutable ASP194 report, SHA-256 `aecb6777e5cfeb5b508ec97dad8fee869a5963f2838af7d27d80ff8047592975`. The existing executed ParseReport source join is `../aspnet194-closed-causal-review01/actual01/facts.json`, SHA `0b9d30f46538b31d9b91f92eef8b73f766c1ac58acb3a91e161fac31ce78724b`: all 99 definitions were joined to their real source text/hash/UTF-16 spans/replacements, against the complete 146 shipping-source inventory. This note ran no utility, build, subject, classifier, disposition reader or campaign.

All three affected definitions are in `Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs`, physical SHA `8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e`, 6,372 bytes, as retained in common194 and the actual native run. Runner `1ea87750…` is another file in the current shipping inventory; it is not the affected ASP source hash. IDs below refer only to this report. All three actual records remain Survived with an empty KilledBy array.

| Report-local ID / UTF-16 span | Exact mutation | Source symbol and line |
| --- | --- | --- |
| 200 / [1297,1375) | String mutation: `"Supply an explicit details authorization predicate when details are enabled."` → `""` | `CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.UseCisHealthcheck`, line 27 |
| 230 / [2855,2860) | Boolean mutation: `false` → `true` | Same class, private `HandleAsync`, line 53: await of the NotFound 404 `WriteAsync` |
| 241 / [3248,3253) | Boolean mutation: `false` → `true` | Same class, private `HandleAsync`, line 59: await of the MethodNotAllowed 405 `WriteAsync` |

`UseCisHealthcheck` receives `IApplicationBuilder`, `IHealthSnapshotSource`, `HealthcheckOptions` and nullable `Func<HttpContext, ValueTask<bool>>`. `HandleAsync` receives `HttpContext`, path, source, copied options and the same nullable authorization callback. The two Boolean changes are specifically the outer `ConfigureAwait` arguments; they do not change `pipe:false`, the route predicate or the response-stream invocation.

## Individual contract findings

**200: descriptive registration reason.** The DetailsEnabled/no-predicate guard, ArgumentException type, `authorizeDetails` parameter and rejection before `app.Use` remain. C07 requires explicit authorization and rejection, not these exact words. Frozen `HealthcheckHttpTransportTests.EnabledDetailsRequiresAuthorizationDuringRegistration` (lines 36–44) checks the exact exception type, parameter and zero source captures. The mutation loses useful explanatory information; that is real diagnostic-quality debt, not equivalence. A generic nonempty Message assertion would not establish this requirement and may merely observe framework parameter text. No new exact-word or invented diagnostic-message contract is justified by the adopted contract alone.

**230: terminal NotFound await.** The reserved-route decision and bounded NotFound report are selected before `WriteAsync` starts. That helper (lines 110–121) checks request cancellation, sets status/content length/type/cache headers and returns the actual stream-write Task, or CompletedTask for HEAD. Changing the await can reschedule completion of `HandleAsync` and its OperationCanceledException catch. It can change real posting, progress, completion or cancellation observation under an admitted custom context/scheduler; it is not universally equivalent. C07 specifies route, response and cancellation behavior without prescribing continuation-context placement.

**241: terminal MethodNotAllowed await.** Unsupported-method rejection, `Allow: GET, HEAD`, the bounded MethodNotAllowed report and real stream-write invocation precede the changed await. The same completion/cancellation/progress boundary applies. No source evidence demonstrates removal of those required decisions. A particular continuation thread or an unpumped caller context is not an adopted independent oracle.

The applicable premises are C07 in `planning/healthcheck/contract-catalog.md` lines 309–336; the adapter's public remarks lines 9–14; the v1 HTTP wire rules; and `docs/modules/healthcheck/transports.md` ASP.NET section. They require reserved routing, explicit details authorization, complete bounded responses, cancellation/isolation and no business fallthrough. The implementation guide's instruction to preserve sync/async behavior and cancellation meaning remains applicable; these judgments do not waive it. Custom SynchronizationContext, TaskScheduler and application value-task sources are not declared unsupported. An actual supported counterexample that violates an adopted response/cancellation/progress requirement must reopen the individual await judgment.

Existing `ReservedErrorsDoNotCaptureAuthorizeOrInvokeBusiness` exercises real 404/405 responses. `ActualBodyWriteCompletionFaultAndCancellationGovernTheHealthRequest` (line 540) joins a genuine gated body write and preserves real completion/fault/cancel outcomes for a successful read path. Those tests are useful contract evidence, but passing them or observing ordinary Kestrel scheduling is not an equivalence proof for 230/241. No additional context-flag test or blocked-context experiment is justified solely to manufacture a kill.

## What the maintained catalogs can and cannot bind

`HealthcheckMutationAspNetAwaitDispositionCatalog.cs` (SHA `ed1ec0a668a4efcfa5d382181d9d2348479ab30191ebf868aa9c8488d8855fa5`) contains the exact 230/241 source tuples, with disclosed scheduling/progress differences. Its records bind report `5f15348728dd4443acbfc6d5fd5392fb3c7fbbed45ba9d4405075436d21549c4`, not the ASP194 report.

`HealthcheckMutationAspNet93DispositionCatalog.cs` (SHA `7313612429a48a29e6faf6c909d3b21eb1dbd659beb730eb97ddbea7be579b16`) contains all three exact source tuples as ReviewedNoncritical, but binds report `c532d6558aae3c9231f9bd7dee57c32c1682900107c84d6ee8b2365772ff9c16`. Its primary/peer bodies are pinned to those old records. Equal source tuples provide a reason to perform a fresh severity review; they do not transfer the old report, IDs, outcomes or approval.

`HealthcheckMutationDispositionValidation.Bind` requires exact maintained review ID/kind/lane/group/report, complete definition identity and current source/span, plus exact closure/evidence pins. `Complete` additionally requires all three complete source-checked partitions and the live report/status/empty-KilledBy binding. `HealthcheckMutationUnionDispositions` physically pins every closure/evidence body. Therefore neither existing catalog can be applied unchanged here. At minimum any future adoption needs three fresh individually reviewed ASP194 records, a distinct primary and independent peer body, fresh exact closure pins, an explicit maintained catalog addition preserving old entries, and actual strict reader/whole-union binding. This note is not that application.

The common194 source/contract premises re-read here retain the old adapter/options/v1/catalog/technical-intent hashes:

- Adapter: `8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e`.
- HealthcheckOptions: `75d3e327b107682e5321b65991825fd51b7c5a6c12f7c539aded2dd758d8d419`.
- v1 README: `9a094b107b807d8ceeb0694c1eb27aaad9aab45e7aac1f9dc50570a7e3794354`.
- Contract catalog: `28a8ae959d30b7e35e8fd3002e585b06d483db7a282bbf78f7528f8e19335ad9`.
- Technical intent: `b7e8fc3f8692ab6d5a28f1eb11d1e6582196afd669a9e27197c285ff8593dab8`.

The currently reviewed transport documentation is SHA `35cb9825cceac23c278ff0a6420e3bd62f599721aaba4004237135ce743db533`, whereas the old catalog pins `3f2fa4ff…`. The frozen actual HTTP fixture is `b800977a11889fc592188339cb23bd5ef148b603a1cea72c036708d148af4dce`, whereas the ASP93 catalog pins `9c92cb0e…`. The workspace implementation guide remains `01b4b234fd460088f6c94abb0fc151ca7089375edaf473bf90bc1215d780a675`. Workspace-only review premises must not be mislabeled as original frozen snapshot bodies. Any new closure must explicitly choose and retain its real body/version rather than changing an old catalog's pins in place.

## Recommendation and remaining holds

The source and unchanged adopted contract support a fresh, individual **ReviewedNoncritical** severity review for each of these three tuples, with the disclosed diagnostic/scheduling differences above. No new genuine assertion gap is established here, and no universal equivalence is claimed. The two await survivors do not require speculative context instrumentation, and the message survivor does not justify freezing prose without a separately adopted diagnostic requirement.

No disposition is applied. If later accepted through the maintained exact-binding route, all three raw Survived definitions remain in the denominator and contribute zero numerator. The current ASP194 aborts, 5 Timeout definitions, child-audit failure, missing launch, incomplete controller/fresh-baseline evidence and every other original hold remain independently unresolved. Neither this source review nor any later noncritical disposition supplies native integrity, score recovery or qualification.
