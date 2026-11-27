using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CIS.Messaging;

namespace CIS.Management.Wire;

/// <summary>Fixed scheduler/catalog DTO framing; no schema interpreter, provider, authorization or runtime work.</summary>
public static partial class ManagementSchedulerWire
{
    private static readonly ManagementSchedulerWireLimits Defaults = new();
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = Options();

    /// <summary>Encodes one declared closed request or fixed command fragment.</summary>
    public static byte[] EncodeRequest<T>(T value, ManagementSchedulerWireLimits? limits = null) where T : class
        => Encode(value, true, limits ?? Defaults);
    /// <summary>Encodes one declared additive response or fixed portable fragment.</summary>
    public static byte[] EncodeResponse<T>(T value, ManagementSchedulerWireLimits? limits = null) where T : class
        => Encode(value, false, limits ?? Defaults);
    /// <summary>Decodes expected malformed request data to null, without executing its contents.</summary>
    public static T? DecodeRequest<T>(ReadOnlyMemory<byte> bytes, ManagementSchedulerWireLimits? limits = null) where T : class
        => Decode<T>(bytes, true, limits ?? Defaults, out _);
    /// <summary>Decodes a closed request and retains its finite expected failure class.</summary>
    public static T? DecodeRequest<T>(ReadOnlyMemory<byte> bytes, out ManagementWireFailure failure,
        ManagementSchedulerWireLimits? limits = null) where T : class
        => Decode<T>(bytes, true, limits ?? Defaults, out failure);
    /// <summary>Decodes an additive response, preserving unknown values without claiming success.</summary>
    public static T? DecodeResponse<T>(ReadOnlyMemory<byte> bytes, ManagementSchedulerWireLimits? limits = null) where T : class
        => Decode<T>(bytes, false, limits ?? Defaults, out _);
    /// <summary>Decodes an additive response and retains its finite expected failure class.</summary>
    public static T? DecodeResponse<T>(ReadOnlyMemory<byte> bytes, out ManagementWireFailure failure,
        ManagementSchedulerWireLimits? limits = null) where T : class
        => Decode<T>(bytes, false, limits ?? Defaults, out failure);

    /// <summary>Checks a fixed intent against the selected resource; it is not permission to execute it.</summary>
    public static bool IsStateIntent(string intent, ManagementSchedulerStateTarget target) => target switch
    {
        ManagementSchedulerStateTarget.Trigger => intent is "enable" or "disable" or "retire" or "resume",
        ManagementSchedulerStateTarget.Task => intent is "enable" or "disable" or "retire" or "activate" or "resume",
        ManagementSchedulerStateTarget.Run => intent == "cancel",
        _ => false
    };

    /// <summary>Uses the same real state-command codec with the actual route's additional intent restriction.</summary>
    public static ManagementSchedulerStateCommand? DecodeStateCommand(ReadOnlyMemory<byte> bytes,
        ManagementSchedulerStateTarget target, ManagementSchedulerWireLimits? limits = null)
    {
        ManagementSchedulerStateCommand? command = DecodeRequest<ManagementSchedulerStateCommand>(bytes, limits);
        return command is not null && IsStateIntent(command.Intent, target) ? command : null;
    }

    /// <summary>Checks the real task-preview route context; this does not bind a particular submitted draft or authorize publication.</summary>
    public static bool IsTaskPreview(ManagementSchedulerTaskPreview preview, bool existingTask = false)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return !preview.Valid || preview.Definition is not null && preview.DraftDigest is not null
            && ManagementConfigurationWire.IsSha256(preview.DraftDigest)
            && preview.TriggerETag is not null && ManagementWire.IsStrongETag(preview.TriggerETag)
            && preview.Preconditions is not null
            && preview.Preconditions.Contains(preview.Definition.JobConfiguration)
            && (preview.Definition.TriggerConfiguration is null || preview.Preconditions.Contains(preview.Definition.TriggerConfiguration))
            && (!existingTask || preview.TaskETag is not null && ManagementWire.IsStrongETag(preview.TaskETag));
    }

    /// <summary>Checks the pure calendar-preview route's absence of invented task context and bounded occurrence count.</summary>
    public static bool IsCalendarPreview(ManagementSchedulerTaskPreview preview, int maximumCount = 100)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return maximumCount is >= 1 and <= 100 && preview.NextOccurrencesUtc.Count <= maximumCount
            && preview.Definition is null && preview.DraftDigest is null && preview.TriggerETag is null
            && preview.TaskETag is null && preview.Preconditions is null && preview.AffectedTasks.Count == 0;
    }

    private static byte[] Encode<T>(T value, bool request, ManagementSchedulerWireLimits limits) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        Supported(typeof(T), request);
        try
        {
            using var output = new BoundedOutput(limits.MaximumBodyBytes);
            JsonSerializer.Serialize(output, value, Json);
            byte[] bytes = output.ToArray();
            using JsonDocument document = Document(bytes, limits);
            Value(typeof(T), document.RootElement, request, limits);
            return bytes;
        }
        catch (Exception error) when (error is DataFailure or JsonException or EncoderFallbackException)
        {
            throw new ArgumentException("Supply a complete valid bounded scheduler value.", nameof(value));
        }
    }

    private static T? Decode<T>(ReadOnlyMemory<byte> bytes, bool request, ManagementSchedulerWireLimits limits,
        out ManagementWireFailure failure) where T : class
    {
        Supported(typeof(T), request);
        failure = ManagementWireFailure.Invalid;
        try
        {
            using JsonDocument document = Document(bytes, limits);
            Value(typeof(T), document.RootElement, request, limits);
            T? value = document.RootElement.Deserialize<T>(Json);
            if (value is null)
            {
                return null;
            }
            failure = ManagementWireFailure.None;
            return (T)Freeze(value);
        }
        catch (DataFailure error)
        {
            failure = error.Failure;
            return null;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static JsonDocument Document(ReadOnlyMemory<byte> bytes, ManagementSchedulerWireLimits limits)
    {
        Require(bytes.Length <= limits.MaximumBodyBytes, ManagementWireFailure.TooLarge);
        Require(MessageWire.ValidateJson(bytes.Span, limits.MaximumJsonDepth));
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaximumJsonDepth });
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 32,
            PropertyNameCaseInsensitive = false
        };
        options.Converters.Add(new StrictStringConverter());
        return options;
    }

    private sealed class StrictStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            Require(value.Length <= ManagementSchedulerLimits.MaximumHttpBytes
                && Utf8.GetByteCount(value) <= ManagementSchedulerLimits.MaximumHttpBytes, ManagementWireFailure.TooLarge);
            writer.WriteStringValue(value);
        }
    }

    private sealed class BoundedOutput(int maximum) : MemoryStream(Math.Min(maximum, 4096))
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Admit(count);
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Admit(buffer.Length);
            base.Write(buffer);
        }
        public override void WriteByte(byte value)
        {
            Admit(1);
            base.WriteByte(value);
        }
        private void Admit(int count) => Require(count <= maximum - Length, ManagementWireFailure.TooLarge);
    }

    private static void Require(bool valid, ManagementWireFailure failure = ManagementWireFailure.Invalid)
    {
        if (!valid)
        {
            throw new DataFailure(failure);
        }
    }

    private sealed class DataFailure(ManagementWireFailure failure) : Exception
    {
        internal ManagementWireFailure Failure { get; } = failure;
    }
}
