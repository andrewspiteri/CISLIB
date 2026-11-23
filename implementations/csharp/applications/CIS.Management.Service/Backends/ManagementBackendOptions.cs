using CIS.Management.Wire;

namespace CIS.Management.Service.Backends;

/// <summary>The two deliberately selected deployment cardinalities for one durable namespace.</summary>
public enum ManagementDeploymentMode
{
    Standalone,
    Shared
}

/// <summary>Provider targets, not a claim that every target has an implemented complete adapter.</summary>
public enum ManagementBackendProvider
{
    PostgreSql,
    Redis,
    CosmosDb
}

/// <summary>Immutable authority bootstrap, independent of any managed application's remote configuration.</summary>
public sealed class ManagementBackendOptions
{
    public ManagementBackendOptions(ManagementBackendProvider provider = ManagementBackendProvider.PostgreSql,
        ManagementDeploymentMode mode = ManagementDeploymentMode.Standalone, string namespaceName = "management",
        long expectedRecoveryEpoch = 1, bool recoveryHold = false, int controlValidForMs = 15000)
    {
        if (!Enum.IsDefined(provider) || provider != ManagementBackendProvider.PostgreSql)
        {
            throw new NotSupportedException("This build supplies only the complete PostgreSQL primary backend.");
        }
        if (!Enum.IsDefined(mode) || !ManagementWire.IsScalarText(namespaceName, 128)
            || expectedRecoveryEpoch < 1 || controlValidForMs is < 1 or > 15000)
        {
            throw new ArgumentException("Select one exact bounded namespace, positive expected recovery epoch and finite control lifetime.");
        }
        Provider = provider;
        Mode = mode;
        NamespaceName = namespaceName;
        ExpectedRecoveryEpoch = expectedRecoveryEpoch;
        RecoveryHold = recoveryHold;
        ControlValidForMs = controlValidForMs;
    }

    public ManagementBackendProvider Provider
    {
        get;
    }
    public ManagementDeploymentMode Mode
    {
        get;
    }
    public string NamespaceName
    {
        get;
    }
    public long ExpectedRecoveryEpoch
    {
        get;
    }
    public bool RecoveryHold
    {
        get;
    }
    public int ControlValidForMs
    {
        get;
    }

    public static ManagementBackendOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new(configuration.GetValue("Provider", ManagementBackendProvider.PostgreSql),
            configuration.GetValue("Mode", ManagementDeploymentMode.Standalone), configuration["Namespace"] ?? "management",
            configuration.GetValue("ExpectedRecoveryEpoch", 1L), configuration.GetValue("RecoveryHold", false),
            configuration.GetValue("ControlValidForMs", 15000));
    }
}

/// <summary>The complete provider's frozen guarantees under its declared primary-only failure model.</summary>
public sealed record ManagementBackendCapabilities(string Provider, string FailureProfile, bool SupportsShared,
    bool DurableAuthority, bool AtomicMetadataResultAudit, bool ConcurrentArbitration, int MaximumInventoryRequestBytes)
{
    public void Validate(ManagementDeploymentMode mode)
    {
        if (!Enum.IsDefined(mode) || !DurableAuthority || !AtomicMetadataResultAudit || !ConcurrentArbitration
            || mode == ManagementDeploymentMode.Shared && !SupportsShared
            || MaximumInventoryRequestBytes is < 1 or > 16777216)
        {
            throw new NotSupportedException("The selected backend cannot supply this complete authoritative deployment profile.");
        }
    }
}

/// <summary>Current persisted namespace identity, separate from process identity, metadata ETags or observation sequences.</summary>
public sealed record ManagementAuthoritySnapshot(string NamespaceName, string AuthorityId, string ControlRevision,
    long RecoveryEpoch, bool RecoveryHold);

/// <summary>A bounded safe provider refusal; no connection string, SQL text or native exception message is exposed.</summary>
public sealed class ManagementBackendException(string code) : InvalidOperationException("The selected management authority is unavailable or not qualified.")
{
    public string Code { get; } = code;
}
