# Short mutation profiles

Later user direction requests more parallelism. The [eight-worker profiles](parallel8/README.md)
are the continuation configuration; the two-worker profiles here preserve the
actual Unit185/Pipes185 inputs.

Use these profiles for further Unit/runtime and Pipes mutation work. They set
`additional-timeout` to **1,000 ms** and keep mutants separate. The complete source
scope, test filters, two runners, coverage modes, no-bail policy and 90% thresholds
are unchanged. Historical profiles and outputs retain their original identities.

The pinned Stryker 4.16.0 calculator uses a fixed 1.5 multiplier on its measured
initialization and selected-test estimate, then adds this allowance. It has no
supported `timeout-factor` option. Setting `additional-timeout` to 15,000 would
add fifteen seconds; it would not impose a fifteen-second ceiling.

For the recorded Unit170 baseline and subsets, the new allowance would reduce
emitted session budgets from 4,518,837–4,534,207 ms to **19,837–35,207 ms**. For the
recorded Pipes180 baseline, it would reduce 2,289,586 ms to **190,586 ms**. These
are calculations from earlier observations, not measurements of a new run.
Fresh launches must record their actual emitted budgets. The instrumented Pipes
baseline takes about two minutes, so fifteen seconds for that complete cohort
would time out healthy tests.

The replacement Unit185 run emitted its first eight mutation budgets at
18:40 UTC on 24 October: **32,995–39,824 ms**. This is a sample from its fresh
baseline, not a complete-campaign audit or a wall-clock guarantee. The separate
Pipes185 replacement was still initializing at that observation. Its first two
mutation requests later emitted **223,522 ms** each at 18:50 UTC.

The original instrumented baseline runs separately from mutation timeouts.
Cancellation, infrastructure retries and the existing five-second host shutdown
can add wall time. These profiles do not claim a hard fifteen-second iteration
limit. No timeout, forced stop, incomplete cleanup or host crash supplies kill
credit, and a timeout must not trigger a silent return to the long allowances.

User direction on 24 October 2026 supersedes the earlier long-budget launch plan:
stop the running Unit170/Pipes180 native trees with their verified ownership,
allow their controllers to preserve incomplete receipts, and use new immutable
outputs for the short profiles. Do not edit an existing run's configuration or
merge its partial results into a complete replacement report.
