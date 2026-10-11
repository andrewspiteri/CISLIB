# Configuration option validation

WP03-S03-T01 implements internal logging-option validation after [captured selection](configuration-inputs.md) and [typed parsing](configuration-resolution.md). It consumes the selected resolution once, reports independent problems in catalog order and preserves selected source keys. Invalid higher-priority values do not fall through to setup or defaults.

| Setting | Inclusive range |
| --- | --- |
| Queue capacity | 1–65536 |
| Flush interval, milliseconds | 10–60000 |
| Shutdown timeout, milliseconds | 1–300000 |
| File segment bytes | 65536–1073741824 |
| Retained files | 1–1000 |
| Schema version | 1 only |
| Maximum entry bytes | 16384–16777216 |

Malformed tokens or invariant decimal syntax report `InvalidToken`; integer domain overflow and range failures report `OutOfRange`. A representable schema integer other than 1 reports `UnsupportedSchemaVersion`. The existing parser owns syntax and typed-enum checks; the validator converts its states into safe problems and applies all seven ranges to parsed values. Range constants are checked against the [portable catalog](../../../contracts/logging/v1/configuration.json).

Every selected option is validated even if console or file output is disabled. An enabled file output additionally requires `FileMaxBytes >= MaxEntryBytes`; disabled files do not impose that relationship. A malformed or individually out-of-range size produces its own problem without a duplicate relationship error. Flush and shutdown intervals have independent ranges; no relative ordering constraint is defined. An invalid inherited global threshold is reported once at its selected parent, while independently selected invalid output options retain their own problems.

The internal `ConfigurationEnvironmentKind` carries the setup-only contract values Unknown (0), Console (1), Service (2), Web (3) and Function (4). Undefined values report `InvalidEnvironmentKind` with key `EnvironmentSetup.EnvironmentKind`. Absent kind defaults to Unknown. Neither a host/environment label nor an unknown environment variable infers or overrides the kind. These values are checked against the proposed C# surface. Public setup/snapshot mapping remains later work.

The returned collection owns getter-only problem descriptors and rejects mutation. Explanations contain fixed guidance and invariant known bounds, never selected tokens, identity/path values or unrelated variables. This phase has no filesystem or host side effects and does not resolve path syntax. The complete public `CoreConfigurationException`, identity/control-character checks and full metadata issue aggregation belong to T02; captured working-directory/path validation belongs to T03. A zero-length option problem list does not establish a valid startup snapshot.

## Executed checks

[Windows evidence](../../../planning/logging/evidence/wp03-s03-t01-windows.json) retains 349 new cases in 15 bound methods: all 276 logging-option rows from the existing per-key table, all nine cross-option membership cases, typed setup boundaries, undefined enums, EnvironmentKind, culture invariance, relationship boundaries, duplicate suppression, safe ordering and owned problem results. Six file-size setup cases inspect an owned empty directory before disposal to establish that validation creates no output. The multiple-error legacy vector specifies membership; a separate test enforces catalog order.

The table adapter accepts the older `issues` array and the entry-limit amendment's single `code` shape, with independent expected selected-source keys. One unpublished expectation (`CFG-MAX-ENTRY-BYTES-INVALID-2`) is corrected from InvalidToken to OutOfRange for a syntactically valid overflowing integer, matching the other numeric vectors and parser policy. Original supplied plans and historical evidence retain their exact bytes. The first targeted run exposed the adapter shape omission (12 failures); corrected and final native runs pass. No whitespace fixture was changed: an inspection with Windows' default text encoding initially displayed mojibake; explicit UTF-8 inspection and execution confirm the existing inputs are valid Unicode whitespace.

The full Developer suite and a fresh copied-source Windows replay pass 866 unit, 15 integration and 351 architecture cases (1232 total), with 43 compiler controls matching expected outcomes. Release checks still return 2 for the incomplete release gates. The existing configuration child-process and compiled ambient-call guard remain active; the new option validator is qualified by Windows native tests/replay. This task makes no new Linux native-family, full startup, runtime truncation or release qualification claim. The remaining 74 per-key identity/path rows await later validation tasks.

## Implementation review

The production validator separates selected parse failures, numeric ranges and the enabled-file relationship into short helpers. The test class exceeds the approximate 300-line review trigger because it binds the shared option/cross tables and setup controls around one validator; methods remain focused, and named setup builders isolate fixture construction. No partial files or broad analyzer/coverage suppressions are added. Core's two approved Microsoft runtime packages and one-assembly packaging remain unchanged.
