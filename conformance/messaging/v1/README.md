# Portable messaging wire vectors

`wire-vectors.json` contains four valid immutable envelopes and twenty-five
rejected framing examples. A `wireUtf8` string is the exact UTF-8 preimage,
without a BOM or a trailing newline. Hashes cover those bytes, including payload
precision and every envelope field. The first valid case also freezes its full
hexadecimal preimage. These expected bytes were authored independently of the
module encoder; SHA-256 values were calculated directly from those preimages.
The malformed UTF-8 rejection uses `wireHex` because invalid bytes cannot be
represented directly as a Unicode JSON string.

The valid cases cover an event with a precision-sensitive decimal represented
as a string and an integer above the exact binary64 range, a command, a durable
request with a structured reply endpoint and operation reference, and a reply
to the current request attempt. Payload bytes are preserved; wire decoding does
not reserialize or normalize them. The transport digest is distinct from the
Operations business fingerprint.

The v1 reader rejects unknown envelope fields and required versions, duplicate
keys even when escaped or nested, malformed required values, inconsistent
request/reply fields, whitespace without a JSON value, comments and trailing JSON.
Its UTC timestamp profile uses
the RFC 3339 `T` separator and `Z`, with whole seconds or one through seven
fractional digits. The writer emits seven fractional digits.

[Native unit tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/Messaging/MessageWireTests.cs)
consume these vectors and separately exercise malformed UTF-8, input/copy
ownership, independent payload/metadata/header/depth bounds and exact byte
retention. Framing conformance establishes no broker authentication, transaction
atomicity, delivery, authorization or committed-effect guarantee.
