# Amendments to original logging specifications

The preserved source guide/plan/backlog and frontend BRD/design describe requirements and historical implementation proposals. They do not direct a new recovery campaign. Current user decisions below supersede conflicting original passages.

| Area | Current accepted direction |
| --- | --- |
| Naming and package | `CIS.Core` is one multi-module package/assembly, root namespace `CIS`. Logging and Microsoft integration use `CIS.Logging` and `CIS.Logging.MicrosoftLogger` inside it. |
| Dependencies | Approved Microsoft-maintained NuGets are permitted with their explicit closure. The user approved `Microsoft.AspNetCore.App` in Core for Healthcheck. Third-party runtime dependencies require separate optional packages. |
| Environment | Correct unpublished `EnviormentsOptions` to `EnvironmentOptions`, with no misspelled alias. Use the selected readonly static constructor/container, automatic defaults then known overrides and bounded common-identity interning. Previous explicit initialization/snapshot and passive-store proposals are superseded. |
| Severity configuration | Accept full names and `TRC`, `DBG`, `INF`/`INFO`, `WRN`/`WARN`, `ERR`, `CRT`/`CRIT`, case-insensitively after trimming. Blank is absent; numeric/unrecognized tokens remain invalid. |
| Entry bound | Default maximum encoded entry is 65536 bytes; `CORE_LOG_MAX_ENTRY_BYTES` configures the selected 16384–16777216 range. Truncate oversized payloads at safe boundaries, preserve the protected envelope and report truncation. Count complete encoded bytes including LF. Enabled file max bytes must accommodate one maximum entry. |
| Scope ownership | Keep `BeginScope` and creator-disposes/LIFO. Detect non-head/absent active handles as documented; arbitrary cross-flow ownership cannot always be detected. Repeated valid disposal is harmless. |
| Runtime and providers | Application owns the runtime; Microsoft provider borrows it. Preserve custom writer ownership, processing/filter/routing/duplicate policy and compiler interpolation. No synchronous runtime disposal or new placeholder startup API. |
| Incoming frontend | The selected rotating frontend BRD/design supersedes full incoming rejection and prefix-based flush membership. Event UTC ticks define age; positive incoming tickets are correlation, not a prefix. Flush captures a bounded batch with explicit loss/pending facts. `TryCreate` returns data-only startup facts and a separate runtime. |
| Future specifications | Preserve accepted specifications without speculative language/module/framework implementations. CIS consumes versioned references separately; Core has no CIS dependency. |

See [current decisions and release gaps](../decisions.md), [EnvironmentOptions](../../docs/modules/logging/environment-options.md), [runtime ownership](../../docs/modules/logging/runtime-contract.md), [incoming behavior](../../docs/modules/logging/incoming-pipeline.md) and the [frontend BRD](priority-spool/brd-v1.2.md). Original source documents retain their original wording; historical campaign outcomes are not current release claims.
