# Windows Node serving-time fast-fail diagnosis

Date: 27 November 2026. Owner: CIS-0030/WORK-192.

## Current conclusion

Node 24.16.0 is now pinned after the explicitly approved private comparison:
4/4 affected browser cases, 2/2 abnormal host-shutdown regressions, 395 portable
SDK tests and 131 focused portal tests passed. The retained 24.15.0 portal
fast-fail and matching primary upstream report support this bounded runtime
mitigation. Both runtimes passed the credential-free HTTP isolation; this does
not identify the local native exception's exact subcode or causal stack, or
prove intermittent Windows crashes impossible. The original proposal and its
nonreproduction limits below remain historical checkpoints.

The independently repaired Next response-finish/close wait is separate. Its
causal HTTP red/green evidence must not be substituted for this native failure.

## Retained local observations

The selected executable is `C:/nodejs/node.exe`, Node `v24.15.0`, Windows x64,
SHA-256 `3331e1ffe19874215472217c5e94f5a0c6d8e18c4ac7111d3937aa0ad5e9b4a5`.
The read-only runtime/file identity matches the
[official 24.15.0 checksum](https://nodejs.org/download/release/v24.15.0/SHASUMS256.txt).
Earlier observations report Windows `10.0.26300.0`.

The initial bounded review classified 19 historical report files with
`hostExitCode=3221226505` (`0xC0000409`): nine at `desktop-browser`, three at
`native-metadata-authoring-and-original-recovery`, three at
`valid-operator-a-complete`, two at
`native-metadata-authoring-and-original-recovery-complete`, and one each at
`configuration-api-editor-a` and `configuration-api-editor-repeat`. This is the
reviewed cohort, not a claim that all retained reports or all executions were
censused. None of these 19 reached the shutdown phase. No successful new target
proof may be inferred from their partial journeys. Retain their failed reports,
actual costs and distinctions between process custody and graceful host stop.
The earlier [WORK-179 observation](../../../.cis/local/testing/rm09-completion/work179-native-node-observation.md)
and [WORK-181/168 observation](../../../.cis/local/testing/rm09-completion/work181168-native-node-observation.md)
retain specific examples and evidence limits.

The new WORK-192 isolation uses built-in HTTP over `127.0.0.1`,
`keepAlive:false` and `Connection: close`, one actual TCP socket per request.
The child receives no credentials or inherited Node/proxy settings. The existing
`ProcessRunner` owns its 30-second deadline, 16 KiB per-stream capture and actual
process/output cleanup. No `process.exit`, JIT flag, security suppression,
credentialed dump or replacement test harness is involved.

| Actual input | Result | Native identity and ownership observations |
| --- | --- | --- |
| 1 request, concurrency 1 | Passed | Node `v24.15.0`, libuv `1.51.0`, `win32`, `x64`; every request/response/socket count 1; listener closed, Agent destroyed and empty; no retained listeners. |
| 4,096 requests, concurrency 8 | Passed | Same runtime; every request/response/socket count 4,096; maximum live client sockets 8; listener closed, Agent destroyed and empty; no retained listeners. |

The retained [probe TRX](../../../.artifacts/management-portal/CIS-0030/WORK-192/rm09c-node-connect-probe-current/results/node-connect.trx)
records 2 passed and 0 failed. The compiled parent parses and asserts the child's startup/final JSON in memory; successful child stdout is not independently persisted in that TRX. Its phase's
`native.log` retains invocation output. These are local nonreproductions, not a
runtime reliability qualification. The test exercises the short-lived native
TCP-connect path, but is HTTP-only and does not reproduce the portal's complete
TLS, OIDC, PostgreSQL, server-rendering and timing conditions.

During the subsequent four-case Configuration browser run, the session/storage
case's owned portal process failed again on 24.15.0, before that case's new
target journey. The sanitized
[phase record](../../../.artifacts/management-portal/CIS-0029/WORK-004/native-host-87aab283-904e-42eb-bcd8-29836cec3692/phase.json)
records `firstFailurePhase=native-metadata-authoring-and-original-recovery`,
`hostExitCode=3221226505`, null signal, joined exit/stdout/stderr/IPC and
`hostStopped=false`. The parent observed blank retained host stderr. Source
coordinates `native-host-consumer.mjs:538:25` and `:461:39` are observer failure
sites, not native crash frames. The four-case run also contained a separate
fixture HTTP 400 concerning instance pagination; that does not establish a
second native failure or a product defect.

No retained native exception reason parameter, credential-free native stack or
matching Application/1000 event currently identifies the local fast-fail cause.
Microsoft documents [fast-fail termination](https://learn.microsoft.com/en-us/cpp/intrinsics/fastfail?view=msvc-170)
as an immediate noncontinuable termination whose additional exception parameter
identifies the reason. The exit code alone cannot distinguish a stack-cookie
failure from other fast-fail reasons. An ordinary empty Node stderr is not
evidence that cleanup completed gracefully.

## Upstream lead and its limits

[Node issue 63620](https://github.com/nodejs/node/issues/63620) reports silent
Windows 24.15.0 failures during many short-lived outbound HTTP connections,
including loopback. Its reporter supplied a native TCP-connect/security-cookie
stack; a Node maintainer confirmed the 24.x fix and closed the issue as completed
after 24.16.0. This is a strong upstream reason to compare the adjacent release.
It is not the local stack.

The [24.16.0 release record](https://nodejs.org/en/blog/release/v24.16.0)
includes libuv 1.52.1 and the libuv fix cherry-pick linked to
[Node PR 62561](https://github.com/nodejs/node/pull/62561). Do not claim that a
specific `OSVERSIONINFOW` overwrite caused this repository's failure: the
reviewed official [24.15.0 TCP source](https://github.com/nodejs/node/blob/v24.15.0/deps/uv/src/win/tcp.c)
uses the older implementation and lacks that helper. The local startup report
also identifies libuv 1.51.0. That mismatch prevents promoting the upstream
description into an observed local diagnosis. Other Windows fast-fail reports
have different proposed causes; disabling JIT would not identify this one.

## Original specific runtime proposal — retained checkpoint

Permit one privately owned official Windows x64 Node 24.16.0 executable for the
comparison below, followed only on accepted evidence by the attached exact
[nine-file pin patch](node-24-16-proposed.patch). The proposal changes Node
24.15.0 to exactly 24.16.0, not a floating range or a general toolchain upgrade.

- Download source: `https://nodejs.org/download/release/v24.16.0/win-x64/node.exe`.
  Verify SHA-256
  `b3094d0b49f9ad602262a9921551737bb97637c05dd357a06ae98188d7290aa3`
  against the [official checksum](https://nodejs.org/download/release/v24.16.0/SHASUMS256.txt)
  before execution.
- New owned path: `.cis/local/tools/node-v24.16.0-win-x64/node.exe`. Refuse an
  existing destination and any binary exceeding 128 MiB. Charge this binary and
  at most 16 KiB checksum metadata inside the existing counted-storage ceiling;
  verify headroom before acquisition. No SDK, symbols, installer, archive or
  memory dump is included.
- Preserve `C:/nodejs/node.exe` and the existing npm 11.12.1, .NET, Next,
  dependencies, lock resolutions/integrities, TLS policy and runtime flags.
  Select the new executable only through the existing absolute executable input.
  No global runtime or npm replacement is proposed.
- The conditional patch touches root `README.md`,
  `.github/workflows/service-tests.yml`, `docs/references/test-suite-profile.md`,
  `implementations/csharp/tests/README.md`, `implementations/typescript/README.md`,
  both workspace/portal `package.json` files, the workspace `package-lock.json`
  and the portal `README.md`. The lock changes only two Node engine metadata
  values; `next-auth` 4.24.15 remains unchanged.
- CI retains npm exactly 11.12.1 in a newly owned runner-temp prefix and invokes
  that package's absolute CLI with the selected Node. Node's bundled npm may
  bootstrap acquisition of this already selected npm version; it does not become
  the workspace package-manager pin. There is no global npm installation.
- All work, acquisition, tests and restoration remain in WORK-192's existing
  shared finite allocation and reserves. No push, merge, release, global
  installation or broader qualification is included.

## Bounded comparison and decision

The fresh portal crash makes further escalation of the pure-HTTP traffic a poor
next discriminator. Stop at the retained 2/2 result on 24.15.0. Before that fresh
crash, the optional stronger bound was one 16,384-request/concurrency-8 child,
then at most two fresh identical children, all within the existing hard caps;
this recommendation is superseded by the concrete portal failure.

After the specific exception is recorded, root serializes all native execution:

1. Verify the newly owned executable's bytes and actual startup identity; run
   the same 1/1 and 4,096/8 tests with the unchanged compiled regression and
   unchanged environment policy. Preserve both runtime results independently.
2. Run the exact failed session/storage portal case using the unchanged input
   and current source candidate, followed by the already scoped affected portal
   checks. Rebuild only when required to keep the candidate coherent. Retain
   failures and actual ownership facts; do not convert a crash into retry success.
3. If these checks pass, record an upstream-led runtime mitigation and apply the
   exact pin patch only after its separate conditional evidence requirement is
   satisfied. Run the affected workspace/portal checks with the existing npm
   11.12.1 and guards. Passing finite observations cannot prove an intermittent
   crash impossible or establish that the upstream stack was the local cause.
4. If the comparison fails or lacks evidence, retain the original pins and leave
   the reliability outcome open. A debugger/subcode investigation would need a
   separate concrete credential-free ownership/storage proposal; no credentialed
   memory capture or new diagnostic-tool campaign is authorized by this note.

At this document's original proposal checkpoint no 24.16.0 binary had been downloaded or executed,
no runtime pin has changed, and the proposed patch has not been applied.

## Recorded exception and comparison checkpoint

The user explicitly approved the isolated comparison and conditional nine-file
correction on27November2026. CIS retained the existing authorization identity,
constraints, limits and history, and added only the exact
`node-runtime-pin-exception` scope. A draft's mistaken archive terminology was
narrowed through the supported authorization operation before acquisition;
`USER-NODE-24-16-EXCEPTION-20261127-BINARY` permits the standalone executable
only. WORK-195 owns that exception and receives1800seconds/one attempt from
WORK-192's prior unused allocation; their combined cap remains three hours and
three attempts. Existing selected task scope bases remain unchanged.

The private acquisition phase
`WORK-195/rm09c-node-24-16-private-acquisition` completed normally. Its92279112
bytes match the approved official SHA-256. Global Node and npm remain unchanged.
The same compiled credential-free cases passed2/2 under the private executable,
recorded independently at
`WORK-192/rm09c-node-connect-probe-comparison/results/node-connect-comparison.trx`.
The four affected browser cases are being qualified separately; this checkpoint
does not claim their result or apply the conditional patch.

Retained follow-up failures are distinct:

- `WORK-193/rm09c-configuration-browser-boundaries` recorded2passed/2failed:
  predeployment and immutable Shared-reference comparison passed; session/storage
  was interrupted before its target journey by the fresh24.15 fast-fail;
  status used an unsupported BFF page-size parameter and failed400 before its UI.
- `WORK-193/rm09c-instance-status-boundaries-current` corrected the fixture to
  follow actual bounded cursors. It reached and passed the mixed-state/two-page
  browser checks, then failed at a details-field selector. The host exited0 and
  joined normally. The locator was corrected to use an exact relative label;
  this is a test correction and is not attributed to Node.

These reports stay failed. Actual response freshness assertions were strengthened,
and delayed delivery now tolerates only a confirmed `net::ERR_ABORTED` on that
specific retired request. The current source manifest is
`.cis/local/testing/rm09-completion/configuration-followup-comparison-candidate.json`.

## Conditional correction applied

The private24.16.0 browser batch completed4/4passed,0failed,0skipped in2m41s:
`WORK-193/rm09c-browser-boundaries-node-comparison/results/configuration-browser-comparison.trx`.
It includes the exact session/storage case interrupted before its target on24.15,
plus the predeployment, Shared-reference-only diff and strengthened status cases.
Every parent verifies its native persisted effects or unchanged fingerprints and
joins the actual host/browser/provider owners. Root then applied the exact reviewed
nine-file patch (SHA-2567223612531b7d0d2d2118b914dc82bd61483db979e7f87cec60b438b0da37ce5).
The applied-file manifest is
`.cis/local/testing/rm09-completion/node-24-16-applied-files.json`.

The ordinary portable TypeScript build and browser type check passed under the
private executable, as did395portable tests and131focused portal
Configuration/session/response-close tests, with0failed/canceled/skipped.
`WORK-192/rm09c-node-correction-source-checks-absolute/native.log` retains those
separate totals; they are not unique coverage or a Cartesian proof. The first
local npm-script invocation failed because its Windows command shell did not
resolve the private `node` prefix. That failed invocation is retained separately
in `rm09c-node-correction-source-checks`; the successful check executes the
unchanged checked-in script bodies with the absolute selected executable, while
checking actual npm remains11.12.1. The Linux CI npm-prefix update is source
reviewed; this local run is not a remote CI execution claim.

This is an **upstream-led runtime mitigation with finite local qualification**.
The available evidence does not establish the exact native fast-fail subcode or
prove every possible Windows intermittent crash impossible. It preserves both
pure-HTTP nonreproductions and all observed24.15portal failures. The previously
proved Next response-close defect remains a separate causal correction.
