using System.Text.Json;
using CIS.Operations;

namespace CIS.Messaging;

internal enum MessageReplyDecode { Unmatched, Invalid, Valid }

// One explicit protocol payload on the registered response contract, without CLR discriminators.
internal static class MessageReplyProtocol
{
    internal static byte[] Encode<T>(MessageRequestDefinition definition, MessageDigest requestDigest, IMessageCodec<T> codec,
        MessageResponse<T> response, MessagingLimits limits)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(requestDigest);
        ArgumentNullException.ThrowIfNull(response);
        byte[]? value = null;
        if (response.Decision?.Decision == OperationBusinessDecision.Succeeded)
        {
            value = codec.Encode(response.Decision.Value!);
            if (value is null || value.Length > limits.MaximumPayloadBytes || !MessageWire.ValidateJson(value, limits.MaximumJsonDepth)
                || !codec.TryDecode(definition.ResponseContract, value, out _))
            {
                throw new InvalidOperationException("The response codec cannot produce its registered bounded representation.");
            }
        }
        // Utf8JsonWriter can request a 4 KiB segment for a much smaller value. Envelope-sized working
        // headroom honors that hint while the four-envelope response permit still covers every array.
        var buffer = new MessageWire.BoundedWireBuffer(limits.MaximumEnvelopeBytes);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("responseVersion", 1);
            writer.WriteString("requestAlias", definition.Alias);
            writer.WriteString("routeAlias", definition.RouteAlias);
            writer.WriteString("requestDigest", Hex(requestDigest));
            writer.WriteString("protocol", Name(response.Disposition));
            if (response.Decision is { } decision)
            {
                writer.WriteString("decision", decision.Decision == OperationBusinessDecision.Succeeded ? "succeeded" : "rejected");
                if (value is not null)
                {
                    writer.WritePropertyName("value");
                    writer.WriteRawValue(value, true);
                }
                else
                {
                    WriteError(writer, decision.Error!.Code, decision.Error.Message);
                }
            }
            else if (response.Error is { } error)
            {
                WriteError(writer, error.Code, error.Message);
            }
            writer.WriteEndObject();
        }
        byte[] encoded = buffer.CopyWritten();
        if (encoded.Length > limits.MaximumPayloadBytes)
        {
            throw new ArgumentException("The response wrapper exceeds the selected payload limit.", nameof(response));
        }
        return encoded;
    }

    internal static MessageReplyDecode TryDecode<T>(EncodedMessage reply, MessageRequestReference expected,
        MessageRequestDefinition definition, IMessageCodec<T> codec, MessagingLimits limits, out MessageResponse<T>? response)
    {
        response = null;
        if (reply.Envelope.PayloadLength > limits.MaximumPayloadBytes
            || !MessageWire.ValidateJson(reply.Envelope.PayloadMemory.Span, limits.MaximumJsonDepth))
        {
            return MessageReplyDecode.Unmatched;
        }
        using JsonDocument document = JsonDocument.Parse(reply.Envelope.PayloadMemory,
            new JsonDocumentOptions { MaxDepth = limits.MaximumJsonDepth });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !String(root, "requestAlias", out string? alias) || alias != definition.Alias
            || !String(root, "routeAlias", out string? route) || route != definition.RouteAlias
            || !String(root, "requestDigest", out string? digest) || digest != Hex(expected.Receipt.Digest))
        {
            return MessageReplyDecode.Unmatched;
        }
        if (root.EnumerateObject().Any(property => property.Name is not ("responseVersion" or "requestAlias" or "routeAlias"
                or "requestDigest" or "protocol" or "decision" or "value" or "error"))
            || !root.TryGetProperty("responseVersion", out JsonElement version) || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out int number) || number != 1
            || !String(root, "protocol", out string? protocol) || !Disposition(protocol!, out MessageProtocolDisposition disposition)
            || reply.Envelope.Contract != expected.ResponseContract || codec.Contract != definition.ResponseContract)
        {
            return MessageReplyDecode.Invalid;
        }
        bool hasDecision = root.TryGetProperty("decision", out JsonElement decision);
        bool hasValue = root.TryGetProperty("value", out JsonElement encodedValue);
        bool hasError = root.TryGetProperty("error", out JsonElement encodedError);
        MessageError? error = null;
        if (hasError && !ReadError(encodedError, out error))
        {
            return MessageReplyDecode.Invalid;
        }
        if (disposition != MessageProtocolDisposition.Completed)
        {
            if (hasDecision || hasValue)
            {
                return MessageReplyDecode.Invalid;
            }
            response = new(disposition, error: error);
            return MessageReplyDecode.Valid;
        }
        if (!hasDecision || decision.ValueKind != JsonValueKind.String)
        {
            return MessageReplyDecode.Invalid;
        }
        if (decision.GetString() == "rejected" && hasError && !hasValue)
        {
            response = new(disposition, OperationDecision<T>.Reject(error!.Code, error.Message));
            return MessageReplyDecode.Valid;
        }
        if (decision.GetString() != "succeeded" || !hasValue || hasError)
        {
            return MessageReplyDecode.Invalid;
        }
        try
        {
            byte[] json = MessageText.Utf8.GetBytes(encodedValue.GetRawText());
            if (!codec.TryDecode(expected.ResponseContract, json, out T? value))
            {
                return MessageReplyDecode.Invalid;
            }
            response = new(disposition, OperationDecision<T>.Succeed(value));
            return MessageReplyDecode.Valid;
        }
        catch (Exception failure) when (failure is JsonException or FormatException)
        {
            return MessageReplyDecode.Invalid;
        }
    }

    private static string Hex(MessageDigest digest) => Convert.ToHexString(digest.Span).ToLowerInvariant();

    private static bool String(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = property.GetString();
        return value is not null;
    }

    private static void WriteError(Utf8JsonWriter writer, string code, string message)
    {
        writer.WriteStartObject("error");
        writer.WriteString("code", code);
        writer.WriteString("message", message);
        writer.WriteEndObject();
    }

    private static bool ReadError(JsonElement root, out MessageError? error)
    {
        error = null;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2
            || !String(root, "code", out string? code) || !String(root, "message", out string? message))
        {
            return false;
        }
        try
        {
            error = new(code!, message!);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Name(MessageProtocolDisposition disposition) => disposition switch
    {
        MessageProtocolDisposition.Completed => "completed",
        MessageProtocolDisposition.KeyConflict => "keyConflict",
        MessageProtocolDisposition.ResultExpired => "resultExpired",
        MessageProtocolDisposition.Incompatible => "incompatible",
        MessageProtocolDisposition.Denied => "denied",
        MessageProtocolDisposition.OutcomeUnknown => "outcomeUnknown",
        MessageProtocolDisposition.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition))
    };

    private static bool Disposition(string name, out MessageProtocolDisposition disposition)
    {
        disposition = name switch
        {
            "completed" => MessageProtocolDisposition.Completed,
            "keyConflict" => MessageProtocolDisposition.KeyConflict,
            "resultExpired" => MessageProtocolDisposition.ResultExpired,
            "incompatible" => MessageProtocolDisposition.Incompatible,
            "denied" => MessageProtocolDisposition.Denied,
            "outcomeUnknown" => MessageProtocolDisposition.OutcomeUnknown,
            "unavailable" => MessageProtocolDisposition.Unavailable,
            _ => (MessageProtocolDisposition)(-1)
        };
        return Enum.IsDefined(disposition);
    }
}

internal sealed class MessageResponseAdmission(MessagingResourceBudget owner, IDisposable lease) : IDisposable
{
    private IDisposable? lease = lease;
    internal void Require(MessagingResourceBudget expected)
    {
        if (!ReferenceEquals(owner, expected) || Volatile.Read(ref lease) is null)
        {
            throw new InvalidOperationException("The response must retain its exact pre-effect byte reservation.");
        }
    }
    public void Dispose() => Interlocked.Exchange(ref lease, null)?.Dispose();
}
