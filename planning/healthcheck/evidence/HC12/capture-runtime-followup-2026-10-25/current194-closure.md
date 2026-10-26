# Closed194 campaigns and execution diagnosis, 26 October

The last maintained joined result remains **2,111/3,104 = 68.009021%,
unqualified**. All three fresh194 controllers closed with exit 1. Their raw
native percentages include timeouts and are not conservative gate scores.

| Campaign | Raw Killed | Timeout | Survived | NoCoverage | CompileError | Ignored | Diagnostic direct assertion IDs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Unit/runtime | 1,772 | 852 | 121 | 29 | 278 | 615 | 774 |
| Pipes | 162 | 80 | 8 | 0 | 28 | 86 | 98 |
| ASP.NET | 72 | 5 | 3 | 0 | 0 | 19 | 51 |

The separate native audits are diagnostic observations, not newly joined
credit. Unit has 2,747 execution hosts, 928 normal; Pipes 502, 257 normal;
ASP.NET 81, 55 normal. Earlier assertions, source identity, child evidence
and unfinished-work holds still require the maintained validator's review.

## Unit

The original receiving failure was a stale 1,399-case timing check despite
the exact 1,396-case family. Original and fresh ordinary tests passed. A
separate driver corrected that count only: timing, binding and native audit
completed, but capture rejected a parent PID transition, closing with exit 2.

Read-only diagnosis located Runner 6 logical line 72,767, byte 215,124,772.
Kernel records show runner 2784012 ended with SIGABRT before runner 2879622
started against the same diagnostic path. The first new physical line has
the new PID/version but lacks the usual `TpTrace` prefix. The parser retained
the old epoch and rejected the next prefixed row; it also assumes a single
Runner epoch. No bytes were repaired or normalized. Any explicit epoch
support must retain the old abort, new exit 1 and protocol/ownership deficits.
Diagnosis receipt:
`fa833d5c1ea28b11850346bac75359c9529d27d85eb22e79f55a49cded2b0cee`,
under `.artifacts/hcg/unit194-capture-mixedpid-diagnosis01`.

Windows C# receiving passed all 64,347 original files / 6,088,950,221 bytes,
manifest `6699299f5bd721515f38b404623ac0a540baacc8216545247965ea99d0492729`,
under `.artifacts/hcg/unit194-closed-receiving01`. Separate diagnostic
receiving passed all 64 files / 145,748,858 bytes, manifest
`a7bb4ea9664adb5eb95cc82ac47945bf58d5bf451862fdeca1c2d792eff9cf83`,
under `.artifacts/hcg/unit194-postclosure-closed-receiving01`.

The [selected493 control](selected493-control/README.md) completed the same
19 tests normally at the unchanged 12,275 ms deadline: 19 healthy passes,
then 19 existing assertion failures with the mutation active. The opt-in
smaller Unit assembly retains all 1,396 cases. A separate one-file options
[pilot](options-pilot01/README.md) passed the same 1,396-case original,
native-initial and fresh controls, and completed in 12.56 minutes. Shutdown
capture passed; 27 aborted execution hosts keep it unqualified. Its detailed
review separates a concrete cancellation-fixture wait from cumulative
session deadlines and eight normal-host assertion-evidence gaps.

## Pipes

Host-close warnings, the failed child audit and a missing child launch remain.
All 193 incomplete child intents belong to aborted parents. Most terminal
child failures also coincide with parent aborts; inspected finite managed
crashes in the remaining cases already have direct assertions.

One Linux timeout selected a Windows-only endpoint branch, yet its Integration
host returned 118 passing tests taking 121.739 seconds plus startup before
abort arrived during the next test under a 132.988-second session budget.
Three inspected Unit host leader traces record exit 0 before their parent
emitted a five-second close warning. One all-503-passing example has complete
22-thread closure: every thread and then the leader exited 0, but the actual
Stryker reaper recorded wait4 8.968 seconds later; the VSTest callback followed
0.104 seconds after reaping. The other two retain leader-only evidence.
The [source and causal review](pipes194-causal-review.md) also identifies
coverage-off selection of all 503 Unit and 121 Integration cases for every
mutant. Launcher ownership and assembly discovery need prospective controls;
neither the close warnings nor the complete child holds are waived.

Complete Ubuntu extraction and C# receiving passed all 195,287 files /
3,049,540,895 bytes, manifest
`b040d1a8efda883804326e4389708c9af948e506cb2c9a9a1a2dfb73a7553cca`.
Archive `4c16789be91310ab8122c57fbf79245939fcb535a67135994577c7921e732c2c`
and manifest were received on Windows under
`.artifacts/hcg/pipes194-archive-receiving01`, with selected analysis bodies
rather than another full expansion. The separately received Ubuntu receipt
SHA is `f81631418c8fb538b94bea81409ef77c668ca0cdc9e24ac8f0bcd0eb262c4ce3`,
under `.artifacts/hcg/pipes194-linux-readback-receiving01`.

## ASP.NET

A separate source-matched later ordinary baseline passed all 61 original
cases with no ownership holds. Its retained first attempt passed 60; the
HTTP/pipe parity case failed because the chosen temporary root produced a
135-byte Unix socket path, rejected against the runtime's reported 108-byte
limit. The successful second attempt used a short root. This closes only
the later ordinary-baseline check; original child/controller holds remain.

Complete Windows C# receiving passed all 3,327 files / 336,553,095 bytes,
manifest `37e37450c3923e6863e3fc2a02ea2c7c85ac60b7bd8fae13144875e96f4a0f23`,
under `.artifacts/hcg/aspnet194-supplemental02-closed-receiving01`. Baseline
receipt SHA:
`08cc58432443047ea6046716cd5039560f6b6d8d806e1c34a4bda4e323fc42c7`.
Neither attempt changes historical mutation outcomes.
