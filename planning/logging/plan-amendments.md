# Amendments to the supplied logging plans

Effective 10 October 2026. The user's architecture and dependency decisions override the corresponding design choices in the source guide/plan/backlog. Original files and their hashes remain unchanged; this record preserves their provenance without executing conflicting instructions.

| Source area | Current direction |
| --- | --- |
| Repository foundation and language portability | CisLib has a separate repository. C# is first; shared contracts/conformance apply to future languages with declared runtime capabilities. The source TypeScript follow-on is a candidate, not the only possible future implementation. |
| REQ-005; guide section 3; WP01-S02 package-reference/bridge boundaries | Core may explicitly reference approved Microsoft-maintained packages. Verify the full resolved runtime closure and packed dependency groups. Third-party runtime dependencies stay in separate optional integration packages. Zero PackageReference items is superseded. |
| Guide sections 5/10; WP01-S01/WP01-S02/WP06 standalone bridge identities | One Core project/package contains the multi-module implementation. Logging and MicrosoftLogger are folders/namespaces inside Core. Microsoft integration remains required planned functionality; its separate package/project requirement is superseded. |
| WP06/WP07/WP08 separate bridge checks and candidate identities | Preserve provider mapping, filter, scope and ownership observations. Qualify them against the Core candidate and include the provider code in Core's architecture, coverage/mutation and compatibility scope. Independent bridge package restoration and a separate bridge assembly are no longer applicable. |
| Public library naming | Project, assembly and package ID are `CIS.Core`; the root namespace is `CIS`. Module namespaces begin `CIS.Logging`, not `CIS.Core.Logging`. |
| First consuming application | A simple ConsoleHuman example host is the initial consumer. It owns the runtime once implemented. A source project reference is explicit development scaffolding; release examples must consume exact tested packages. |
| CIS integration backlog | CisLib produces versioned library/reference artifacts; CIS owns their import, managed installation and routing. A library release and a CIS release remain separately identified. |

The source behavioral contracts, bounded lifecycle requirements and verification families remain the proposed implementation baseline. No logging story is marked Done by initialization. The public facade spelling is amended below. Indefinite compatibility policy, licence and release policy still need their publication disposition.

## Severity aliases selected during WP01-S03

The user expanded the accepted configuration tokens on 10 October 2026. In addition to the seven full severity names, accept `TRC` (Trace), `DBG` (Debug), `INF`/`INFO` (Information), `WRN`/`WARN` (Warning), `ERR` (Error) and `CRT`/`CRIT` (Critical), case-insensitively after trimming. This supersedes the source guide's full-name-only token list. Blank values remain absent; unrecognized and numeric tokens remain invalid. Serialization status mappings are unchanged. Preserve this parser and its tests for WP03-S02; the public logging API is still pending.

## Startup facade name selected during WP02-S01

The user explicitly selected **`EnvironmentOptions`** before publication, replacing the source guide/plan's **`EnviormentsOptions`** spelling. Use `CIS.EnvironmentOptions` for the proposed C# facade. This supersedes the original naming passages in REQ-002, WP01-S01, WP02-S01, WP03-S04 and subsequent consumer examples; the preserved source files and historical evidence remain unchanged. No misspelled compatibility alias is required because neither name has been published.

WP02-S01 specifies the surface and portable expectations using a nonshipping reference-only assembly and compiled consumer fixtures. Runtime metadata/configuration resolution and static publication remain WP03 implementation work. Selected type/property/code details are implementation choices recorded in [the startup contract](../../docs/modules/logging/startup-contract.md), rather than additional directions attributed to the user.
