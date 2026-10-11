# Typed configuration resolution

WP03-S02-T02 implements the internal [resolver](../../../implementations/csharp/src/CIS.Core/Environment/Resolution/ConfigurationResolver.cs) over one [captured environment](configuration-inputs.md), typed setup inputs and discovered application identity. It selects and parses all 18 settings in the [shared catalog](../../../contracts/logging/v1/configuration.json). This result is an intermediate parsed configuration, before range/identity/path validation and the public `CIS.EnvironmentOptions` snapshot.

| Setting | Default |
| --- | --- |
| `CORE_SERVICE_NAME` | Discovered application name; absent if unavailable |
| `CORE_ENVIRONMENT` | `unknown` |
| `CORE_SERVICE_VERSION` | Actual `BuildVersion` |
| `CORE_LOG_LEVEL` | Information |
| `CORE_LOG_CONSOLE_FORMAT` | Human (`human`) |
| `CORE_LOG_CONSOLE_LEVEL` | Inherit Level when absent |
| `CORE_LOG_CONSOLE_STREAM` | Standard error (`stderr`) |
| `CORE_LOG_HUMAN_DETAIL` | Normal (`normal`) |
| `CORE_LOG_FILE_PATH` | Absent |
| `CORE_LOG_FILE_FORMAT` | JSON Lines (`jsonl`) |
| `CORE_LOG_FILE_LEVEL` | Inherit Level when absent |
| `CORE_LOG_QUEUE_CAPACITY` | 1024 |
| `CORE_LOG_FLUSH_INTERVAL_MS` | 1000 |
| `CORE_LOG_SHUTDOWN_TIMEOUT_MS` | 5000 |
| `CORE_LOG_FILE_MAX_BYTES` | 10485760 |
| `CORE_LOG_FILE_RETAINED_FILES` | 10 |
| `CORE_LOG_SCHEMA_VERSION` | 1 |
| `CORE_LOG_MAX_ENTRY_BYTES` | 65536 (64 KiB) |

Console and file thresholds inherit only when their own selected value is absent. Level is not a global cap: Information globally, Warning on console and Debug in the file preserves the file's Debug interest. An explicitly selected None disables that output. Console enablement depends on its threshold; file enablement requires both a nonblank path and a threshold other than None. Explicit formats and stream remain selected without inference from path extensions or host settings. All options are parsed even if both outputs are disabled.

The [parser](../../../implementations/csharp/src/CIS.Core/Environment/Resolution/ConfigurationSettingParser.cs) reuses the proven severity parser, including TRC, DBG, INF/INFO, WRN/WARN, ERR and CRT/CRIT. Other accepted tokens are human/jsonl, stderr/stdout and normal/verbose. Matching is ordinal and case-insensitive after selection trims whitespace. Numeric severity tokens and undefined typed enum values are invalid. Identity and path text preserve trimmed case.

Integer tokens accept ASCII decimal digits with an optional leading sign, using invariant culture. Grouping, decimal fractions, exponents, hexadecimal and Unicode digits are invalid. Integer-domain overflow is OutOfRange; FileMaxBytes retains a 64-bit domain while the other numeric settings use 32-bit domains. A representable schema integer other than 1 is UnsupportedSchemaVersion. Flush and shutdown expose millisecond values and their corresponding TimeSpan values.

A selected invalid value keeps its source and parse state and never falls through to a lower-priority value. Inheritance from an invalid global threshold is explicitly unresolved; an independently valid output threshold still parses. Enablement is nullable where a required threshold is unresolved. Actual BuildVersion remains separate from the selected service-version label. Getter-only parsed settings and a read-only ordered list retain scalar values and source keys, without retaining the input object or variable reader. Object formatting does not automatically print selected raw values.

Parsed values are not yet validated options. Negative or out-of-policy numbers, control characters in identity, and syntactically invalid paths can remain parsed here. WP03-S03 applies option ranges, identity limits, cross-option relationships (including FileMaxBytes >= MaxEntryBytes for an enabled file), safe aggregated diagnostics and captured absolute-path resolution before publication. No file, directory, writer, worker or network client is created by resolution, and no process/framework environment is changed. Runtime entry truncation remains WP04/WP05 work.

The 121 native cases bind 17 methods to defaults, every environment setting, typed setup, severity aliases, independent outputs, invalid inherited thresholds, invariant syntax/domain overflow, culture, schema selection, selected-invalid retention, disabled outputs, ownership and the parsed/validated boundary. [Windows evidence](../../../planning/logging/evidence/wp03-s02-t02-windows.json) retains Developer verification and isolated source replay. The 350 complete per-key and nine cross-option fixture expectations remain pending full resolver/validator qualification. [T03 qualifies normalization](configuration-normalization.md); shared configuration fixtures remain T04; public startup/publication and full Linux test-family qualification remain pending.

[T04 shared configuration fixtures](configuration-fixtures.md) now qualify complete parsed-selection snapshots and host-label preservation on Windows/Ubuntu; full option/identity/path validation and public publication remain pending.
