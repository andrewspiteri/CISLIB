# Logging template API design and plan

Preparation draft, 19 October 2026. The user subsequently authorized implementation using C# interpolation and handlers. See the [execution record](README.md) for current progress and the [implemented contract](../../../docs/modules/logging/interpolation.md) for finalized API and resource choices. Stage states below describe the original preparation point.

The user selected C# interpolated strings and .NET formatting under the existing method names, such as `Info`. This replaces the earlier proposal for a separate named-placeholder grammar and supersedes interpolation deferral for this next feature. Capture owns the input on the caller; the background consumer renders the message. The rotating frontend continues to queue, evict and hand off owned occurrences under its existing contract.

## Existing behavior

[LogEventData](../../../implementations/csharp/src/CIS.Core/Logging/LogEventData.cs) already supplies a message, an optional template, typed attributes, an exception and an operation identifier. [EventProjection](../../../implementations/csharp/src/CIS.Core/Logging/Events/EventProjection.cs) copies call-boundary facts and retains explicit attributes separately from merged scope attributes. A supplied template is currently metadata; it does not request message rendering.

[CoreLogger](../../../implementations/csharp/src/CIS.Core/Logging/CoreLogger.Convenience.cs) has message-only and named-message convenience methods. For example, `Info(string eventName, string message)` already assigns a meaning to two strings. The new API must preserve those bindings and the existing caller-side factory and provider-formatter behavior.

The frontend completion record covers F0 through F5. This API work remains separate from that frontend feature; the preserved BRD records the earlier direction in U14 and D04. The older logging roadmap remains paused.

## Proposed public API

Use C# interpolation directly at the call site:

```csharp
logger.Info($"Order {OrderId} accepted", values);
logger.Info("order.accepted", $"Order {OrderId} accepted", values);
```

Use the same approach under `Trace`, `Debug`, `Warn`, `Error`, `Critical` and the general `Write` API. The unnamed form uses the existing `log.message` event name. In these examples, `OrderId` is a C# expression in caller scope. The suggested role of `values` is additional structured attributes using the existing typed attribute model; the interpolated expression supplies its own value independently.

Use the compiler-supported interpolated-string handler extension point to capture owned arguments for consumer-side formatting. Its exact public type and overload signatures are T0 technical design work. A new handler overload must be selected for direct interpolated expressions while existing string and event-data calls retain their behavior. A string already formatted into a local variable remains an ordinary message.

The proposed rich event form carries exception and operation facts through the existing event input:

```csharp
logger.Write(CoreLogLevel.Warning, $"Order {OrderId} will retry", new LogEventData
{
    EventName = "order.retry",
    Attributes = values,
    Exception = error,
    OperationId = operationId
});
```

The interpolation argument supplies message content; the event input supplies metadata and attributes. Finalize how conflicting existing message fields are validated with this overload in T0. Interpolation does not automatically create a structured `OrderId` attribute; callers can supply that attribute explicitly in `values`.

Retained format metadata can use generated positional items, such as `Order {0} accepted`. Do not claim that ordinary interpolation preserves the original expression name `OrderId` or produces a named structured template automatically.

## C# and .NET formatting semantics

Use the existing language syntax for expressions, escaped braces, alignment, format specifiers and raw interpolated strings. Use .NET formatting behavior, including an empty string for an interpolated null. Missing C# variables and malformed interpolation syntax are compiler errors. No separate placeholder lookup, case-matching, missing-key fallback or map-to-placeholder binding is required. These semantics follow the [C# interpolation reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/tokens/interpolated).

Compiler validation does not validate every type-specific format string. An unsupported numeric format can still throw `FormatException` at runtime; contain recoverable rendering failures through the established backend failure path. See [.NET numeric formatting](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings).

Standard interpolation targeted at `string` constructs the message on the caller. The logging adapter uses the [interpolated-string handler pattern](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/interpolated-string-handler) to capture segments, values, alignment and format information for later rendering. The extension supplies ownership and logging integration; .NET supplies value-formatting behavior.

`FormattableString` is another compiler-supported representation, but its [arguments are objects](https://learn.microsoft.com/en-us/dotnet/api/system.formattablestring?view=net-10.0). Enqueuing it directly would not establish owned snapshots of mutable arguments. Any capture mechanism must satisfy the same ownership contract.

## Capture and consumer integration

1. The compiler evaluates interpolation expressions on the caller and passes literals and expression results to the handler. Expressions may execute caller code; their exceptions cannot be treated as errors raised inside the later logger method. Skipping evaluation when logging is disabled must follow the explicit handler short-circuit contract.
2. Capture owns the interpolation arguments and formatting metadata alongside scope, occurrence time, activity identifiers, attributes and exception facts. It retains no mutable caller argument, dictionary, exception or user formatter for later consumer invocation. T0 specifies supported value snapshots and caller-side handling of types that require user-defined formatting.
3. Mark the owned occurrence for consumer-side rendering. Explicit attributes remain structured context and do not supply interpolation bindings. Scope and explicit-attribute precedence stay as established.
4. The frontend publishes and evicts this owned occurrence exactly as it does today. Formatting and cache management add no work inside ring publication.
5. After a successful consumer claim, render once using .NET formatting before the processing pipeline's initial byte projection and interceptor callbacks. This step also runs when no processing callbacks are configured. T0 records the culture policy explicitly so the worker does not accidentally substitute its own culture.
6. Clear pending rendering in the resulting facts. Apply existing scalar and aggregate-byte projection, then continue through interceptors, filtering, routing, duplicate suppression and writers. Later interceptor changes remain authoritative; no subsequent interpolation can resurrect redacted values.

The processing view and all writers therefore see the same rendered message, retained template and bounded attributes. Existing redaction guidance still applies: changing only attributes does not redact a message that already contains those values.

Use no global template cache in the first implementation. Keep owned interpolation segments and argument snapshots bounded alongside the existing 8,192-scalar message/template, 1,024-node and depth-eight value limits. The default encoded-entry cap remains 65,536 bytes including LF. Qualify extreme alignment and numeric precision against bounded allocation and rendering work; formatting the complete oversized result and then clipping it is insufficient. The adapter must enforce logging limits around .NET formatting without defining another template language.

Caller capture and user collection access remain distinct from the bounded ring publication guarantee. Measure the new capture and consumer rendering costs separately from the existing ring measurements.

## Results and lifecycle

`Accepted` continues to mean frontend admission. It cannot synchronously promise a rendered message, backend delivery or the absence of later truncation. Capture-time truncation remains visible in the write result; consumer-time rendering and byte projection use the established backend truncation observations.

Evicted or abandoned occurrences never need template rendering. Rendered occurrences retain their original creation timestamp and frontend identity. Rendering completes inside the backend dispatch already covered by captured-batch flush and stop coordination; it introduces no separate worker, task, queue or flush boundary.

Recoverable backend formatting failures use the existing processing-failure path without recursive diagnostic logging. Legacy factories and Microsoft formatter callbacks remain on the caller, at most once when needed.

## Implementation plan

All stages below are not started.

| Stage | Work | Exit evidence |
| --- | --- | --- |
| T0 | Specify the C# handler adapter, overload shapes, owned value snapshots, culture, retained format metadata and separate structured attributes. Define matching contracts and C# reference signatures. | Compiler checks for handler selection, existing calls, null literals, constant and raw interpolations, named arguments, method groups, and mapped native acceptance scenarios. |
| T1 | Implement bounded argument capture and consumer-side integration with .NET formatting. | Literal expected-output cases for standard formatting, alignment, nulls, braces, repeated expressions, Unicode, culture, runtime format failures and allocation limits under extreme width/precision. |
| T2 | Add interpolation overloads under the existing method names and one consumer rendering step before processing. | Real runtime tests for owned arguments, caller dictionary mutation after return, per-event scope/activity ownership, defined handling of user formatters, and ordinary/provider-message preservation. |
| T3 | Qualify processing and lifecycle integration. | Real interceptor redaction, filtering/routing/duplicate behavior, both output formats and byte caps, eviction without rendering, a held consumer, flush dispatch and stop-deadline cases. |
| T4 | Update usage and contracts, record separate capture/rendering measurements, run the coherent Developer checkpoint and refresh graph context. | Native reports with source identity, acceptance reconciliation, public API review and current documentation. |

Add tests with the behavior they verify. Reuse existing public runtime, controlled-writer and lifecycle fixtures; formatting-helper tests alone do not qualify integration. Run focused native verification during each stage and the established Developer profile at T4. Release qualification, publication, external review and resuming the older roadmap remain separate work.

## Review points

Naming and syntax are settled: use existing method names with C# interpolation and .NET formatting. The remaining design work concerns the logging adapter's ownership, overload compatibility, culture, bounded resource use and failure integration. These are library integration requirements; a separate template grammar and dictionary-binding policy are no longer proposed. The suggested interpretation of the optional `values` argument is additional structured attributes.
