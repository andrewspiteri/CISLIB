---
title: Configuration effective values and synchronized commits
type: document
status: draft
cis:
  stable_id: cislib.document.modules.configuration.mapping
---

# Configuration effective values and synchronized commits

Configuration composes captured defaults, the `CommonConfiguration` record and
the fixed host application's record, in that order. The
[retained-object contract](core.md) supplies the root, mapped shape and shared
reader gate. This guide describes the fixed value pipeline selected under
[CIS-0008](../../changes/CIS-0008/authorization.json); actual native results are
recorded separately in its [verification record](../../changes/CIS-0008/verification.md).
It does not establish production storage, platform or release qualification.

## Presence and source validation

A provided record must be a JSON object. Absence is supplied separately by the
storage coordinator; JSON `null` is not a missing root record. Every supplied
record is checked for duplicate ordinal object keys, including objects inside
arrays and ignored unknown subtrees. Names are case-sensitive: `Value` and
`value` are different names.

Known properties in each source are checked for shape, scalar format/range and
explicit-null permission before the sources are overlaid. An invalid common
value therefore fails preparation even when a valid application override would
mask it. Omitted source members are permitted and inherit lower values. Only the
complete effective candidate receives application cross-property validation.

Nested passive objects merge their present members. When an object is supplied
over a null lower value, its starting members come from that declared child's
once-captured defaults. Sequences and dictionaries replace the entire lower
collection. Object elements in replacement collections begin with their declared
child defaults. Empty collections, empty strings, false and zero are real values.
Removing a property or record rebuilds from the current lower sources and the
original captured defaults, never stale live children or newly generated defaults.

Null is accepted for nullable value types and reference properties/elements whose
write-nullability permits it. An oblivious reference annotation has no nonnull
contract. Explicit null for a known nonnullable source member or collection
element fails even when masked later. Missing members can still be supplied by
lower levels; final completion rejects remaining nonnullable null values.

Unknown properties are ignored after structural validation. An optional internal
diagnostic callback receives the root type and `Configuration.UnknownProperty`
plus an escaped property name. Each preparation reports at most eight names,
each truncated to 64 Unicode scalar values before JSON escaping. It reports no
property values, source payloads or exception messages. Callback failure cannot
alter mapping or skip subsequent validation. Dictionary keys are data, not
unknown-property warnings.

## Fixed scalar formats

Only the passive types listed in the [core profile](core.md) participate. Numeric
conversion uses the declared CLR width, invariant JSON syntax and finite values.
There is no string-to-number, string-to-bool or general conversion extension.

| Type | Accepted source and effective representation |
| --- | --- |
| Signed/unsigned integers | JSON integral token fitting the actual width; fractional/exponent tokens and numeric strings fail. |
| `float`, `double`, `decimal` | JSON number fitting the declared type; floating infinities/NaN fail. Floating signed zero normalizes to zero. |
| `bool`, `string` | Corresponding JSON kind; no implicit coercion. |
| `char` | One nonsurrogate UTF-16 character in a JSON string. |
| `Guid` | JSON string in dashed `D` format; case differences normalize. |
| `TimeSpan` | JSON string in invariant constant `c` format. |
| Enum | Exact ordinal declared name, or a JSON integral number fitting the actual underlying signed/unsigned width. Unnamed numbers, including zero, are permitted. Numeric strings, case folding and comma/flags parsing are not added; validators own semantic restrictions. |
| `DateTime` | The System.Text.Json ISO-8601 profile. No offset remains `Unspecified` wall-clock ticks. Explicit `Z` or an offset converts to UTC. The mapper never infers the current timezone for no-offset input. |
| `DateTimeOffset` | The same ISO profile with an explicit `Z` or offset required; effective values normalize to UTC. |

Public `JsonPropertyName` names are honored; `JsonIgnore(Always)` members are
excluded. Conditional ignore settings do not remove members from effective data.
Array/List sequences and ordinary default/ordinal `Dictionary<string,T>` values
are reconstructed using the fixed supported backing types. Custom converters,
polymorphic activation and arbitrary container/comparer behavior remain outside
the profile.

## Detached candidates and validation

Preparation obtains one fresh exact-type candidate from the captured root
factory. Before assigning mapped data, it claims that candidate through the same
event ownership check as retained roots. Returning the live root, another owning
host's root or a previously claimed candidate fails without candidate assignments.

Every nested object, sequence and dictionary replacement is constructed from
detached effective data. Preparation never walks and mutates a factory
candidate's existing child graph, which could alias live default objects.
Constructors may run for working objects, but every mapped member is assigned
from the captured/default-and-source composition before validation.

An optional per-type validator inspects the complete typed candidate
synchronously. It may reject by throwing; it must not mutate data, retain the
candidate, perform I/O or start work. Failures become safe
`Configuration.Validation` issues without raw exception text. Effective mapped
mutations detected after the call reject the candidate. Prepared replacements
are materialized independently from the validator's candidate, so its object
references are not installed into the retained root. Unmapped side effects of
arbitrary user code are outside the passive-object contract.

## Effective checksum

The checksum is lowercase SHA-256 of a fixed UTF-8 JSON representation of the
complete normalized typed values. It is not a hash of source JSON, an opaque
source token, an authenticity check or a routine log field. Object/property and
dictionary names sort ordinally; sequence order remains significant.

The node grammar is `["object",{name:node}]`, `["dictionary",{key:node}]`,
`["sequence",[node]]`, `["null"]`, or `[tag,"text"]` for scalars. Scalar tags are
`i8/u8/i16/u16/i32/u32/i64/u64`, `f32/f64`, `decimal`, `bool`, `string`, `char`,
`guid`, `timespan`, `datetime:unspecified`, `datetime:utc` and
`datetimeoffset:utc`. Enums prefix their actual integer tag with `enum:`.
Integers/ticks use invariant decimal digits; booleans use `true/false`; decimals
use invariant `G29`; floats use invariant round-trip `R` with zero normalized.
Guid text uses lowercase `D`. Date/time tags carry tick strings; unspecified and
UTC DateTime remain distinct, while equivalent explicit UTC offsets compare equal.

For four mapped properties, this is a complete canonical vector:

```json
["object",{"Count":["i32","2"],"Mode":["enum:i32","1"],"Price":["decimal","1.25"],"When":["datetime:utc","621355968000000000"]}]
```

Its SHA-256 is `d46cedba454387ba4193f3a677cbe66db3e075a37e5e6a3dafcb3e5d81a389b6`.
JSON formatting/property order, equivalent decimal scales, enum names versus
their numeric value and equivalent UTC offsets do not create effective changes.

## Commit and failure boundary

Preparation, factories, validators and diagnostics run outside `ValueGate`.
Apply takes the same private entry gate as `IConfigurationReader`, checks actual
cancellation/stopping and disabled state, then compares the accepted checksum.
An unchanged value performs no live assignments or revision advance. Initial
population establishes revision zero and returns no runtime change/event.

A real change checks revision capacity before assigning, writes the prepared
root properties and accepted checksum/revision inside that gate, then returns a
change result. Cancellation after admission cannot turn an accepted commit into
an uncommitted one. The serialized refresh owner publishes its one event after
the value gate is released, including when cancellation follows commit. The
value pipeline does not publish events itself.

Live setters must remain passive and nonthrowing. If one unexpectedly throws,
the entry is disabled before the failure escapes. No accepted checksum/revision
or success event advances, and subsequent applies fail. Earlier assignments may
already have occurred: no rollback or intact-old-data claim is made. The refresh
owner must surface that terminal fault through existing diagnostics/health.
Ordinary invalid candidates remain retriable and leave accepted live values
untouched. Raw unsynchronized reads and consumer mutation remain outside the
shared-reader guarantee.
