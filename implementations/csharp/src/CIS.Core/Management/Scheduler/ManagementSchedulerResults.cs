using System.Text.Json.Serialization;
using CIS.RestClient;
using CIS.Management.Wire;

namespace CIS.Management;

/// <summary>One fixed administrative decision, without captured business input or a replacement operation identity.</summary>
public sealed record ManagementSchedulerDecision(int CommandVersion, string Kind, string Decision, string Code,
    string DecidedAtUtc, string ApplicationId, string ObjectId, string? Revision = null, string? ETag = null,
    string? RunId = null, string? AttemptId = null)
{
    /// <summary>Gets the explicit known interpretation; a future value never becomes success.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownDecision => new(Decision, Decision is "applied" or "rejected");

    /// <summary>Gets the explicit known interpretation; a future value never becomes success.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownKind => new(Kind, Kind is "triggerCreated" or "triggerUpdated"
        or "triggerStateChanged" or "taskCreated" or "taskUpdated" or "taskActivated" or "taskStateChanged"
        or "manualRunAdmitted" or "cancellationRequested");
}

/// <summary>Original durable result and current read/arbitration cleanup are independent facts.</summary>
public sealed record ManagementSchedulerOperationResult(ManagementOperationReference Operation, string ResultSource,
    ManagementSchedulerDecision? Result, string OriginalCompletion, ManagementTransactionFacts? AttemptCompletion = null,
    ManagementOperationRejection? Rejection = null)
{
    /// <summary>Gets the explicit known interpretation; a future value never becomes success.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownResultSource => new(ResultSource, ResultSource is "prepared" or "replayed" or "found");

    /// <summary>Gets the explicit known interpretation; a future value never becomes success.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownOriginalCompletion => new(OriginalCompletion,
        OriginalCompletion is "committed" or "notCommitted" or "unknown");
}

/// <summary>Scheduler-specific Problem Details can retain the actual original result on a failed exchange.</summary>
public sealed record ManagementSchedulerProblem(string Type, string Title, int Status, string Code,
    string? Detail = null, string? CorrelationId = null, ManagementOperationReference? Operation = null,
    ManagementSchedulerOperationResult? Result = null);

/// <summary>Actual native exchange and its safely decoded original result; uncertainty never creates a new key.</summary>
public sealed class ManagementSchedulerCallResult<T>
{
    /// <summary>Retains detached validated facts without selecting runtime work or establishing native authority.</summary>
    public ManagementSchedulerCallResult(RestResult<T> exchange, ManagementSchedulerProblem? problem = null,
        string? operationKey = null)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        if (problem is not null && (exchange.Success || exchange.Context.HttpStatusCode is not { } status
            || (int)status != problem.Status))
        {
            throw new ArgumentException("Problem Details must describe the actual failed exchange.", nameof(problem));
        }

        if (operationKey is not null && !ManagementWire.IsOperationKey(operationKey))
        {
            throw new ArgumentException("Retain the original bounded administrative key.", nameof(operationKey));
        }

        Exchange = exchange;
        Problem = problem;
        OperationKey = operationKey;
    }

    /// <summary>Gets the detached exchange data.</summary>
    public RestResult<T> Exchange { get; }
    /// <summary>Gets the detached problem data.</summary>
    public ManagementSchedulerProblem? Problem { get; }
    /// <summary>Gets the detached operation key data.</summary>
    public string? OperationKey { get; }
}
