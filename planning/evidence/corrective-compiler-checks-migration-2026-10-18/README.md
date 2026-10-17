# Native compiler-check migration — 18 October 2026

The logging roadmap stays paused. This is the user-requested corrective migration; Claude was not used.

The existing C# engineering tool now owns these checks. The three PowerShell entry points retain their parameters and default output directories and only bootstrap/invoke the tool. No shipping source, package identity, dependency, compiler fixture or historical baseline changes.

| Previous entry point | Native replacement | Preserved controls |
| --- | --- | --- |
| check-startup-contract.ps1 | CompilerChecks startup | Approved restored logging compile asset and hash; source hashes; reference-only assembly; four positive and 20 negative consumers (25 builds including the reference). |
| check-compatibility-samples.ps1 | CompilerChecks samples | Successful startup prerequisite, contained/hash-checked baseline, unchanged logging asset, six source consumers and eight incompatible API mutations with their reference builds (22 builds). Mutation anchors occur exactly once, independently of CRLF/LF. |
| check-log-value-consumers.ps1 | CompilerChecks log-values | Selected SDK/compiler/reference-pack hashes, unchanged actual Release Core DLL, all 12 catalog controls, binary/log hashes and the exact-runtime typed consumer execution. |

Negative outcomes require nonzero exit and an actual error with the exact diagnostic code. A filename, warning, longer code or unrelated error mentioning that code cannot satisfy the control. The native process runner bounds each child to two minutes and drains both streams. Report schemas and qualification limits remain: reference-only shape controls are not published compatibility evidence.

Windows and native Ubuntu each passed all **59 compiler controls** and **126 selected architecture cases**, including 20 new cases bound separately in the inventory. Windows focused run: bb31263314fc4d66af996db71e44ead1; Ubuntu: cd6e7d0083864e5ebaa68fbc64c53782. Zero failures or skips. The first selected run on each lane passed 125 and failed the new failed-prerequisite-report case: InvalidDataException was missing from the report handler. The handler was corrected and both selected runs passed. Successful compiler control outcomes predate that handler-only correction; the following combined Developer checkpoint will rerun them on the final source.

The six retained JSON files are the native compiler reports. Their log/binary references point into the lane-local ignored Developer contract directories. Full command logs and preliminary failure output remain under .artifacts/corrective/compiler-checks-migration-2026-10-18 on the respective lanes. These focused results do not replace the previous full Developer checkpoint or qualify coverage, mutation, cumulative released-package compatibility, offline replay or publication.

CIS exact class queries for StartupContractTests and DependencyBoundaryTests were unique and untruncated against the previously fresh graph. The graph is refreshed after this batch; graph context is navigation evidence only. Native Ubuntu retains its existing Git metadata and lane-local binaries. Unrelated user-owned Web launch settings remain untouched.

Dependency checking is the next separate batch. The example boundary verifier's real role-guard mutation must be adapted to native source when its delegated dependency policy moves; it must not be silently removed.
