# Short mutation timeouts, 24 October 2026

User direction requests much shorter mutation iterations. The new
[Unit/runtime and Pipes profiles](../../../qualification/gate-closure/short/README.md)
set `additional-timeout` to 1,000 ms and disable mutant mixing. Source scopes,
test filters, coverage settings, two runners, no-bail behavior and the 90% gate
remain unchanged. The maintained historical configurations retain their bytes.

Pinned Stryker 4.16.0 has a fixed adaptive multiplier and no supported hard
15-second cap. The allowance is added to its measured estimate. Earlier
observations imply about 20–35 seconds for Unit requests and 191 seconds for a
complete Pipes request. Cancellation, infrastructure retries and host shutdown
can add time. Initialization and the original baseline run separately. These
estimates are not measurements of the replacement campaigns.

## Executed checkpoint

The configuration validator accepts exactly the new 1,000 ms/no-mixing Unit and
Pipes profiles while preserving its other checks. The focused suite passed
175/175. Developer185 exited 0 at 18:32:22 UTC: **10,270 passed, zero failed or
skipped**, comprising 5,299 Unit, 2,323 Integration, 2,619 Architecture and 29
Business cases. Structured validation reported no issues. All 1,171 C# source
and configuration inputs still matched the observed profile inventory after
completion. This is ordinary verification, not mutation qualification.

Preparation used the current checker, source profile
`omit-archived-healthcheck-evidence-v1`, and 1,979 selected files/48,408,037 bytes.
Source manifest SHA256:
`55111d5966fd42a3c2f26fa0f790ad5b8b9c105b4cf77b90d849dc2dfe1cccb2`.
Both new launchers passed their source/PE/PDB and physical staging checks. The
Pipes reference families come from the actual complete passing Developer185
discovery/results: 501 Unit plus 121 Integration cases, with the complete
972-file C# origin inventory. Fresh Linux controls remain required.

## Stopped attempts and storage

The owned Unit170 and Pipes180 native trees were stopped at 18:09 UTC in response
to the shorter-timeout direction. Both returned native exit 137; their
controllers closed with exit 1 and `completeExecution=false`. Their actual Core
DLL restoration checks failed after the forced stop. Capture, restoration and
fresh-baseline holds remain; no completion or kill credit is inferred.

Closed Unit143/Unit170/Pipes180 run, payload and temporary roots were retained
together with the external stop receipts in `closed-long185.tar.gz`:
595,014,038 bytes, SHA256
`501e03468aa897505cfb1bb521a6229a7db491465bd39cf55629b84ee6fddcf0`.
The archive compared successfully against the closed remote files and its
downloaded bytes matched. Symlinks and omitted transient socket names were
inventoried. Copies remain under the Ubuntu task root's
`closed-long185-retention01` and local `.artifacts/hcg/closed-long185-retained01`.
Only then were those nine closed roots removed. Read-only payload directories
needed an owner-write adjustment before removal; the archive was unchanged.
Available `/data` space was 13,605,261,312 bytes before replacement uploads.

## Replacement campaigns

Both run on `user@192.168.88.161` beneath
`/data/cis-healthcheck-20261021-01a15a72`. They use new immutable, fully
hash-checked payloads and separate outputs; no old run was edited or restarted.

| Campaign | Started UTC | Launcher PID | Cohort | Input SHA256 |
| --- | --- | --- | --- | --- |
| `unit-complete-short185-run01` | 18:30:13 | 1928422 | 1,390 Unit | `398adeac26b544540a91806a721415d112e6f154163ae7693dde6b6fa2527fc2` |
| `pipes-complete-short185-run01` | 18:32:50 | 1929423 | 501 Unit + 121 Integration | `ca433f085c5b1430d923635618d77c1d511a2f621a6757efe569b51e678fb7c6` |

At 18:40 UTC, Unit185 emitted its first eight actual mutation budgets:
**32,995–39,824 ms**, replacing the former roughly 75-minute budgets. These are
observed adaptive session budgets, not a whole-campaign audit or a wall-clock
guarantee. Pipes185 was still initializing at that observation.
Pipes185's first two mutation requests subsequently emitted **223,522 ms** each
at 18:50 UTC, reflecting its fresh full-cohort baseline.
The original whole-controller/resource guards remain 720 minutes, 4 GB for Unit,
3.5 GB for Pipes and a 2 GB free reserve. These guards do not restore the former
75/35-minute per-request allowances. No timeout or forced stop earns kill credit.

The accepted historical union remains **1,591/3,065 = 51.908646%, unqualified**.
The replacement reports are not yet complete or audited. Existing index writers
also need explicit compatibility with the new 185 campaign receipts before
these reports can enter the source-checked union; no adapter approval is implied
by launching the stock tool.

## Retained verification

The [preparation, launcher and Developer185 packet](retained/preparation-and-developer185-01.tar.gz)
is 20,973,452 bytes, SHA256
`a3315df4b89436da265ec3ccb63175f13a45bed5d996e95721bde33d95c66266`.
All 2,566 extracted regular files matched their retained originals in the
[readback check](retained/preparation-and-developer185-readback01.json).
It includes source/binary bindings, focused/full native verification, fresh
family evidence, failed preparation attempts and independent source reviews.
Live replacement-campaign outputs remain separate and incomplete.

The [first native timeout observation](retained/native-timeout-observation02.tar.gz)
retains the first eight Unit budget lines and receiving/configuration receipts;
2,925 bytes, SHA256
`7f53351d7809239b5accc10c8db7678bd229d5edac85115c3f946402afc70097`.
The [stopped-campaign receipts](retained/stopped-campaign-receipts01.tar.gz)
retain actual143/170/180 results and external-stop observations; 6,761 bytes,
SHA256 `7c8dcc9cf21532158075a917e5a57542ed1dbb57106ab55cc0ed7fd4a3357575`.
[Retention/cleanup metadata](retained/closed-long-campaign-retention-metadata02.tar.gz)
and the [local archive copy check](retained/closed-long-campaign-local-copy-verification01.json)
preserve the separate cold-storage operation.

Later user direction requests higher parallelism. The new
[eight-worker profiles](../../../qualification/gate-closure/short/parallel8/README.md)
are being prepared from this successful short-timeout checkpoint. The two-worker
185 campaign inputs and observations retain their original identities.
