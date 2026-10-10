# Shared logging conformance cases

These catalogs are language-neutral. Stable native case groups and requirement mappings distinguish implemented checks from planned obligations. Configuration, metadata and startup lifecycle vectors remain runtime expectations for WP03.

WP02-S02 adds [record fixtures](event-record-cases.json), [explicit value fixtures](event-value-cases.json), and [future capture expectations](event-capture-expectations.json). The C# development-only validator executes every record/value case, including Unicode, node/depth, numeric and exact escaped-byte boundaries. Producer projection and additive reader evolution use distinct modes. Tagged constructor inputs preserve distinctions that cannot be inferred from serialized strings/numbers alone.

Capture expectations for correlation overrides, nested copying, scopes and exception clipping remain unexecuted until WP04 uses the real library. Compiler-only consumers qualify shape, not behavior. No other language, collector ingestion, binary compatibility or platform-specific file behavior is established by these checks.

WP02-S03 adds [45 admission examples](admission-cases.json) and [28 fixed-boundary result projections](drain-result-cases.json), executed only against development specification validators. The [18 runtime lifecycle expectations](runtime-lifecycle-expectations.json) remain unexecuted until real queue/worker/provider implementation. Exact decimal counter strings preserve Int64 fixture transport; they do not amend event wire types.
