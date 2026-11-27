using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CIS.Messaging;

namespace CIS.Management.Wire;

/// <summary>Strict finite configuration DTO codec. It performs no I/O, authorization or runtime materialization.</summary>
public static partial class ManagementConfigurationWire
{
    private static readonly ManagementConfigurationWireLimits Defaults = new();
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = Options();

    /// <summary>Encodes a declared closed configuration request, preserving exact payload text.</summary>
    public static byte[] EncodeRequest<T>(T value, ManagementConfigurationWireLimits? limits = null) where T : class
        => Encode(value, true, limits ?? Defaults);

    /// <summary>Encodes a declared additive configuration response within the separate configuration profile.</summary>
    public static byte[] EncodeResponse<T>(T value, ManagementConfigurationWireLimits? limits = null) where T : class
        => Encode(value, false, limits ?? Defaults);

    /// <summary>Decodes a declared closed request; expected malformed data returns null.</summary>
    public static T? DecodeRequest<T>(ReadOnlyMemory<byte> bytes, ManagementConfigurationWireLimits? limits = null) where T : class
        => Decode<T>(bytes, true, limits ?? Defaults, out _);

    /// <summary>Decodes a declared closed request and exposes the expected finite failure.</summary>
    public static T? DecodeRequest<T>(ReadOnlyMemory<byte> bytes, out ManagementWireFailure failure,
        ManagementConfigurationWireLimits? limits = null) where T : class
        => Decode<T>(bytes, true, limits ?? Defaults, out failure);

    /// <summary>Decodes an additive response, retaining exact strings and explicitly unknown statuses.</summary>
    public static T? DecodeResponse<T>(ReadOnlyMemory<byte> bytes, ManagementConfigurationWireLimits? limits = null) where T : class
        => Decode<T>(bytes, false, limits ?? Defaults, out _);

    /// <summary>Decodes an additive response and exposes an expected finite failure.</summary>
    public static T? DecodeResponse<T>(ReadOnlyMemory<byte> bytes, out ManagementWireFailure failure,
        ManagementConfigurationWireLimits? limits = null) where T : class
        => Decode<T>(bytes, false, limits ?? Defaults, out failure);

    /// <summary>Validates trusted target identity before content, preserving only genuinely identified rejections.</summary>
    /// <remarks>Run inside the native content decoder's owned deadline. No content integrity work remains after return.</remarks>
    public static ManagementConfigurationSnapshotRead DecodeSnapshot(ReadOnlyMemory<byte> bytes,
        ManagementConfigurationSnapshotTarget expected, ManagementConfigurationWireLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ManagementConfigurationWireLimits selected = limits ?? Defaults;
        ValidateExpected(expected);
        ManagementConfigurationDesiredContext? desired = null;
        try
        {
            using JsonDocument document = Document(bytes, selected);
            JsonElement root = document.RootElement;
            Shape(root, false, ["desired", "eTag", "commonSource", "applicationSource"]);
            Desired(root.GetProperty("desired"));
            var candidateDesired = root.GetProperty("desired").Deserialize<ManagementConfigurationDesiredContext>(Json)!;
            Require(Matches(candidateDesired, expected), "configuration.target.mismatch");
            ETag(root, "eTag");
            desired = candidateDesired;
            // A valid identity header alone is not content acceptance. Only failures
            // after this fence may identify the rejected desired release/generation.
            SourceContent(root.GetProperty("commonSource"), selected, desired, "common");
            SourceContent(root.GetProperty("applicationSource"), selected, desired, "application");
            var snapshot = root.Deserialize<ManagementConfigurationSnapshot>(Json)!;
            return new(snapshot, desired, ManagementWireFailure.None);
        }
        catch (DataFailure error)
        {
            if (error.Code == "configuration.target.mismatch")
            {
                desired = null;
            }
            return new(null, desired, error.Failure, error.Code);
        }
        catch (JsonException)
        {
            return new(null, null, ManagementWireFailure.Invalid, "configuration.wire.invalid");
        }
    }

    /// <summary>Computes SHA-256 over exact well-formed decoded source JSON UTF-8, never a reserialization.</summary>
    public static string ComputePayloadSha256(string payloadJson, ManagementConfigurationWireLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        try
        {
            byte[] source = SourceBytes(payloadJson, limits ?? Defaults);
            return Convert.ToHexStringLower(SHA256.HashData(source));
        }
        catch (DataFailure)
        {
            throw new ArgumentException("Supply bounded valid exact source JSON.", nameof(payloadJson));
        }
    }

    /// <summary>Tests exact lowercase hexadecimal SHA-256 text.</summary>
    public static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static byte[] Encode<T>(T value, bool request, ManagementConfigurationWireLimits limits) where T : class
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
            throw new ArgumentException("Supply a complete valid bounded configuration wire value.", nameof(value));
        }
    }

    private static T? Decode<T>(ReadOnlyMemory<byte> bytes, bool request, ManagementConfigurationWireLimits limits,
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
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonDocument Document(ReadOnlyMemory<byte> bytes, ManagementConfigurationWireLimits limits)
    {
        Require(bytes.Length <= limits.MaximumBodyBytes, "configuration.wire.tooLarge", ManagementWireFailure.TooLarge);
        Require(MessageWire.ValidateJson(bytes.Span, limits.MaximumJsonDepth), "configuration.wire.invalid");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaximumJsonDepth });
    }

    private static byte[] SourceBytes(string value, ManagementConfigurationWireLimits limits)
    {
        Require(value.Length <= limits.MaximumSourceBytes, "configuration.content.tooLarge", ManagementWireFailure.TooLarge);
        byte[] bytes;
        try
        {
            Require(Utf8.GetByteCount(value) <= limits.MaximumSourceBytes,
                "configuration.content.tooLarge", ManagementWireFailure.TooLarge);
            bytes = Utf8.GetBytes(value);
        }
        catch (EncoderFallbackException)
        {
            throw new DataFailure("configuration.content.invalid");
        }
        Require(bytes.Length <= limits.MaximumSourceBytes, "configuration.content.tooLarge", ManagementWireFailure.TooLarge);
        Require(MessageWire.ValidateJson(bytes, limits.MaximumJsonDepth), "configuration.content.invalid");
        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaximumJsonDepth });
        Require(document.RootElement.ValueKind == JsonValueKind.Object, "configuration.content.invalid");
        return bytes;
    }

    private static void Supported(Type type, bool request)
    {
        bool command = type == typeof(ManagementConfigurationCreationCommand) || type == typeof(ManagementConfigurationCandidatePreviewCommand) || type == typeof(ManagementConfigurationCatalogCommand)
            || type == typeof(ManagementConfigurationPublicationCommand) || type == typeof(ManagementConfigurationReleaseCommand)
            || type == typeof(ManagementConfigurationAssignmentCommand) || type == typeof(ManagementConfigurationValidationCommand)
            || type == typeof(ManagementConfigurationPreviewCommand) || type == typeof(ManagementConfigurationStatusReport);
        bool response = type == typeof(ManagementConfigurationCreationContext) || type == typeof(ManagementConfigurationSummary)
            || type == typeof(ManagementPage<ManagementConfigurationSummary>) || type == typeof(ManagementConfigurationCandidatePreview) || type == typeof(ManagementConfigurationEditorContext)
            || type == typeof(ManagementConfigurationDefinition) || type == typeof(ManagementConfigurationCatalog)
            || type == typeof(ManagementConfigurationSource) || type == typeof(ManagementConfigurationPublication)
            || type == typeof(ManagementConfigurationPublicationMetadata) || type == typeof(ManagementConfigurationRelease)
            || type == typeof(ManagementConfigurationAssignmentMap) || type == typeof(ManagementConfigurationSnapshot)
            || type == typeof(ManagementConfigurationValidationResult) || type == typeof(ManagementConfigurationPreview)
            || type == typeof(ManagementConfigurationDecision) || type == typeof(ManagementConfigurationOperationResult)
            || type == typeof(ManagementConfigurationProblem) || type == typeof(ManagementConfigurationStatusReport)
            || type == typeof(ManagementPage<ManagementConfigurationDefinition>)
            || type == typeof(ManagementPage<ManagementConfigurationPublicationMetadata>)
            || type == typeof(ManagementPage<ManagementConfigurationRelease>)
            || type == typeof(ManagementPage<ManagementConfigurationAdoptionEntry>);
        if (request ? !command : !response)
        {
            throw new NotSupportedException("Select a declared configuration request or response DTO.");
        }
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 32
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
            if (value.Length > 256 * 1024 || Utf8.GetByteCount(value) > 256 * 1024)
            {
                throw new JsonException("Supply bounded well-formed Unicode text.");
            }
            writer.WriteStringValue(value);
        }
    }

    private sealed class BoundedOutput(int maximum) : MemoryStream(Math.Min(maximum, 4096))
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            Check(1);
            base.WriteByte(value);
        }

        private void Check(int count)
        {
            Require(count <= maximum - Length, "configuration.wire.tooLarge", ManagementWireFailure.TooLarge);
        }
    }

    private static void Require(bool valid, string code = "configuration.wire.invalid",
        ManagementWireFailure failure = ManagementWireFailure.Invalid)
    {
        if (!valid)
        {
            throw new DataFailure(code, failure);
        }
    }

    private sealed class DataFailure(string code, ManagementWireFailure failure = ManagementWireFailure.Invalid) : Exception
    {
        internal string Code { get; } = code;
        internal ManagementWireFailure Failure { get; } = failure;
    }
}
