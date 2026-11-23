using CIS.Healthcheck;
using CIS.Management.Wire;

namespace CIS.Management;

/// <summary>Reported application facts without a reporter-owned sequence, timestamp or credential identity.</summary>
public sealed record ManagementObservationContent(string Lifecycle, string Health);

/// <summary>A complete reported catalog without a reporter-owned sequence or timestamp.</summary>
/// <remarks>Capture returns the whole selected catalog. The reporting owner validates and detaches it before retention.</remarks>
public sealed record ManagementInventoryContent(IReadOnlyList<ManagementModuleInventory> Modules,
    IReadOnlyList<ManagementCategoryInventory> Categories, IReadOnlyList<ManagementJobInventory> Jobs);

/// <summary>Copies current local observation facts without I/O, collector execution or authority changes.</summary>
public interface IManagementObservationSource
{
    /// <summary>Captures current facts. Cancellation is cooperative and never certifies unfinished work has stopped.</summary>
    ManagementObservationContent CaptureObservation(CancellationToken cancellationToken = default);
}

/// <summary>Copies a complete local inventory without resolving handlers, performing I/O or changing enablement.</summary>
public interface IManagementInventorySource
{
    /// <summary>Captures all selected content or refuses it explicitly; partial catalogs must never be returned.</summary>
    ManagementInventoryContent CaptureInventory(CancellationToken cancellationToken = default);
}

/// <summary>An explicit local complete-inventory refusal, distinct from a remote transport failure.</summary>
public sealed class ManagementInventoryRejectedException : Exception
{
    /// <summary>Creates a bounded, safe local refusal without retaining rejected payloads.</summary>
    public ManagementInventoryRejectedException(string code, Exception? innerException = null)
        : base("The complete management inventory could not be captured within its selected contract.", innerException)
    {
        if (code is not ("management.inventory.local_limit" or "management.inventory.local_invalid"
            or "management.inventory.local_changed"))
        {
            throw new ArgumentException("Select a documented local inventory refusal.", nameof(code));
        }
        Code = code;
    }

    /// <summary>Gets the finite local reason without rejected content.</summary>
    public string Code
    {
        get;
    }
}

/// <summary>Projects the existing detached health observation without running collectors.</summary>
public sealed class HealthManagementObservationSource(IHealthSnapshotSource health) : IManagementObservationSource
{
    /// <inheritdoc />
    public ManagementObservationContent CaptureObservation(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(health);
        HealthSnapshot snapshot = health.CaptureSnapshot();
        string lifecycle = snapshot.Application.IsStopping ? "stopping"
            : snapshot.Application.StartupCompleted ? "ready" : "starting";
        return new(lifecycle, snapshot.HealthStatus == HealthStatus.Healthy ? "healthy" : "unhealthy");
    }
}

/// <summary>Passive opt-in registration inputs, copied before startup and validated by the startup participant.</summary>
public sealed class ManagementRegistrationOptions
{
    /// <summary>Selects the existing dedicated keyed management/REST alias.</summary>
    public ManagementRegistrationOptions(string clientName) => ClientName = clientName;

    /// <summary>Gets the dedicated alias; its native REST profile must have LogCalls disabled.</summary>
    public string ClientName
    {
        get;
    }
    /// <summary>Gets or sets the retained legacy option; selected managed execution always requires live bootstrap.</summary>
    public bool RequireInitialRegistration
    {
        get; set;
    }
    /// <summary>Gets or sets the direct initial registration budget, initially thirty seconds, from one millisecond through one day.</summary>
    public TimeSpan InitialRegistrationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the completion-based report interval, initially thirty seconds, from ten milliseconds through one day.</summary>
    public TimeSpan ReportingInterval { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the control check interval, initially five seconds, from ten milliseconds through fifteen seconds.</summary>
    public TimeSpan ControlCheckInterval { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets the control/reconciliation attempt budget, initially five seconds, at most fifteen seconds.</summary>
    public TimeSpan ControlTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets the first reconnect delay, initially five seconds, from ten milliseconds through one day, before bounded positive jitter.</summary>
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets the finite reconnect cap, initially sixty seconds, no smaller than the first delay and at most one day.</summary>
    public TimeSpan MaximumReconnectDelay { get; set; } = TimeSpan.FromSeconds(60);
    /// <summary>Gets or sets the retained legacy policy; managed admission affects readiness without failing process liveness.</summary>
    public bool IsHealthCritical
    {
        get; set;
    }
    /// <summary>Gets or sets an optional reported label; omission uses EnvironmentOptions.InstanceId, never verified identity.</summary>
    public string? ReportedInstanceId
    {
        get; set;
    }
    /// <summary>Gets or sets the actual process start; omission uses the native process-start fact and fails if unavailable.</summary>
    public DateTimeOffset? ProcessStartedAtUtc
    {
        get; set;
    }
    /// <summary>Gets or sets the reported version; omission uses the exact native ServiceVersion.</summary>
    public string? ApplicationVersion
    {
        get; set;
    }
    /// <summary>Gets or sets immutable finite complete-snapshot limits, initially the management wire defaults.</summary>
    public ManagementWireLimits WireLimits { get; set; } = new();

    internal ManagementRegistrationOptions Copy() => new(ClientName)
    {
        RequireInitialRegistration = RequireInitialRegistration,
        InitialRegistrationTimeout = InitialRegistrationTimeout,
        ReportingInterval = ReportingInterval,
        ControlCheckInterval = ControlCheckInterval,
        ControlTimeout = ControlTimeout,
        InitialReconnectDelay = InitialReconnectDelay,
        MaximumReconnectDelay = MaximumReconnectDelay,
        IsHealthCritical = IsHealthCritical,
        ReportedInstanceId = ReportedInstanceId,
        ProcessStartedAtUtc = ProcessStartedAtUtc,
        ApplicationVersion = ApplicationVersion,
        WireLimits = WireLimits
    };
}

/// <summary>Bounded local reporting facts; registration and connectivity do not establish remote health or ownership.</summary>
public sealed record ManagementRegistrationStatus(string RegistrationId, string? ApplicationId, bool IsRegistered,
    bool IsConnected, bool IsStopping, bool IsSuspended, bool InventoryPending, bool InventoryValid,
    string? LastErrorCode, long FailedAttempts, long SuppressedDiagnostics)
{
    /// <summary>Gets the current local admission observation; a retained snapshot is no permission for later work.</summary>
    public ManagedWorkAdmissionSnapshot? Admission
    {
        get; init;
    }
}
