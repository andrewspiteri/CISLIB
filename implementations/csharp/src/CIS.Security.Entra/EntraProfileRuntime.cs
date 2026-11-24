using Azure.Core;
using Azure.Identity;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.RestClient;
using CIS.Coordination.Hosting;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CIS.Security.Entra;

// One selected profile owns one reused SDK credential and all of its admitted acquisition work.
internal sealed class EntraProfileRuntime : IAsyncDisposable, ISecurityCredentialControl
{
    private readonly object gate = new();
    private readonly object validationOwner = new();
    private readonly EntraOptionsSnapshot options;
    private readonly ISecurityProfileStatusReporter reporter;
    private readonly TimeProvider clock;
    private readonly IHealthMonitorFactory? health;
    private readonly IHeartbeatOwnerFactory? heartbeat;
    private readonly ILogger<EntraProfileRuntime>? logger;
    private readonly Func<WorkloadIdentityCredential>? borrowedCredential;
    private readonly bool bootstrap;
    private readonly Action? beforeActivation;
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim acquisitionGate = new(1, 1);
    private readonly TaskCompletionSource cutoffSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration stopRegistration;
    private readonly CoordinationHostLifetime? coordination;
    private WorkloadIdentityCredential? credential;
    private IConfigurationManager<OpenIdConnectConfiguration>? metadataManager;
    private EntraJwtEvents? jwtEvents;
    private IConditionMonitor? monitor;
    private IHeartbeatOwner? statusOwner;
    private Task? statusDrain;
    private ExceptionDispatchInfo? cutoffFailure;
    private Task? startup;
    private Task? recovery;
    private Task? disposal;
    private TaskCompletionSource? drain;
    private bool started;
    private bool admissionClosed;
    private int pending;
    private SecurityProfileAvailability? lastAvailability;
    private string? lastFailureCode;
    private DateTimeOffset? lastObservedExpiry;
    private long lastAcquisitionAttempt;
    private bool hasAcquisitionAttempt;
    private long lastDiagnostic;
    private int diagnosticCount;

    internal EntraProfileRuntime(EntraOptionsSnapshot options, ISecurityProfileStatusReporter reporter,
        TimeProvider clock, IHealthMonitorFactory? health, IHeartbeatOwnerFactory? heartbeat, ILogger<EntraProfileRuntime>? logger,
        IHostApplicationLifetime? lifetime, Func<WorkloadIdentityCredential>? borrowedCredential,
        CoordinationHostLifetime? coordination = null, bool bootstrap = false, Action? beforeActivation = null)
    {
        this.options = options;
        this.reporter = reporter;
        this.clock = clock;
        this.health = health;
        this.heartbeat = heartbeat;
        this.logger = logger;
        this.borrowedCredential = borrowedCredential;
        this.coordination = coordination;
        this.bootstrap = bootstrap;
        this.beforeActivation = beforeActivation;
        CancellationToken cutoff = coordination?.DependencyStopping ?? lifetime?.ApplicationStopping ?? default;
        stopRegistration = cutoff.Register(BeginStopping);
    }

    public string Profile => options.Profile.Alias;

    internal EntraOptionsSnapshot Options => options;
    internal object ValidationOwner => validationOwner;

    internal EntraJwtEvents GetJwtEvents()
    {
        lock (gate)
        {
            return jwtEvents ??= new(this);
        }
    }

    internal IConfigurationManager<OpenIdConnectConfiguration> GetMetadataManager()
    {
        lock (gate)
        {
            // This is the native handler's normal discovery/key-refresh owner, not a CIS token cache.
            return metadataManager ??= new ConfigurationManager<OpenIdConnectConfiguration>(
                options.Issuer + "/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true });
        }
    }

    internal Task StartAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource? begin = null;
        Task work;
        lock (gate)
        {
            if (startup is null)
            {
                ObjectDisposedException.ThrowIf(admissionClosed, this);
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                startup = begin.Task;
            }
            work = startup;
        }
        if (begin is not null)
        {
            _ = StartCoreAsync(begin, cancellationToken);
        }
        return work;
    }

    private async Task StartCoreAsync(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                stopping.Token.ThrowIfCancellationRequested();
                beforeActivation?.Invoke();
                if (options.Profile.RequiredForReadiness && !bootstrap)
                {
                    monitor = (health ?? throw new InvalidOperationException("A readiness-required Entra profile needs the host Healthcheck integration."))
                        .CreateCondition(new ConditionMonitorOptions { Name = "Security: " + Profile, IsCritical = true, InitiallyFlagged = true });
                    if ((options.Profile.Roles & SecurityProfileRoles.Outbound) != 0)
                    {
                        statusOwner = (heartbeat ?? throw new InvalidOperationException("A readiness-required outgoing Entra profile needs the existing Heartbeat owner factory."))
                            .CreateOwner();
                        statusOwner.Service.Register(new CredentialAvailabilityObserver(this), new()
                        {
                            Name = "Security availability: " + Profile,
                            Interval = options.StatusCheckInterval,
                            InitialDelay = options.StatusCheckInterval
                        });
                    }
                }
                if ((options.Profile.Roles & SecurityProfileRoles.Outbound) != 0)
                {
                    credential = borrowedCredential is not null ? borrowedCredential()
                        : new WorkloadIdentityCredential(CreateCredentialOptions(options));
                    if (credential is null)
                    {
                        throw new InvalidOperationException("The borrowed workload credential factory returned null.");
                    }
                }
                started = true;
            }

            if ((options.Profile.Roles & SecurityProfileRoles.Outbound) == 0)
            {
                Publish(SecurityProfileAvailability.Available);
            }
            else
            {
                Publish(SecurityProfileAvailability.Unavailable, Unavailable("Workload acquisition has not yet established a usable credential."));
                if (options.Profile.RequiredForReadiness && !bootstrap)
                {
                    // Required warmup uses existing sequential Startup and its cancellation budget.
                    _ = await AcquireAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    internal static WorkloadIdentityCredentialOptions CreateCredentialOptions(EntraOptionsSnapshot options)
    {
        var native = new WorkloadIdentityCredentialOptions
        {
            TenantId = options.TenantId,
            ClientId = options.ClientId,
            TokenFilePath = options.TokenFile,
            AuthorityHost = options.AuthorityHost,
            IsUnsafeSupportLoggingEnabled = false
        };
        native.AdditionallyAllowedTenants.Clear();
        native.Diagnostics.IsLoggingContentEnabled = false;
        if (options.Transport is { } transport)
        {
            native.Transport = transport;
        }
        return native;
    }

    internal async Task<SecurityFailure?> CheckPreparedCredentialAsync(CancellationToken cancellationToken)
        => (await AcquireAsync(cancellationToken).ConfigureAwait(false)).Failure;

    internal async Task AuthenticateAsync(RestAuthenticationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(context.Url, UriKind.Absolute, out Uri? destination)
            || destination.Scheme != Uri.UriSchemeHttps
            || !options.Profile.AllowedOrigins.Contains(destination.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase))
        {
            context.FailWithSecurity(new(SecurityFailureKind.CredentialInvalid,
                "The finalized destination is outside the workload credential's configured HTTPS origins."));
            return;
        }
        Acquisition acquired = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (acquired.Failure is { } failure)
        {
            context.FailWithSecurity(failure);
            return;
        }
        context.Headers.Set("Authorization", "Bearer " + acquired.Token.Token);
    }

    private async ValueTask<Acquisition> AcquireAsync(CancellationToken cancellationToken,
        bool recoveryCheck = false, bool explicitRequest = false)
    {
        WorkloadIdentityCredential selected;
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (admissionClosed)
            {
                return new(default, Unavailable("The workload credential is stopping."));
            }
            if (!started || credential is null)
            {
                return new(default, Unavailable("The outgoing workload integration has not started."));
            }
            selected = credential;
            pending++;
        }
        bool entered = false;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
            await acquisitionGate.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            linked.Token.ThrowIfCancellationRequested();
            lock (gate)
            {
                // A foreground call may have recovered while this background check waited.
                if (recoveryCheck && (!explicitRequest && HasUsableObservation() || !RecoverySpacingElapsed()))
                {
                    return new(default, null);
                }
                lastAcquisitionAttempt = clock.GetTimestamp();
                hasAcquisitionAttempt = true;
            }
            AccessToken token;
            try
            {
                token = await selected.GetTokenAsync(new TokenRequestContext([options.Scope!]), linked.Token).ConfigureAwait(false);
            }
            catch (CredentialUnavailableException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                linked.Token.ThrowIfCancellationRequested();
                return Fail(Unavailable("The configured workload credential is unavailable."));
            }
            catch (AuthenticationFailedException error)
            {
                cancellationToken.ThrowIfCancellationRequested();
                linked.Token.ThrowIfCancellationRequested();
                // The SDK's exception can represent federation, provider or network failures.
                // Do not infer expiry/invalidity from its unrestricted message or disclose it.
                return Fail(ClassifyProviderFailure(error));
            }
            catch (IOException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                linked.Token.ThrowIfCancellationRequested();
                return Fail(Unavailable("The projected workload assertion source is unavailable."));
            }
            catch (UnauthorizedAccessException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                linked.Token.ThrowIfCancellationRequested();
                return Fail(Unavailable("The projected workload assertion source is unavailable."));
            }
            cancellationToken.ThrowIfCancellationRequested();
            linked.Token.ThrowIfCancellationRequested();
            if (token.ExpiresOn <= clock.GetUtcNow())
            {
                return Fail(new(SecurityFailureKind.CredentialExpired, "The SDK returned an already expired workload credential."), token.ExpiresOn);
            }
            if (!IsBearerValue(token.Token))
            {
                return Fail(new(SecurityFailureKind.CredentialInvalid, "The SDK returned an invalid bearer credential representation."));
            }
            Publish(SecurityProfileAvailability.Available, expiry: token.ExpiresOn);
            return new(token, null);
        }
        finally
        {
            if (entered)
            {
                acquisitionGate.Release();
            }
            lock (gate)
            {
                pending--;
                if (pending == 0)
                {
                    drain?.TrySetResult();
                }
            }
        }
    }

    private Acquisition Fail(SecurityFailure failure, DateTimeOffset? expiry = null)
    {
        Publish(failure.Kind == SecurityFailureKind.CredentialExpired
            ? SecurityProfileAvailability.Expired : SecurityProfileAvailability.Unavailable, failure, expiry);
        return new(default, failure);
    }

    private static bool IsBearerValue(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 64 * 1024)
        {
            return false;
        }
        foreach (char character in token)
        {
            if (character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z')
                and not (>= '0' and <= '9') and not '-' and not '.' and not '_' and not '~'
                and not '+' and not '/' and not '=')
            {
                return false;
            }
        }
        return true;
    }

    public ValueTask RequestSourceRecheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(admissionClosed, this);
        }
        Task work = RecheckAvailabilityAsync(cancellationToken, explicitRequest: true);
        return new(cancellationToken.CanBeCanceled
            ? CompleteRecheckForCallerAsync(work, cancellationToken)
            : work);
    }

    private static async Task CompleteRecheckForCallerAsync(Task work, CancellationToken cancellationToken)
    {
        // Retain actual shared settlement and its original fault before observing this caller's cancellation.
        await work.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private Task RecheckAvailabilityAsync(CancellationToken cancellationToken, bool explicitRequest = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource begin;
        Task work;
        lock (gate)
        {
            if (admissionClosed || !started || (options.Profile.Roles & SecurityProfileRoles.Outbound) == 0)
            {
                return Task.CompletedTask;
            }
            if (recovery is { IsCompleted: false })
            {
                return recovery;
            }
            if (!explicitRequest && HasUsableObservation())
            {
                return Task.CompletedTask;
            }
            if (!RecoverySpacingElapsed())
            {
                return Task.CompletedTask;
            }
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            recovery = work = begin.Task;
        }
        _ = RecheckCoreAsync(begin, cancellationToken, explicitRequest);
        return work;
    }

    // Called only under gate. Expiry is metadata about the last SDK result, not proof a replacement is unavailable.
    private bool HasUsableObservation() => lastAvailability == SecurityProfileAvailability.Available
        && lastObservedExpiry > clock.GetUtcNow();

    private bool RecoverySpacingElapsed()
    {
        TimeSpan minimumSpacing = options.StatusCheckInterval < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromSeconds(1) : options.StatusCheckInterval;
        return !hasAcquisitionAttempt || clock.GetElapsedTime(lastAcquisitionAttempt, clock.GetTimestamp()) >= minimumSpacing;
    }

    private async Task RecheckCoreAsync(TaskCompletionSource completion, CancellationToken cancellationToken, bool explicitRequest)
    {
        try
        {
            // The SDK owns acquisition and its cache. No bearer value is retained by the recovery task.
            _ = await AcquireAsync(cancellationToken, recoveryCheck: true, explicitRequest: explicitRequest).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (OperationCanceledException error)
        {
            completion.TrySetCanceled(error.CancellationToken);
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private void Publish(SecurityProfileAvailability availability, SecurityFailure? failure = null, DateTimeOffset? expiry = null)
    {
        bool log;
        IConditionMonitor? condition;
        lock (gate)
        {
            if (admissionClosed && availability != SecurityProfileAvailability.Stopping)
            {
                return;
            }
            bool changed = availability != lastAvailability || failure?.Code != lastFailureCode;
            lastAvailability = availability;
            lastFailureCode = failure?.Code;
            if (expiry is not null)
            {
                lastObservedExpiry = expiry;
            }
            expiry = lastObservedExpiry;
            long now = clock.GetTimestamp();
            log = changed && diagnosticCount < 64
                && (diagnosticCount < 4 || clock.GetElapsedTime(lastDiagnostic, now) >= TimeSpan.FromMinutes(1));
            if (log)
            {
                diagnosticCount++;
                lastDiagnostic = now;
            }
            condition = monitor;
        }
        reporter.Publish(new(Profile, availability, failure, expiry));
        if (condition is not null)
        {
            if (availability == SecurityProfileAvailability.Available)
            {
                condition.Clear();
            }
            else
            {
                condition.Flag(new(failure?.Code ?? "security.stopping", failure?.Message ?? "The workload integration is stopping."));
            }
        }
        if (log && logger is not null)
        {
            try
            {
                logger.Log(availability is SecurityProfileAvailability.Unavailable or SecurityProfileAvailability.Expired
                    ? LogLevel.Warning : LogLevel.Information, new EventId(6200, "security.entra.availability"),
                    "Security profile {Profile} availability {Availability}; reason {Reason}",
                    Profile, availability, failure?.Code ?? "none");
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            {
                // Diagnostics do not change acquisition, cancellation or authorization outcomes.
            }
        }
    }

    private void BeginStopping()
    {
        lock (gate)
        {
            if (admissionClosed)
            {
                return;
            }
            admissionClosed = true;
        }
        try
        {
            stopping.Cancel();
        }
        catch (Exception error)
        {
            RecordCutoffFailure(error);
        }
        try
        {
            statusDrain = statusOwner?.CloseAsync();
        }
        catch (Exception error)
        {
            RecordCutoffFailure(error);
        }
        try
        {
            Publish(SecurityProfileAvailability.Stopping);
        }
        catch (Exception error)
        {
            RecordCutoffFailure(error);
        }
        finally
        {
            cutoffSettled.TrySetResult();
        }
    }

    private void RecordCutoffFailure(Exception error)
    {
        lock (gate)
        {
            cutoffFailure ??= ExceptionDispatchInfo.Capture(error);
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? begin = null;
        Task work;
        lock (gate)
        {
            if (disposal is null)
            {
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = begin.Task;
            }
            work = disposal;
        }
        if (begin is not null)
        {
            _ = DisposeCoreAsync(begin);
        }
        return new(work);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            if (coordination is not null)
            {
                await coordination.WaitForDependenciesAsync().ConfigureAwait(false);
            }

            BeginStopping();
            await cutoffSettled.Task.ConfigureAwait(false);
            Task? starting;
            Task? recovering;
            Task draining;
            lock (gate)
            {
                starting = startup;
                recovering = recovery;
                drain ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                if (pending == 0)
                {
                    drain.TrySetResult();
                }
                draining = drain.Task;
            }
            if (starting is not null)
            {
                try
                {
                    await starting.ConfigureAwait(false);
                }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
                {
                    // Startup's caller retains its original fault; disposal must still join owned work.
                }
            }
            ExceptionDispatchInfo? fault;
            lock (gate)
            {
                fault = cutoffFailure;
            }
            try
            {
                if (recovering is not null)
                {
                    await recovering.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                // The cutoff cancels recovery, but disposal still joins the actual SDK operation.
            }
            catch (Exception error)
            {
                fault ??= ExceptionDispatchInfo.Capture(error);
            }
            try
            {
                await draining.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                fault ??= ExceptionDispatchInfo.Capture(error);
            }
            try
            {
                if (statusDrain is not null)
                {
                    await statusDrain.ConfigureAwait(false);
                }
                if (statusOwner is not null)
                {
                    await statusOwner.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception error)
            {
                fault ??= ExceptionDispatchInfo.Capture(error);
            }
            try
            {
                if (borrowedCredential is null && credential is IDisposable owned)
                {
                    owned.Dispose();
                }
            }
            catch (Exception error)
            {
                fault ??= ExceptionDispatchInfo.Capture(error);
            }
            try
            {
                if (monitor is not null)
                {
                    await monitor.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception error)
            {
                fault ??= ExceptionDispatchInfo.Capture(error);
            }
            try
            {
                stopRegistration.Dispose();
            }
            catch (Exception error)
            {
                fault ??= ExceptionDispatchInfo.Capture(error);
            }
            stopping.Dispose();
            acquisitionGate.Dispose();
            fault?.Throw();
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private static SecurityFailure Unavailable(string message) => new(SecurityFailureKind.CredentialUnavailable, message);

    private static SecurityFailure ClassifyProviderFailure(AuthenticationFailedException error)
    {
        Exception? current = error;
        for (int depth = 0; current is not null && depth < 4; depth++, current = current.InnerException)
        {
            if (current is MsalServiceException provider)
            {
                string? known = provider.ErrorCode switch
                {
                    "invalid_client" => "invalid_client",
                    "invalid_grant" => "invalid_grant",
                    "invalid_scope" => "invalid_scope",
                    "unauthorized_client" => "unauthorized_client",
                    _ => null
                };
                if (known is not null)
                {
                    return new(SecurityFailureKind.CredentialInvalid,
                        "The workload provider rejected its configured credential or API scope.", providerCode: known);
                }
            }
        }
        return Unavailable("The workload provider could not supply a usable credential.");
    }

    private readonly record struct Acquisition(AccessToken Token, SecurityFailure? Failure);

    private sealed class CredentialAvailabilityObserver(EntraProfileRuntime runtime) : IHeartbeatSubscriber
    {
        public async Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
        {
            await runtime.RecheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        public Task OnErrorAsync(Exception exception) => Task.CompletedTask;
    }
}
