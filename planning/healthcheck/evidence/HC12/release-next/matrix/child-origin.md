# Child origin and finite native mutation evidence

Historical schema3 snapshot. The later [owner-schema4 record](child-owner.md)
contains the current owner binding, shorter Windows paths and nine parser
controls. Statements below describe their original frozen executions.

This 21 October 2026 snapshot records actual coordinator executions and read-only
inspection of their retained artifacts. The finite proofs establish selected
HTTP/probe mutation detection with actual child origin and lifecycle evidence.
They do not qualify the full automatic AspNetCore/Pipes mutation matrix or the
global release gates. Raw failed attempts remain unchanged. Artifact attempt
labels such as `v3` and `v4` are distinct from the receipt protocol version.

The subject pins remain SDK `10.0.401`, runtime/frameworks `10.0.12`, and installed
Stryker `4.16.0`. Pinned `Stryker.Core.dll` SHA-256 is
`bf02f78d3725e1243edb7a32d88dc1a02c88f1f9f74b60b18730636073f234d7`.
The maintained standalone launcher uses real public Stryker orchestration,
mutators, compiler, VSTest runner, report generation and restoration. Its finite
mutator adapter is supplemental verification code, outside shipping Core.

## Retained attempts and bounded conclusions

| Retained attempt | Actual result and evidence boundary |
| --- | --- |
| `child-smoke-windows-01` | Ordinary CLI smoke: two probe conditional mutants were killed; child audit passed 60 launches, 40 instrumented. The native report also has 82 CompileError and 113 Ignored mutants. Of the compile errors, 72 are in the HTTP source and 10 in the probe source. HTTP children carrying a probe selector prove propagation, not HTTP-mutant detection. |
| `aspnet-windows-01` | Untouched broad CLI attempt: native 14 Killed, 10 Survived, 72 CompileError, five Ignored. Child audit failed 34 terminal receipts; 987 rows passed and 34 failed. Complete origin receipts alone did not qualify missing terminal evidence. The 72 compiler rollbacks are not kills. |
| `conditional-v2-windows-01` | Fresh-source finite attempt failed reading the live test-output Core after native Stryker had moved it to its backup. Proof failed and `nativeRestoration=false`; the baseline-only child audit found no instrumented child. Earlier reused-source success did not demonstrate this fresh-backup path. |
| `conditional-v3-windows-02` | After the real-backup correction, native finite four passed with native restoration; child audit passed 100 launches, 80 instrumented. This attempt used the older lifecycle timestamp rule. |
| `conditional-v3-linux-02` | Native finite four passed and native restoration succeeded. Child audit remained invalid: 12 rows passed and 88 failed only the OS `Process.StartTime >= manifest.CreatedUtc` comparison. These original rows were not rewritten or retroactively qualified. |
| `conditional-v4-windows-01` | Fresh finite four passed, native restoration succeeded, and the current-protocol audit passed all 100 launches, 80 instrumented/selected. |
| `conditional-v4-linux-02` | Native Linux finite four passed, native restoration succeeded, and the current-protocol audit passed all 100 launches, 80 instrumented/selected. Native WSL paths are retained in the copied raw artifacts. |
| `registration-v3-windows-01` | Native three were killed with native restoration, but audit remained invalid: three final child launches, one per mutant/test host, lacked parent exit receipts despite exact managed Core failure receipts. Outer native session interruption is a supported diagnosis, not an observed parent terminal. |
| `registration-v4-windows-01` | One existing real HTTP test plus scheduling, with explicit 60-second additional native budget: native registration three passed with native restoration; all four launches passed audit, three instrumented. Actual selected parent exits were `-532462766`. |
| `registration-v4-linux-01` | The corresponding native Linux registration three passed with native restoration; all four launches passed audit, three instrumented. Actual selected parent exits were `134`. These are observed platform codes, not hard-coded acceptance rules. |
| `child-audit-controls-windows-01` | Eight derivative parser controls passed: one positive rebased copy, then seven independently rejected corruptions. `nativeExecution=false`; this creates no native mutation, process or release credit. |

The ordinary CLI's HTTP compiler failures were collateral definite-assignment
errors around the `authorized` and `report` locals in instrumented `HandleAsync`.
CLI mutate/exclusion filters run after compilation and cannot make these rolled
back mutations testable. No production refactoring, suppression, final-scope
exclusion or tool-version change was introduced to turn these errors into kills.

The fresh-backup correction uses the actual source analyzer `TargetPath` and real
native `.stryker-unchanged` backup. It requires the backup to equal the freshly
built source assembly and any still-present injection assembly, retains their
hashes, writes genuine compiler output, and verifies normal native restoration
against the originals. Exceptional recovery calls public
`TestProjectsInfo.RestoreOriginalAssembly`; needing this fallback still leaves
`nativeRestoration=false` and fails the finite proof.

The default finite scope is exactly two real conditional-expression mutants each
at HTTP response-status selection and probe selected-health exit selection. It
rejects compiler rollback or any missing expected mutant and requires all four
native statuses to be Killed. Separate registration mode uses exactly the three
actual logical/negation/is-null replacements of
`settings.DetailsEnabled && authorizeDetails is null`. The successful one-case
registration lanes do not replace the broader 21-case failed attempt.

## Current child lifecycle controls

The current protocol is `cis.healthcheck.stryker-children/v3-development`.
`ObserveStarted` captures required parent `ObservedStartedUtc` immediately after
actual `Process.Start`. Audit orders the manifest before this parent observation
and before the parent's exit observation, while retaining raw OS start/exit times
and requiring OS start no later than OS exit. Linux reconstructs `StartTime` from
process ticks and cached boot time; it can precede the manifest wall clock by
milliseconds even for a freshly launched child. See the pinned
[runtime implementation](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Linux.cs).
The correction does not invent or patch an old receipt timestamp.

Actual HTTP requests now pass through fixture wrappers. Only an
`OperationCanceledException` with inner `TimeoutException` marks the exact child
as timed out before rethrowing; intentional client cancellation retains its
semantics. The retained `http-timeout-integration-01.log` records a clean build and
145 passed, zero failed/skipped native integration cases. The current launch-site
audit also rejects bypasses of these wrappers.

Each launch still requires exact parent/child identity, argv, runtime, source,
dependency, Core hash/MVID/controller and active-selector agreement. Normal child
and actual parent exit receipts must agree. The managed alternative is limited to
an exact terminating `System.ArgumentException` whose first managed throw frame
is the verified public Core `UseCisHealthcheck` registration method, with exact
PE method token/type/name/MVID/hash and origin binding, plus an actual nonzero
parent exit. This is not a broad exception allow-list. Missing receipts, unknown
failures, launch/runtime/staging faults, forced stops or timeouts remain invalid;
they receive no kill credit.

The copied-receipt controls preserve original source/config/parent references,
PIDs, times and native exit values, rebase the isolated stage and child paths, and
recompute the derivative session/launch bindings. The positive copy must audit
successfully before independent missing-parent, missing-terminal,
exit-disagreement, forced-stop flag, timed-out flag, extra-timeout-marker and
changed-Core-hash controls are assessed. Original selected evidence/stage hashes
were checked unchanged. These controls did not test a broader managed exception
acceptance policy.

At this snapshot the coordinator is starting fresh full automatic AspNetCore
Windows/Linux lanes with the 53-case closure and documented 300-second additional
native session budget, allowing slow managed crash cleanup to finish per mutant.
The mutation break threshold remains 90; actual child timeouts and lifecycle
guards remain strict. These in-progress lanes have no outcome credited here.
Full automatic AspNetCore/Pipes and global release qualification remain open.

## Retained artifact pins

Paths below are relative to repository
`.artifacts/healthcheck/release-next/`. Hashes pin original small results, audits,
configs and launcher inputs; full native reports are referenced by hash rather
than reproduced. The launcher-04 source equals the maintained standalone
`verification/mutation/healthcheck/conditional-proof/Program.cs` at this snapshot.

| Retained path | SHA-256 |
| --- | --- |
| `child-smoke-windows-01/audit/results.json` | `ac2cd1182cd8a7f2285c44249d2311c33132cdf33b27dab8089152c52b02a413` |
| `child-smoke-windows-01/native/reports/mutation-report.json` | `db9a98ad21bf1430f55fcf85c5add3a2a970e6ba41048a67d730ba8696deb00f` |
| `aspnet-windows-01/audit/results.json` | `29205a89ec242c4b502924091c39aa61337eb548e19bc833cf2029af01a2f6dc` |
| `aspnet-windows-01/native/reports/mutation-report.json` | `fd2927cc7b9497ce1bddd34cbe9721f42cc857bbd38339375c64bdaac0b73fa6` |
| `aspnet-windows-01/config.json` | `a85df9629c2ae0a09780e4f498cf45b4bee83e7df75b856a177b6e6c8bc04926` |
| `conditional-v2-windows-01/proof/results.json` | `ae3d28481bd318a7f67ce776b920c5842261ca046ea9bb254764df209a3343d9` |
| `conditional-v3-windows-02/proof/results.json` | `a895dec2039f94e4a8502757c062967361839bdc7df471a658d293b4ffffdc02` |
| `conditional-v3-windows-02/audit/results.json` | `5685cffa7a95fbb2cf110a42e6739bb1001e4759a6f2abf13a5dddf804cefaa2` |
| `conditional-v3-linux-02/proof/results.json` | `8e6d74ecfb4007b09aca0d9528cd6df01f9a6ca4425eb6b6e23e2312ac9e5c57` |
| `conditional-v3-linux-02/audit/results.json` | `bec89096baa44ad0f856f705a5dadf6697843ec1e157c72843bcb83db1b768be` |
| `conditional-v4-windows-01/proof/results.json` | `4ecd79ea264a767700624d104c7704c4e7db702cf35ce872c3e70eb188f95283` |
| `conditional-v4-windows-01/audit/results.json` | `0bebc0e1949954fa7781e0796467feebe03da8b061e9327092115e72cfc728aa` |
| `conditional-v4-windows-01/config.json` | `8a2737771486f6d28d3c6728c06efef34f8cbb3b23aad0bc00cef42acfb217f7` |
| `conditional-v4-linux-02/proof/results.json` | `ba2e86a9114c86f715d95aacd0452888b3bb046a2c442a01d7dde2de64c19df5` |
| `conditional-v4-linux-02/audit/results.json` | `3908a1bfedc9540ef123832adb01994825a4353adef3353e20841ccf98f08da6` |
| `conditional-v4-linux-02/config.json` | `8a2737771486f6d28d3c6728c06efef34f8cbb3b23aad0bc00cef42acfb217f7` |
| `registration-v3-windows-01/audit/results.json` | `3efa79d2157d271f812c473a28e28c17e92aa499034c4cafed488e58a28f44e2` |
| `registration-v4-windows-01/proof/results.json` | `9377ea31f0dec940f241ad189095400cc0b6f6a0b76dc5afd599a57ba6efd7af` |
| `registration-v4-windows-01/audit/results.json` | `810463d1521e2ec85ddf005bbefc0377038faa0c287bf86f06677eb0385fa0f2` |
| `registration-v4-windows-01/config.json` | `e6af6abc9fb186537ebcad1aa206831df7384a08c3d10bf65fa3b5f608c16a34` |
| `registration-v4-linux-01/proof/results.json` | `805dfc48a1229801cd27aa4d5e08c181c536c7238f9adc9a98f835df42df4a71` |
| `registration-v4-linux-01/audit/results.json` | `07332106ac6633ceff58545cd2debecfc3faf29543c1a0362976fde88b54e3d1` |
| `registration-v4-linux-01/config.json` | `e6af6abc9fb186537ebcad1aa206831df7384a08c3d10bf65fa3b5f608c16a34` |
| `child-audit-controls-windows-01/results.json` | `172b77303f3cb8d27513cb4eb29a7d5aed73894d71ac7ce5287dc38ede002ddb` |
| `http-timeout-integration-01.log` | `cd3e84fca6dfaa7599f1d5a53d74cdac7c669d64f5be008e2243f2b8ba03cdbd` |
| `conditional-launcher-04/Program.cs` | `17c237ca07805d4a70fd6f54e5eca29752e0b37ffac09c1c00dd88698abfbc1d` |
| `conditional-launcher-04/Launcher.csproj` | `ae184d8b1f5116cbc5c8ef831c2f35dbb3efaea30e1efff6861888144189600e` |
| `conditional-launcher-04/bin/Release/net10.0/CIS.Healthcheck.ConditionalProof.dll` | `d102c58b6075a3918ba397dbbc620d6e90cd8aa0e8f618696b7dab823637ba6e` |
