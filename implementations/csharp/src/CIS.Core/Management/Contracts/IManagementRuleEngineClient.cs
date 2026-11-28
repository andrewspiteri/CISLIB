using System.Text.Json.Serialization;
using CIS.RestClient;
using CIS.RuleEngine;

namespace CIS.Management;

/// <summary>One trusted ruleset and its current administrative edit precondition.</summary>
public sealed record ManagementRuleSetSummary(string ApplicationId, string Environment, string RulesetId,
    string DisplayName, string SchemaRevision, string EditETag);

/// <summary>One immutable schema in the resolved ruleset scope, retaining exact JSON text.</summary>
public sealed record ManagementRuleSchema(string ApplicationId, string Environment, string RulesetId,
    string SchemaRevision, string SchemaJson);

/// <summary>One immutable saved version, without its definition payload.</summary>
public sealed record ManagementRuleVersionSummary(string ApplicationId, string Environment, string RulesetId,
    string Version, string SchemaRevision, string SavedAtUtc)
{
    /// <summary>Gets the original note if it was recorded with this saved version.</summary>
    public string? VersionNote { get; init; }
}

/// <summary>One immutable saved version and its complete raw definition.</summary>
public sealed record ManagementRuleVersion(string ApplicationId, string Environment, string RulesetId,
    string Version, string SchemaRevision, string SavedAtUtc, string DefinitionJson)
{
    /// <summary>Gets the original note if it was recorded with this saved version.</summary>
    public string? VersionNote { get; init; }

    /// <summary>Decodes the exact portable definition and checks its echoed identity.</summary>
    public RuleDefinition DecodeDefinition() =>
        ManagementRuleEngineWire.Definition(DefinitionJson, RulesetId, SchemaRevision, Version);
}

/// <summary>A retained, deployment-eligible rule version for one authenticated workload target and enrollment.</summary>
public sealed record ManagementRuleWorkloadVersion(ManagementConfigurationSnapshotTarget Target,
    string RulesetId, string Version, string DefinitionJson)
{
    /// <summary>Decodes the exact immutable definition and verifies its rule and version identity.</summary>
    public RuleDefinition DecodeDefinition() => ManagementRuleEngineWire.WorkloadDefinition(DefinitionJson, RulesetId, Version);
}

/// <summary>One complete proposed version, with an optional immutable version used as its editing base.</summary>
public sealed record ManagementSaveRuleVersionCommand(string SchemaRevision, string DefinitionJson, string Reason,
    string? BaseVersion = null)
{
    /// <summary>Encodes a typed draft without losing portable decimal or JSON metadata values.</summary>
    public static ManagementSaveRuleVersionCommand FromDefinition(RuleDefinition definition, string reason,
        string? baseVersion = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new(definition.SchemaRevision, ManagementRuleEngineWire.Utf8.GetString(RuleDefinitionCodec.Encode(definition)),
            reason, baseVersion);
    }
}

/// <summary>The original saved version and resulting edit precondition.</summary>
public sealed record ManagementRuleVersionDecision(string Version, string SchemaRevision, string EditETag, string SavedAtUtc);

/// <summary>Original save facts and present lookup or cleanup observations, independent of HTTP success.</summary>
public sealed record ManagementRuleVersionSaveResult(string ApplicationId, string Environment, string RulesetId,
    ManagementOperationReference Operation, string ResultSource, string OriginalCompletion,
    ManagementRuleVersionDecision? Result = null, ManagementTransactionFacts? AttemptCompletion = null,
    ManagementOperationRejection? Rejection = null)
{
    /// <summary>Gets the recognized original result source without inventing a result for future values.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownResultSource => new(ResultSource, ResultSource is "prepared" or "replayed" or "found");

    /// <summary>Gets the original completion independently of current attempt cleanup.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownOriginalCompletion => new(OriginalCompletion,
        OriginalCompletion is "committed" or "notCommitted" or "unknown");
}

/// <summary>A genuine HTTP problem with any original Rule Engine operation facts the server established.</summary>
public sealed record ManagementRuleEngineProblem(string Type, string Title, int Status, string Code,
    string? Detail = null, string? CorrelationId = null, ManagementOperationReference? Operation = null,
    ManagementRuleVersionSaveResult? Result = null)
{
    /// <summary>Gets original deployment facts when the server established them for this failed exchange.</summary>
    public ManagementRuleDeploymentOperationResult? DeploymentResult
    {
        get; init;
    }

    /// <summary>Gets original counter reset facts when the server established them for this failed exchange.</summary>
    public ManagementRuleCounterResetOperationResult? CounterResetResult { get; init; }
}

/// <summary>The actual native exchange, safely decoded RM11 problem and original key when applicable.</summary>
public sealed class ManagementRuleEngineCallResult<T>
{
    /// <summary>Captures original response facts without inferring successful work from HTTP status.</summary>
    public ManagementRuleEngineCallResult(RestResult<T> exchange, ManagementRuleEngineProblem? problem = null,
        string? operationKey = null)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        if (problem is not null && (exchange.Success || exchange.Context.HttpStatusCode != problem.Status))
        {
            throw new ArgumentException("The problem must describe this failed HTTP exchange.", nameof(problem));
        }

        if (operationKey is not null && !Wire.ManagementWire.IsOperationKey(operationKey))
        {
            throw new ArgumentException("Supply the original bounded operation key.", nameof(operationKey));
        }

        Exchange = exchange;
        Problem = problem;
        OperationKey = operationKey;
    }

    /// <summary>Gets the original native REST result, including actual HTTP and cleanup facts.</summary>
    public RestResult<T> Exchange
    {
        get;
    }

    /// <summary>Gets genuine decoded Problem Details, if established.</summary>
    public ManagementRuleEngineProblem? Problem
    {
        get;
    }

    /// <summary>Gets the original save or recovery key unchanged.</summary>
    public string? OperationKey
    {
        get;
    }
}

/// <summary>Optional scoped ruleset reads and version authoring on the existing Management API.</summary>
public interface IManagementRuleEngineClient
{
    /// <summary>Lists trusted rulesets visible under the resolved application and environment.</summary>
    Task<ManagementRuleEngineCallResult<ManagementPage<ManagementRuleSetSummary>>> ListRulesetsAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads an exact immutable schema revision.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleSchema>> GetSchemaAsync(string applicationId, string rulesetId,
        string schemaRevision, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Lists saved versions under one ruleset with a scoped opaque cursor.</summary>
    Task<ManagementRuleEngineCallResult<ManagementPage<ManagementRuleVersionSummary>>> ListVersionsAsync(
        string applicationId, string rulesetId, ManagementPageRequest? page = null, ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the exact immutable saved version and definition.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleVersion>> GetVersionAsync(string applicationId, string rulesetId,
        string version, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Saves a complete definition under its original key and strong current edit precondition.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleVersionSaveResult>> SaveVersionAsync(string applicationId,
        string rulesetId, string operationKey, string ifMatch, ManagementSaveRuleVersionCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Looks up the original save under the current caller and exact ruleset scope.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleVersionSaveResult>> GetSaveOperationAsync(string applicationId,
        string rulesetId, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>The version, schema and immutable RM08 release selected by a committed assignment.</summary>
public sealed record ManagementRuleDeploymentSelection(string Version, string SchemaRevision, string ReleaseId, string Generation);

/// <summary>The current desired assignment and its whole-map precondition, including an initialized empty desired state.</summary>
public sealed record ManagementRuleDeployment(string ApplicationId, string Environment, string RulesetId,
    string AssignmentETag, ManagementRuleDeploymentSelection? Desired = null)
{
    /// <summary>Gets the original request time for the current desired generation when retained.</summary>
    public string? RequestedAtUtc { get; init; }
}

/// <summary>Current desired rule identity and separate original RM08 observations for one instance.</summary>
public sealed record ManagementRuleDeploymentInstance(string RulesetId, ManagementConfigurationAdoptionEntry Configuration,
    string? RequestedVersion = null, string? ReportedDesiredVersion = null, string? AppliedVersion = null,
    string? LastSuccessfulVersion = null);

/// <summary>A deploy or rollback request for an existing immutable saved version.</summary>
public sealed record ManagementRequestRuleDeploymentCommand(string Version, string Intent, string Reason);

/// <summary>The immutable outcome of the original deployment request and its resulting desired assignment.</summary>
public sealed record ManagementRuleDeploymentDecision(string Intent, string AssignmentETag, string RequestedAtUtc,
    ManagementRuleDeploymentSelection Desired);

/// <summary>Original deployment facts and the current recovery or cleanup observation.</summary>
public sealed record ManagementRuleDeploymentOperationResult(string ApplicationId, string Environment, string RulesetId,
    ManagementOperationReference Operation, string ResultSource, string OriginalCompletion,
    ManagementRuleDeploymentDecision? Result = null, ManagementTransactionFacts? AttemptCompletion = null,
    ManagementOperationRejection? Rejection = null)
{
    /// <summary>Gets the recognized source of the original deployment result.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownResultSource => new(ResultSource, ResultSource is "prepared" or "replayed" or "found");

    /// <summary>Gets original completion independently of the current attempt's cleanup.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownOriginalCompletion => new(OriginalCompletion,
        OriginalCompletion is "committed" or "notCommitted" or "unknown");
}

/// <summary>Optional desired assignment reads and deployment requests over the existing Management API.</summary>
public interface IManagementRuleEngineDeploymentClient
{
    /// <summary>Reads the current desired assignment and exact whole-map edit precondition.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleDeployment>> GetDeploymentAsync(string applicationId,
        string rulesetId, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Requests deployment of an existing scoped saved version under its original operation key and map precondition.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleDeploymentOperationResult>> RequestDeploymentAsync(string applicationId,
        string rulesetId, string operationKey, string ifMatch, ManagementRequestRuleDeploymentCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Recovers the original request under the current caller and exact ruleset scope.</summary>
    Task<ManagementRuleEngineCallResult<ManagementRuleDeploymentOperationResult>> GetDeploymentOperationAsync(string applicationId,
        string rulesetId, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}
