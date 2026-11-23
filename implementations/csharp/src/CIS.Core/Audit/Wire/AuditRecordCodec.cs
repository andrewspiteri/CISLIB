using System.Buffers;
using System.Globalization;
using System.Text.Json;
using CIS.Messaging;
using CIS.Security;

namespace CIS.Audit;

/// <summary>Finite common-envelope decoding failures, before any receiving authority is established.</summary>
public enum AuditWireFailure
{
    /// <summary>A valid portable record was decoded.</summary>
    None,
    /// <summary>The configured complete payload byte limit was exceeded.</summary>
    TooLarge,
    /// <summary>The JSON structure, Unicode, depth or required values are invalid.</summary>
    Invalid,
    /// <summary>The common auditVersion is unsupported.</summary>
    UnsupportedVersion
}

/// <summary>Strict Audit v1 JSON with exact original bytes and opaque application details.</summary>
public sealed class AuditRecordCodec : IMessageCodec<AuditRecord>
{
    private static readonly MessageContract AuditContract = new("cis.audit.record", 1);
    private readonly AuditLimits limits;

    /// <summary>Captures immutable complete-payload limits without runtime or network access.</summary>
    public AuditRecordCodec(AuditLimits? limits = null)
    {
        this.limits = limits ?? new();
    }

    /// <summary>Gets the stable existing Messaging event contract.</summary>
    public MessageContract Contract => AuditContract;

    /// <summary>Copies the exact original payload; it never rewrites a decoded record.</summary>
    public byte[] Encode(AuditRecord value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.OriginalMemory.Length > limits.MaximumPayloadBytes
            || !MessageWire.ValidateJson(value.OriginalMemory.Span, limits.MaximumJsonDepth))
        {
            throw new ArgumentException("The audit record exceeds the selected payload profile.", nameof(value));
        }
        return value.OriginalMemory.ToArray();
    }

    /// <summary>Decodes only the selected Messaging contract, without creating source eligibility.</summary>
    public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out AuditRecord? value)
    {
        value = null;
        return contract == Contract && TryDecode(json, out value, out _);
    }

    /// <summary>Validates common v1 facts and retains a private complete copy, including unknown details.</summary>
    public bool TryDecode(ReadOnlyMemory<byte> json, out AuditRecord? value, out AuditWireFailure failure)
    {
        value = null;
        failure = AuditWireFailure.Invalid;
        if (json.Length > limits.MaximumPayloadBytes)
        {
            failure = AuditWireFailure.TooLarge;
            return false;
        }
        byte[] owned = json.ToArray();
        if (!MessageWire.ValidateJson(owned, limits.MaximumJsonDepth))
        {
            return false;
        }
        return TryReadOwned(owned, limits, out value, out failure);
    }

    internal static AuditRecord EncodeCapture<T>(Guid id, AuditEventDefinition<T> definition, T details,
        AuditCaptureOptions options, AuditProducer producer, AuditActor actor, DateTimeOffset recordedAtUtc,
        AuditLimits limits, MessagingResourceBudget budget)
    {
        // Capture's existing-budget admission precedes this bounded buffer and its final owned copy.
        using var buffer = new CaptureBuffer(limits.MaximumPayloadBytes, budget, id);
        var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = limits.MaximumJsonDepth });
        try
        {
            writer.WriteStartObject();
            writer.WriteNumber("auditVersion", 1);
            writer.WriteString("auditId", id.ToString("D"));
            writer.WriteString("eventType", definition.EventType);
            writer.WriteNumber("eventVersion", definition.EventVersion);
            writer.WriteString("category", CategoryName(definition.Category));
            writer.WriteString("occurredAtUtc", Timestamp(options.OccurredAtUtc ?? recordedAtUtc));
            writer.WriteString("recordedAtUtc", Timestamp(recordedAtUtc));
            WriteProducer(writer, producer);
            WriteActor(writer, actor);
            writer.WriteString("outcome", OutcomeName(options.Outcome));
            if (options.Target is { } target)
            {
                WriteTarget(writer, target);
            }
            if (options.Reason is { } reason)
            {
                writer.WriteStartObject("reason");
                writer.WriteString("code", reason.Code);
                WriteOptionalString(writer, "text", reason.Text);
                writer.WriteEndObject();
            }
            if (options.OperationReference is { } operation)
            {
                writer.WriteStartObject("operationReference");
                writer.WriteString("operationName", operation.OperationName);
                writer.WriteString("scope", operation.Scope);
                writer.WriteString("key", operation.Key);
                writer.WriteEndObject();
            }
            WriteOptionalString(writer, "correlationId", options.CorrelationId);
            if (options.CausationAuditId is { } cause)
            {
                writer.WriteString("causationAuditId", cause.ToString("D"));
            }
            WriteOptionalString(writer, "domainEventId", options.DomainEventId);
            writer.WritePropertyName("details");
            definition.DetailsCodec.Write(writer, details);
            writer.WriteEndObject();
            writer.Flush();
        }
        catch
        {
            try
            {
                writer.Dispose();
            }
            catch
            {
                // Flushing partial invalid/oversized output during disposal cannot replace the
                // original selected-codec fault, cancellation or earlier admission refusal.
            }
            throw;
        }
        writer.Dispose();

        byte[] owned = buffer.CopyWritten();
        if (!MessageWire.ValidateJson(owned, limits.MaximumJsonDepth)
            || !TryReadOwned(owned, limits, out AuditRecord? value, out _))
        {
            throw new ArgumentException("The selected audit mapping did not produce a valid bounded v1 record.", nameof(details));
        }
        return value!;
    }

    private static bool TryReadOwned(byte[] owned, AuditLimits limits, out AuditRecord? value, out AuditWireFailure failure)
    {
        value = null;
        failure = AuditWireFailure.Invalid;
        try
        {
            using JsonDocument document = JsonDocument.Parse(owned, new JsonDocumentOptions { MaxDepth = limits.MaximumJsonDepth });
            JsonElement root = document.RootElement;
            Fields(root, ["auditVersion", "auditId", "eventType", "eventVersion", "category", "occurredAtUtc",
                "recordedAtUtc", "producer", "actor", "outcome", "target", "reason", "operationReference", "correlationId",
                "causationAuditId", "domainEventId", "details"],
                ["auditVersion", "auditId", "eventType", "eventVersion", "category", "occurredAtUtc", "recordedAtUtc",
                    "producer", "actor", "outcome", "details"]);
            if (root.GetProperty("auditVersion").GetInt32() != 1)
            {
                failure = AuditWireFailure.UnsupportedVersion;
                return false;
            }
            Guid id = ReadId(root.GetProperty("auditId"));
            string eventType = MessageText.Contract(String(root, "eventType"), "eventType");
            int eventVersion = root.GetProperty("eventVersion").GetInt32();
            ArgumentOutOfRangeException.ThrowIfLessThan(eventVersion, 1);
            AuditCategory category = ReadCategory(String(root, "category"));
            DateTimeOffset occurred = ReadTimestamp(String(root, "occurredAtUtc"));
            DateTimeOffset recorded = ReadTimestamp(String(root, "recordedAtUtc"));
            AuditProducer producer = ReadProducer(root.GetProperty("producer"));
            AuditActor actor = ReadActor(root.GetProperty("actor"));
            AuditOutcome outcome = ReadOutcome(String(root, "outcome"));
            AuditTarget? target = root.TryGetProperty("target", out JsonElement targetJson) ? ReadTarget(targetJson) : null;
            AuditReason? reason = root.TryGetProperty("reason", out JsonElement reasonJson) ? ReadReason(reasonJson) : null;
            AuditOperationReference? operation = root.TryGetProperty("operationReference", out JsonElement operationJson)
                ? ReadOperation(operationJson) : null;
            string? correlation = OptionalString(root, "correlationId");
            if (correlation is not null)
            {
                correlation = SecurityText.Bounded(correlation, 128, "correlationId");
            }
            Guid? cause = root.TryGetProperty("causationAuditId", out JsonElement causeJson) ? ReadId(causeJson) : null;
            string? domain = SecurityText.OptionalIdentifier(OptionalString(root, "domainEventId"), "domainEventId");
            Require(root.GetProperty("details").ValueKind == JsonValueKind.Object);
            (int offset, int length) = FindDetails(owned, limits.MaximumJsonDepth);
            value = new(owned, offset, length, id, eventType, eventVersion, category, occurred, recorded,
                producer, actor, outcome, target, reason, operation, correlation, cause, domain);
            failure = AuditWireFailure.None;
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException
            or FormatException or OverflowException)
        {
            return false;
        }
    }

    private static AuditProducer ReadProducer(JsonElement value)
    {
        Fields(value, ["application", "environment", "instance"], ["application", "environment"]);
        AuditInstance? instance = value.TryGetProperty("instance", out JsonElement selected) ? ReadInstance(selected) : null;
        return new(String(value, "application"), String(value, "environment"), instance);
    }

    private static AuditActor ReadActor(JsonElement value)
    {
        Fields(value, ["kind", "subject", "application", "environment", "profile", "authenticationMethod", "policyVersion",
            "instance", "humanSubject", "verifiedDelegation"], ["kind"]);
        AuditSubject? subject = value.TryGetProperty("subject", out JsonElement selectedSubject) ? ReadSubject(selectedSubject) : null;
        AuditInstance? instance = value.TryGetProperty("instance", out JsonElement selectedInstance) ? ReadInstance(selectedInstance) : null;
        AuditSubject? human = value.TryGetProperty("humanSubject", out JsonElement selectedHuman) ? ReadSubject(selectedHuman) : null;
        return new(ReadActorKind(String(value, "kind")), subject, OptionalString(value, "application"), OptionalString(value, "environment"),
            OptionalString(value, "profile"), OptionalString(value, "authenticationMethod"), OptionalString(value, "policyVersion"),
            instance, human, OptionalString(value, "verifiedDelegation"));
    }

    private static AuditSubject ReadSubject(JsonElement value)
    {
        Fields(value, ["issuer", "identifier"], ["issuer", "identifier"]);
        return new(String(value, "issuer"), String(value, "identifier"));
    }

    private static AuditInstance ReadInstance(JsonElement value)
    {
        Fields(value, ["kind", "reportedIdentifier", "verifiedIdentifier"], ["kind"]);
        var instance = new AuditInstance(OptionalString(value, "reportedIdentifier"), OptionalString(value, "verifiedIdentifier"));
        Require(String(value, "kind") == InstanceName(instance.Kind));
        return instance;
    }

    private static AuditTarget ReadTarget(JsonElement value)
    {
        Fields(value, ["application", "environment", "resourceType", "resourceIdentifier"], ["application", "environment"]);
        return new(String(value, "application"), String(value, "environment"), OptionalString(value, "resourceType"),
            OptionalString(value, "resourceIdentifier"));
    }

    private static AuditReason ReadReason(JsonElement value)
    {
        Fields(value, ["code", "text"], ["code"]);
        return new(String(value, "code"), OptionalString(value, "text"));
    }

    private static AuditOperationReference ReadOperation(JsonElement value)
    {
        Fields(value, ["operationName", "scope", "key"], ["operationName", "scope", "key"]);
        return new(String(value, "operationName"), String(value, "scope"), String(value, "key"));
    }

    private static void Fields(JsonElement value, string[] allowed, string[] required)
    {
        Require(value.ValueKind == JsonValueKind.Object);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            Require(Array.IndexOf(allowed, property.Name) >= 0);
        }
        foreach (string property in required)
        {
            Require(value.TryGetProperty(property, out _));
        }
    }

    private static string String(JsonElement value, string property)
    {
        string? selected = value.GetProperty(property).GetString();
        Require(selected is not null);
        return selected!;
    }

    private static string? OptionalString(JsonElement value, string property)
        => value.TryGetProperty(property, out _) ? String(value, property) : null;

    private static Guid ReadId(JsonElement value)
    {
        string? selected = value.GetString();
        Require(Guid.TryParseExact(selected, "D", out Guid parsed) && parsed != Guid.Empty);
        return parsed;
    }

    private static DateTimeOffset ReadTimestamp(string value)
    {
        bool shape = value.Length == 20 || value.Length is >= 22 and <= 28 && value[19] == '.'
            && value.AsSpan(20, value.Length - 21).IndexOfAnyExceptInRange('0', '9') < 0;
        Require(shape && value.EndsWith('Z'));
        return DateTimeOffset.ParseExact(value, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static (int Offset, int Length) FindDetails(byte[] owned, int maximumDepth)
    {
        var reader = new Utf8JsonReader(owned, new JsonReaderOptions { MaxDepth = maximumDepth });
        Require(reader.Read() && reader.TokenType == JsonTokenType.StartObject);
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool selected = reader.ValueTextEquals("details");
            Require(reader.Read());
            int start = checked((int)reader.TokenStartIndex);
            reader.Skip();
            if (selected)
            {
                return (start, checked((int)reader.BytesConsumed - start));
            }
        }
        throw new JsonException("The details object is required.");
    }

    private static void WriteProducer(Utf8JsonWriter writer, AuditProducer producer)
    {
        writer.WriteStartObject("producer");
        writer.WriteString("application", producer.Application);
        writer.WriteString("environment", producer.Environment);
        WriteInstance(writer, producer.Instance);
        writer.WriteEndObject();
    }

    private static void WriteActor(Utf8JsonWriter writer, AuditActor actor)
    {
        writer.WriteStartObject("actor");
        writer.WriteString("kind", ActorName(actor.Kind));
        WriteSubject(writer, "subject", actor.Subject);
        WriteOptionalString(writer, "application", actor.Application);
        WriteOptionalString(writer, "environment", actor.Environment);
        WriteOptionalString(writer, "profile", actor.Profile);
        WriteOptionalString(writer, "authenticationMethod", actor.AuthenticationMethod);
        WriteOptionalString(writer, "policyVersion", actor.PolicyVersion);
        WriteInstance(writer, actor.Instance);
        WriteSubject(writer, "humanSubject", actor.HumanSubject);
        WriteOptionalString(writer, "verifiedDelegation", actor.VerifiedDelegation);
        writer.WriteEndObject();
    }

    private static void WriteSubject(Utf8JsonWriter writer, string property, AuditSubject? subject)
    {
        if (subject is null)
        {
            return;
        }
        writer.WriteStartObject(property);
        writer.WriteString("issuer", subject.Issuer);
        writer.WriteString("identifier", subject.Identifier);
        writer.WriteEndObject();
    }

    private static void WriteInstance(Utf8JsonWriter writer, AuditInstance? instance)
    {
        if (instance is null)
        {
            return;
        }
        writer.WriteStartObject("instance");
        writer.WriteString("kind", InstanceName(instance.Kind));
        WriteOptionalString(writer, "reportedIdentifier", instance.ReportedIdentifier);
        WriteOptionalString(writer, "verifiedIdentifier", instance.VerifiedIdentifier);
        writer.WriteEndObject();
    }

    private static void WriteTarget(Utf8JsonWriter writer, AuditTarget target)
    {
        writer.WriteStartObject("target");
        writer.WriteString("application", target.Application);
        writer.WriteString("environment", target.Environment);
        WriteOptionalString(writer, "resourceType", target.ResourceType);
        WriteOptionalString(writer, "resourceIdentifier", target.ResourceIdentifier);
        writer.WriteEndObject();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string property, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(property, value);
        }
    }

    private static string Timestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static string CategoryName(AuditCategory value) => value switch
    {
        AuditCategory.Business => "business",
        AuditCategory.Administration => "administration",
        AuditCategory.Security => "security",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static AuditCategory ReadCategory(string value) => value switch
    {
        "business" => AuditCategory.Business,
        "administration" => AuditCategory.Administration,
        "security" => AuditCategory.Security,
        _ => throw new JsonException("Unsupported audit category.")
    };

    private static string OutcomeName(AuditOutcome value) => value switch
    {
        AuditOutcome.Accepted => "accepted",
        AuditOutcome.Succeeded => "succeeded",
        AuditOutcome.Rejected => "rejected",
        AuditOutcome.Failed => "failed",
        AuditOutcome.Canceled => "canceled",
        AuditOutcome.Unknown => "unknown",
        AuditOutcome.Allowed => "allowed",
        AuditOutcome.Denied => "denied",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static AuditOutcome ReadOutcome(string value) => value switch
    {
        "accepted" => AuditOutcome.Accepted,
        "succeeded" => AuditOutcome.Succeeded,
        "rejected" => AuditOutcome.Rejected,
        "failed" => AuditOutcome.Failed,
        "canceled" => AuditOutcome.Canceled,
        "unknown" => AuditOutcome.Unknown,
        "allowed" => AuditOutcome.Allowed,
        "denied" => AuditOutcome.Denied,
        _ => throw new JsonException("Unsupported audit outcome.")
    };

    private static string ActorName(AuditActorKind value) => value switch
    {
        AuditActorKind.User => "user",
        AuditActorKind.Application => "application",
        AuditActorKind.System => "system",
        AuditActorKind.Anonymous => "anonymous",
        AuditActorKind.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static AuditActorKind ReadActorKind(string value) => value switch
    {
        "user" => AuditActorKind.User,
        "application" => AuditActorKind.Application,
        "system" => AuditActorKind.System,
        "anonymous" => AuditActorKind.Anonymous,
        "unknown" => AuditActorKind.Unknown,
        _ => throw new JsonException("Unsupported audit actor.")
    };

    private static string InstanceName(AuditInstanceKind value) => value switch
    {
        AuditInstanceKind.NotProvided => "notProvided",
        AuditInstanceKind.Reported => "reported",
        AuditInstanceKind.Verified => "verified",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new JsonException("Invalid required audit v1 fields.");
        }
    }

    // JSON writer capacity hints can exceed the bytes ultimately written (especially for escaped
    // strings). Admit that actual finite capacity, without confusing a hint with payload size.
    private sealed class CaptureBuffer(int capacity, MessagingResourceBudget budget, Guid auditId) : IBufferWriter<byte>, IDisposable
    {
        private readonly List<(byte[] Bytes, int Written)> completed = [];
        private readonly List<IDisposable> extraAdmission = [];
        private readonly int scratchLimit = checked(6 * capacity + 8192);
        private byte[] current = [];
        private int allocated;
        private int reserved = checked(4 * capacity);
        private int written;
        private int totalWritten;

        public void Advance(int count)
        {
            if (count < 0 || count > current.Length - written)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (count > capacity - totalWritten)
            {
                throw new ArgumentException("The encoded audit record exceeds the selected payload limit.");
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

        internal byte[] CopyWritten()
        {
            Reserve(checked(allocated + totalWritten));
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

        public void Dispose()
        {
            foreach (IDisposable admission in extraAdmission)
            {
                admission.Dispose();
            }
            extraAdmission.Clear();
        }

        private void Ensure(int sizeHint)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
            int required = Math.Max(1, sizeHint);
            if (required <= current.Length - written)
            {
                return;
            }
            int remaining = scratchLimit - allocated;
            if (required > remaining)
            {
                throw new ArgumentException("The encoded audit record exceeds the selected payload capacity.");
            }
            int next = Math.Min(remaining, Math.Max(4096, required));
            Reserve(checked(allocated + next));
            if (written != 0)
            {
                completed.Add((current, written));
            }
            current = new byte[next];
            allocated += next;
            written = 0;
        }

        private void Reserve(int required)
        {
            int additional = required - reserved;
            if (additional <= 0)
            {
                return;
            }
            IDisposable? admission;
            try
            {
                if (!budget.TryAcquireBytes(additional, out admission))
                {
                    throw new AuditCaptureException(auditId, AuditCaptureFailure.Overloaded);
                }
            }
            catch (ArgumentOutOfRangeException error) when (error.ParamName == "bytes")
            {
                // An encoder hint larger than the whole existing native byte budget is also
                // a known nonblocking capacity refusal, never an implicit budget expansion.
                throw new AuditCaptureException(auditId, AuditCaptureFailure.Overloaded);
            }
            extraAdmission.Add(admission!);
            reserved = required;
        }
    }
}
