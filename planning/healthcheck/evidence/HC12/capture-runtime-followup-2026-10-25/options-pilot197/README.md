# Repaired Options pilot, 26 October

The bounded Ubuntu pilot closed after **802.518 seconds** with controller exit
1 and native Stryker exit 0. It produced **91 direct assertion kills among 96
eligible definitions (94.791667%)**, but remains **unqualified**. Five hosts
aborted and 5,167 non-direct failure observations remain held. This is one
source-file diagnostic, with zero campaign credit; the maintained three-report
union remains **2,111/3,104 = 68.009021%, unqualified**.

| Evidence | Earlier Options pilot | Repaired pilot |
| --- | ---: | ---: |
| Eligible definitions | 96 | 96 |
| Raw Killed / Timeout / Survived | 87 / 5 / 4 | 96 / 0 / 0 |
| Direct assertion definitions | 57 | 91 |
| Normal execution hosts | 71 / 98 | 93 / 98 |
| Aborted execution hosts | 27 | 5 |
| Original / native initial / fresh ordinary cases | 1,396 each, passed | 1,396 each, passed |
| Shutdown capture | Passed | Passed |

All 120 definition tuples match the earlier embedded Options source exactly,
including six CompileError and eighteen Ignored definitions excluded from the
denominator. The independent review joins source tuples rather than carrying
IDs or statuses. It confirms the four guidance scenarios and eleven
early-return scenarios in fifteen normal hosts, with twenty-six actual expected
assertion rows. Of the old direct definitions, 56 remain direct, 35 become
newly direct, and one loses normal-host eligibility. Multiple test/profile
changes prevent attributing all improvement to one change.

The repaired source is frozen separately from current files. The pilot uses
four workers, 10,000 ms additional timeout, perTest coverage, no mixing and
disabled result bailing. Actual adaptive native budgets are **19,529–32,185 ms**;
there is no hard 15-second limit. The 20-minute overall limit, 4 GB artifact
allowance, 2 GB free reserve, 5,000 ms host-close wait and qualification holds
remain. Actual peak new bytes were 1,123,605,017 and minimum available bytes
14,817,611,776; no resource guard fired. The unchanged maintained reference
configuration was checked separately; `maintainedPilotProfileAccepted=false`
remains explicit for this diagnostic profile.

## Remaining execution and assertion work

The [five-host review](abnormal-host-review.md) separates their causes:

- One copied-options mutation disables the expected endpoint. A successful
  no-endpoint start is followed by a real 15-second client connection timeout.
  Its test needs an enabled-start prerequisite within the existing owner.
  The later [selected startup control](../disabled-endpoint199/README.md)
  now proves that repair with healthy2P and two normal direct assertion
  failures; this historical host remains aborted and receives no new credit.
- One host returned all 850 requested cases before the deadline, then emitted
  both aborted and nonaborted completions. Its normality/malformed holds remain.
- The other cutoffs occurred during finite work. An identical 850-case
  inactive calibration is the next timing evidence; a last-body hang is not
  established merely by the cutoff.

The [non-direct review](nondirect-review.md) joins all 5,167 observations to
actual returned rows in 31 normal hosts. All 31 already have direct witnesses
elsewhere. The observations are **4,283 configuration exceptions, 789 null
references, 93 issue-cardinality failures and two platform exceptions**; these
are not thousands of distinct mutants or causes of the five aborts.

After this frozen run, five explicit issue-count assertions were adopted before
existing `Single()` calls. They preserve the original structured error checks
and passed all **44 focused Unit cases**, run
`b8a55056f66b4d9d9cfb37ba48a145bd`, zero failures/skips and strict warnings/errors.
They do not retroactively qualify the 93 old rows. The next configuration
assertion work should begin at the two known-valid execution-scope constructors
identified in the review; broad exception conversion is not justified.

A separate later Pipes fixture change races the existing valid default/upper
CLI request against the real child exit and retains strict child, stream,
frame and cleanup ownership. Its source-bound argument-rejection assertion
occurs only after genuine cleanup. All **120 focused Pipe/Probe cases** passed,
run `c2717eaef7b34602944beaf4f3df7440`, with zero failures/skips and strict
warnings/errors. Its Ubuntu negative control and renewed child calibration
remain pending. The first misqualified namespace filter selected zero cases
and supplies no passing evidence. Both later changes postdate the complete
[10,333-test Developer checkpoint](../current195-execution-repair01/developer197/summary.json).

## Retention and exact records

All 9,548 regular files / 1,202,421,696 bytes passed full Windows C# receiving,
with no exclusions or extra files. Inputs, complete raw logs, temporary files
and original failures remain in
`.artifacts/hcg/unit197-options-pilot-closed-receiving01/readback01`.

- Archive SHA: `3858f1da5c53e50f690ab591a079b9073005780bc16f127d44a8e5025a36aac7`.
- Manifest SHA: `70684b6c208a41d00665a7b6708d3a9247b06734d653d12f94848e701efa7f98`.
- [Original result](original-results.json), SHA
  `038d0b9eba9499a64e3fd89f80a191dda886a0857e7c0ada73f1d82f56f794ed`.
- [Original native audit](original-native-audit.json), SHA
  `003049b2f144a2fd27466b65526f5694d3fe1f373c768b735e31c497cfb46ec5`.
- [Full receiving](full-receiving.json), SHA
  `2837ef218e61a14a816718625e156ba67a054cc34465abc8ffbb8f6d0e1f8bbf`.
- [Independent review](independent-review.md), SHA
  `c3608c76d5bf63b0fe2b0a6d443cf8d7d2a1438c590f558a8b093b5f03556577`.

The six closed compact artifacts also remain in
`.artifacts/hcg/unit197-options-pilot-compact-receiving01`; their hashes match
the closed Ubuntu record. Original flags, failures, statuses and all 5,190
audit issues remain unchanged. No validator, threshold or report was relaxed.
