# Child stage ownership and Windows artifact roots

This 21 October 2026 follow-up records the stage-owner correction and its actual
bounded validation. [child-origin.md](child-origin.md) remains the frozen earlier
snapshot. Attempt labels such as `v5` are distinct from the current receipt
protocol, `cis.healthcheck.stryker-children/v4-development`. No complete automatic
Pipes, automatic AspNetCore or release qualification is claimed here.

## Observed collisions and owner binding

The retained `pipes-windows-01` v1 session has 165 parent failures reporting
`A private child stage unexpectedly already exists.` They form three groups of
55 attempts against older successful probe stages. Every collided old stage
contains exactly eight files and matches all eight retained deployed hashes.
Their common instrumented Core SHA-256 is
`2215aca8d6d0cc22173fe80b9e5f54c09ec80283539f539b6e85896f5ccf6736`.

| Parent PID | Original launch evidence | Original launch interval, UTC | Collision failure-file write interval, UTC |
| --- | --- | --- | --- |
| 84676 | `a1b8e5f37305433da073d44ce9b2f71f`, active 2707 | 12:19:15–12:19:40 | 12:29:11–12:29:12 |
| 67716 | `af9d617a1364407abdc05872b0735599`, active 2865 | 12:59:04–12:59:28 | 13:16:01–13:16:02 |
| 12428 | `dad1e360a31743ccb61a126fdc3d75cd`, active 2874 | 13:01:23–13:02:04 | 13:47:07–13:47:09 |

The old stage address used PID/Core hash/executable, but its cache belonged to one
loaded Support instance. PID alone cannot identify that instance across fresh
processes or assembly load contexts. PID reuse is visible elsewhere in the raw
receipts, but v1 captured neither parent incarnation nor loader-context identity;
it cannot conclusively distinguish PID reuse from fresh loader static state for
each collision. The complete old stages must not be reused or overwritten. The
old audit remains invalid, also retaining 105 missing-exit failures and one
temporary-file failure; the owner correction does not retroactively qualify it.

Current Support creates one readonly lowercase Guid-N owner nonce per loaded
Support instance and records it as `Launch.StageOwnerId`. Stages are now exactly
`stages/<PID>/<owner>/<CoreSHA256>/<executable-name>`. Audit validates the nonempty
canonical owner GUID, exact stage path, original and deployed file hashes, loaded
Core identity, selector, actual parent and child PIDs, runtime and terminal
receipts. Parent observation also binds the launch to its actual Support owner.
The invariant that a loaded parent keeps one Core is scoped to `(PID, owner)`;
separate incarnations sharing a recycled PID are not merged. Existing
no-overwrite staging, timeout, forced-stop and managed-frame guards remain.

The ninth derivative parser control changes the launch owner to another valid
GUID and recomputes the launch hash in origin and parent receipts, while keeping
the copied stage unchanged. It must reach the exact stage-owner rejection, rather
than fail an earlier stale-hash check. Positive copies preserve the recorded owner.
These controls create no native execution or mutation credit.

## Frozen inputs and actual validation

The `v5-inputs-01` preparation retained 1,338 selected files. Its manifest SHA-256 is
`7ca8c8ad766058da9aa4dc77d1950a7a0317607019cc9f573758db4c34d266b1`;
source and verified destination input digests both are
`29fa354f0eee9a977ade7c2d1427d34d4bd5bffe4218e45fab3b5ee932e99bc5`.
Preparation verified the source before/after copying and the destination against
the manifest. Preparation itself executes no native test and is not a whole-repo
or release qualification receipt. Source code stayed frozen for the following
coordinator executions.

| Retained lane | Actual bounded outcome |
| --- | --- |
| `conditional-v5-linux-01` | Finite four native mutants killed; native restoration true; audit passed 100 launches, 80 instrumented/selected. |
| `child-audit-controls-linux-02` | All nine derivative parser controls passed; `nativeExecution=false`. |
| `conditional-v5-windows-01` | Deep-root initial baseline failed. HTTP child observers rejected executable-path equality; audit had nine passing baseline probe rows and zero instrumented rows. Finite proof failed, native restoration false, and no mutation credit is taken. |
| `aspnet-windows-03` | Deep-root initial baseline failed; native exit 1 and audit exit 1, zero qualified child rows. No native mutation credit is taken. |
| `.artifacts/hcc/c1` | Short-root Windows finite proof completed: four native mutants killed, native restoration true; audit passed 100 launches, 80 instrumented/selected; native and audit exits both 0. |
| `.artifacts/hcc/t1` | All nine derivative parser controls passed; `nativeExecution=false`. |
| `owner-normal-integration-01` | Current canonical native integration baseline passed 145/145, zero failed or skipped, with retained TRX. Other families are not credited by this focused run. |

For example, failed deep-root launch `08779e09ec15455897e280ef7b402d8a`
has a 237-character stage directory and 269-character expected HTTP DLL path.
Its retained observer diagnostic says `The child started a different executable.`
It does not retain the failing actual `Assembly.Location`, so a particular path
alias or normalization mechanism has not been observed. Linux success with the
same frozen code and Windows success after shortening only the artifact root
support a path-length-related inference. A successful short-root HTTP origin
receipt records a 225-character actual entry path matching its expected stage.
Executable equality and exact binary checks were not bypassed.

Use short Windows child-qualification roots such as `.artifacts/hcc/<run>` for
subsequent runs. The full Windows `.artifacts/hcc/a1` automatic AspNetCore lane is
in progress at this snapshot, with 24 selected mutants reported started at
16:29:17 local time. Its recorded configuration uses a 750,000 ms additional
outer native session budget and unchanged 90% break threshold. The 15-second
actual fixture bounds and strict timeout/forced-stop markers remain enforced.
No final outcome from this running lane is credited. Compiler failures, unknown
failures, missing receipts and timeouts do not become kills.

## Artifact pins

All paths below are repository-relative. Original raw evidence remains in place;
full source, receipts and native reports are not reproduced here.

| Retained path | SHA-256 |
| --- | --- |
| `.artifacts/healthcheck/release-next/v5-inputs-01/preparation.json` | `088241b987400e5d0cd4d5543f22d8edb23010a1657bd8df162ce1f009971ade` |
| `.artifacts/healthcheck/release-next/v5-inputs-01/source-inputs.json` | `7ca8c8ad766058da9aa4dc77d1950a7a0317607019cc9f573758db4c34d266b1` |
| `.artifacts/healthcheck/release-next/pipes-windows-01/audit/results.json` | `cadfdebaa5222b7574357192a82ccb50973d4a7a5ab928f28fc10ad6908d9cb2` |
| `.artifacts/healthcheck/release-next/conditional-v5-linux-01/proof/results.json` | `471931dc307abc88e1bfd7368c357d05325dc1c820470e9fbc3923b5c010087b` |
| `.artifacts/healthcheck/release-next/conditional-v5-linux-01/audit/results.json` | `a805021d62d3a5a87445a44f3aa6f8bef76e8df07842bf5642451d2daf997b05` |
| `.artifacts/healthcheck/release-next/conditional-v5-linux-01/config.json` | `8a2737771486f6d28d3c6728c06efef34f8cbb3b23aad0bc00cef42acfb217f7` |
| `.artifacts/healthcheck/release-next/child-audit-controls-linux-02/results.json` | `6a1b872652238c39dccebcad275836efd898967c424de9706b6e46d4a2d8da87` |
| `.artifacts/healthcheck/release-next/conditional-v5-windows-01/proof/results.json` | `36014969cf1cf72a5dc9efcef4062c86466ae495bbf62f09e3fd505f19728d0a` |
| `.artifacts/healthcheck/release-next/conditional-v5-windows-01/audit/results.json` | `c11bb57a2e4fe99b520484689cf5a5d0e8791ea04c498408febbf185f67f3087` |
| `.artifacts/healthcheck/release-next/aspnet-windows-03/exits.json` | `c5616c6b0e74e857c01734754d0a9c8132f634b359c6ad28555eeb7a16a28511` |
| `.artifacts/healthcheck/release-next/aspnet-windows-03/audit/results.json` | `20a238673f76a28a7dbd85c27533bf1e11837084548e7e4c1382f92c99ebb868` |
| `.artifacts/hcc/c1/proof/results.json` | `51e293221b15043af72d46bb1c7a5370d4c4b70b1d1033fbd1864ee97a2c8adc` |
| `.artifacts/hcc/c1/audit/results.json` | `cd7328a2038878f75a68dc9af0c628b3f0703695d74b045d637a9a1fd029993e` |
| `.artifacts/hcc/c1/config.json` | `8a2737771486f6d28d3c6728c06efef34f8cbb3b23aad0bc00cef42acfb217f7` |
| `.artifacts/hcc/t1/results.json` | `2e89fb36095ffe644e7d6ce902222fb784598343019df58caf84bbe68016957b` |
| `.artifacts/hcc/a1/config.json` | `3495ad51e0713121f58d647f72d1a971480d83c178ae540ee48122827db7bba3` |
| `.artifacts/healthcheck/release-next/owner-normal-integration-01.log` | `adf857395d63a3d6376e06f9fff8cbd7d8f190360c0be9f636e5572fb082c8b2` |
| `.artifacts/csharp/focused/ef3116b5edb04e2daab0e3d8e3e82ad7/selected.trx` | `6ee3df7e920b303e408d60af2f322d64978daf2801d2464c28ea51f23447d14f` |
