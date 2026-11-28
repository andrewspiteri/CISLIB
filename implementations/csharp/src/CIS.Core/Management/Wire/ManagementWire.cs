using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CIS.Messaging;
using CIS.Text;

namespace CIS.Management.Wire;

/// <summary>Finite v1 management DTO encoding and validation; no arbitrary runtime types, I/O or authority are introduced.</summary>
public static partial class ManagementWire
{
    private static readonly ManagementWireLimits Defaults = new();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    /// <summary>Gets the dedicated finite management problem-body capture bound: 64 KiB.</summary>
    public const int MaximumErrorBodyBytes = 64 * 1024;

    /// <summary>The greatest portable control-confirmation lifetime, measured from local request entry.</summary>
    public const int MaximumControlValidForMs = 15_000;

    /// <summary>Encodes one of the four closed typed requests and rejects an invalid complete replacement.</summary>
    /// <remarks>Input collections must remain unchanged until encoding finishes. Returned bytes are detached.</remarks>
    public static byte[] EncodeRequest<T>(T value, ManagementWireLimits? limits = null) where T : class
        => Encode(value, true, limits ?? Defaults);

    /// <summary>Encodes a declared response DTO using the same finite field and body rules.</summary>
    public static byte[] EncodeResponse<T>(T value, ManagementWireLimits? limits = null) where T : class
        => Encode(value, false, limits ?? Defaults);

    /// <summary>Decodes a supported closed request, returning null for expected invalid wire data.</summary>
    public static T? DecodeRequest<T>(ReadOnlyMemory<byte> json, ManagementWireLimits? limits = null) where T : class
        => Decode<T>(json, true, limits ?? Defaults, out _);

    /// <summary>Decodes a supported closed request and reports an expected wire failure without an exception.</summary>
    public static T? DecodeRequest<T>(ReadOnlyMemory<byte> json, out ManagementWireFailure failure,
        ManagementWireLimits? limits = null) where T : class
        => Decode<T>(json, true, limits ?? Defaults, out failure);

    /// <summary>Decodes a supported additive response, retaining original strings and explicit unknown values.</summary>
    public static T? DecodeResponse<T>(ReadOnlyMemory<byte> json, ManagementWireLimits? limits = null) where T : class
        => Decode<T>(json, false, limits ?? Defaults, out _);

    /// <summary>Decodes a supported additive response and reports expected malformed data.</summary>
    public static T? DecodeResponse<T>(ReadOnlyMemory<byte> json, out ManagementWireFailure failure,
        ManagementWireLimits? limits = null) where T : class
        => Decode<T>(json, false, limits ?? Defaults, out failure);

    /// <summary>Tests canonical lowercase nonempty UUID D form without normalizing an identifier.</summary>
    public static bool IsUuid(string? value)
        => value is not null && value.Length == 36 && Guid.TryParseExact(value, "D", out Guid parsed)
            && parsed != Guid.Empty && parsed.ToString("D", CultureInfo.InvariantCulture) == value;

    /// <summary>Tests one through 128 unreserved safe ASCII characters, excluding the complete dot-segment keys <c>.</c> and <c>..</c>.</summary>
    public static bool IsOperationKey(string? value)
        => value is { Length: >= 1 and <= 128 } && value is not ("." or "..")
            && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '.' or '_' or '~');

    /// <summary>Tests one bounded strong quoted ETag, rejecting weak, wildcard and multiple-value forms.</summary>
    public static bool IsStrongETag(string? value)
    {
        if (value is null || value.Length is < 3 or > 256 || value[0] != '"' || value[^1] != '"')
        {
            return false;
        }
        foreach (char character in value.AsSpan(1, value.Length - 2))
        {
            if (character is < (char)0x21 or > (char)0x7e or '"')
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Tests a canonical positive Int64 decimal string; configuration revisions may explicitly permit zero.</summary>
    public static bool IsOrderedInteger(string? value, bool allowZero = false)
    {
        if (value is null || value.Length is < 1 or > 19 || value.Any(character => character is < '0' or > '9')
            || value.Length > 1 && value[0] == '0')
        {
            return false;
        }
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number)
            && (allowZero ? number >= 0 : number > 0);
    }

    /// <summary>Tests the exact seven-fraction UTC Z timestamp profile and actual calendar validity.</summary>
    public static bool IsTimestamp(string? value)
        => value is { Length: 28 } && DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _);

    /// <summary>Tests finite well-formed Unicode scalar text without trimming, normalization or controls.</summary>
    public static bool IsScalarText(string? value, int maximumScalars, bool allowEmpty = false)
        => value is not null && maximumScalars > 0 && (allowEmpty || !string.IsNullOrWhiteSpace(value))
            && value.Length <= maximumScalars * 2L && UnicodeScalarText.IsValid(value, maximumScalars, allowControls: false);

    /// <summary>Tests the native Jobs/Coordination UTF-8 byte bound, separately from scalar-based metadata.</summary>
    public static bool IsUtf8Text(string? value, int maximumBytes, bool allowEmpty = false)
    {
        if (value is null || maximumBytes < 1 || value.Length > maximumBytes)
        {
            return false;
        }
        if (value.Length == 0)
        {
            return allowEmpty;
        }
        if (!IsScalarText(value, maximumBytes))
        {
            return false;
        }
        return StrictUtf8.GetByteCount(value) <= maximumBytes;
    }

    /// <summary>Validates one explicit page request without interpreting its opaque cursor.</summary>
    public static bool IsPageRequest(ManagementPageRequest? page)
        => page is not null && page.PageSize is >= 1 and <= 500
            && (page.Cursor is null || IsOpaqueCursor(page.Cursor));

    private static byte[] Encode<T>(T value, bool request, ManagementWireLimits limits) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureSupported(typeof(T), request);
        ValidateOpaqueInputs(value, limits);
        int maximum = BodyLimit(typeof(T), request, limits);
        using var output = new BoundedOutput(maximum);
        try
        {
            JsonSerializer.Serialize(output, value, Json);
        }
        catch (BodyLimitException)
        {
            throw new ArgumentException("The complete encoded management body exceeds its configured byte limit.", nameof(value));
        }
        catch (JsonException)
        {
            throw new ArgumentException("Supply bounded well-formed Unicode management data.", nameof(value));
        }
        byte[] encoded = output.ToArray();
        try
        {
            Validate(encoded, typeof(T), request, limits);
        }
        catch (WireDataException)
        {
            throw new ArgumentException("Supply a complete valid management v1 value within its configured limits.", nameof(value));
        }
        catch (JsonException)
        {
            throw new ArgumentException("Supply a complete valid management v1 value.", nameof(value));
        }
        return encoded;
    }

    private static T? Decode<T>(ReadOnlyMemory<byte> json, bool request, ManagementWireLimits limits,
        out ManagementWireFailure failure) where T : class
    {
        EnsureSupported(typeof(T), request);
        failure = ManagementWireFailure.Invalid;
        if (json.Length > BodyLimit(typeof(T), request, limits))
        {
            failure = ManagementWireFailure.TooLarge;
            return null;
        }
        try
        {
            Validate(json, typeof(T), request, limits);
            T? value = JsonSerializer.Deserialize<T>(json.Span, Json);
            if (value is null)
            {
                return null;
            }
            failure = ManagementWireFailure.None;
            return (T)Freeze(value);
        }
        catch (WireDataException error)
        {
            failure = error.Failure;
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Validate(ReadOnlyMemory<byte> json, Type type, bool request, ManagementWireLimits limits)
    {
        Require(json.Length <= BodyLimit(type, request, limits), ManagementWireFailure.TooLarge);
        // Reuse the existing strict JSON primitive for UTF-8, escaped duplicate names,
        // malformed Unicode and depth. HTTP field evolution is validated separately below.
        Require(MessageWire.ValidateJson(json.Span, limits.MaximumJsonDepth));
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = limits.MaximumJsonDepth });
        ValidateValue(type, document.RootElement, request, limits);
    }

    private static int BodyLimit(Type type, bool request, ManagementWireLimits limits)
        => request ? type == typeof(ManagementInventorySnapshot) ? limits.MaximumInventoryRequestBytes : limits.MaximumRegularRequestBytes
            : type == typeof(ManagementPage<ManagementInventoryEntry>) || type == typeof(ManagementInventoryEntry)
                || type == typeof(ManagementInventorySnapshot) ? limits.MaximumInventoryResponseBytes : limits.MaximumRegularResponseBytes;

    private static void EnsureSupported(Type type, bool request)
    {
        bool supportedRequest = type == typeof(ManagementRegistrationRequest) || type == typeof(ManagementObservation)
            || type == typeof(ManagementInventorySnapshot) || type == typeof(ManagementMetadataCommand);
        bool supportedResponse = type == typeof(ManagementApplication) || type == typeof(ManagementServiceInfo)
            || type == typeof(ManagementServiceFeaturePage) || type == typeof(ManagementApplicationApiCapabilityPage)
            || type == typeof(ManagementCapabilities) || type == typeof(ManagementRegistrationReceipt)
            || type == typeof(ManagementControlConfirmation)
            || type == typeof(ManagementReportReceipt) || type == typeof(ManagementCloseReceipt)
            || type == typeof(ManagementInstance) || type == typeof(ManagementMetadataResult)
            || type == typeof(ManagementMetadataDecision) || type == typeof(ManagementOperationRejection) || type == typeof(ManagementProblem)
            || type == typeof(ManagementPage<ManagementApplication>) || type == typeof(ManagementPage<ManagementInstance>)
            || type == typeof(ManagementEnvironment) || type == typeof(ManagementPage<ManagementEnvironment>)
            || type == typeof(ManagementPage<ManagementInventoryEntry>) || type == typeof(ManagementInventoryEntry)
            || type == typeof(ManagementInventorySnapshot);
        if (request ? !supportedRequest : !supportedResponse)
        {
            throw new NotSupportedException("Select a declared management v1 request or response DTO.");
        }
    }

    private static bool IsOpaqueCursor(string value)
        => value.Length is >= 1 and <= 2048 && value.All(character => character is >= '!' and <= '~');

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 32
        };
        options.Converters.Add(new ScalarStringConverter());
        return options;
    }

    private static void ValidateOpaqueInputs(object value, ManagementWireLimits limits)
    {
        if (value is ManagementInventorySnapshot inventory)
        {
            if (inventory.Modules is null || inventory.Modules.Count > limits.MaximumModules)
            {
                throw new ArgumentException("Supply a bounded complete module inventory.", nameof(value));
            }
            foreach (ManagementModuleInventory module in inventory.Modules)
            {
                if (module?.Details is not { } details)
                {
                    continue;
                }
                string raw = details.GetRawText();
                if (details.ValueKind != JsonValueKind.Object || raw.Length > limits.MaximumInventoryRequestBytes
                    || StrictUtf8.GetByteCount(raw) > limits.MaximumInventoryRequestBytes
                    || !MessageWire.ValidateJson(StrictUtf8.GetBytes(raw), limits.MaximumJsonDepth))
                {
                    throw new ArgumentException("Supply bounded well-formed opaque module data.", nameof(value));
                }
            }
        }
        else if (value is ManagementInventoryEntry entry)
        {
            ValidateOpaqueInputs(entry.Inventory, limits);
        }
        else if (value is ManagementPage<ManagementInventoryEntry> page)
        {
            if (page.Items is null || page.Items.Count > 500)
            {
                throw new ArgumentException("Supply a bounded inventory page.", nameof(value));
            }
            foreach (ManagementInventoryEntry item in page.Items)
            {
                ValidateOpaqueInputs(item, limits);
            }
        }
    }

    // Every typed textual field is at most 2,048 scalars. Check before the native
    // encoder could replace an invalid surrogate or buffer an unbounded string.
    // Opaque JsonElement data keeps its separate complete-body/depth boundary.
    private sealed class ScalarStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value.Length > 4096 || !UnicodeScalarText.IsValid(value, 2048, allowControls: true))
            {
                throw new JsonException("Typed management text must be bounded well-formed Unicode.");
            }
            writer.WriteStringValue(value);
        }
    }

    private static void Require(bool condition, ManagementWireFailure failure = ManagementWireFailure.Invalid)
    {
        if (!condition)
        {
            throw new WireDataException(failure);
        }
    }

    private sealed class WireDataException(ManagementWireFailure failure) : Exception
    {
        internal ManagementWireFailure Failure { get; } = failure;
    }

    private sealed class BodyLimitException : Exception
    {
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
            if (count > maximum - Length)
            {
                throw new BodyLimitException();
            }
        }
    }
}
