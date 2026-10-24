# Eight-worker campaigns, 24 October 2026

Subsequent investigation found substantial startup aborts. Both187 campaigns
were stopped at22:18:53 Europe/Malta with their incomplete outcomes retained.
See the [startup investigation](../unit-startup-investigation-2026-10-24/README.md)
for the current state; the launch observations below are historical.

User direction requests more parallel runs and available memory. The new
[profiles](../../../qualification/gate-closure/short/parallel8/README.md) set
**eight workers per Unit/runtime and Pipes campaign**, up to 16 across both.
The 1,000 ms added timeout, separate mutants, complete scopes/cohorts, coverage
modes, no-bail setting and 90% gate remain unchanged. Shipping Core and the
Healthcheck test fixtures are unchanged.

The server has 32 logical CPUs/16 physical cores and 125 GiB RAM. Before this
change, about 121 GiB was available and CPU idle was 87–89%. Process memory limits,
user/session cgroup CPU and memory limits and .NET heap configuration impose no
additional cap. RAM already grows as needed; the 4 GB/3.5 GB guards apply to
output files. The user task limit is 339,999, well above observed use.

## Verification and launch

The exact eight-worker/1,000 ms/no-mixing policy passed **220 focused cases**.
Developer187 exited 0 at 19:17:06 UTC: **10,315 passed, zero failed/skipped**:
5,299 Unit, 2,323 Integration, 2,664 Architecture and 29 Business cases. All
1,171 C# source/configuration inputs matched after completion. The fresh family
reader joined the full native results and selected exactly 501 Unit plus 121
Integration cases against the complete 972-file C# origin inventory. Full
PE/PDB/document checks cover the observed three binary pairs and all 642 PDB
documents. These are observations, not retrospective loaded-image claims.

The current checker was copied with all seven runtime files after the focused
build, with before/copy/after checks, so preparation could run independently of
the shared Developer build. Preparer SHA256:
`a127d0db7f707fb36d6f4cff778f666a1016183058fdbc95740ed44d6235d2d1`.
The maintained preparation contains 1,985 selected files; source manifest
`30ecc1effe82ee6dfe673ee71501338b7951fee643ccfe81c4c7a974fb7ee323`.
Both launchers/stagers passed independent source reviews, compiled-source
bindings, full physical snapshot/policy checks and actual staging. Uploaded
payloads matched every staged file, with no extra files, before becoming read-only.

Both run under `/data/cis-healthcheck-20261021-01a15a72` on `192.168.88.161`:

| Campaign | Started UTC | Launcher/controller PID | Input SHA256 |
| --- | --- | --- | --- |
| `unit-complete-parallel187-run01` | 19:14:53 | 1954669 / 1954670 | `568e1f5c8e0ed42202341236563688a31a126ea921cd794d72a3131add0f8432` |
| `pipes-complete-parallel187-run01` | 19:16:31 | 1955494 / 1955495 | `7dc5d616b7053b9ed5c972a244c4b60a53143fe0ec2c862c4628dbf40785c628` |

By 19:27 UTC, actual `vstest.console` processes for Runner0 through Runner7 were
observed for each campaign. Unit emitted its first eight mutation budgets at
19:23:43 UTC: 15,211–21,038 ms. This is an observed sample, not a hard 15-second
wall limit. Pipes was still running its fresh instrumented baseline at that
observation. About 121 GiB RAM remained available, without swap use.

The first 50 Unit dispatches spanned about 111.69 seconds, versus 151.36 seconds
in the previous two-worker run. This is an initial request sample, not a
steady-state benchmark or an accepted-kill rate. Several current Unit sessions
also logged actual test-session expiry and aborted completions. Those outcomes
remain held; a faster dispatch rate is not evidence of a higher mutation score.

## Retained attempts and qualification

The two-worker185 native trees were stopped at 19:11:51–19:11:54 UTC after the
eight-worker payloads were validated. Both native exits were 137 and both
controllers closed with exit 1, `completeExecution=false`; native DLL restoration
checks failed after the forced stops. No cleanup/completion or kill credit is
inferred. Their whole run/payload/temp roots and stop receipts were retained in
`closed-short185.tar.gz`, 238,045,923 bytes, SHA256
`1614d8ac0ad1bc99197f7c54bbd8829c77c1649cf3eae21d16c8d44cae50ec7e`.
The closed archive compared successfully to the remote files, and the local
download matched. Symlinks and ignored transient socket names were inventoried.
Only then were the six closed roots removed, following resolved-path and open-file
checks. The new187 roots were separate and remained active. The cold archive
remains in remote `closed-short185-retention01` and local
`.artifacts/hcg/closed-short185-retained01`; it was not expanded wholesale locally.

The [complete preparation and ordinary verification packet](retained/preparation-and-developer187-01.tar.gz)
is 41,866,935 bytes, SHA256
`7e8f8df421fb9cd37c6c8cedb45e67f38d8272a466b7815349f4151791fa73ed`.
It includes both complete immutable uploaded payload archives, source/binary
bindings, native focused/full results, failed preparation records and independent
reviews. All 2,533 extracted regular files matched in the
[readback check](retained/preparation-and-developer187-readback01.json).

The [live launch/resource observation and cold-retention metadata](retained/launch-observation-and-retention01.tar.gz)
is 11,879 bytes, SHA256
`06751ca5bfaa8b7ae3df5392fe3c5d32a6ccfd5fceb06f9764f7349c99a3eecf`.
It retains the sixteen-runner observation, first50 Unit budget lines, sampled
actual expiry/aborted-completion evidence, configuration receipts and the
closed-root archive/cleanup records. These are point-in-time samples, with no
complete host audit or score credit. The separately extracted
[actual two-worker controller results](retained/stopped-two-worker185-receipts01.tar.gz)
are 3,894 bytes, SHA256
`12ff5dec6ff69624afae324d91847e484094ee31036589514dd03cf6c401a84e`;
the [local cold-archive check](retained/stopped-two-worker185-local-copy-check01.json)
predates the later recorded deletion.

The campaigns remain incomplete and unaudited. The existing index writers still
need reviewed187 receipt compatibility. The accepted historical union remains
**1,591/3,065 = 51.908646%, unqualified**. Eight workers, earlier assertions,
timeouts and partial reports do not supply automatic qualification or kill credit.
