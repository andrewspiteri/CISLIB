# Metrics v1 fixtures

[primitive-cases.json](v1/primitive-cases.json) contains small independent literal
expectations for the [portable contract](../../contracts/metrics/README.md).
Native tests may consume applicable sections with the existing test framework;
the fixture is not a test runner or evidence of a pass.

- `measurementCases` describe raw accepted emissions, not SDK aggregation. Input
  and expected numeric values are decimal strings. `NaN`, `Infinity` and
  `-Infinity` are named invalid binary64 tokens, not JSON numeric extensions.
  Signed-64-bit inputs are strings so a JSON reader cannot round them first.
- `dimensions` are arrays of `[key,value]` pairs so duplicate-key inputs survive
  parsing. Compare valid dimension sets ordinally without imposing pair order.
  Invalid input drops the entire measurement. `expectedDropped` counts omitted
  recording attempts, not diagnostic log calls or a public metric counter.
- `timingScopeCases` use explicit monotonic frequency/timestamps in decimal
  strings. `expectedStopSeconds` lists only Stop return values; Dispose has no
  return. The first completion's emission/drop is final even when later calls
  supply valid or different dimensions.
- `observableCases` describe one collection's callback result. An empty result or
  ordinary callback failure emits nothing, rather than a synthetic zero.
- `definitionCases` validate at registration, not at definition construction.
  Histogram bounds are finite JSON numbers; no custom parser/generator is needed.
- `exportedFamilies` pins exact required sample names/types for the selected
  underscore/suffix exporter profile. It contains no custom serializer or SDK
  aggregation oracle. The recorded Windows optional-package exporter case
  consumes all eight families; Core-only fixture checks alone cannot establish
  that an exporter emitted them.

Do not generate expected results by calling the production naming/validation
method under test. Do not infer a release/platform pass from parsing this file.
The Core consumer executes the measurement, timing and observable cases. Do not
claim that the definition-case section was executed merely because the file was
loaded; registration also has separate native boundary tests. Actual selected
cases, exporter observations, input currentness and remaining scope are recorded
under [CIS-0009](../../docs/changes/CIS-0009/verification.md).
