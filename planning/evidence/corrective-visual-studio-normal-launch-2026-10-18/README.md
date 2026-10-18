# Normal Visual Studio test startup

The roadmap remains paused. No Claude service was used. This correction supersedes the special launch command in the earlier [environment diagnosis](../corrective-visual-studio-environment-2026-10-18/README.md).

## Change

The existing shared `tests/TestAssembly.cs`, already linked into every MSTest suite, now initializes the test process before its cases run. On Windows it appends the standard PowerShell 7 and user WindowsApps directories only when they contain `pwsh.exe` and are absent from PATH. Existing PATH entries retain precedence. It defaults `UseSharedCompilation=false` and `MSBUILDDISABLENODEREUSE=1` when their existing values are empty, isolating fixture builds while preserving explicit caller settings.

Open Visual Studio normally, build the solution and run tests. The initializer changes only its test process and children. It installs nothing, edits no registry values and adds no dependencies. Production code, test selection, assertions, worker count and deadlines are unchanged. SDK/runtime, installed PowerShell and retained architecture/compatibility prerequisites still apply; see [Test Explorer guidance](../../../docs/architecture/csharp-focused-tests.md#test-explorer-prerequisites).

## Verification

The four affected unit cases fail before the change with `pwsh` missing under the machine-only PATH, then all four pass under that same caller PATH with both compiler variables absent: [before](before-unit.trx), [after](after-unit.trx). The initial focused native run also passes all eleven deterministic/process-support cases.

A fresh, unfiltered run uses the VSTest 18.10.1 runner installed with Visual Studio 18 Insiders, every Debug suite, the existing twenty-minute runsettings, a machine-only caller PATH without the user PowerShell alias, and both compiler variables unset. [Launch identities and starting environment](normal-vs-runner-launch.json). All **6,858 cases pass**, with zero failures/skips:

| Suite | Passed |
| --- | ---: |
| Unit | 3,750 |
| Integration | 2,120 |
| Architecture | 853 |
| Development compatibility | 111 |
| Business cases | 24 |

The eleven process-support cases also pass on native Ubuntu with the same startup source, compiler variables unset and caller PATH unchanged: [native result](ubuntu-support.trx). This focused Linux check is not a new complete Ubuntu checkpoint.

The [C# audit](VerifyNativeResults.cs) checks native counters/outcomes, before/after selection equality, all five full-run suite counts, all twenty saved IDE failure names, the starting caller environment, and unchanged startup source/test-binary hashes. [Verified summary and full native report SHA-256](verification.json). The complete native report and logs remain under `.artifacts/corrective/visual-studio-normal-launch-2026-10-18/normal-vs-all-final/` and its parent; the audit can be run as a .NET 10 file-based application from `implementations/csharp` against that repository root.

The first all-suite attempt passes 6,853 cases and fails five helper-launcher cases because a pre-existing idle SDK compiler server holds `CIS.Core.TestSupport.sourcelink.json`. Read-only Windows Restart Manager identifies PID 10496, created before this correction. Graceful shutdown does not respond, so that idle server alone is stopped and exclusive access to the file is confirmed: [cleanup record](stale-compiler-cleanup.json). All five cases then [pass separately](stale-lock-recheck.trx), followed by the complete fresh 6,858-case pass above. The two launcher classes already declare non-parallel execution; no scheduling changes were needed. The failed full attempt is preserved separately and earns no complete-run pass credit. The initial focused CLI tool bootstrap also stalls before executing tests and is stopped; its replacement native focused run succeeds.

The actual Test Explorer UI was not driven. The complete native Visual Studio runner execution reproduces the relevant normal-launch environment; it does not claim a new Developer/release qualification, cumulative published-package compatibility or publication approval. The user-owned Web launch settings remain unchanged.

CIS graph schema 2 is fresh/valid: build sha256:1a9bed7e2e723e5ee35f52cbddee57b8fa067afb8370fc0faa6d740f2494f268, 11,597 nodes, 58,159 edges, zero diagnostics and strict=false. The exact CIS.Tests.TestAssemblySetup class resolves uniquely and untruncated at tests/TestAssembly.cs line 8 under the csharp-tooling source component. That source-level graph identity does not establish five separate project/compiler bindings; the shared Compile import and native test execution are the implementation/verification evidence. Derived .cis/local state is not committed.
