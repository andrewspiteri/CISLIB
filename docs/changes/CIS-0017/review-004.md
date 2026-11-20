# WORK-004 independent observation review

Initial independent source review used twelve active minutes and found one P2
required correction: quiet mode suppressed a failed unit when an escaping
callback fault was followed by ordinary await-using disposal. The public snapshot
correctly remained NotCommitted/UncommittedDisposal with no primary/cleanup error,
so a snapshot-only logging predicate lost failure eligibility.

The narrow correction supplies an internal rollback-only boolean to Completed,
captured under the existing gate after any callback settles and passed at the
existing guarded observation after actual cleanup. It changes no public outcome,
end reason, error, native failure count, timing or admission/terminal decision.
No raw reason or exception is exposed. Two actual await-using regression rows
retain the original failure/cancellation and expect one safe Warning, no Error,
balanced active measurement and zero guessed native failures.

Independent correction review used two further minutes and resolved the finding
in source. No other required findings or optional corrections remain. Complete
binding capture, publication-through-cleanup lifetime, nullable report seconds,
two disposal failure increments, fixed host/alias domains, registration order,
opt-outs, capacity validation and individual telemetry guards were reviewed.
The existing gate, callback marker and metric attachment design remain direct;
no registry, scheduler, aggregator, new provider or collector in Core is justified.

Final authored scope is nineteen Unit cases and ten Integration cases. This
review makes no passing-test or scrape claim. Native observation/collector
verification remains pending the actual current candidate and restore.

## Final native evidence review — 21 November 2026

The subsequent integrated receipt and retained native logs/TRXs now supersede
the pending-execution statement above. Independent file/hash inspection found:

| Actual lane | Transaction Unit cases | Collection cases | Failed / not executed |
| --- | --- | --- | --- |
| Windows | 73, including 19 telemetry cases | 33, including 10 transaction collection cases and 23 affected regressions | 0 / 0 |
| Ubuntu | 73, including 19 telemetry cases | The same 33-case scope | 0 / 0 |

TRX counters, individual result outcomes and class membership agree with
`.artifacts/transactions/CIS-0017/integrated-receipt.json`. The Windows and Ubuntu
collection TRX SHA-256 values respectively match
`b66642efeb3b225877e3257ce98dc437d3a58cc742b34c513ad231457b3fb93b` and
`93e3a16d38465fac0cebbfb41663bb74acd52de8573191ed0adbc0080e99a2b8`.
Both 73-case Unit TRX hashes also match that receipt; the native logs report the
corresponding successful collection runs.

Both collection TRXs explicitly record
`ActualScrapeUsesTheReportDurationAfterHeldCleanupAndKeepsMandatoryIdentity` as
passed. Its reviewed assertions exercise the actual exporter/HTTP scrape,
active work during held native cleanup, no early duration, the report's exact
3.75-second completed measurement, balanced active state, exported names and
histogram bounds, authoritative identity and absence of private connection text.
The other new cases exercised registration order, passive bounded metadata,
undersized capacity rejection, opt-outs and registration-owner identity.

Current Core source-project, Unit-output and Integration-output copies all hash
to `ea1eef108d6efcc07350388e9493d5bb0ca0a81b4309f8bfb7dcb3a7b29eb69b`.
The collector source-project and Integration-output copies both hash to
`a1c9e82a999227b25a71da4b318bd40c0748de9396751eb8034f095c98e150c1`.
These and every component entry in the integrated receipt match current bytes.
No new required finding remains for WORK-004.

This final evidence inspection took five further active minutes. The reviewer
launched no tests/builds, changed no product source and performed no Git/CIS or
database operation. This confirms the retained transaction observation/collector
checkpoint, not a clean-commit, real PostgreSQL, package-consumer, EF,
network-fault or broader release qualification claim.
