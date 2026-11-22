using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CIS.Messaging;

/// <summary>Finite framing failures, before any handler or canonical identity is admitted.</summary>
public enum MessageWireFailure
{
    /// <summary>A valid owned envelope was produced.</summary>
    None,
    /// <summary>The payload/metadata/whole-envelope size bound was exceeded.</summary>
    TooLarge,
    /// <summary>UTF-8, JSON, duplicate keys, depth or required-field shape is invalid.</summary>
    Invalid,
    /// <summary>The required envelope version is unsupported.</summary>
    UnsupportedVersion
}

/// <summary>Strict portable v1 framing, with exact retained bytes and no CLR type serialization.</summary>
public static class MessageWire
{
    private static readonly MessagingLimits DefaultLimits = new();

    /// <summary>Validates and encodes an envelope once, taking ownership before returning.</summary>
    public static EncodedMessage Encode(MessageEnvelope envelope, MessagingLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        MessagingLimits selected = limits ?? DefaultLimits;
        if (envelope.PayloadLength > selected.MaximumPayloadBytes
            || !ValidateJson(envelope.PayloadMemory.Span, selected.MaximumJsonDepth))
        {
            throw new ArgumentException("The payload violates the selected byte/depth/JSON contract.", nameof(envelope));
        }
        var buffer = new BoundedWireBuffer(selected.MaximumEnvelopeBytes);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("envelopeVersion", 1);
            writer.WriteString("messageId", envelope.MessageId.ToString("D"));
            writer.WriteString("kind", envelope.Kind.ToString().ToLowerInvariant());
            writer.WriteString("messageType", envelope.Contract.Name);
            writer.WriteNumber("schemaVersion", envelope.Contract.Version);
            writer.WriteString("producerApplication", envelope.ProducerApplication);
            writer.WriteString("producerEnvironment", envelope.ProducerEnvironment);
            writer.WriteString("createdAtUtc", envelope.CreatedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            writer.WritePropertyName("payload");
            writer.WriteRawValue(envelope.PayloadMemory.Span, skipInputValidation: true);
            if (envelope.RequestAttemptId is { } attempt)
            {
                writer.WriteString("requestAttemptId", attempt.ToString("D"));
            }
            if (envelope.ReplyRoute is { } route)
            {
                writer.WriteStartObject("replyRoute");
                writer.WriteString("application", route.Application);
                writer.WriteString("environment", route.Environment);
                writer.WriteString("endpointId", route.EndpointId.ToString("D"));
                writer.WriteEndObject();
            }
            if (envelope.InReplyToMessageId is { } original)
            {
                writer.WriteString("inReplyToMessageId", original.ToString("D"));
            }
            if (envelope.Operation is { } operation)
            {
                writer.WriteStartObject("operation");
                writer.WriteString("operationName", operation.OperationName);
                writer.WriteString("requestedScope", operation.RequestedScope);
                writer.WriteString("key", operation.Key);
                writer.WriteEndObject();
            }
            if (envelope.CorrelationId is { } correlation)
            {
                writer.WriteString("correlationId", correlation);
            }
            if (envelope.CausationId is { } cause)
            {
                writer.WriteString("causationId", cause.ToString("D"));
            }
            if (envelope.Headers.Count != 0)
            {
                writer.WriteStartObject("headers");
                foreach ((string key, string value) in envelope.Headers.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(key, value);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        byte[] owned = buffer.CopyWritten();
        EncodedMessage? encoded = null;
        if (!ValidateJson(owned, selected.MaximumJsonDepth + 2)
            || !TryReadOwned(owned, out encoded, out _, selected))
        {
            throw new ArgumentException("The envelope exceeds the selected framing contract.", nameof(envelope));
        }
        return encoded!;
    }

    /// <summary>Copies bounded valid bytes once; returned payload metadata slices that same owned buffer.</summary>
    public static bool TryDecode(ReadOnlyMemory<byte> bytes, out EncodedMessage? message,
        out MessageWireFailure failure, MessagingLimits? limits = null)
    {
        MessagingLimits selected = limits ?? DefaultLimits;
        message = null;
        failure = MessageWireFailure.Invalid;
        if (bytes.Length > selected.MaximumEnvelopeBytes)
        {
            failure = MessageWireFailure.TooLarge;
            return false;
        }
        if (!ValidateJson(bytes.Span, selected.MaximumJsonDepth + 2))
        {
            return false;
        }
        return TryReadOwned(bytes.ToArray(), out message, out failure, selected);
    }

    private static bool TryReadOwned(byte[] owned, out EncodedMessage? message, out MessageWireFailure failure, MessagingLimits selected)
    {
        message = null;
        failure = MessageWireFailure.Invalid;
        try
        {
            MessageEnvelope envelope = ReadEnvelope(owned, selected, out bool unsupported);
            if (unsupported)
            {
                failure = MessageWireFailure.UnsupportedVersion;
                return false;
            }
            if (envelope.PayloadLength > selected.MaximumPayloadBytes
                || owned.Length - envelope.PayloadLength > MessagingLimits.MaximumMetadataBytes)
            {
                failure = MessageWireFailure.TooLarge;
                return false;
            }
            message = new(owned, envelope);
            failure = MessageWireFailure.None;
            return true;
        }
        catch (UnsupportedEnvelopeException)
        {
            failure = MessageWireFailure.UnsupportedVersion;
            return false;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException
            or FormatException or OverflowException)
        {
            return false;
        }
    }

    // Retain at most two envelope capacities across all segments. Flatten once into the final owned wire.
    // Together with one owned codec payload, staging's four-envelope reservation covers every live buffer.
    private sealed class BoundedWireBuffer(int maximumEnvelopeBytes) : IBufferWriter<byte>
    {
        private readonly List<(byte[] Bytes, int Written)> completed = [];
        private byte[] current = [];
        private int written;
        private int totalWritten;
        private int allocated;

        public void Advance(int count)
        {
            if (count < 0 || count > current.Length - written)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            written += count;
            totalWritten += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return current.AsMemory(written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return current.AsSpan(written);
        }

        private void Ensure(int sizeHint)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
            int required = Math.Max(1, sizeHint);
            if (current.Length - written >= required)
            {
                return;
            }
            int remaining = checked(2 * maximumEnvelopeBytes) - allocated;
            if (required > remaining)
            {
                throw new ArgumentException("The encoded envelope exceeds its bounded writer capacity.");
            }
            int next = Math.Min(remaining, Math.Max(required, current.Length == 0 ? 256 : checked(2 * current.Length)));
            if (written != 0)
            {
                completed.Add((current, written));
            }
            current = new byte[next];
            allocated += next;
            written = 0;
        }

        internal byte[] CopyWritten()
        {
            if (totalWritten > maximumEnvelopeBytes)
            {
                throw new ArgumentException("The encoded envelope exceeds the selected framing limit.");
            }
            var owned = new byte[totalWritten];
            int position = 0;
            foreach ((byte[] bytes, int count) in completed)
            {
                bytes.AsSpan(0, count).CopyTo(owned.AsSpan(position));
                position += count;
            }
            current.AsSpan(0, written).CopyTo(owned.AsSpan(position));
            return owned;
        }
    }

    internal static bool ValidateJson(ReadOnlySpan<byte> bytes, int maximumDepth)
    {
        if (bytes.Length == 0)
        {
            return false;
        }
        try
        {
            _ = MessageText.Utf8.GetCharCount(bytes);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = maximumDepth });
            var objects = new Stack<HashSet<string>?>();
            bool hasValue = false;
            while (reader.Read())
            {
                hasValue = true;
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    objects.Push(new(StringComparer.Ordinal));
                }
                else if (reader.TokenType == JsonTokenType.StartArray)
                {
                    objects.Push(null);
                }
                else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
                {
                    objects.Pop();
                }
                else if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string name = reader.GetString()!;
                    _ = MessageText.Utf8.GetByteCount(name);
                    if (!objects.Peek()!.Add(name))
                    {
                        return false;
                    }
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    _ = MessageText.Utf8.GetByteCount(reader.GetString()!);
                }
            }
            return hasValue && reader.BytesConsumed == bytes.Length && objects.Count == 0;
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or EncoderFallbackException
            or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static MessageEnvelope ReadEnvelope(byte[] owned, MessagingLimits limits, out bool unsupported)
    {
        unsupported = false;
        var reader = new Utf8JsonReader(owned, new JsonReaderOptions { MaxDepth = limits.MaximumJsonDepth + 2 });
        Require(reader.Read() && reader.TokenType == JsonTokenType.StartObject);
        int? version = null;
        Guid? messageId = null;
        MessageKind? kind = null;
        string? contract = null;
        int? schema = null;
        string? application = null;
        string? environment = null;
        DateTimeOffset? created = null;
        ReadOnlyMemory<byte>? payload = null;
        Guid? attempt = null;
        MessageReplyRoute? reply = null;
        Guid? original = null;
        MessageOperationReference? operation = null;
        string? correlation = null;
        Guid? causation = null;
        Dictionary<string, string>? headers = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            Require(reader.TokenType == JsonTokenType.PropertyName);
            string name = reader.GetString()!;
            Require(reader.Read());
            switch (name)
            {
                case "envelopeVersion":
                    version = reader.GetInt32();
                    if (version != 1)
                    {
                        throw new UnsupportedEnvelopeException();
                    }
                    break;
                case "messageId":
                    messageId = ReadId(ref reader);
                    break;
                case "kind":
                    kind = reader.GetString() switch
                    {
                        "event" => MessageKind.Event,
                        "command" => MessageKind.Command,
                        "request" => MessageKind.Request,
                        "reply" => MessageKind.Reply,
                        _ => throw new JsonException("Unknown message kind.")
                    };
                    break;
                case "messageType":
                    contract = reader.GetString();
                    break;
                case "schemaVersion":
                    schema = reader.GetInt32();
                    break;
                case "producerApplication":
                    application = reader.GetString();
                    break;
                case "producerEnvironment":
                    environment = reader.GetString();
                    break;
                case "createdAtUtc":
                    string? timestamp = reader.GetString();
                    Require(timestamp is not null && HasTimestampShape(timestamp) && DateTimeOffset.TryParseExact(timestamp,
                        ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"], CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _));
                    created = DateTimeOffset.ParseExact(timestamp!,
                        ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"], CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                    break;
                case "payload":
                    int start = checked((int)reader.TokenStartIndex);
                    Require(reader.TrySkip());
                    int length = checked((int)reader.BytesConsumed - start);
                    payload = owned.AsMemory(start, length);
                    Require(ValidateJson(payload.Value.Span, limits.MaximumJsonDepth));
                    break;
                case "requestAttemptId":
                    attempt = ReadId(ref reader);
                    break;
                case "replyRoute":
                    reply = ReadReply(ref reader);
                    break;
                case "inReplyToMessageId":
                    original = ReadId(ref reader);
                    break;
                case "operation":
                    operation = ReadOperation(ref reader);
                    break;
                case "correlationId":
                    correlation = reader.GetString();
                    break;
                case "causationId":
                    causation = ReadId(ref reader);
                    break;
                case "headers":
                    headers = ReadHeaders(ref reader);
                    break;
                default:
                    throw new JsonException("Unknown v1 envelope field.");
            }
        }
        Require(version == 1 && messageId.HasValue && kind.HasValue && contract is not null && schema.HasValue
            && application is not null && environment is not null && created.HasValue && payload.HasValue);
        Require(!reader.Read());
        return new(messageId!.Value, kind!.Value, new(contract!, schema!.Value), application!, environment!, created!.Value,
            payload!.Value, attempt, reply, original, operation, correlation, causation, headers, true);
    }

    private static MessageReplyRoute ReadReply(ref Utf8JsonReader reader)
    {
        Require(reader.TokenType == JsonTokenType.StartObject);
        string? application = null;
        string? environment = null;
        Guid? endpoint = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            Require(reader.Read());
            switch (name)
            {
                case "application":
                    application = reader.GetString();
                    break;
                case "environment":
                    environment = reader.GetString();
                    break;
                case "endpointId":
                    endpoint = ReadId(ref reader);
                    break;
                default:
                    throw new JsonException("Unknown reply namespace field.");
            }
        }
        Require(application is not null && environment is not null && endpoint.HasValue);
        return new(application!, environment!, endpoint!.Value);
    }

    private static MessageOperationReference ReadOperation(ref Utf8JsonReader reader)
    {
        Require(reader.TokenType == JsonTokenType.StartObject);
        string? name = null;
        string? scope = null;
        string? key = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string property = reader.GetString()!;
            Require(reader.Read());
            switch (property)
            {
                case "operationName":
                    name = reader.GetString();
                    break;
                case "requestedScope":
                    scope = reader.GetString();
                    break;
                case "key":
                    key = reader.GetString();
                    break;
                default:
                    throw new JsonException("Unknown operation reference field.");
            }
        }
        Require(name is not null && scope is not null && key is not null);
        return new(name!, scope!, key!);
    }

    private static Dictionary<string, string> ReadHeaders(ref Utf8JsonReader reader)
    {
        Require(reader.TokenType == JsonTokenType.StartObject);
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            Require(reader.Read() && reader.TokenType == JsonTokenType.String);
            Require(headers.Count < MessagingLimits.MaximumHeaders);
            headers.Add(name, reader.GetString()!);
        }
        return headers;
    }

    private static Guid ReadId(ref Utf8JsonReader reader)
    {
        string? value = reader.GetString();
        Require(value is not null && Guid.TryParseExact(value, "D", out Guid parsed) && parsed != Guid.Empty);
        return Guid.ParseExact(value!, "D");
    }

    private static bool HasTimestampShape(string value)
    {
        if (value.Length == 20)
        {
            return true;
        }
        if (value.Length is < 22 or > 28 || value[19] != '.')
        {
            return false;
        }
        for (int index = 20; index < value.Length - 1; index++)
        {
            if (value[index] is < '0' or > '9')
            {
                return false;
            }
        }
        return true;
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new JsonException("Invalid required v1 envelope fields.");
        }
    }

    private sealed class UnsupportedEnvelopeException : Exception
    {
    }
}
