namespace CIS.Management;

/// <summary>Exact optional feature query; absence of observations does not erase trusted definitions.</summary>
public sealed record ManagementCapabilityQuery(string? Kind = null, string? ExecutionScope = null,
    string? DefinitionId = null, string? DefinitionVersion = null);

/// <summary>Optional logical application capability discovery, separate from the existing HTTP API-capabilities protocol.</summary>
public interface IManagementApplicationCatalogClient
{
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementCallResult<ManagementPage<ManagementCapabilityDefinition>>> GetCapabilityDefinitionsAsync(
        string applicationId, ManagementCapabilityQuery? query = null, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementCallResult<ManagementCapabilityDefinition>> GetCapabilityDefinitionAsync(string applicationId,
        ManagementCapabilityKey key, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementCallResult<ManagementPage<ManagementCapabilityParticipation>>> GetCapabilityParticipationAsync(
        string applicationId, ManagementCapabilityQuery? query = null, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Finite installed action eligibility, advisory only; every write and result lookup reauthorizes.</summary>
public sealed record ManagementAdministrativeAction(string OperationId, bool Supported, bool Allowed, string? ReasonCode = null);

/// <summary>Actual source/binding action facts. No issuer/subject memberships or hidden values are disclosed.</summary>
public sealed record ManagementConfigurationActions(string ResourceId,
    IReadOnlyList<ManagementAdministrativeAction> Actions)
{
    /// <summary>Gets the detached actions data.</summary>
    public IReadOnlyList<ManagementAdministrativeAction> Actions { get; } = SchedulerValue.Copy(Actions, 32, nameof(Actions));
}

/// <summary>A trusted authorized rollout choice, not a browser-defined identity group.</summary>
public sealed record ManagementConfigurationCohortChoice(string CohortId, string DisplayName);

/// <summary>Additive RM08 administrative companions; existing client substitutes gain no compulsory member.</summary>
public interface IManagementConfigurationAdministrationClient
{
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationActions>> GetSourceActionsAsync(string sourceId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationActions>> GetBindingActionsAsync(string applicationId,
        string configurationId, string schemaVersion, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationCohortChoice>>> GetCohortsAsync(
        string applicationId, string configurationId, string schemaVersion, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}
