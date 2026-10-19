# C# interpolated logging

The severity methods accept C# interpolated expressions directly:

```csharp
int orderId = 42;
var values = new Dictionary<string, LogValue>
{
    ["order_id"] = LogValue.Integer(orderId)
};
logger.Info($"Order {orderId:D4} accepted", values);
logger.Info("order.accepted", $"Order {orderId} accepted", values);
logger.Write(CoreLogLevel.Warning, $"Order {orderId} will retry", new LogEventData
{
    EventName = "order.retry",
    Attributes = values,
    OperationId = "operation-42",
    Exception = error
});
```

`Info`, `Trace`, `Debug`, `Warn`, `Error` and `Critical` retain their existing names and ordinary string, named-message, event-data and lazy overloads. New handler overloads support an interpolated message alone, a message with explicit attributes, and a named event with optional attributes. The general `Write` method additionally accepts rich event data. The unnamed event name remains `log.message`.

The compiler supplies a `LogInterpolatedStringHandler` for direct interpolated arguments. Public handler parameters use `ref` to preserve existing target-typed `new()` event-data calls. Normal interpolation call sites need no `ref` keyword. Manually constructed handler variables use `ref` when passed to a logger. The handler is a regular struct so an interpolation expression can contain `await`.

## Formatting and attributes

Use C# expressions, alignment, escaped braces, raw interpolated strings and .NET format specifiers. Built-in scalar formatting uses invariant culture, giving stable logging text independently of caller and worker locale. Interpolated nulls produce empty text. Format validity remains type dependent; a compiler-valid expression can still encounter a runtime `FormatException`.

`values` supplies structured attributes. It does not bind interpolation expressions, and interpolation does not derive attribute names from variable names. Explicit attributes keep their existing precedence over scope attributes. Generated `message_template` metadata uses positional items such as `Order {0:D4} accepted`; it does not promise preservation of source expression names. A previously formatted string variable remains an ordinary message.

The rich `Write(level, message, context)` overload requires empty `context.Message` and absent `context.MessageTemplate`. Conflicting supplied text returns `InvalidEvent` with `ConflictingInterpolationInput`. The `context` name keeps `data:` unique to the existing `Write(level, data)` API and its named-argument compatibility controls.

## Caller and consumer ownership

Expressions execute in lexical order on the caller, including when the logging level is disabled or the runtime is stopped. This API does not skip arbitrary expression side effects. Applications can use `IsEnabled` or the existing lazy API when evaluation must be conditional. Exceptions raised while evaluating a C# expression occur before the logger method and retain ordinary C# behavior.

The handler captures strings and spans into bounded immutable text. It copies built-in values into its own boxes: Boolean, integer types including native-sized and 128-bit integers, Half/Single/Double/Decimal, DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan and Guid. This includes inputs supplied through a caller-owned box. Characters become owned text. The consumer formats these built-in values after claiming the event; no mutable caller argument or borrowed span crosses publication.

Other types, including enums and user-defined formatters, are formatted once on the caller through their standard `IFormattable` or `ToString` behavior using invariant culture. Only the bounded resulting text is retained. Recoverable formatter failures return `InvalidEvent` with `InterpolationCaptureFailed` when the call reaches capture. User formatter execution and allocation remain application work outside the ring's publication bound.

This caller-side category also includes framework types outside the listed scalar set, such as `BigInteger` and `NFloat`. Their formatting can allocate intermediate text before the retained result is bounded.

Freezing copies the handler's bounded parts array. Later manual changes to a handler cannot alter an already published occurrence. Invalid Unicode produces `InvalidUnicode`, including invalid text beyond a span's retained prefix.

## Resource limits and delivery

Capture retains at most 1,024 parts and a total budget of 8,192 Unicode scalars across captured literal, string and format text. Format strings longer than 256 UTF-16 characters omit that formatted part and mark capture truncated. For the listed owned built-in scalars, standard numeric precision greater than 8,192 also omits the part; the numeric precision guard does not reinterpret other framework or user formatters. Additional parts beyond the limit are omitted. Compiler size hints never determine allocation size.

Consumer output has the existing 8,192-scalar message cap. Alignment is calculated without integer overflow and padding is limited to remaining output capacity. Built-in `TryFormat` uses a fixed 16,384-character destination; values that cannot fit are omitted and marked truncated. The precision and format-length guards also bound work performed inside .NET numeric formatting, which can construct intermediate output before copying into a span. Encoded JSONL and human output still obey their actual configured byte limits and escaping rules; the default entry limit is 65,536 bytes including LF.

Capture-time omissions appear in `LogWriteResult.IsTruncated`. Later rendering and byte-projection omissions appear in existing diagnostics and flush/stop observations. `Accepted` means frontend admission. A runtime format failure is counted in `Incoming.Failed`, leaves the writer usable for later events and makes covered lifecycle observations report loss.

Rendering runs once before processing interceptors and initial backend byte projection, including when no processing policy is configured. Interceptors see the rendered message and retain authority over redaction; no later interpolation reconstructs removed content. As with supplied messages, removing an attribute alone does not remove text already present in the message.

The existing ring, P01 replacement, captured-batch flush and shared stop coordination apply. Evicted or abandoned waiting occurrences need no rendering. Consumer rendering is part of the dispatch covered by existing lifecycle boundaries. Internal output-only qualification seams format synchronously and return `InvalidEvent/InterpolationFormatFailed` for an invalid built-in format.

Recompiling direct interpolated arguments selects the handler overloads, including their invariant culture and generated `message_template` metadata. Expression trees cannot contain handler conversions; explicitly convert an interpolated argument to `string` to select the existing ordinary-message overload. That form retains ordinary string formatting and emits no generated template metadata.

See the [portable behavior record](../../../contracts/logging/v1/interpolation-contract.json), [acceptance bindings](../../../conformance/logging/interpolation-acceptance.json) and [execution record](../../../planning/logging/template-api/README.md).
