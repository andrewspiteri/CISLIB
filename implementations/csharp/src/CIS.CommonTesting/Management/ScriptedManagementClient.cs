using CIS.Management;
using CIS.Management.Wire;

namespace CIS.CommonTesting;

/// <summary>The finite typed management operation selected by one script step.</summary>
public enum ScriptedManagementCallKind
{
    /// <summary>Authenticated feature discovery.</summary>
    ServiceInfo,
    /// <summary>Create or recover registration.</summary>
    Registration,
    /// <summary>Latest observation replacement.</summary>
    Observation,
    /// <summary>Complete inventory replacement.</summary>
    InventoryReport,
    /// <summary>Terminal registration close.</summary>
    RegistrationClose,
    /// <summary>Application page.</summary>
    Applications,
    /// <summary>Application detail.</summary>
    Application,
    /// <summary>Instance page.</summary>
    Instances,
    /// <summary>Inventory page.</summary>
    Inventory,
    /// <summary>Installed APIs and caller hints.</summary>
    Capabilities,
    /// <summary>Original-key metadata mutation.</summary>
    MetadataReplace,
    /// <summary>Original-key result lookup.</summary>
    MetadataOperation,
    /// <summary>Current authoritative control confirmation, without native authority or freshness evaluation.</summary>
    Control
}

/// <summary>Detached safe call metadata, without credentials, payloads, ETags, options or caller contexts.</summary>
/// <param name="Kind">The exact typed API operation.</param>
/// <param name="ApplicationId">Optional canonical resource ID.</param>
/// <param name="RegistrationId">Optional canonical registration ID.</param>
/// <param name="OperationKey">Optional bounded original key, which grants no authority.</param>
public sealed record ScriptedManagementCall(ScriptedManagementCallKind Kind, string? ApplicationId = null,
    string? RegistrationId = null, string? OperationKey = null)
{
    /// <summary>Gets an optional safe control attempt UUID, which is no authority or idempotency key.</summary>
    public string? AttemptId
    {
        get; init;
    }
}

/// <summary>Controls one deliberately held logical call without a timer, native transport or detached cleanup.</summary>
public sealed class PendingManagementCall
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool observeCancellation;

    internal PendingManagementCall(bool observeCancellation) => this.observeCancellation = observeCancellation;

    /// <summary>Gets when the actual held step entered its awaited work.</summary>
    public Task Entered => entered.Task;

    /// <summary>Allows actual held work to settle; repeated release is harmless.</summary>
    public void Release() => released.TrySetResult();

    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        entered.TrySetResult();
        if (observeCancellation)
        {
            await released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await released.Task.ConfigureAwait(false);
        }
    }
}

/// <summary>A finite ordered management script with bounded safe call records; it proves no native authority or durability.</summary>
public sealed class ScriptedManagementClient : IManagementClient
{
    private readonly object gate = new();
    private readonly Queue<Step> steps = new();
    private readonly BoundedRecords<ScriptedManagementCall> calls;
    private readonly int capacity;

    /// <summary>Bounds both queued scripts and retained records, initially sixteen, through 65,536.</summary>
    public ScriptedManagementClient(int capacity = 16)
    {
        calls = new(capacity);
        this.capacity = capacity;
    }

    /// <summary>Gets a detached snapshot of finite safe call metadata.</summary>
    public IReadOnlyList<ScriptedManagementCall> Calls => calls.Snapshot();
    /// <summary>Gets the saturating count of admitted calls since Clear.</summary>
    public long TotalCalls => calls.Total;
    /// <summary>Gets the saturating count of evicted call records since Clear.</summary>
    public long DroppedCalls => calls.Dropped;

    /// <summary>Clears queued scripts and records, leaving already accepted work with its caller.</summary>
    public void Clear()
    {
        lock (gate)
        {
            steps.Clear();
            calls.Clear();
        }
    }

    /// <summary>Queues one exact supplied typed result for the selected API.</summary>
    public void EnqueueResult<T>(ScriptedManagementCallKind kind, ManagementCallResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Enqueue(new ResultStep<T>(kind, result));
    }

    /// <summary>Queues one logical control reply derived from safe call metadata, such as the current attempt echo.</summary>
    /// <remarks>The finite synchronous factory runs outside the script lock and proves no native authority or freshness.</remarks>
    public void EnqueueControl(Func<ScriptedManagementCall, ManagementCallResult<ManagementControlConfirmation>> response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Enqueue(new ControlStep(response));
    }

    /// <summary>Queues the original fault; an unrelated or matching OCE remains a faulted task.</summary>
    public void EnqueueException(ScriptedManagementCallKind kind, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Enqueue(new FaultStep(kind, exception));
    }

    /// <summary>Queues a controlled actual wait followed by the exact typed result.</summary>
    public PendingManagementCall EnqueuePending<T>(ScriptedManagementCallKind kind, ManagementCallResult<T> result,
        bool observeCancellation = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        var pending = new PendingManagementCall(observeCancellation);
        Enqueue(new PendingStep<T>(kind, result, pending));
        return pending;
    }

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementServiceInfo>> GetServiceInfoAsync(ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementServiceInfo>(new(ScriptedManagementCallKind.ServiceInfo), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementRegistrationReceipt>> RegisterAsync(string registrationId, ManagementRegistrationRequest request,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementRegistrationReceipt>(new(ScriptedManagementCallKind.Registration, RegistrationId: registrationId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementControlConfirmation>> GetControlAsync(string registrationId, string attemptId,
        CancellationToken cancellationToken = default)
        => Dispatch<ManagementControlConfirmation>(new(ScriptedManagementCallKind.Control, RegistrationId: registrationId)
        {
            AttemptId = attemptId
        }, cancellationToken);
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementReportReceipt>> ReportObservationAsync(string registrationId, ManagementObservation observation,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementReportReceipt>(new(ScriptedManagementCallKind.Observation, RegistrationId: registrationId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementReportReceipt>> ReportInventoryAsync(string registrationId, ManagementInventorySnapshot inventory,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementReportReceipt>(new(ScriptedManagementCallKind.InventoryReport, RegistrationId: registrationId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementCloseReceipt>> CloseRegistrationAsync(string registrationId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementCloseReceipt>(new(ScriptedManagementCallKind.RegistrationClose, RegistrationId: registrationId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementPage<ManagementApplication>>> ListApplicationsAsync(ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementPage<ManagementApplication>>(new(ScriptedManagementCallKind.Applications), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementApplication>> GetApplicationAsync(string applicationId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementApplication>(new(ScriptedManagementCallKind.Application, ApplicationId: applicationId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementPage<ManagementInstance>>> ListInstancesAsync(string applicationId, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementPage<ManagementInstance>>(new(ScriptedManagementCallKind.Instances, ApplicationId: applicationId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementPage<ManagementInventoryEntry>>> ListInventoryAsync(string applicationId, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementPage<ManagementInventoryEntry>>(new(ScriptedManagementCallKind.Inventory, ApplicationId: applicationId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementCapabilities>> GetCapabilitiesAsync(string applicationId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementCapabilities>(new(ScriptedManagementCallKind.Capabilities, ApplicationId: applicationId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementMetadataResult>> ReplaceMetadataAsync(string applicationId, string operationKey,
        string ifMatch, ManagementMetadataCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementMetadataResult>(new(ScriptedManagementCallKind.MetadataReplace, applicationId, OperationKey: operationKey), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementMetadataResult>> GetMetadataOperationAsync(string applicationId, string operationKey,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementMetadataResult>(new(ScriptedManagementCallKind.MetadataOperation, applicationId, OperationKey: operationKey), cancellationToken);

    private void Enqueue(Step step)
    {
        if (!Enum.IsDefined(step.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }
        lock (gate)
        {
            if (steps.Count == capacity)
            {
                throw new InvalidOperationException("The finite management script capacity is full.");
            }
            steps.Enqueue(step);
        }
    }

    private Task<ManagementCallResult<T>> Dispatch<T>(ScriptedManagementCall call, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromCanceled<ManagementCallResult<T>>(token);
        }
        bool applicationRequired = call.Kind is ScriptedManagementCallKind.Application or ScriptedManagementCallKind.Instances
            or ScriptedManagementCallKind.Inventory or ScriptedManagementCallKind.Capabilities
            or ScriptedManagementCallKind.MetadataReplace or ScriptedManagementCallKind.MetadataOperation;
        bool registrationRequired = call.Kind is ScriptedManagementCallKind.Registration or ScriptedManagementCallKind.Observation
            or ScriptedManagementCallKind.InventoryReport or ScriptedManagementCallKind.RegistrationClose or ScriptedManagementCallKind.Control;
        bool keyRequired = call.Kind is ScriptedManagementCallKind.MetadataReplace or ScriptedManagementCallKind.MetadataOperation;
        if (applicationRequired && !ManagementWire.IsUuid(call.ApplicationId)
            || registrationRequired && !ManagementWire.IsUuid(call.RegistrationId)
            || call.Kind == ScriptedManagementCallKind.Control && !ManagementWire.IsUuid(call.AttemptId)
            || keyRequired && !ManagementWire.IsOperationKey(call.OperationKey))
        {
            return Task.FromException<ManagementCallResult<T>>(new ArgumentException("Supply bounded safe call identifiers."));
        }
        Step step;
        lock (gate)
        {
            if (steps.Count == 0)
            {
                return Task.FromException<ManagementCallResult<T>>(new InvalidOperationException("No management result is scripted."));
            }
            step = steps.Dequeue();
            calls.Add(call);
        }
        return step.Kind == call.Kind ? step.ExecuteAsync<T>(call, token)
            : Task.FromException<ManagementCallResult<T>>(new InvalidOperationException("The management script API kind does not match."));
    }

    private abstract class Step(ScriptedManagementCallKind kind)
    {
        internal ScriptedManagementCallKind Kind { get; } = kind;
        internal abstract Task<ManagementCallResult<T>> ExecuteAsync<T>(ScriptedManagementCall call, CancellationToken token);
    }

    private sealed class ResultStep<TValue>(ScriptedManagementCallKind kind, ManagementCallResult<TValue> value) : Step(kind)
    {
        internal override Task<ManagementCallResult<T>> ExecuteAsync<T>(ScriptedManagementCall call, CancellationToken token)
            => typeof(T) == typeof(TValue) ? Task.FromResult((ManagementCallResult<T>)(object)value)
                : Task.FromException<ManagementCallResult<T>>(new InvalidOperationException("The management script response type does not match."));
    }

    private sealed class FaultStep(ScriptedManagementCallKind kind, Exception error) : Step(kind)
    {
        internal override Task<ManagementCallResult<T>> ExecuteAsync<T>(ScriptedManagementCall call, CancellationToken token)
            => Task.FromException<ManagementCallResult<T>>(error);
    }

    private sealed class PendingStep<TValue>(ScriptedManagementCallKind kind, ManagementCallResult<TValue> value,
        PendingManagementCall pending) : Step(kind)
    {
        internal override Task<ManagementCallResult<T>> ExecuteAsync<T>(ScriptedManagementCall call, CancellationToken token)
        {
            if (typeof(T) != typeof(TValue))
            {
                return Task.FromException<ManagementCallResult<T>>(new InvalidOperationException("The management script response type does not match."));
            }
            return AwaitAsync<T>(token);
        }

        private async Task<ManagementCallResult<T>> AwaitAsync<T>(CancellationToken token)
        {
            await pending.WaitAsync(token).ConfigureAwait(false);
            return (ManagementCallResult<T>)(object)value;
        }
    }

    private sealed class ControlStep(Func<ScriptedManagementCall, ManagementCallResult<ManagementControlConfirmation>> response)
        : Step(ScriptedManagementCallKind.Control)
    {
        internal override Task<ManagementCallResult<T>> ExecuteAsync<T>(ScriptedManagementCall call, CancellationToken token)
        {
            if (typeof(T) != typeof(ManagementControlConfirmation))
            {
                return Task.FromException<ManagementCallResult<T>>(new InvalidOperationException("The control script response type does not match."));
            }
            try
            {
                ManagementCallResult<ManagementControlConfirmation> value = response(call)
                    ?? throw new InvalidOperationException("The selected control reply factory returned no result.");
                return Task.FromResult((ManagementCallResult<T>)(object)value);
            }
            catch (Exception error)
            {
                return Task.FromException<ManagementCallResult<T>>(error);
            }
        }
    }
}
