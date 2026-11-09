# Shared logging behavior fixtures

The JSON catalogs hold language-neutral inputs and expected behavior for configuration, metadata, values, scopes, capture/encoding, admission, queues, file output, providers and lifecycle boundaries. Native tests exercise the applicable real Core behaviors, including malformed Unicode, bounds, ownership and concurrency. Tagged value inputs preserve distinctions that cannot be inferred from serialized JSON alone; transport-only rejections are not runtime behavior claims.

These catalogs are fixtures, not execution reports or release history. A case definition does not prove it ran. Obsolete requirement-binding inventories, qualification maps and recovery metadata were removed; accepted requirements remain in [source specifications](../../planning/logging/sources/core-library-implementation-guide-v1.md), [current amendments](../../planning/logging/plan-amendments.md) and the [frontend BRD](../../planning/logging/priority-spool/brd-v1.2.md).

Use native tests through the C# solution described in [README](../../README.md). Other languages must declare their own capabilities/applicability when actually implemented.
