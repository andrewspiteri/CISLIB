using System.Text.Json.Serialization;
using CIS.RestClient;

namespace CIS.Management;

/// <summary>One fixed durable decision. It retains metadata and references, never source payloads.</summary>
/// <param name="CommandVersion">Supported recorded version, initially one.</param>
/// <param name="Kind">catalogApplied, configurationCreated, sourcePublished, releaseCreated or assignmentsReplaced.</param>
/// <param name="Decision">Raw original business decision, retaining future values.</param>
/// <param name="Code">Safe stable business code.</param>
/// <param name="DecidedAtUtc">Original precise decision time.</param>
/// <param name="CatalogId">Catalog decision's stable UUID.</param>
/// <param name="ManifestDigest">Catalog decision's selected manifest digest.</param>
/// <param name="SourceId">Publication decision's source UUID.</param>
/// <param name="PublicationId">Publication decision's immutable UUID.</param>
/// <param name="Sha256">Publication decision's exact-content digest.</param>
/// <param name="ApplicationId">Release or assignment decision's actual application UUID.</param>
/// <param name="ConfigurationId">Release or assignment decision's portable identity.</param>
/// <param name="SchemaVersion">Release or assignment decision's exact schema.</param>
/// <param name="ReleaseId">Created release UUID.</param>
/// <param name="Generation">Assignment decision's resulting positive generation.</param>
/// <param name="ETag">Original resulting strong resource ETag.</param>
/// <param name="Intent">Original assignment replace, promote or rollback intent.</param>
/// <param name="Assignment">Original complete resulting map, preserving changed and retained overrides.</param>
public sealed record ManagementConfigurationDecision(int CommandVersion, string Kind, string Decision, string Code,
    string DecidedAtUtc, string? CatalogId = null, string? ManifestDigest = null, string? SourceId = null,
    string? PublicationId = null, string? Sha256 = null, string? ApplicationId = null, string? ConfigurationId = null,
    string? SchemaVersion = null, string? ReleaseId = null, string? Generation = null, string? ETag = null,
    string? Intent = null, ManagementConfigurationAssignmentMap? Assignment = null)
{
    /// <summary>Gets the creation decision's actual newly prepared binding UUID.</summary>
    public string? BindingId
    {
        get; init;
    }

    /// <summary>Gets the creation decision's actual application host scope.</summary>
    public string? HostApplicationName
    {
        get; init;
    }

    /// <summary>Gets the creation decision's initial source precondition; publication remains separate.</summary>
    public string? SourceETag
    {
        get; init;
    }

    /// <summary>Gets the original decision interpretation without treating unknown as success.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownDecision => new(Decision, Decision is "applied" or "rejected");

    /// <summary>Gets the fixed decision family without inventing a generic command dispatcher.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownKind => new(Kind,
        Kind is "catalogApplied" or "sourcePublished" or "releaseCreated" or "assignmentsReplaced" or "configurationCreated");

    /// <summary>Gets the original assignment intent without converting a future response value to replace.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownIntent => new(Intent ?? "unknown", Intent is "replace" or "promote" or "rollback");
}

/// <summary>Original durable configuration facts, separate from HTTP and current cleanup facts.</summary>
/// <param name="Operation">Genuine resolved original operation identity.</param>
/// <param name="ResultSource">prepared, replayed or found, retaining future values explicitly.</param>
/// <param name="Result">Original typed metadata-only decision, absent for a native rejection.</param>
/// <param name="OriginalCompletion">Original transaction completion.</param>
/// <param name="AttemptCompletion">Current attempt facts, never substituted for original completion.</param>
/// <param name="Rejection">Unchanged native rejection; exactly one of Result or Rejection is present.</param>
public sealed record ManagementConfigurationOperationResult(ManagementOperationReference Operation, string ResultSource,
    ManagementConfigurationDecision? Result, string OriginalCompletion, ManagementTransactionFacts? AttemptCompletion = null,
    ManagementOperationRejection? Rejection = null)
{
    /// <summary>Gets the recognized original result source.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownResultSource => new(ResultSource, ResultSource is "prepared" or "replayed" or "found");

    /// <summary>Gets recognized original completion separately from current owner cleanup.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownOriginalCompletion => new(OriginalCompletion,
        OriginalCompletion is "committed" or "notCommitted" or "unknown");

    /// <summary>Gets genuine rejection or typed decision facts without inventing absent values.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownBusinessDecision => Rejection is not null
        ? new("rejected", true) : Result is { KnownKind.IsKnown: true } result
            ? result.KnownDecision : new ManagementKnownValue(Result?.Decision ?? "unknown", false);
}

/// <summary>Separate configuration Problem Details; metadata results remain unchanged.</summary>
/// <param name="Type">Absolute problem type URI.</param>
/// <param name="Title">Bounded safe title.</param>
/// <param name="Status">Actual non-success HTTP status.</param>
/// <param name="Code">Safe stable machine code.</param>
/// <param name="Detail">Optional bounded safe explanation.</param>
/// <param name="CorrelationId">Diagnostic correlation, never an operation key.</param>
/// <param name="Operation">Actual resolved operation reference, if established.</param>
/// <param name="Result">Actual original configuration result, if established.</param>
public sealed record ManagementConfigurationProblem(string Type, string Title, int Status, string Code,
    string? Detail = null, string? CorrelationId = null, ManagementOperationReference? Operation = null,
    ManagementConfigurationOperationResult? Result = null);

/// <summary>Payload-free identified content rejection, established only after independently trusted target validation.</summary>
/// <remarks>This is a client/local acceptance fact, not server Problem Details or proof of an applied value.</remarks>
public sealed record ManagementConfigurationContentRejection
{
    /// <summary>Captures an identified desired release and bounded safe machine code without payload or exception text.</summary>
    /// <param name="desired">Independently target-validated desired identity.</param>
    /// <param name="code">One through 128 ASCII letters, digits, dots, hyphens, underscores or colons.</param>
    public ManagementConfigurationContentRejection(ManagementConfigurationDesiredContext desired, string code)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (code is null || code.Length is < 1 or > 128 || !code.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '.' or '-' or '_' or ':'))
        {
            throw new ArgumentException("Supply a bounded safe ASCII content-rejection code.", nameof(code));
        }
        Desired = desired;
        Code = code;
    }

    /// <summary>Gets the identified desired identity, never a fabricated source or accepted value.</summary>
    public ManagementConfigurationDesiredContext Desired
    {
        get;
    }

    /// <summary>Gets the bounded stable content-failure code.</summary>
    public string Code
    {
        get;
    }
}

/// <summary>Native REST facts with safely decoded configuration-specific result information.</summary>
public sealed class ManagementConfigurationCallResult<T>
{
    /// <summary>Retains actual exchange, bounded final problem and unchanged original mutation key.</summary>
    public ManagementConfigurationCallResult(RestResult<T> exchange, ManagementConfigurationProblem? problem = null,
        string? operationKey = null, ManagementConfigurationContentRejection? contentRejection = null)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        if (problem is not null && (exchange.Success || exchange.Context.HttpStatusCode is not { } status
            || (int)status != problem.Status))
        {
            throw new ArgumentException("A configuration problem must describe the actual failed HTTP exchange.", nameof(problem));
        }
        if (operationKey is not null && !Wire.ManagementWire.IsOperationKey(operationKey))
        {
            throw new ArgumentException("Supply the original bounded operation key.", nameof(operationKey));
        }
        if (contentRejection is not null && (exchange.Success || problem is not null))
        {
            throw new ArgumentException("An identified content rejection belongs to a failed exchange without server Problem Details.", nameof(contentRejection));
        }
        Exchange = exchange;
        Problem = problem;
        OperationKey = operationKey;
        ContentRejection = contentRejection;
    }

    /// <summary>Gets original native status, security, cancellation and actual cleanup facts.</summary>
    public RestResult<T> Exchange
    {
        get;
    }

    /// <summary>Gets safely decoded final configuration Problem Details.</summary>
    public ManagementConfigurationProblem? Problem
    {
        get;
    }

    /// <summary>Gets the unchanged caller-selected mutation key through uncertainty.</summary>
    public string? OperationKey
    {
        get;
    }

    /// <summary>Gets payload-free identified content failure after trusted target validation, including a failed decode with HTTP 200.</summary>
    public ManagementConfigurationContentRejection? ContentRejection
    {
        get;
    }
}
