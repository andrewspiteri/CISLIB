using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CIS.Security;

namespace CIS.Messaging;

/// <summary>An explicit application-owned UTF-8 JSON DTO codec, without reflective CLR discriminators.</summary>
public interface IMessageCodec<T>
{
    /// <summary>Gets the stable current portable name/version captured at registration.</summary>
    MessageContract Contract
    {
        get;
    }
    /// <summary>Encodes once before staging. The runtime takes ownership of the returned bytes.</summary>
    byte[] Encode(T value);
    /// <summary>Decodes a registered representation or reports expected incompatibility without inventing a value.</summary>
    bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out T? value);
}

/// <summary>One immutable logical envelope, distinct from provider-owned delivery metadata.</summary>
public sealed class MessageEnvelope
{
    private readonly ReadOnlyMemory<byte> payload;

    /// <summary>Copies owned typed JSON and bounded metadata. Constructing producer metadata grants no authority.</summary>
    public MessageEnvelope(Guid messageId, MessageKind kind, MessageContract contract, string producerApplication,
        string producerEnvironment, DateTimeOffset createdAtUtc, ReadOnlyMemory<byte> payload,
        Guid? requestAttemptId = null, MessageReplyRoute? replyRoute = null, Guid? inReplyToMessageId = null,
        MessageOperationReference? operation = null, string? correlationId = null, Guid? causationId = null,
        IReadOnlyDictionary<string, string>? headers = null)
        : this(messageId, kind, contract, producerApplication, producerEnvironment, createdAtUtc, CopyBoundedPayload(payload),
            requestAttemptId, replyRoute, inReplyToMessageId, operation, correlationId, causationId, headers, false)
    {
    }

    internal MessageEnvelope(Guid messageId, MessageKind kind, MessageContract contract, string producerApplication,
        string producerEnvironment, DateTimeOffset createdAtUtc, ReadOnlyMemory<byte> payload,
        Guid? requestAttemptId, MessageReplyRoute? replyRoute, Guid? inReplyToMessageId,
        MessageOperationReference? operation, string? correlationId, Guid? causationId,
        IReadOnlyDictionary<string, string>? headers, bool validatedPayload)
    {
        MessageId = MessageText.Id(messageId, nameof(messageId));
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        ArgumentNullException.ThrowIfNull(contract);
        var producer = new CapabilityTarget(producerApplication, producerEnvironment);
        Kind = kind;
        Contract = contract;
        ProducerApplication = producer.Application;
        ProducerEnvironment = producer.Environment;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        if (payload.Length is < 1 or > MessagingLimits.MaximumSupportedPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }
        if (!validatedPayload && !MessageWire.ValidateJson(payload.Span, MessagingLimits.MaximumSupportedJsonDepth))
        {
            throw new ArgumentException("Supply strict UTF-8 JSON without duplicate keys or excessive depth.", nameof(payload));
        }
        this.payload = payload;
        RequestAttemptId = requestAttemptId is { } attempt ? MessageText.Id(attempt, nameof(requestAttemptId)) : null;
        InReplyToMessageId = inReplyToMessageId is { } original ? MessageText.Id(original, nameof(inReplyToMessageId)) : null;
        CausationId = causationId is { } cause ? MessageText.Id(cause, nameof(causationId)) : null;
        ReplyRoute = replyRoute;
        Operation = operation;
        CorrelationId = correlationId is null ? null : MessageText.Unicode(correlationId, 128, nameof(correlationId));
        Headers = CopyHeaders(headers);
        ValidateKind();
    }

    /// <summary>Gets the only supported envelope version.</summary>
    public int EnvelopeVersion => 1;
    /// <summary>Gets the original immutable logical identity.</summary>
    public Guid MessageId
    {
        get;
    }
    /// <summary>Gets Event, Command, Request or Reply.</summary>
    public MessageKind Kind
    {
        get;
    }
    /// <summary>Gets the registered portable payload name/version.</summary>
    public MessageContract Contract
    {
        get;
    }
    /// <summary>Gets claimed producer application metadata, never authentication.</summary>
    public string ProducerApplication
    {
        get;
    }
    /// <summary>Gets claimed producer environment metadata, never authentication.</summary>
    public string ProducerEnvironment
    {
        get;
    }
    /// <summary>Gets UTC creation metadata, never a commit or ordering timestamp.</summary>
    public DateTimeOffset CreatedAtUtc
    {
        get;
    }
    /// <summary>Gets the bounded payload byte length without allocating.</summary>
    public int PayloadLength => payload.Length;
    /// <summary>Gets a detached application-owned payload copy.</summary>
    public byte[] CopyPayload() => payload.ToArray();
    /// <summary>Gets the request-attempt identity, required for Request and Reply.</summary>
    public Guid? RequestAttemptId
    {
        get;
    }
    /// <summary>Gets structured requester routing, required only for Request.</summary>
    public MessageReplyRoute? ReplyRoute
    {
        get;
    }
    /// <summary>Gets the original request identity, required only for Reply.</summary>
    public Guid? InReplyToMessageId
    {
        get;
    }
    /// <summary>Gets untrusted operation-reference data for a registered durable command.</summary>
    public MessageOperationReference? Operation
    {
        get;
    }
    /// <summary>Gets optional bounded diagnostics, excluded from idempotency identity.</summary>
    public string? CorrelationId
    {
        get;
    }
    /// <summary>Gets an optional causing message identity.</summary>
    public Guid? CausationId
    {
        get;
    }
    /// <summary>Gets at most 32 copied bounded string headers with no authority.</summary>
    public IReadOnlyDictionary<string, string> Headers
    {
        get;
    }
    internal ReadOnlyMemory<byte> PayloadMemory => payload;

    private static byte[] CopyBoundedPayload(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length is < 1 or > MessagingLimits.MaximumSupportedPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }
        return payload.ToArray();
    }

    private void ValidateKind()
    {
        bool attemptRequired = Kind is MessageKind.Request or MessageKind.Reply;
        if (attemptRequired != RequestAttemptId.HasValue
            || (Kind == MessageKind.Request) != (ReplyRoute is not null)
            || (Kind == MessageKind.Reply) != InReplyToMessageId.HasValue
            || Kind == MessageKind.Event && Operation is not null)
        {
            throw new ArgumentException("The request/reply fields do not match this envelope kind.");
        }
    }

    internal static IReadOnlyDictionary<string, string> CopyHeaders(IReadOnlyDictionary<string, string>? values)
    {
        var copied = new Dictionary<string, string>(StringComparer.Ordinal);
        int bytes = 0;
        if (values is not null)
        {
            foreach ((string key, string value) in values)
            {
                string name = MessageText.Alias(key, nameof(values));
                string text = MessageText.Unicode(value, 1024, nameof(values));
                bytes = checked(bytes + MessageText.Utf8.GetByteCount(name) + MessageText.Utf8.GetByteCount(text));
                if (copied.Count >= MessagingLimits.MaximumHeaders || bytes > MessagingLimits.MaximumHeaderBytes
                    || !copied.TryAdd(name, text))
                {
                    throw new ArgumentException("Headers exceed their count/byte bounds or contain a duplicate name.", nameof(values));
                }
            }
        }
        return new ReadOnlyDictionary<string, string>(copied);
    }
}

/// <summary>One owned immutable wire body and transport digest. Retries reuse these exact bytes.</summary>
public sealed class EncodedMessage
{
    private readonly byte[] bytes;

    internal EncodedMessage(byte[] ownedBytes, MessageEnvelope envelope)
    {
        bytes = ownedBytes;
        Envelope = envelope;
        Digest = new(SHA256.HashData(bytes));
    }

    /// <summary>Gets immutable metadata and a payload slice owned by this same encoded buffer.</summary>
    public MessageEnvelope Envelope
    {
        get;
    }
    /// <summary>Gets the exact wire size to reserve before materialization or an adapter working copy.</summary>
    public int Length => bytes.Length;
    /// <summary>Gets the immutable transport digest, distinct from the Operations fingerprint.</summary>
    public MessageDigest Digest
    {
        get;
    }
    /// <summary>Gets a detached application-owned copy; module runtimes account their own copies before allocation.</summary>
    public byte[] CopyBytes() => bytes.ToArray();
    /// <summary>Copies into caller-owned already admitted storage without exposing mutable internal buffers.</summary>
    public void CopyTo(Memory<byte> destination)
    {
        if (destination.Length < bytes.Length)
        {
            throw new ArgumentException("The destination is smaller than the immutable envelope.", nameof(destination));
        }
        bytes.AsSpan().CopyTo(destination.Span);
    }

    internal ReadOnlyMemory<byte> BytesMemory => bytes;
}
