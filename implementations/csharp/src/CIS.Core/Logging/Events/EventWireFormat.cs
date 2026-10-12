using System.Globalization;
using System.Text.Json;
using CIS.Logging.Exceptions;

namespace CIS.Logging.Events;

// One v1 JSON format; measurement includes delimiters, the actual string encoder and final LF.
internal static class EventWireFormat
{
    internal static int PropertySize(string name, int valueBytes) => name.Length + 4 + valueBytes;
    internal static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    internal static int Size(CapturedEventFacts facts, int limit)
    {
        int size = 2 + PropertySize("schema_version", 1)
            + PropertySize("timestamp", JsonTextBytes.Size(Timestamp(facts.TimestampUtc), limit))
            + PropertySize("status", JsonTextBytes.Size(facts.Status, limit))
            + PropertySize("event_name", JsonTextBytes.Size(facts.EventName, limit))
            + PropertySize("message", JsonTextBytes.Size(facts.Message, limit))
            + PropertySize("service", JsonTextBytes.Size(facts.Service, limit))
            + PropertySize("env", JsonTextBytes.Size(facts.Environment, limit))
            + PropertySize("version", JsonTextBytes.Size(facts.Version, limit))
            + PropertySize("component", JsonTextBytes.Size(facts.Component, limit));
        if (size > limit) { return limit + 1; }
        size += PropertySize("resource", ValueSize(facts.Resource, limit));
        if (size > limit) { return limit + 1; }
        size += PropertySize("attributes", ValueSize(facts.Attributes, limit));
        if (facts.OperationId is { } operation) { size += PropertySize("operation_id", JsonTextBytes.Size(operation, limit)); }
        if (facts.TraceId is { } trace) { size += PropertySize("trace_id", JsonTextBytes.Size(trace, limit)); }
        if (facts.SpanId is { } span) { size += PropertySize("span_id", JsonTextBytes.Size(span, limit)); }
        if (size > limit) { return limit + 1; }
        if (facts.Exception is { } error) { size += PropertySize("exception", ExceptionSize(error, limit)); }
        if (facts.MessageTemplate is { } template) { size += PropertySize("message_template", JsonTextBytes.Size(template, limit)); }
        if (facts.IsTruncated) { size += PropertySize("truncated", 4); }
        // Each property includes its leading comma; remove the first comma and add LF (net zero).
        return size > limit ? limit + 1 : size;
    }

    internal static int ValueSize(LogValue value, int limit)
    {
        switch (value.Kind)
        {
            case LogValueKind.Null: return 4;
            case LogValueKind.Boolean: return value.Scalar.Boolean ? 4 : 5;
            case LogValueKind.Integer: return value.Scalar.Integer.ToString(CultureInfo.InvariantCulture).Length;
            case LogValueKind.Number: return value.Scalar.Number.ToString("R", CultureInfo.InvariantCulture).Length;
            case LogValueKind.String:
            case LogValueKind.IntegerString:
            case LogValueKind.DecimalString:
            case LogValueKind.Uuid:
            case LogValueKind.Timestamp: return JsonTextBytes.Size(value.Scalar.Text!, limit);
            case LogValueKind.Array:
                int arraySize = 2;
                foreach (LogValue item in value.ArrayItems)
                {
                    arraySize += (arraySize == 2 ? 0 : 1) + ValueSize(item, limit);
                    if (arraySize > limit) { return limit + 1; }
                }
                return arraySize;
            case LogValueKind.Map:
                int mapSize = 2;
                foreach ((string key, LogValue item) in value.MapProperties)
                {
                    mapSize += (mapSize == 2 ? 0 : 1) + JsonTextBytes.Size(key, limit) + 1 + ValueSize(item, limit);
                    if (mapSize > limit) { return limit + 1; }
                }
                return mapSize;
            default: throw new InvalidOperationException("Unsupported owned log value.");
        }
    }

    internal static int ExceptionSize(CapturedException error, int limit)
    {
        int size = 1 + PropertySize("type", JsonTextBytes.Size(error.Type, limit))
            + PropertySize("message", JsonTextBytes.Size(error.Message, limit));
        if (error.StackTrace is { } stack) { size += PropertySize("stack_trace", JsonTextBytes.Size(stack, limit)); }
        if (size > limit) { return limit + 1; }
        if (error.InnerExceptions.Count > 0)
        {
            size += PropertySize("inner_exceptions", 2);
            for (int i = 0; i < error.InnerExceptions.Count; i++)
            {
                size += (i == 0 ? 0 : 1) + ExceptionSize(error.InnerExceptions[i], limit);
                if (size > limit) { return limit + 1; }
            }
        }
        if (error.IsTruncated) { size += PropertySize("truncated", 4); }
        return size > limit ? limit + 1 : size;
    }

    internal static void Write(Utf8JsonWriter writer, CapturedEventFacts facts)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schema_version", facts.SchemaVersion);
        Text(writer, "timestamp", Timestamp(facts.TimestampUtc));
        Text(writer, "status", facts.Status);
        Text(writer, "event_name", facts.EventName);
        Text(writer, "message", facts.Message);
        Text(writer, "service", facts.Service);
        Text(writer, "env", facts.Environment);
        Text(writer, "version", facts.Version);
        Text(writer, "component", facts.Component);
        writer.WritePropertyName("resource"); WriteValue(writer, facts.Resource);
        writer.WritePropertyName("attributes"); WriteValue(writer, facts.Attributes);
        if (facts.OperationId is { } operation) { Text(writer, "operation_id", operation); }
        if (facts.TraceId is { } trace) { Text(writer, "trace_id", trace); }
        if (facts.SpanId is { } span) { Text(writer, "span_id", span); }
        if (facts.Exception is { } error) { writer.WritePropertyName("exception"); WriteException(writer, error); }
        if (facts.MessageTemplate is { } template) { Text(writer, "message_template", template); }
        if (facts.IsTruncated) { writer.WriteBoolean("truncated", true); }
        writer.WriteEndObject();
    }

    internal static void WriteValue(Utf8JsonWriter writer, LogValue value)
    {
        switch (value.Kind)
        {
            case LogValueKind.Null: writer.WriteNullValue(); break;
            case LogValueKind.Boolean: writer.WriteBooleanValue(value.Scalar.Boolean); break;
            case LogValueKind.Integer: writer.WriteNumberValue(value.Scalar.Integer); break;
            case LogValueKind.Number: writer.WriteNumberValue(value.Scalar.Number); break;
            case LogValueKind.Array:
                writer.WriteStartArray();
                foreach (LogValue child in value.ArrayItems) { WriteValue(writer, child); }
                writer.WriteEndArray(); break;
            case LogValueKind.Map:
                writer.WriteStartObject();
                foreach ((string key, LogValue child) in value.MapProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(JsonEncodedText.Encode(key, JsonTextBytes.Encoder)); WriteValue(writer, child);
                }
                writer.WriteEndObject(); break;
            default: writer.WriteStringValue(JsonEncodedText.Encode(value.Scalar.Text!, JsonTextBytes.Encoder)); break;
        }
    }

    private static void WriteException(Utf8JsonWriter writer, CapturedException error)
    {
        writer.WriteStartObject();
        Text(writer, "type", error.Type); Text(writer, "message", error.Message);
        if (error.StackTrace is { } stack) { Text(writer, "stack_trace", stack); }
        if (error.InnerExceptions.Count > 0)
        {
            writer.WriteStartArray("inner_exceptions");
            foreach (CapturedException child in error.InnerExceptions) { WriteException(writer, child); }
            writer.WriteEndArray();
        }
        if (error.IsTruncated) { writer.WriteBoolean("truncated", true); }
        writer.WriteEndObject();
    }

    private static void Text(Utf8JsonWriter writer, string name, string value)
    {
        writer.WritePropertyName(name); writer.WriteStringValue(JsonEncodedText.Encode(value, JsonTextBytes.Encoder));
    }
}
