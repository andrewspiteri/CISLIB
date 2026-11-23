using System.Text.Json.Serialization;
using CIS.RestClient;

namespace CIS.Management;

/// <summary>A closed versioned complete metadata replacement; omitted description clears it.</summary>
/// <param name="CommandVersion">The supported command version, initially one.</param>
/// <param name="DisplayName">Exact non-normalized administrative display text.</param>
/// <param name="Description">Optional exact description; null means omitted when encoding this DTO.</param>
public sealed record ManagementMetadataCommand(int CommandVersion, string DisplayName, string? Description = null);

/// <summary>An actual resolved logical operation identity; this data grants no authority.</summary>
/// <param name="Name">The registered operation name.</param>
/// <param name="Scope">The actual resolved actor/resource scope, at most 256 UTF-8 bytes.</param>
/// <param name="Key">The original caller-selected safe ASCII key.</param>
public sealed record ManagementOperationReference(string Name, string Scope, string Key);

/// <summary>The immutable original business result retained by the existing operation journal.</summary>
/// <param name="CommandVersion">The command result version, initially one.</param>
/// <param name="ApplicationId">The actual managed application UUID.</param>
/// <param name="Decision">Applied or rejected, or an explicitly unknown future value.</param>
/// <param name="Code">Stable safe business code.</param>
/// <param name="Metadata">The original decision's resulting administrative metadata.</param>
/// <param name="MetadataRevision">The original resulting positive Int64 revision string.</param>
/// <param name="MetadataETag">The original resulting strong metadata-only ETag.</param>
/// <param name="DecidedAtUtc">Original precise decision timestamp text.</param>
public sealed record ManagementMetadataDecision(int CommandVersion, string ApplicationId, string Decision, string Code,
    ManagementApplicationMetadata Metadata, string MetadataRevision, string MetadataETag, string DecidedAtUtc)
{
    /// <summary>Gets a recognized business interpretation without converting unknown values to Applied.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownDecision => new(Decision, Decision is "applied" or "rejected");
}

/// <summary>Safe actual attempt completion and cleanup facts, independent of an original replayed result.</summary>
/// <param name="Outcome">committed, notCommitted or unknown, retaining future values explicitly.</param>
/// <param name="CleanupSucceeded">Whether the actual attempt's cleanup succeeded.</param>
/// <param name="CleanupCodes">Optional bounded safe cleanup codes, without exception or provider text.</param>
public sealed record ManagementTransactionFacts(string Outcome, bool CleanupSucceeded, IReadOnlyList<string>? CleanupCodes = null)
{
    /// <summary>Gets the recognized actual completion interpretation.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownOutcome => new(Outcome, Outcome is "committed" or "notCommitted" or "unknown");
}

/// <summary>The original RM-03 rejection, which contains no invented metadata snapshot or decision time.</summary>
/// <param name="Code">The unchanged nonblank printable ASCII application code, at most 128 bytes.</param>
/// <param name="Message">The unchanged valid Unicode application message, at most 1,024 UTF-8 bytes, permitting controls except NUL.</param>
public sealed record ManagementOperationRejection(string Code, string Message);

/// <summary>Original result facts and their present source, distinct from HTTP exchange success.</summary>
/// <param name="Operation">The genuine resolved operation reference.</param>
/// <param name="ResultSource">prepared, replayed or found, or an explicit unknown future value.</param>
/// <param name="Result">The unchanged typed original value, absent for a native RM-03 rejection.</param>
/// <param name="OriginalCompletion">The original transaction's established completion.</param>
/// <param name="AttemptCompletion">Optional current arbitration/read/commit owner's actual facts.</param>
/// <param name="Rejection">The unchanged native rejection; exactly one of Result or Rejection is present.</param>
public sealed record ManagementMetadataResult(ManagementOperationReference Operation, string ResultSource,
    ManagementMetadataDecision? Result, string OriginalCompletion, ManagementTransactionFacts? AttemptCompletion = null,
    ManagementOperationRejection? Rejection = null)
{
    /// <summary>Gets a recognized source interpretation without inventing a retained result.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownResultSource => new(ResultSource, ResultSource is "prepared" or "replayed" or "found");

    /// <summary>Gets the recognized original completion, separately from current attempt cleanup.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownOriginalCompletion => new(OriginalCompletion,
        OriginalCompletion is "committed" or "notCommitted" or "unknown");

    /// <summary>Gets the actual rejection branch or the typed value's interpretation, without inventing absent result fields.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownBusinessDecision => Rejection is not null
        ? new("rejected", true) : Result?.KnownDecision ?? new ManagementKnownValue("unknown", false);
}

/// <summary>Bounded RFC 9457 details and genuine optional operation facts.</summary>
/// <param name="Type">The problem type URI.</param>
/// <param name="Title">Safe bounded title.</param>
/// <param name="Status">The actual non-success HTTP status.</param>
/// <param name="Code">Stable machine-readable safe code.</param>
/// <param name="Detail">Optional safe bounded explanation.</param>
/// <param name="CorrelationId">Optional diagnostic correlation, never an operation key.</param>
/// <param name="Operation">Optional actual resolved operation reference.</param>
/// <param name="Result">Optional actual coordinator/result facts.</param>
public sealed record ManagementProblem(string Type, string Title, int Status, string Code, string? Detail = null,
    string? CorrelationId = null, ManagementOperationReference? Operation = null, ManagementMetadataResult? Result = null);

/// <summary>Preserves native REST facts alongside safely decoded management-specific error data.</summary>
public sealed class ManagementCallResult<T>
{
    /// <summary>Retains the exact native result and optional final per-call problem and original mutation key.</summary>
    public ManagementCallResult(RestResult<T> exchange, ManagementProblem? problem = null, string? operationKey = null)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        if (problem is not null && (exchange.Success || exchange.Context.HttpStatusCode != problem.Status))
        {
            throw new ArgumentException("A management problem must match the actual failed HTTP exchange.", nameof(problem));
        }
        if (operationKey is not null && !Wire.ManagementWire.IsOperationKey(operationKey))
        {
            throw new ArgumentException("Supply the original bounded operation key.", nameof(operationKey));
        }
        Exchange = exchange;
        Problem = problem;
        OperationKey = operationKey;
    }

    /// <summary>Gets the original REST result, including actual status, security evidence, cancellation classification and cleanup duration.</summary>
    public RestResult<T> Exchange
    {
        get;
    }

    /// <summary>Gets final safely parsed problem details, if available in the bounded error body.</summary>
    public ManagementProblem? Problem
    {
        get;
    }

    /// <summary>Gets the unchanged caller-selected mutation key retained through uncertainty.</summary>
    public string? OperationKey
    {
        get;
    }
}
