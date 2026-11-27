using CIS.Management.Wire;

namespace CIS.Management;

public sealed partial class ManagementSchedulerClient
{
    /// <inheritdoc />
    public async Task<ManagementCallResult<ManagementPage<ManagementCapabilityDefinition>>> GetCapabilityDefinitionsAsync(
        string applicationId, ManagementCapabilityQuery? query = null, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        var mapper = new SchedulerMapper(settings.WireLimits, kind: ProblemKind.Catalog);
        var exchange = await SendAsync<ManagementPage<ManagementCapabilityDefinition>>("GET",
            "applications/" + Uuid(applicationId) + "/capability-definitions", null, page ?? new(), CapabilityQuery(query),
            null, mapper, "catalog", options, cancellationToken).ConfigureAwait(false);
        return new(exchange, FinalProblem(exchange, mapper) ? mapper.CatalogProblem : null);
    }

    /// <inheritdoc />
    public async Task<ManagementCallResult<ManagementCapabilityDefinition>> GetCapabilityDefinitionAsync(string applicationId,
        ManagementCapabilityKey key, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var query = new Dictionary<string, string>(CapabilityQuery(new(key.Kind, key.ExecutionScope, key.DefinitionId, key.DefinitionVersion)))
        {
            ["ownerApplication"] = key.OwnerApplication
        };
        var mapper = new SchedulerMapper(settings.WireLimits,
            value => Require(value is ManagementCapabilityDefinition definition && definition.Key == key), kind: ProblemKind.Catalog);
        var exchange = await SendAsync<ManagementCapabilityDefinition>("GET",
            "applications/" + Uuid(applicationId) + "/capability-definition", null, null, query,
            null, mapper, "catalog", options, cancellationToken).ConfigureAwait(false);
        return new(exchange, FinalProblem(exchange, mapper) ? mapper.CatalogProblem : null);
    }

    /// <inheritdoc />
    public async Task<ManagementCallResult<ManagementPage<ManagementCapabilityParticipation>>> GetCapabilityParticipationAsync(
        string applicationId, ManagementCapabilityQuery? query = null, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        var mapper = new SchedulerMapper(settings.WireLimits, kind: ProblemKind.Catalog);
        var exchange = await SendAsync<ManagementPage<ManagementCapabilityParticipation>>("GET",
            "applications/" + Uuid(applicationId) + "/capability-participation", null, page ?? new(), CapabilityQuery(query),
            null, mapper, "catalog", options, cancellationToken).ConfigureAwait(false);
        return new(exchange, FinalProblem(exchange, mapper) ? mapper.CatalogProblem : null);
    }

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationActions>> GetSourceActionsAsync(string sourceId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => ConfigurationReadAsync<ManagementConfigurationActions>("configuration/sources/" + Uuid(sourceId) + "/actions",
            null, null, value => Require(value is ManagementConfigurationActions actions && actions.ResourceId == sourceId), options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationActions>> GetBindingActionsAsync(string applicationId,
        string configurationId, string schemaVersion, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => ConfigurationReadAsync<ManagementConfigurationActions>(Binding(applicationId, configurationId) + "/actions",
            SchemaQuery(schemaVersion), null, null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationCohortChoice>>> GetCohortsAsync(string applicationId,
        string configurationId, string schemaVersion, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => ConfigurationReadAsync<ManagementPage<ManagementConfigurationCohortChoice>>(Binding(applicationId, configurationId) + "/cohorts",
            SchemaQuery(schemaVersion), page ?? new(), null, options, cancellationToken);

    private async Task<ManagementConfigurationCallResult<T>> ConfigurationReadAsync<T>(string path, IReadOnlyDictionary<string, string>? query,
        ManagementPageRequest? page, Action<object?>? check, ManagementCallOptions? options, CancellationToken token)
    {
        var mapper = new SchedulerMapper(settings.WireLimits, check, kind: ProblemKind.Configuration);
        var exchange = await SendAsync<T>("GET", path, null, page, query, null, mapper, "configuration", options, token).ConfigureAwait(false);
        return new(exchange, FinalProblem(exchange, mapper) ? mapper.ConfigurationProblem : null);
    }

    private static IReadOnlyDictionary<string, string> CapabilityQuery(ManagementCapabilityQuery? query)
    {
        query ??= new();
        ManagementSchedulerWire.EncodeRequest(query);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(values, "kind", query.Kind);
        Add(values, "executionScope", query.ExecutionScope);
        Add(values, "definitionId", query.DefinitionId);
        Add(values, "definitionVersion", query.DefinitionVersion);
        return values;
    }

    private static string Binding(string application, string configuration)
        => "applications/" + Uuid(application) + "/configuration/" + Key(configuration);

    private static IReadOnlyDictionary<string, string> SchemaQuery(string schemaVersion)
    {
        RequireArgument(ManagementWire.IsOrderedInteger(schemaVersion), nameof(schemaVersion));
        return new Dictionary<string, string>(StringComparer.Ordinal) { ["schemaVersion"] = schemaVersion };
    }
}

