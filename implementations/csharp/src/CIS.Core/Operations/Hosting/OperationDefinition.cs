using CIS.Security;

namespace CIS.Operations.Hosting;

/// <summary>Mutable setup copied by an operation definition before registration.</summary>
public sealed class OperationDefinitionOptions
{
    /// <summary>Gets or sets the canonical frame byte limit, initially 64 KiB and at most one MiB.</summary>
    public int MaximumCanonicalBytes { get; set; } = 65_536;
    /// <summary>Gets or sets the result/error payload limit, initially 256 KiB and at most one MiB.</summary>
    public int MaximumResultBytes { get; set; } = 262_144;
    /// <summary>Gets or sets explicitly comparable fingerprint versions; null selects only the current version.</summary>
    public IReadOnlyList<int>? FingerprintVersions
    {
        get; set;
    }
    /// <summary>Gets or sets one to eight capabilities covering submission and disclosure of a retained result.</summary>
    public IReadOnlyList<string> ExecuteCapabilities { get; set; } = [];
    /// <summary>Gets or sets one to eight capabilities required before lookup/disclosure.</summary>
    public IReadOnlyList<string> LookupCapabilities { get; set; } = [];
    /// <summary>Gets or sets up to eight maintenance capabilities; empty leaves maintenance disabled.</summary>
    public IReadOnlyList<string> MaintenanceCapabilities { get; set; } = [];
    /// <summary>Gets or sets retention for new completed rows; both defaults are indefinite.</summary>
    public OperationRetentionPolicy Retention { get; set; } = new();
}

/// <summary>Immutable finite operation metadata; it does not construct scoped dependencies or perform I/O.</summary>
public abstract class OperationDefinition
{
    private protected OperationDefinition(string name, string databaseName, OperationContract fingerprintContract,
        OperationResultMetadata result, Func<IServiceProvider, IOperationScopeResolver> scopeResolver,
        OperationDefinitionOptions options)
    {
        Name = OperationText.Alias(name, nameof(name));
        DatabaseName = OperationText.Alias(databaseName, nameof(databaseName));
        ArgumentNullException.ThrowIfNull(fingerprintContract);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Retention);
        FingerprintContract = fingerprintContract;
        ResultContract = result.Current;
        ReadableResultContracts = result.Readers;
        MaximumCanonicalBytes = OperationText.ByteLimit(options.MaximumCanonicalBytes, nameof(options.MaximumCanonicalBytes));
        MaximumResultBytes = OperationText.ByteLimit(options.MaximumResultBytes, nameof(options.MaximumResultBytes));
        FingerprintVersions = CopyVersions(options.FingerprintVersions ?? [fingerprintContract.Version], fingerprintContract.Version);
        ExecuteCapabilities = CopyCapabilities(options.ExecuteCapabilities, nameof(options.ExecuteCapabilities), required: true);
        LookupCapabilities = CopyCapabilities(options.LookupCapabilities, nameof(options.LookupCapabilities), required: true);
        MaintenanceCapabilities = CopyCapabilities(options.MaintenanceCapabilities, nameof(options.MaintenanceCapabilities), required: false);
        Retention = new(options.Retention.ResultRetention, options.Retention.DuplicateProtectionRetention);
        ScopeResolver = scopeResolver;
    }

    /// <summary>Gets the unique ordinal operation alias.</summary>
    public string Name
    {
        get;
    }
    /// <summary>Gets the application's registered authoritative database alias.</summary>
    public string DatabaseName
    {
        get;
    }
    /// <summary>Gets the current stable fingerprint contract.</summary>
    public OperationContract FingerprintContract
    {
        get;
    }
    /// <summary>Gets at most four explicitly comparable versions of the same fingerprint contract.</summary>
    public IReadOnlyList<int> FingerprintVersions
    {
        get;
    }
    /// <summary>Gets the current stable result contract.</summary>
    public OperationContract ResultContract
    {
        get;
    }
    /// <summary>Gets at most four copied supported readers, including the current result version.</summary>
    public IReadOnlyList<OperationContract> ReadableResultContracts
    {
        get;
    }
    /// <summary>Gets the bounded complete canonical frame limit.</summary>
    public int MaximumCanonicalBytes
    {
        get;
    }
    /// <summary>Gets the bounded encoded result/error payload limit.</summary>
    public int MaximumResultBytes
    {
        get;
    }
    /// <summary>Gets capabilities required before submission or replay disclosure.</summary>
    public IReadOnlyList<string> ExecuteCapabilities
    {
        get;
    }
    /// <summary>Gets capabilities required before lookup disclosure.</summary>
    public IReadOnlyList<string> LookupCapabilities
    {
        get;
    }
    /// <summary>Gets explicitly configured privileged maintenance capabilities; empty means disabled.</summary>
    public IReadOnlyList<string> MaintenanceCapabilities
    {
        get;
    }
    /// <summary>Gets immutable policy used only for newly completed rows.</summary>
    public OperationRetentionPolicy Retention
    {
        get;
    }
    internal Func<IServiceProvider, IOperationScopeResolver> ScopeResolver
    {
        get;
    }
    internal abstract Type RequestType
    {
        get;
    }
    internal abstract Type ResultType
    {
        get;
    }

    private static IReadOnlyList<int> CopyVersions(IEnumerable<int> versions, int current)
    {
        var copied = new List<int>();
        foreach (int version in versions)
        {
            if (version < 1 || copied.Count == 4 || copied.Contains(version))
            {
                throw new ArgumentException("Supply one to four distinct positive fingerprint versions.", nameof(versions));
            }
            copied.Add(version);
        }
        if (!copied.Contains(current))
        {
            throw new ArgumentException("Comparable fingerprint versions must include the current version.", nameof(versions));
        }
        return Array.AsReadOnly(copied.ToArray());
    }

    private static IReadOnlyList<string> CopyCapabilities(IEnumerable<string> capabilities, string name, bool required)
    {
        ArgumentNullException.ThrowIfNull(capabilities, name);
        var copied = new List<string>();
        foreach (string capability in capabilities)
        {
            string validated = SecurityText.Capability(capability, name);
            if (copied.Count == 8 || copied.Contains(validated, StringComparer.Ordinal))
            {
                throw new ArgumentException("Supply at most eight distinct capabilities.", name);
            }
            copied.Add(validated);
        }
        if (required && copied.Count == 0)
        {
            throw new ArgumentException("An explicit nonempty capability requirement is required.", name);
        }
        return Array.AsReadOnly(copied.ToArray());
    }
}

/// <summary>A trusted application normalizer, codec and scoped resource resolver under immutable metadata.</summary>
public sealed class OperationDefinition<TRequest, TResult> : OperationDefinition, IOperationResultDefinition<TResult>
{
    /// <summary>Captures metadata without invoking the normalizer, encoding values or resolving application services.</summary>
    public OperationDefinition(string name, string databaseName, OperationContract fingerprintContract,
        IOperationRequestNormalizer<TRequest> normalizer, IOperationResultCodec<TResult> codec,
        Func<IServiceProvider, IOperationScopeResolver> scopeResolver, OperationDefinitionOptions options)
        : base(name, databaseName, fingerprintContract, OperationResultMetadata.Capture(codec), scopeResolver, options)
    {
        ArgumentNullException.ThrowIfNull(normalizer);
        Normalizer = normalizer;
        Codec = codec;
    }

    internal IOperationRequestNormalizer<TRequest> Normalizer
    {
        get;
    }
    internal IOperationResultCodec<TResult> Codec
    {
        get;
    }
    IOperationResultCodec<TResult> IOperationResultDefinition<TResult>.Codec => Codec;
    internal override Type RequestType => typeof(TRequest);
    internal override Type ResultType => typeof(TResult);
}

internal interface IOperationResultDefinition<TResult>
{
    IOperationResultCodec<TResult> Codec
    {
        get;
    }
}

internal sealed record OperationResultMetadata(OperationContract Current, IReadOnlyList<OperationContract> Readers)
{
    internal static OperationResultMetadata Capture<TResult>(IOperationResultCodec<TResult> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        OperationContract current = codec.CurrentContract
            ?? throw new ArgumentException("Supply a stable current result contract.", nameof(codec));
        IReadOnlyList<OperationContract> readers = codec.ReadableContracts
            ?? throw new ArgumentException("Supply explicit result readers.", nameof(codec));
        var copied = new List<OperationContract>();
        foreach (OperationContract reader in readers)
        {
            ArgumentNullException.ThrowIfNull(reader);
            if (reader.Identifier != current.Identifier || copied.Count == 4 || copied.Contains(reader))
            {
                throw new ArgumentException("Supply at most four distinct versions of the current stable result contract.", nameof(codec));
            }
            copied.Add(reader);
        }
        if (!copied.Contains(current))
        {
            throw new ArgumentException("Result readers must include the current version.", nameof(codec));
        }
        return new(current, Array.AsReadOnly(copied.ToArray()));
    }
}
