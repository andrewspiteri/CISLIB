using System.Globalization;
using System.Security.Cryptography;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.Startup;
using Microsoft.Extensions.Logging;

namespace CIS.Management;

/// <summary>Owns an explicitly selected process registration and one coalesced existing Heartbeat subscription.</summary>
/// <remarks>Construction is passive. Startup performs the direct initial attempt; shutdown signals cancellation before
/// joining actual accepted work. The host must close REST admission before waiting for this owner's drain.</remarks>
public sealed partial class ManagementRegistration : IStartable, IHeartbeatSubscriber, IAsyncDisposable
{
    private const string FingerprintTime = "2000-01-01T00:00:00.0000000Z";
    private readonly object gate = new();
    private readonly IManagementClient client;
    private readonly IHeartbeatOwnerFactory heartbeat;
    private readonly IManagementObservationSource observations;
    private readonly IManagementInventorySource inventory;
    private readonly ManagementRegistrationOptions options;
    private readonly IHealthMonitorFactory? health;
    private readonly ILogger<ManagementRegistration>? logger;
    private readonly TimeProvider clock;
    private readonly ManagedWorkAdmission admission;
    private readonly IManagedStateReconciler[] reconcilers;
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationToken stoppingToken;
    private Task? startup;
    private Task? invocation;
    private Task? stop;
    private Task? cancellation;
    private IHeartbeatOwner? owner;
    private IConditionMonitor? monitor;
    private ManagementRegistrationRequest? request;
    private ManagementObservation? pendingObservation;
    private ManagementInventorySnapshot? pendingInventory;
    private byte[]? pendingInventoryDigest;
    private byte[]? publishedInventoryDigest;
    private string registrationId = Guid.NewGuid().ToString("D");
    private string? applicationId;
    private string? lastError;
    private string? inventoryError;
    private long observationSequence;
    private long inventorySequence;
    private long failures;
    private long suppressedDiagnostics;
    private long lastAttempt;
    private long lastDiagnostic;
    private long lastInventoryUpload;
    private TimeSpan nextDelay;
    private int reconnectFailures;
    private int diagnosticCount;
    private bool hasDiagnostic;
    private bool registered;
    private bool connected;
    private bool closing;
    private bool suspended;
    private bool inventoryValid = true;
    private bool inventoryUploaded;
    private bool inventoryRefused;
    private bool inventoryUnconfirmed;
    private bool controlHealthy;
    private bool controlAvailable;
    private bool presenceAttempted;
    private long lastPresenceAttempt;
    private bool controlAttempted;
    private long lastControlAttempt;
    private readonly List<Exception> cutoffFailures = [];

    /// <summary>Captures application-owned ports and options without starting work or creating a provider.</summary>
    public ManagementRegistration(IManagementClient client, IHeartbeatOwnerFactory heartbeat,
        IManagementObservationSource observations, IManagementInventorySource inventory, ManagementRegistrationOptions options,
        TimeProvider? timeProvider = null, IHealthMonitorFactory? health = null, ILogger<ManagementRegistration>? logger = null)
        : this(new ManagedWorkAdmission(timeProvider), client, heartbeat, observations, inventory, options, null,
            timeProvider, health, logger)
    {
    }

    /// <summary>Captures the selected actual gate and finite reconciliation ports without invoking them or starting work.</summary>
    public ManagementRegistration(ManagedWorkAdmission admission, IManagementClient client, IHeartbeatOwnerFactory heartbeat,
        IManagementObservationSource observations, IManagementInventorySource inventory, ManagementRegistrationOptions options,
        IEnumerable<IManagedStateReconciler>? reconcilers = null,
        TimeProvider? timeProvider = null, IHealthMonitorFactory? health = null, ILogger<ManagementRegistration>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(heartbeat);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(admission);
        this.client = client;
        this.heartbeat = heartbeat;
        this.observations = observations;
        this.inventory = inventory;
        this.options = options.Copy();
        this.health = health;
        this.logger = logger;
        this.admission = admission;
        this.reconcilers = (reconcilers ?? []).Take(33).ToArray();
        if (this.reconcilers.Length > 32 || this.reconcilers.Any(item => item is null))
        {
            throw new ArgumentException("Select at most thirty-two actual module reconcilers.", nameof(reconcilers));
        }
        admission.BindPrerequisites(PrerequisitesReady);
        clock = timeProvider ?? TimeProvider.System;
        stoppingToken = stopping.Token;
    }

    /// <summary>Copies bounded current local facts, without a request, payload or attempt history.</summary>
    public ManagementRegistrationStatus CaptureStatus()
    {
        ManagementRegistrationStatus status;
        lock (gate)
        {
            status = new(registrationId, applicationId, registered, connected, closing, suspended,
                pendingInventory is not null, inventoryValid, inventoryError ?? lastError, failures, suppressedDiagnostics);
        }
        return status with
        {
            Admission = admission.CaptureSnapshot()
        };
    }

    /// <summary>Gets the actual local admission port; using a captured status never grants a later admission.</summary>
    public IManagedWorkAdmission Admission => admission;

    /// <summary>Validates local selection, attempts initial registration directly, then installs one recurring subscription.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource? completion = null;
        Task operation;
        lock (gate)
        {
            if (startup is null)
            {
                if (closing)
                {
                    return Task.FromCanceled(new CancellationToken(canceled: true));
                }
                completion = Completion();
                startup = completion.Task;
            }
            operation = startup;
        }
        if (completion is not null)
        {
            _ = StartCoreAsync(completion, cancellationToken);
        }
        return operation;
    }

    private async Task StartCoreAsync(TaskCompletionSource completion, CancellationToken caller)
    {
        CancellationToken selected = default;
        try
        {
            ValidateOptions();
            {
                using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, stoppingToken);
                CancellationToken token = startupCancellation.Token;
                selected = token;
                token.ThrowIfCancellationRequested();
                request = new(1, options.ReportedInstanceId ?? EnvironmentOptions.InstanceId,
                    Timestamp(options.ProcessStartedAtUtc ?? EnvironmentOptions.ProcessStartedAtUtc
                        ?? throw new ArgumentException("Select an actual process start when the native fact is unavailable.")),
                    options.ApplicationVersion ?? EnvironmentOptions.ServiceVersion);
                _ = ManagementWire.EncodeRequest(request, options.WireLimits);
                admission.BindRegistration(registrationId);
                PrepareLatest(initial: true, token);
                monitor = health?.CreateCondition(new ConditionMonitorOptions
                {
                    IsCritical = false,
                    InitiallyFlagged = false,
                    Name = "Management connectivity"
                });
                owner = heartbeat.CreateOwner();
                bool initialRegistered;
                using (var budget = new CancellationTokenSource(options.InitialRegistrationTimeout, clock))
                using (var initial = CancellationTokenSource.CreateLinkedTokenSource(token, budget.Token))
                {
                    try
                    {
                        initialRegistered = await RegisterAsync(initial.Token,
                            new ManagementCallOptions(options.InitialRegistrationTimeout)).ConfigureAwait(false);
                        initial.Token.ThrowIfCancellationRequested();
                    }
                    catch (OperationCanceledException error) when (!token.IsCancellationRequested && budget.IsCancellationRequested
                        && error.CancellationToken == initial.Token)
                    {
                        MarkFailure("management.registration.timeout");
                        initialRegistered = false;
                    }
                    catch (OperationCanceledException error) when (token.IsCancellationRequested && error.CancellationToken == initial.Token)
                    {
                        throw new OperationCanceledException(token);
                    }
                }
                token.ThrowIfCancellationRequested();
                if (!initialRegistered)
                {
                    throw new InvalidOperationException("Required initial management registration did not return a valid receipt.");
                }
                if (!await ConfirmControlAsync(token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Required live management bootstrap or module reconciliation did not complete.");
                }
                await ReportCurrentAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                SetNextAttempt();
                TimeSpan heartbeatInterval = Minimum(options.ReportingInterval,
                    Minimum(options.InitialReconnectDelay, options.ControlCheckInterval));
                owner.Service.Register(this, new HeartbeatRegistrationOptions
                {
                    Name = "Management registration",
                    Interval = heartbeatInterval,
                    InitialDelay = heartbeatInterval
                }, IsDueAsync);
            }
            completion.TrySetResult();
        }
        catch (OperationCanceledException error) when (selected.IsCancellationRequested && error.CancellationToken == selected)
        {
            completion.TrySetCanceled(error.CancellationToken);
        }
        catch (Exception error)
        {
            completion.TrySetException(CloseAdmissionAfterFault(error));
        }
    }

    private void ValidateOptions()
    {
        if (!ManagementWire.IsScalarText(options.ClientName, 64))
        {
            throw new ArgumentException("Select a bounded dedicated management client alias.");
        }
        ArgumentNullException.ThrowIfNull(options.WireLimits);
        ValidateDuration(options.InitialRegistrationTimeout, TimeSpan.FromMilliseconds(1), nameof(options.InitialRegistrationTimeout));
        ValidateDuration(options.ReportingInterval, TimeSpan.FromMilliseconds(10), nameof(options.ReportingInterval));
        ValidateDuration(options.InitialReconnectDelay, TimeSpan.FromMilliseconds(10), nameof(options.InitialReconnectDelay));
        ValidateDuration(options.MaximumReconnectDelay, TimeSpan.FromMilliseconds(10), nameof(options.MaximumReconnectDelay));
        ValidateDuration(options.ControlCheckInterval, TimeSpan.FromMilliseconds(10), nameof(options.ControlCheckInterval));
        ValidateDuration(options.ControlTimeout, TimeSpan.FromMilliseconds(1), nameof(options.ControlTimeout));
        if (options.ControlCheckInterval > TimeSpan.FromSeconds(15) || options.ControlTimeout > TimeSpan.FromSeconds(15))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Control checks and attempt budgets cannot exceed fifteen seconds.");
        }
        if (options.InitialReconnectDelay > options.MaximumReconnectDelay)
        {
            throw new ArgumentException("The first reconnect delay cannot exceed its finite cap.");
        }
    }

    private static void ValidateDuration(TimeSpan duration, TimeSpan minimum, string name)
    {
        if (duration < minimum || duration > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(name, "Select a duration within the documented finite interval.");
        }
    }

    private Task<bool> IsDueAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return Task.FromResult(!closing && !suspended && clock.GetElapsedTime(lastAttempt) >= nextDelay);
        }
    }

    /// <summary>Performs at most one due coalesced report; overlapping direct invocations share its actual operation.</summary>
    public Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }
        TaskCompletionSource<bool>? completion = null;
        Task<bool> operation;
        lock (gate)
        {
            if (closing || suspended || startup?.IsCompletedSuccessfully != true)
            {
                return Task.FromResult(false);
            }
            if (invocation is Task<bool> active)
            {
                return active;
            }
            if (clock.GetElapsedTime(lastAttempt) < nextDelay)
            {
                return Task.FromResult(true);
            }
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            operation = completion.Task;
            invocation = operation;
        }
        _ = ReportCoreAsync(completion, cancellationToken);
        return operation;
    }

    private async Task ReportCoreAsync(TaskCompletionSource<bool> completion, CancellationToken caller)
    {
        Exception? failure = null;
        OperationCanceledException? canceled = null;
        bool succeeded = false;
        CancellationToken selected = default;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, stoppingToken);
            CancellationToken token = linked.Token;
            selected = token;
            token.ThrowIfCancellationRequested();
            bool reportsDue = !presenceAttempted || clock.GetElapsedTime(lastPresenceAttempt) >= options.ReportingInterval;
            if (reportsDue || !registered)
            {
                PrepareLatest(initial: false, token);
            }
            bool retained = registered || await RegisterAsync(token).ConfigureAwait(false);
            bool controlDue = !controlHealthy || !controlAttempted
                || clock.GetElapsedTime(lastControlAttempt) >= options.ControlCheckInterval;
            bool controlled = retained && (!controlDue || await ConfirmControlAsync(token).ConfigureAwait(false));
            bool reported = true;
            if (retained && reportsDue && controlAvailable)
            {
                reported = await ReportCurrentAsync(token).ConfigureAwait(false);
            }
            succeeded = controlled && reported;
        }
        catch (OperationCanceledException error) when (selected.IsCancellationRequested && error.CancellationToken == selected)
        {
            canceled = error;
        }
        catch (Exception error)
        {
            failure = CloseAdmissionAfterFault(error);
        }
        finally
        {
            try
            {
                SetNextAttempt();
            }
            catch (Exception error)
            {
                failure ??= error;
            }
            lock (gate)
            {
                invocation = null;
            }
        }
        if (failure is not null)
        {
            completion.TrySetException(failure);
        }
        else if (canceled is not null)
        {
            completion.TrySetCanceled(canceled.CancellationToken);
        }
        else
        {
            completion.TrySetResult(succeeded);
        }
    }

    private void PrepareLatest(bool initial, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ManagementObservationContent observation = observations.CaptureObservation(token)
            ?? throw new InvalidOperationException("The observation source returned no complete content.");
        long nextObservation = checked(observationSequence + 1);
        var observationRequest = new ManagementObservation(Decimal(nextObservation), Timestamp(clock.GetUtcNow()),
            observation.Lifecycle, observation.Health);
        byte[] encodedObservation = ManagementWire.EncodeRequest(observationRequest, options.WireLimits);
        ManagementObservation detachedObservation = ManagementWire.DecodeRequest<ManagementObservation>(encodedObservation, options.WireLimits)
            ?? throw new InvalidOperationException("The validated observation could not be detached.");
        lock (gate)
        {
            observationSequence = nextObservation;
            pendingObservation = detachedObservation;
        }
        try
        {
            ManagementInventoryContent content = inventory.CaptureInventory(token)
                ?? throw new InvalidOperationException("The inventory source returned no complete content.");
            byte[] encoded;
            try
            {
                encoded = ManagementWire.EncodeRequest(new ManagementInventorySnapshot("1", FingerprintTime,
                    content.Modules, content.Categories, content.Jobs), options.WireLimits);
            }
            catch (ArgumentException error)
            {
                throw new ManagementInventoryRejectedException("management.inventory.local_invalid", error);
            }
            ManagementInventorySnapshot detached = ManagementWire.DecodeRequest<ManagementInventorySnapshot>(encoded, options.WireLimits)
                ?? throw new ManagementInventoryRejectedException("management.inventory.local_invalid");
            byte[] digest = SHA256.HashData(encoded);
            lock (gate)
            {
                inventoryValid = true;
                inventoryError = null;
                if (!inventoryUnconfirmed && publishedInventoryDigest is not null
                    && digest.AsSpan().SequenceEqual(publishedInventoryDigest))
                {
                    pendingInventory = null;
                    pendingInventoryDigest = null;
                    inventoryRefused = false;
                }
                else if (pendingInventoryDigest is null || !digest.AsSpan().SequenceEqual(pendingInventoryDigest))
                {
                    long nextInventory = checked(inventorySequence + 1);
                    ManagementInventorySnapshot candidate = detached with
                    {
                        Sequence = Decimal(nextInventory),
                        ObservedAtUtc = Timestamp(clock.GetUtcNow())
                    };
                    try
                    {
                        // The real sequence can use more bytes than the fingerprint placeholder.
                        _ = ManagementWire.EncodeRequest(candidate, options.WireLimits);
                    }
                    catch (ArgumentException error)
                    {
                        throw new ManagementInventoryRejectedException("management.inventory.local_invalid", error);
                    }
                    inventorySequence = nextInventory;
                    pendingInventory = candidate;
                    pendingInventoryDigest = digest;
                }
            }
        }
        catch (ManagementInventoryRejectedException error)
        {
            lock (gate)
            {
                inventoryValid = false;
                inventoryError = error.Code;
                connected = false;
            }
            admission.Invalidate();
            Flag(error.Code);
            Diagnose(error.Code);
            if (initial)
            {
                throw;
            }
        }
    }

    private async Task<bool> RegisterAsync(CancellationToken token, ManagementCallOptions? callOptions = null)
    {
        ManagementCallResult<ManagementRegistrationReceipt> result = await client.RegisterAsync(registrationId,
            request ?? throw new InvalidOperationException("Registration facts have not been initialized."), callOptions, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!result.Exchange.Success || result.Exchange.Value is not { } receipt)
        {
            Refused(result);
            return false;
        }
        if (receipt.RegistrationId != registrationId || !ManagementWire.IsUuid(receipt.ApplicationId)
            || !ManagementWire.IsTimestamp(receipt.RegisteredAtUtc))
        {
            MarkFailure("management.registration.invalid_receipt");
            return false;
        }
        lock (gate)
        {
            registered = true;
            applicationId = receipt.ApplicationId;
        }
        return true;
    }

    private async Task<bool> ReportCurrentAsync(CancellationToken token)
    {
        try
        {
            return await ReportPreparedAsync(token).ConfigureAwait(false);
        }
        finally
        {
            lastPresenceAttempt = clock.GetTimestamp();
            presenceAttempted = true;
        }
    }

    private async Task<bool> ReportPreparedAsync(CancellationToken token)
    {
        ManagementObservation? observation = pendingObservation;
        if (observation is not null)
        {
            ManagementCallResult<ManagementReportReceipt> result = await client.ReportObservationAsync(registrationId,
                observation, cancellationToken: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Accepted(result, "observation", observation.Sequence))
            {
                return false;
            }
            lock (gate)
            {
                pendingObservation = null;
            }
        }
        ManagementInventorySnapshot? snapshot = pendingInventory;
        if (snapshot is not null && (!inventoryUploaded || clock.GetElapsedTime(lastInventoryUpload) >= options.ReportingInterval))
        {
            lastInventoryUpload = clock.GetTimestamp();
            inventoryUploaded = true;
            // A lost result or original fault cannot prove that this replacement was not accepted remotely.
            // Latch before invoking the port so thrown and canceled calls retain the same conservative fact.
            inventoryUnconfirmed = true;
            ManagementCallResult<ManagementReportReceipt> result = await client.ReportInventoryAsync(registrationId,
                snapshot, cancellationToken: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Accepted(result, "inventory", snapshot.Sequence))
            {
                lock (gate)
                {
                    inventoryRefused = true;
                }
                return false;
            }
            lock (gate)
            {
                publishedInventoryDigest = pendingInventoryDigest;
                pendingInventory = null;
                pendingInventoryDigest = null;
                inventoryRefused = false;
                inventoryUnconfirmed = false;
            }
        }
        bool recovered;
        lock (gate)
        {
            recovered = !connected && failures != 0;
            connected = !closing && inventoryValid && !inventoryRefused && !inventoryUnconfirmed;
            if (connected)
            {
                lastError = null;
                if (controlHealthy)
                {
                    reconnectFailures = 0;
                }
            }
        }
        if (connected)
        {
            monitor?.Clear();
            if (recovered)
            {
                Diagnose("management.registration.connected");
            }
        }
        return connected;
    }

    private bool Accepted(ManagementCallResult<ManagementReportReceipt> result, string stream, string sequence)
    {
        if (!result.Exchange.Success || result.Exchange.Value is not { } receipt)
        {
            Refused(result);
            return false;
        }
        if (receipt.RegistrationId != registrationId || receipt.Stream != stream || receipt.SubmittedSequence != sequence
            || receipt.AcceptedSequence != sequence || !receipt.KnownDisposition.IsKnown
            || !ManagementWire.IsTimestamp(receipt.ReceivedAtUtc))
        {
            MarkFailure("management.registration.invalid_receipt");
            return false;
        }
        return true;
    }

    private void Refused<T>(ManagementCallResult<T> result)
    {
        string? replacement = null;
        string? code = result.Problem?.Code;
        if (registered && result.Exchange.Context.HttpStatusCode == 404
            && code is "management.registration.not_found" or "management.not_found")
        {
            lock (gate)
            {
                registrationId = Guid.NewGuid().ToString("D");
                replacement = registrationId;
                applicationId = null;
                registered = false;
                observationSequence = 0;
                inventorySequence = 0;
                pendingObservation = null;
                pendingInventory = null;
                pendingInventoryDigest = null;
                publishedInventoryDigest = null;
                inventoryUploaded = false;
                inventoryRefused = false;
                inventoryUnconfirmed = false;
                presenceAttempted = false;
            }
        }
        if (replacement is not null)
        {
            admission.BindRegistration(replacement);
        }
        if (code is "management.registration.closed" or "management.registration.conflict"
            or "management.sequence.conflict" or "management.sequence.obsolete")
        {
            lock (gate)
            {
                suspended = true;
            }
        }
        MarkFailure(code is "management.not_found" or "management.registration.not_found" or "management.registration.closed" or "management.registration.conflict"
            or "management.sequence.conflict" or "management.sequence.obsolete" ? code
            : result.Exchange.Context.FailureKind == RestFailureKind.Timeout ? "management.registration.timeout"
            : "management.registration.disconnected");
    }

    private void MarkFailure(string code, bool closeAdmission = true)
    {
        lock (gate)
        {
            connected = false;
            lastError = code;
            failures = failures == long.MaxValue ? failures : failures + 1;
            reconnectFailures = Math.Min(reconnectFailures + 1, 30);
            controlHealthy = false;
        }
        if (closeAdmission)
        {
            admission.Invalidate();
        }
        Flag(code);
        Diagnose(code);
    }

    private void Flag(string code) => monitor?.Flag(new(code, "Management reporting is disconnected or its complete inventory was refused."));

    private void SetNextAttempt()
    {
        lock (gate)
        {
            lastAttempt = clock.GetTimestamp();
            double multiplier = Math.Pow(2, Math.Min(Math.Max(reconnectFailures - 1, 0), 20));
            double reconnect = Math.Min(options.MaximumReconnectDelay.TotalMilliseconds,
                options.InitialReconnectDelay.TotalMilliseconds * multiplier * (1 + Random.Shared.NextDouble() * 0.2));
            if (controlHealthy)
            {
                TimeSpan untilControl = controlAttempted
                    ? options.ControlCheckInterval - clock.GetElapsedTime(lastControlAttempt, lastAttempt) : TimeSpan.Zero;
                TimeSpan untilReport = presenceAttempted
                    ? options.ReportingInterval - clock.GetElapsedTime(lastPresenceAttempt, lastAttempt) : TimeSpan.Zero;
                nextDelay = Minimum(untilControl, untilReport);
                if (nextDelay < TimeSpan.Zero)
                {
                    nextDelay = TimeSpan.Zero;
                }
            }
            else
            {
                nextDelay = TimeSpan.FromMilliseconds(reconnect);
            }
        }
    }

    private static TimeSpan Minimum(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private void Diagnose(string code)
    {
        bool emit;
        lock (gate)
        {
            emit = logger is not null && diagnosticCount < 16
                && (!hasDiagnostic || clock.GetElapsedTime(lastDiagnostic) >= TimeSpan.FromMinutes(1));
            if (emit)
            {
                hasDiagnostic = true;
                lastDiagnostic = clock.GetTimestamp();
                diagnosticCount++;
            }
            else
            {
                suppressedDiagnostics = suppressedDiagnostics == long.MaxValue ? suppressedDiagnostics : suppressedDiagnostics + 1;
            }
        }
        if (emit)
        {
            try
            {
                logger!.Log(code == "management.registration.connected" ? LogLevel.Information : LogLevel.Warning,
                    "Management reporting state: {Code}.", code);
            }
            catch (Exception)
            {
                // Diagnostics cannot replace a registration result or an original reporting fault.
            }
        }
    }

    /// <summary>Records a finite safe diagnostic while Heartbeat retains the original callback fault.</summary>
    public Task OnErrorAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!stoppingToken.IsCancellationRequested)
        {
            MarkFailure("management.registration.fault");
        }
        return Task.CompletedTask;
    }

    /// <summary>Closes reporting admission and signals owned cancellation without waiting for callbacks or REST.</summary>
    public void BeginStopping()
    {
        try
        {
            admission.BeginStopping();
        }
        catch (Exception error)
        {
            lock (gate)
            {
                if (cutoffFailures.Count < 8)
                {
                    cutoffFailures.Add(error);
                }
            }
        }
        TaskCompletionSource? completion = null;
        lock (gate)
        {
            if (!closing)
            {
                closing = true;
                connected = false;
                completion = Completion();
                cancellation = completion.Task;
            }
        }
        if (completion is not null)
        {
            _ = CancelCoreAsync(completion);
        }
    }

    private async Task CancelCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    /// <summary>Joins actual startup, cancellation, report and owner drain; cancellation affects only this caller's wait.</summary>
    public Task StopAsync(CancellationToken waitCancellation = default)
    {
        BeginStopping();
        TaskCompletionSource? completion = null;
        Task operation;
        lock (gate)
        {
            if (stop is null)
            {
                completion = Completion();
                stop = completion.Task;
            }
            operation = stop;
        }
        if (completion is not null)
        {
            _ = StopCoreAsync(completion);
        }
        return waitCancellation.CanBeCanceled ? operation.WaitAsync(waitCancellation) : operation;
    }

    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
        var failures = new List<Exception>();
        try
        {
            Task? initializing;
            lock (gate)
            {
                initializing = startup;
            }
            if (initializing is not null)
            {
                try
                {
                    await initializing.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The original sequential startup caller retains its original failure.
                }
            }
            Task? active;
            Task? signaling;
            lock (gate)
            {
                active = invocation;
                signaling = cancellation;
            }
            await Capture(signaling ?? Task.CompletedTask).ConfigureAwait(false);
            Task ownerDrain = owner?.CloseAsync() ?? Task.CompletedTask;
            await Task.WhenAll(Capture(active ?? Task.CompletedTask), Capture(ownerDrain)).ConfigureAwait(false);
            if (monitor is not null)
            {
                await Capture(monitor.DisposeAsync().AsTask()).ConfigureAwait(false);
            }
            lock (gate)
            {
                failures.AddRange(cutoffFailures);
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("Management reporting cleanup did not fully settle.", failures);
            }
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
        finally
        {
            stopping.Dispose();
        }

        async Task Capture(Task operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (operation.IsCanceled && stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                lock (failures)
                {
                    failures.Add(error);
                }
            }
        }
    }

    /// <summary>Joins the same actual stop operation; no final network request is required for local teardown.</summary>
    public ValueTask DisposeAsync() => new(StopAsync());

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string Decimal(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Timestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
