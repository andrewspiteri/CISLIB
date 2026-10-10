# Shared logging conformance cases

Place language-neutral input/output fixtures and a stable case catalog here as logging behavior is specified. Cases need explicit capabilities and runtime applicability; do not silently omit unsupported cases or claim that a C# run verifies another language.

WP02-S01 adds configuration, metadata and startup lifecycle expectations, with explicit pending runtime execution. The configuration catalog covers every section 8 key, alias/default, token/range, precedence and applicable path rule. C# contract metadata/consumer checks verify shape and fixture completeness; they do not count these vectors as passing resolver tests. JSON encodings, scope identity and observable output lifecycle remain later stories. Native binary compatibility and platform-specific file behavior use their language/platform verification lanes.
