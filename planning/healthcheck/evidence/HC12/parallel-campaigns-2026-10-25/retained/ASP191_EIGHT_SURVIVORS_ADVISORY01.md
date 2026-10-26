# ASP.NET191 eight survivors: source-only advisory

Keep all eight as Survived. No waiver, exclusion, disposition, kill credit, new test or source change is proposed for the current campaign. Root reports the closed raw result as72 Killed /80 denominator =90%; this note does not replace the independent native/union/join audit or increase that score.

Root's complete C# retention/readback receipt `.artifacts/hcg/aspnet191-closed-readback02.json` reports verified109024 files /661,533,036B, no extra files, retention manifest SHA `2ff8790be4bb0b13bc404db3e8ffd40264bae83dbf11be936e92d547dcb5de83`. I did not repeat that whole readback. The four selected report/source/contract/test bodies were separately read and their physical SHA/byte values matched the corresponding retention-manifest rows:

- Actual report: `3c6457e517b12584978f043b55432d2534accebded9b8b896c713bcc860d35b6`,1,953,637B.
- Subject: `8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e`,6,372B.
- `contracts/healthcheck/v1/README.md`: `9a094b107b807d8ceeb0694c1eb27aaad9aab45e7aac1f9dc50570a7e3794354`,7,271B.
- `HealthcheckHttpTransportTests.cs`: `9c92cb0ec52b1289dfdc8ca9870098d8c3d93256ec686fa75677631e9b592db5`,62,856B.

The source path in every tuple is `Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs` with the subject SHA above. All eight report definitions have static=false. IDs below are addresses only in this exact report; identity rests on physical source, reported half-open span, operator, original text and replacement.

| Report ID | Exact reported span | Operator / original -> replacement | Actual source boundary |
|---|---|---|---|
|200|27:41–27:119|String mutation: `"Supply an explicit details authorization predicate when details are enabled."` -> `""`|Registration ArgumentException free text; same type and `authorizeDetails` parameter|
|230|53:149–53:154|Boolean mutation: `false` -> `true`|Await NotFound body write|
|241|59:165–59:170|Boolean mutation: `false` -> `true`|Await MethodNotAllowed body write|
|245|64:125–64:130|Boolean mutation: `false` -> `true`|Outer await of details authorization|
|255|69:155–69:160|Boolean mutation: `false` -> `true`|Await Forbidden body write|
|260|76:70–76:75|Boolean mutation: `false` -> `true`|Await normal health/readiness/details body write|
|267|84:62–84:67|Boolean mutation: `false` -> `true`|Inner await of application authorization ValueTask|
|274|88:168–88:173|Boolean mutation: `false` -> `true`|Await ReportingUnavailable body write after authorization error|

The string mutation is observable through Exception.Message, but the inspected Healthcheck contract/XML documentation does not promise that exact wording. `EnabledDetailsRequiresAuthorizationDuringRegistration` already checks the actual ArgumentException type, parameter name and absence of snapshot capture. Adding an exact prose assertion solely to kill200 would add a new compatibility requirement; no current behavioral contract violation is demonstrated by this survivor.

The seven ConfigureAwait changes are **not universal semantic equivalences**. A genuinely pending await can resume through a non-default SynchronizationContext or TaskScheduler when true, whereas false does not request that capture. That can change real posting, the completion timing of the middleware Task, and for245/267 the execution of the following cancellation/source/response work. ConfigureAwait(false) does not itself suppress ExecutionContext/AsyncLocal flow, so these are not evidence of a newly changed ambient-data privacy contract.

The inspected HTTP contract specifies reserved routing, authorization results, statuses/headers, complete formatting before response commitment and cancellation isolation. It does not explicitly promise context-free continuation. Thus the source shows an observable scheduling difference in other invocation environments, but this review does not establish a violated current contract or prove equivalence across all supported callers. The current native Survived result cannot settle that question by itself.

Existing tests already exercise genuine pending authorization (`SuccessfulAuthorizationRechecksRequestCancellationBeforeSnapshotCapture`) and actual gated body I/O (`ActualBodyWriteCompletionFaultAndCancellationGovernTheHealthRequest`), with real request/stream/service cleanup. They do not install an explicitly queued SynchronizationContext or non-default TaskScheduler. Ordinary MemoryStream writes also complete synchronously in several error routes. This explains a concrete unmeasured observation boundary without claiming which scheduling context the historical host actually loaded.

If context-independent middleware completion is explicitly selected as a supported property later, the smallest meaningful experiment is to extend the existing real gated-body case: invoke the actual public-built RequestDelegate under a queued context while its real body WriteAsync is pending, restore the caller context, complete the real write, and observe actual request completion versus an actual queued continuation. The original must complete after the real operation; a mutant capture must produce a real Post. Cleanup must then drain/pump every queued callback and join the same actual request/body/service, retaining every unknown fault. A short watchdog or a queued but unjoined continuation is not an assertion kill or settlement proof. Use an exact original positive plus one exact source-leaf negative before expanding any test.

Source-specific phases matter: the Forbidden and authorization-error write leaves need synchronous denied/faulted authorization followed by a genuinely pending body write. If authorization first suspends and its earlier false await resumes off-context, a later write-site true mutation can see no relevant context and remain indistinguishable. For245/267 use an actually incomplete authorization ValueTask. For230/241 use the actual respective404/405 route and a pending write. No artificial callback/task completion or generic context harness should replace those real operations.

Priority recommendation: preserve this advisory and the unchanged eight-survivor denominator, finish the actual three-partition source/normality/restoration/child audit and mandatory joined gate first. ASP already meets the stated raw90% threshold. No current campaign should be restarted, strengthened retroactively or granted extra credit for this analysis.
