using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CIS.Management.Wire;

namespace CIS.Management.Service.Configuration;

/// <summary>One immutable global schema/defaults contract, independent of application host bindings.</summary>
public sealed record ManagementConfigurationSchema(string ConfigurationId, string SchemaVersion,
    ManagementConfigurationShape Shape, string DefaultsJson, string? Description = null);

/// <summary>A trusted application/environment binding; this never selects a consumer's local update mode.</summary>
public sealed record ManagementConfigurationBinding(string Application, string Environment, string HostApplicationName,
    string ConfigurationId, string SchemaVersion);

/// <summary>One explicitly permitted common source stream.</summary>
public sealed record ManagementConfigurationCommonSource(string ConfigurationId, string SchemaVersion, string Environment);

/// <summary>A deployment-controlled native subject mapping, captured once when a registration is admitted.</summary>
public sealed record ManagementConfigurationCohort(string Issuer, string Subject, string Application,
    string Environment, string CohortId);

/// <summary>Finite deployment data; HTTP callers can select its digest but cannot supply this manifest.</summary>
public sealed record ManagementConfigurationManifest(int ManifestVersion,
    IReadOnlyList<ManagementConfigurationSchema> Definitions, IReadOnlyList<ManagementConfigurationBinding> Bindings,
    IReadOnlyList<ManagementConfigurationCommonSource> CommonSources, IReadOnlyList<ManagementConfigurationCohort> Cohorts);

/// <summary>Captured trusted bootstrap input and explicitly selected logical storage policy.</summary>
public sealed class ManagementConfigurationOptions
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32
    };

    /// <summary>Captures one exact manifest without I/O. Load operator-owned files before constructing the host.</summary>
    public ManagementConfigurationOptions(string manifestJson, long quotaBytes, int retainedUnreferencedReleases = 10,
        IReadOnlyList<string>? pinnedReleaseIds = null)
    {
        ArgumentNullException.ThrowIfNull(manifestJson);
        if (quotaBytes is < 1 or > 9_007_199_254_740_991 || retainedUnreferencedReleases is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(quotaBytes), "Select an explicit bounded quota and retention policy.");
        }
        const int maximumManifestBytes = 16 * 1024 * 1024;
        if (manifestJson.Length > maximumManifestBytes || Utf8.GetByteCount(manifestJson) > maximumManifestBytes)
        {
            throw new ArgumentException("Supply one bounded strict trusted manifest.", nameof(manifestJson));
        }
        byte[] bytes = Utf8.GetBytes(manifestJson);
        if (!IsStrictManifest(bytes))
        {
            throw new ArgumentException("Supply one bounded strict trusted manifest.", nameof(manifestJson));
        }
        ManagementConfigurationManifest parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ManagementConfigurationManifest>(bytes, Json)
                ?? throw new JsonException();
        }
        catch (JsonException error)
        {
            throw new ArgumentException("Supply a complete closed trusted manifest.", nameof(manifestJson), error);
        }
        if (parsed.ManifestVersion != 1 || parsed.Definitions is null || parsed.Bindings is null
            || parsed.CommonSources is null || parsed.Cohorts is null || parsed.Definitions.Count > 4096
            || parsed.Bindings.Count > 4096 || parsed.CommonSources.Count > 4096 || parsed.Cohorts.Count > 16384)
        {
            throw new ArgumentException("The trusted manifest exceeds its finite deployment profile.", nameof(manifestJson));
        }
        var schemas = new Dictionary<(string, string), ManagementConfigurationSchema>();
        foreach (ManagementConfigurationSchema definition in parsed.Definitions)
        {
            if (definition is null)
            {
                throw new ArgumentException("A schema cannot be null.", nameof(manifestJson));
            }
            var descriptor = new ManagementConfigurationDefinition(definition.ConfigurationId, definition.SchemaVersion,
                "11111111-1111-1111-1111-111111111111", "manifest", "common", definition.Shape,
                definition.DefaultsJson, definition.Description);
            ManagementConfigurationDefinition frozen = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationDefinition>(
                ManagementConfigurationWire.EncodeResponse(descriptor))!;
            ManagementConfigurationValidationResult validation = ManagementConfigurationStructure.Validate(frozen.Shape,
                definition.DefaultsJson, complete: true);
            if (!validation.Valid || !schemas.TryAdd((definition.ConfigurationId, definition.SchemaVersion),
                new(definition.ConfigurationId, definition.SchemaVersion, frozen.Shape, definition.DefaultsJson, definition.Description)))
            {
                throw new ArgumentException("Schema identities and complete defaults must be valid and unique.", nameof(manifestJson));
            }
        }
        var bindings = new HashSet<(string, string, string, string)>();
        foreach (ManagementConfigurationBinding binding in parsed.Bindings)
        {
            if (binding is null || !ManagementWire.IsScalarText(binding.Application, 255)
                || !ManagementWire.IsScalarText(binding.Environment, 255)
                || !ManagementWire.IsScalarText(binding.HostApplicationName, 255) || binding.HostApplicationName == "CommonConfiguration"
                || !schemas.ContainsKey((binding.ConfigurationId, binding.SchemaVersion))
                || !bindings.Add((binding.Application, binding.Environment, binding.ConfigurationId, binding.SchemaVersion)))
            {
                throw new ArgumentException("Application bindings must be exact, unique and refer to declared schemas.", nameof(manifestJson));
            }
        }
        if (parsed.Bindings.GroupBy(item => (item.Application, item.Environment)).Any(group => group.Count() > 256))
        {
            throw new ArgumentException("One application/environment can enroll at most 256 definitions.", nameof(manifestJson));
        }
        var common = new HashSet<(string, string, string)>();
        foreach (ManagementConfigurationCommonSource source in parsed.CommonSources)
        {
            if (source is null || !schemas.ContainsKey((source.ConfigurationId, source.SchemaVersion))
                || !ManagementWire.IsScalarText(source.Environment, 255)
                || !common.Add((source.ConfigurationId, source.SchemaVersion, source.Environment)))
            {
                throw new ArgumentException("Common sources must be exact, unique declared environment scopes.", nameof(manifestJson));
            }
        }
        var subjects = new HashSet<(string, string, string, string)>();
        foreach (ManagementConfigurationCohort cohort in parsed.Cohorts)
        {
            if (cohort is null || !ManagementWire.IsScalarText(cohort.Issuer, 2048) || !ManagementWire.IsScalarText(cohort.Subject, 2048)
                || !ManagementWire.IsOperationKey(cohort.CohortId)
                || !bindings.Any(item => item.Item1 == cohort.Application && item.Item2 == cohort.Environment)
                || !subjects.Add((cohort.Issuer, cohort.Subject, cohort.Application, cohort.Environment)))
            {
                throw new ArgumentException("Cohorts require a unique native subject and an actual declared application/environment.", nameof(manifestJson));
            }
        }
        string[] pins = pinnedReleaseIds?.ToArray() ?? [];
        if (pins.Length > 4096 || pins.Any(pin => !ManagementWire.IsUuid(pin)) || pins.Distinct(StringComparer.Ordinal).Count() != pins.Length)
        {
            throw new ArgumentException("Supply bounded unique exact release pins.", nameof(pinnedReleaseIds));
        }
        Manifest = new(1, Array.AsReadOnly(schemas.Values.ToArray()), Array.AsReadOnly(parsed.Bindings.ToArray()),
            Array.AsReadOnly(parsed.CommonSources.ToArray()), Array.AsReadOnly(parsed.Cohorts.ToArray()));
        using (IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            Span<byte> length = stackalloc byte[4];
            foreach (byte[] part in new[] { Utf8.GetBytes("cis.management.configuration.deployment.v1"), bytes,
                Utf8.GetBytes(quotaBytes.ToString(CultureInfo.InvariantCulture)),
                Utf8.GetBytes(retainedUnreferencedReleases.ToString(CultureInfo.InvariantCulture)),
                JsonSerializer.SerializeToUtf8Bytes(pins.Order(StringComparer.Ordinal).ToArray(), Json) })
            {
                BinaryPrimitives.WriteInt32BigEndian(length, part.Length);
                digest.AppendData(length);
                digest.AppendData(part);
            }
            ManifestDigest = Convert.ToHexStringLower(digest.GetHashAndReset());
        }
        QuotaBytes = quotaBytes;
        RetainedUnreferencedReleases = retainedUnreferencedReleases;
        PinnedReleaseIds = Array.AsReadOnly(pins);
    }

    public string ManifestDigest
    {
        get;
    }
    public long QuotaBytes
    {
        get;
    }
    /// <summary>Retains this many newest eligible unreferenced releases per binding and orphan publications per source.</summary>
    public int RetainedUnreferencedReleases
    {
        get;
    }
    public IReadOnlyList<string> PinnedReleaseIds
    {
        get;
    }
    internal ManagementConfigurationManifest Manifest
    {
        get;
    }

    private static bool IsStrictManifest(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            return document.RootElement.ValueKind == JsonValueKind.Object && HasUniqueValidNamesAndStrings(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or EncoderFallbackException or InvalidOperationException)
        {
            return false;
        }
    }

    // One trusted-manifest preflight. The typed closed reader and finite schema validator perform all domain checks.
    private static bool HasUniqueValidNamesAndStrings(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                _ = Utf8.GetByteCount(property.Name);
                if (!names.Add(property.Name) || !HasUniqueValidNamesAndStrings(property.Value))
                {
                    return false;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in value.EnumerateArray())
            {
                if (!HasUniqueValidNamesAndStrings(child))
                {
                    return false;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            _ = Utf8.GetByteCount(value.GetString()!);
        }
        return true;
    }

    /// <summary>Explicit application bootstrap file loading; ordinary runtime never watches or reapplies it.</summary>
    public static ManagementConfigurationOptions? FromConfiguration(IConfiguration configuration)
    {
        string? file = configuration["ManifestFile"];
        if (file is null)
        {
            return null;
        }
        if (!Path.IsPathFullyQualified(file) || new FileInfo(file).Length > 16 * 1024 * 1024)
        {
            throw new ArgumentException("Select one absolute bounded operator-controlled manifest file.");
        }
        long quota = configuration.GetValue<long?>("QuotaBytes")
            ?? throw new ArgumentException("Configuration publication requires an explicit logical byte quota.");
        using FileStream source = File.OpenRead(file);
        using var content = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = source.Read(buffer)) != 0)
        {
            if (content.Length > 16 * 1024 * 1024 - count)
            {
                throw new ArgumentException("The selected trusted manifest exceeds its finite byte limit.");
            }
            content.Write(buffer, 0, count);
        }
        return new(Utf8.GetString(content.ToArray()), quota, configuration.GetValue("RetainedUnreferencedReleases", 10),
            configuration.GetSection("PinnedReleaseIds").Get<string[]>());
    }
}
