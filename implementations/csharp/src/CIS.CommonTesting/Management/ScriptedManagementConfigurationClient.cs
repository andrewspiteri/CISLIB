using CIS.Management;
using CIS.Management.Wire;

namespace CIS.CommonTesting;

/// <summary>The finite typed configuration API selected by a script step.</summary>
public enum ScriptedManagementConfigurationCallKind
{
    /// <summary>Definition metadata page.</summary>
    Definitions,
    /// <summary>Side-effect-free source validation.</summary>
    Validate,
    /// <summary>Side-effect-free recipient preview.</summary>
    Preview,
    /// <summary>Source metadata/history.</summary>
    Source,
    /// <summary>Original-key source publication.</summary>
    PublishSource,
    /// <summary>Immutable publication content.</summary>
    Publication,
    /// <summary>Original-key immutable release creation.</summary>
    CreateRelease,
    /// <summary>Immutable release read.</summary>
    Release,
    /// <summary>Complete assignment map read.</summary>
    Assignments,
    /// <summary>Original-key complete assignment replacement.</summary>
    ReplaceAssignments,
    /// <summary>One expected-target runtime snapshot.</summary>
    Snapshot,
    /// <summary>Separately sequenced adoption status.</summary>
    ReportStatus,
    /// <summary>Current-target adoption page.</summary>
    Status,
    /// <summary>Catalog bootstrap read.</summary>
    Catalog,
    /// <summary>Original-key manifest apply.</summary>
    ApplyCatalog,
    /// <summary>Original catalog decision recovery.</summary>
    CatalogOperation,
    /// <summary>Original publication decision recovery.</summary>
    PublicationOperation,
    /// <summary>Original release decision recovery.</summary>
    ReleaseOperation,
    /// <summary>Original assignment decision recovery.</summary>
    AssignmentOperation
}

/// <summary>Bounded safe call facts; it records no payload, ETag, grant, options or caller context.</summary>
/// <param name="Kind">Selected typed API.</param>
/// <param name="ApplicationId">Optional actual application UUID.</param>
/// <param name="RegistrationId">Optional registration UUID.</param>
/// <param name="ResourceId">Optional source/publication/release UUID.</param>
/// <param name="ConfigurationId">Optional exact portable ID.</param>
/// <param name="SchemaVersion">Optional exact positive schema text.</param>
/// <param name="OperationKey">Optional unchanged original key.</param>
/// <param name="HasRetainedSnapshot">Whether a conditional call supplied retained state, without recording that state.</param>
public sealed record ScriptedManagementConfigurationCall(ScriptedManagementConfigurationCallKind Kind,
    string? ApplicationId = null, string? RegistrationId = null, string? ResourceId = null,
    string? ConfigurationId = null, string? SchemaVersion = null, string? OperationKey = null, bool HasRetainedSnapshot = false);

/// <summary>A finite ordered substitute with bounded safe records; it establishes no native integrity, authorization or durability.</summary>
public sealed class ScriptedManagementConfigurationClient : IManagementConfigurationClient
{
    private readonly object gate = new();
    private readonly Queue<Step> steps = new();
    private readonly BoundedRecords<ScriptedManagementConfigurationCall> calls;
    private readonly int capacity;

    /// <summary>Bounds queued steps and retained call records, initially sixteen, through 65,536.</summary>
    public ScriptedManagementConfigurationClient(int capacity = 16)
    {
        calls = new(capacity);
        this.capacity = capacity;
    }

    /// <summary>Gets a detached safe call snapshot.</summary>
    public IReadOnlyList<ScriptedManagementConfigurationCall> Calls => calls.Snapshot();
    /// <summary>Gets the saturating call count since Clear.</summary>
    public long TotalCalls => calls.Total;
    /// <summary>Gets the saturating number of evicted call records.</summary>
    public long DroppedCalls => calls.Dropped;

    /// <summary>Clears pending scripts and records without abandoning admitted work.</summary>
    public void Clear()
    {
        lock (gate)
        {
            steps.Clear();
            calls.Clear();
        }
    }

    /// <summary>Queues an exact typed result.</summary>
    public void EnqueueResult<T>(ScriptedManagementConfigurationCallKind kind, ManagementConfigurationCallResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Enqueue(new ResultStep<T>(kind, result));
    }

    /// <summary>Queues the original fault; unrelated or matching OCE remains a faulted task.</summary>
    public void EnqueueException(ScriptedManagementConfigurationCallKind kind, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Enqueue(new FaultStep(kind, exception));
    }

    /// <summary>Holds one actual logical call until release or the selected cancellation behavior.</summary>
    public PendingManagementCall EnqueuePending<T>(ScriptedManagementConfigurationCallKind kind,
        ManagementConfigurationCallResult<T> result, bool observeCancellation = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        var pending = new PendingManagementCall(observeCancellation);
        Enqueue(new PendingStep<T>(kind, result, pending));
        return pending;
    }

    /// <summary>Queues a response derived from the independently supplied target, outside the script lock.</summary>
    /// <remarks>This controlled logical response is not wire validation, authentication or authority renewal.</remarks>
    public void EnqueueSnapshot(Func<ManagementConfigurationSnapshotTarget, ManagementConfigurationCallResult<ManagementConfigurationSnapshot>> response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Enqueue(new SnapshotStep(response));
    }

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationDefinition>>> GetDefinitionsAsync(ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementPage<ManagementConfigurationDefinition>>(new(ScriptedManagementConfigurationCallKind.Definitions), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationValidationResult>> ValidateAsync(ManagementConfigurationValidationCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationValidationResult>(new(ScriptedManagementConfigurationCallKind.Validate, ResourceId: command.SourceId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationPreview>> PreviewAsync(ManagementConfigurationPreviewCommand command,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationPreview>(new(ScriptedManagementConfigurationCallKind.Preview, command.ApplicationId,
            ConfigurationId: command.ConfigurationId, SchemaVersion: command.SchemaVersion), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationSource>> GetSourceAsync(string sourceId, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationSource>(new(ScriptedManagementConfigurationCallKind.Source, ResourceId: sourceId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> PublishSourceAsync(string sourceId, string operationKey,
        string ifMatch, ManagementConfigurationPublicationCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationOperationResult>(new(ScriptedManagementConfigurationCallKind.PublishSource, ResourceId: sourceId, OperationKey: operationKey), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationPublication>> GetPublicationAsync(string publicationId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationPublication>(new(ScriptedManagementConfigurationCallKind.Publication, ResourceId: publicationId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> CreateReleaseAsync(string applicationId, string configurationId,
        string schemaVersion, string operationKey, string ifMatch, ManagementConfigurationReleaseCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationOperationResult>(new(ScriptedManagementConfigurationCallKind.CreateRelease, applicationId,
            ConfigurationId: configurationId, SchemaVersion: schemaVersion, OperationKey: operationKey), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationRelease>> GetReleaseAsync(string releaseId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationRelease>(new(ScriptedManagementConfigurationCallKind.Release, ResourceId: releaseId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationAssignmentMap>> GetAssignmentsAsync(string applicationId, string configurationId,
        string schemaVersion, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationAssignmentMap>(new(ScriptedManagementConfigurationCallKind.Assignments, applicationId,
            ConfigurationId: configurationId, SchemaVersion: schemaVersion), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> ReplaceAssignmentsAsync(string applicationId, string configurationId,
        string schemaVersion, string operationKey, string ifMatch, ManagementConfigurationAssignmentCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationOperationResult>(new(ScriptedManagementConfigurationCallKind.ReplaceAssignments, applicationId,
            ConfigurationId: configurationId, SchemaVersion: schemaVersion, OperationKey: operationKey), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationSnapshot>> GetRegistrationConfigurationAsync(ManagementConfigurationSnapshotTarget expectedTarget,
        ManagementConfigurationSnapshot? retainedSnapshot = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationSnapshot>(new(ScriptedManagementConfigurationCallKind.Snapshot, expectedTarget.ApplicationId,
            expectedTarget.RegistrationId, ConfigurationId: expectedTarget.ConfigurationId, SchemaVersion: expectedTarget.SchemaVersion,
            HasRetainedSnapshot: retainedSnapshot is not null), cancellationToken, expectedTarget);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementReportReceipt>> ReportStatusAsync(string registrationId, ManagementConfigurationStatusReport report,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementReportReceipt>(new(ScriptedManagementConfigurationCallKind.ReportStatus, RegistrationId: registrationId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationAdoptionEntry>>> GetStatusAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementPage<ManagementConfigurationAdoptionEntry>>(new(ScriptedManagementConfigurationCallKind.Status, applicationId), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationCatalog>> GetCatalogAsync(ManagementCallOptions? options = null,
        CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationCatalog>(new(ScriptedManagementConfigurationCallKind.Catalog), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> ApplyCatalogAsync(string operationKey, string ifMatch,
        ManagementConfigurationCatalogCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationOperationResult>(new(ScriptedManagementConfigurationCallKind.ApplyCatalog, OperationKey: operationKey), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetCatalogOperationAsync(string operationKey,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationOperationResult>(new(ScriptedManagementConfigurationCallKind.CatalogOperation, OperationKey: operationKey), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetPublicationOperationAsync(string sourceId, string operationKey,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationOperationResult>(new(ScriptedManagementConfigurationCallKind.PublicationOperation, ResourceId: sourceId, OperationKey: operationKey), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetReleaseOperationAsync(string applicationId, string configurationId,
        string schemaVersion, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationOperationResult>(new(ScriptedManagementConfigurationCallKind.ReleaseOperation, applicationId,
            ConfigurationId: configurationId, SchemaVersion: schemaVersion, OperationKey: operationKey), cancellationToken);
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetAssignmentOperationAsync(string applicationId, string configurationId,
        string schemaVersion, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationOperationResult>(new(ScriptedManagementConfigurationCallKind.AssignmentOperation, applicationId,
            ConfigurationId: configurationId, SchemaVersion: schemaVersion, OperationKey: operationKey), cancellationToken);

    private void Enqueue(Step step)
    {
        if (!Enum.IsDefined(step.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }
        lock (gate)
        {
            if (steps.Count >= capacity)
            {
                throw new InvalidOperationException("The finite configuration script capacity is full.");
            }
            steps.Enqueue(step);
        }
    }

    private Task<ManagementConfigurationCallResult<T>> Dispatch<T>(ScriptedManagementConfigurationCall call, CancellationToken token,
        ManagementConfigurationSnapshotTarget? target = null)
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromCanceled<ManagementConfigurationCallResult<T>>(token);
        }
        bool applicationRequired = call.Kind is ScriptedManagementConfigurationCallKind.Preview or ScriptedManagementConfigurationCallKind.CreateRelease
            or ScriptedManagementConfigurationCallKind.Assignments or ScriptedManagementConfigurationCallKind.ReplaceAssignments
            or ScriptedManagementConfigurationCallKind.Snapshot or ScriptedManagementConfigurationCallKind.Status
            or ScriptedManagementConfigurationCallKind.ReleaseOperation or ScriptedManagementConfigurationCallKind.AssignmentOperation;
        bool registrationRequired = call.Kind is ScriptedManagementConfigurationCallKind.Snapshot or ScriptedManagementConfigurationCallKind.ReportStatus;
        bool resourceRequired = call.Kind is ScriptedManagementConfigurationCallKind.Validate or ScriptedManagementConfigurationCallKind.Source
            or ScriptedManagementConfigurationCallKind.PublishSource or ScriptedManagementConfigurationCallKind.Publication
            or ScriptedManagementConfigurationCallKind.Release or ScriptedManagementConfigurationCallKind.PublicationOperation;
        bool configurationRequired = call.Kind is ScriptedManagementConfigurationCallKind.Preview or ScriptedManagementConfigurationCallKind.CreateRelease
            or ScriptedManagementConfigurationCallKind.Assignments or ScriptedManagementConfigurationCallKind.ReplaceAssignments
            or ScriptedManagementConfigurationCallKind.Snapshot or ScriptedManagementConfigurationCallKind.ReleaseOperation
            or ScriptedManagementConfigurationCallKind.AssignmentOperation;
        bool keyRequired = call.Kind is ScriptedManagementConfigurationCallKind.PublishSource or ScriptedManagementConfigurationCallKind.CreateRelease
            or ScriptedManagementConfigurationCallKind.ReplaceAssignments or ScriptedManagementConfigurationCallKind.ApplyCatalog
            or ScriptedManagementConfigurationCallKind.CatalogOperation or ScriptedManagementConfigurationCallKind.PublicationOperation
            or ScriptedManagementConfigurationCallKind.ReleaseOperation or ScriptedManagementConfigurationCallKind.AssignmentOperation;
        if (applicationRequired && call.ApplicationId is null || registrationRequired && call.RegistrationId is null
            || resourceRequired && call.ResourceId is null || configurationRequired && (call.ConfigurationId is null || call.SchemaVersion is null)
            || keyRequired && call.OperationKey is null
            || new[] { call.ApplicationId, call.RegistrationId, call.ResourceId }.Any(id => id is not null && !ManagementWire.IsUuid(id))
            || call.ConfigurationId is not null && !ManagementWire.IsOperationKey(call.ConfigurationId)
            || call.SchemaVersion is not null && !ManagementWire.IsOrderedInteger(call.SchemaVersion)
            || call.OperationKey is not null && !ManagementWire.IsOperationKey(call.OperationKey))
        {
            return Task.FromException<ManagementConfigurationCallResult<T>>(new ArgumentException("Supply bounded exact call identities."));
        }
        Step step;
        lock (gate)
        {
            if (steps.Count == 0)
            {
                return Task.FromException<ManagementConfigurationCallResult<T>>(new InvalidOperationException("No configuration response is scripted."));
            }
            step = steps.Dequeue();
            calls.Add(call);
        }
        return step.Kind == call.Kind ? step.ExecuteAsync<T>(target, token)
            : Task.FromException<ManagementConfigurationCallResult<T>>(new InvalidOperationException("The configuration script API kind does not match."));
    }

    private abstract class Step(ScriptedManagementConfigurationCallKind kind)
    {
        internal ScriptedManagementConfigurationCallKind Kind { get; } = kind;
        internal abstract Task<ManagementConfigurationCallResult<T>> ExecuteAsync<T>(ManagementConfigurationSnapshotTarget? target, CancellationToken token);
    }

    private sealed class ResultStep<TValue>(ScriptedManagementConfigurationCallKind kind, ManagementConfigurationCallResult<TValue> value) : Step(kind)
    {
        internal override Task<ManagementConfigurationCallResult<T>> ExecuteAsync<T>(ManagementConfigurationSnapshotTarget? target, CancellationToken token)
            => typeof(T) == typeof(TValue) ? Task.FromResult((ManagementConfigurationCallResult<T>)(object)value)
                : Task.FromException<ManagementConfigurationCallResult<T>>(new InvalidOperationException("The configuration response type does not match."));
    }

    private sealed class FaultStep(ScriptedManagementConfigurationCallKind kind, Exception error) : Step(kind)
    {
        internal override Task<ManagementConfigurationCallResult<T>> ExecuteAsync<T>(ManagementConfigurationSnapshotTarget? target, CancellationToken token)
            => Task.FromException<ManagementConfigurationCallResult<T>>(error);
    }

    private sealed class PendingStep<TValue>(ScriptedManagementConfigurationCallKind kind, ManagementConfigurationCallResult<TValue> value,
        PendingManagementCall pending) : Step(kind)
    {
        internal override async Task<ManagementConfigurationCallResult<T>> ExecuteAsync<T>(ManagementConfigurationSnapshotTarget? target, CancellationToken token)
        {
            if (typeof(T) != typeof(TValue))
            {
                throw new InvalidOperationException("The configuration response type does not match.");
            }
            await pending.WaitAsync(token).ConfigureAwait(false);
            return (ManagementConfigurationCallResult<T>)(object)value;
        }
    }

    private sealed class SnapshotStep(Func<ManagementConfigurationSnapshotTarget, ManagementConfigurationCallResult<ManagementConfigurationSnapshot>> response)
        : Step(ScriptedManagementConfigurationCallKind.Snapshot)
    {
        internal override Task<ManagementConfigurationCallResult<T>> ExecuteAsync<T>(ManagementConfigurationSnapshotTarget? target, CancellationToken token)
        {
            try
            {
                if (typeof(T) != typeof(ManagementConfigurationSnapshot) || target is null)
                {
                    throw new InvalidOperationException("Select a snapshot target and response.");
                }
                return Task.FromResult((ManagementConfigurationCallResult<T>)(object)(response(target)
                    ?? throw new InvalidOperationException("The snapshot factory returned no response.")));
            }
            catch (Exception error) { return Task.FromException<ManagementConfigurationCallResult<T>>(error); }
        }
    }
}
