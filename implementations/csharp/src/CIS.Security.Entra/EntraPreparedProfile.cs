using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using Azure.Identity;
using CIS.Coordination.Hosting;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.RestClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace CIS.Security.Entra;

/// <summary>One explicitly prepared workload credential and exact policy, borrowed by sequential bootstrap and final runtimes.</summary>
/// <remarks>The application retains this owner before Build and drains REST before disposing it.
/// It owns only a credential it constructs; supplied credentials and native SDK transports remain borrowed.</remarks>
public sealed class EntraPreparedProfile : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly EntraOptionsSnapshot options;
    private readonly WorkloadIdentityCredential credential;
    private readonly bool ownsCredential;
    private readonly EntraProfileRuntime bootstrap;
    private readonly string? authenticationScheme;
    private EntraProfileRuntime? hosted;
    private object? hostedProvider;
    private Task? retirement;
    private Task? disposal;
    private bool closed;

    private EntraPreparedProfile(EntraOptionsSnapshot options, string? authenticationScheme,
        WorkloadIdentityCredential credential, bool ownsCredential, TimeProvider clock)
    {
        this.options = options;
        this.authenticationScheme = authenticationScheme;
        this.credential = credential;
        this.ownsCredential = ownsCredential;
        TimeProvider = clock;
        Policy = options.Policy;
        Binding = new(Policy, options.Profile.Alias, RestSecurityCredentialKind.RequestAuthentication);
        bootstrap = new(options, new BootstrapStatus(), clock, null, null, null, null,
            () => credential, bootstrap: true);
    }

    /// <summary>Gets the exact immutable policy that the final provider must register and retain.</summary>
    public SecurityPolicySnapshot Policy
    {
        get;
    }

    /// <summary>Gets the actual policy-owned request-authentication binding for standalone bootstrap REST.</summary>
    public RestSecurityBinding Binding
    {
        get;
    }

    /// <summary>Gets the exact preparation clock required for the adopting native runtime.</summary>
    public TimeProvider TimeProvider
    {
        get;
    }

    /// <summary>Captures detached provider options and establishes a usable native SDK credential before DI construction.</summary>
    /// <param name="policy">The captured trusted policy, reused by reference in the final provider.</param>
    /// <param name="profile">The selected outbound Entra profile.</param>
    /// <param name="options">Detached provider configuration; later changes are not adopted.</param>
    /// <param name="authenticationScheme">Exact inbound scheme when the profile also has an inbound role.</param>
    /// <param name="borrowedCredential">Optional already configured native workload credential; its owner retains disposal.</param>
    /// <param name="clock">Borrowed time provider, or the system provider.</param>
    /// <param name="cancellationToken">Preparation cancellation; actual SDK and cleanup settlement are still joined.</param>
    public static async Task<EntraPreparedProfile> PrepareAsync(SecurityPolicySnapshot policy, string profile,
        EntraSecurityOptions options, string? authenticationScheme = null,
        WorkloadIdentityCredential? borrowedCredential = null, TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        string alias = EntraText.Identifier(profile, nameof(profile), 64);
        var captured = new EntraOptionsSnapshot(policy, alias, authenticationScheme, options);
        if ((captured.Profile.Roles & SecurityProfileRoles.Outbound) == 0)
        {
            throw new ArgumentException("Preparation requires an explicitly enabled outbound workload profile.", nameof(profile));
        }
        WorkloadIdentityCredential credential = borrowedCredential
            ?? new WorkloadIdentityCredential(EntraProfileRuntime.CreateCredentialOptions(captured));
        var prepared = new EntraPreparedProfile(captured, authenticationScheme, credential, borrowedCredential is null,
            clock ?? global::System.TimeProvider.System);
        try
        {
            await prepared.bootstrap.StartAsync(cancellationToken).ConfigureAwait(false);
            SecurityFailure? failure = await prepared.bootstrap.CheckPreparedCredentialAsync(cancellationToken).ConfigureAwait(false);
            if (failure is not null)
            {
                throw new AuthenticationException("The prepared workload credential is unavailable: " + failure.Code);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return prepared;
        }
        catch (Exception original)
        {
            try
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Workload preparation and its owned cleanup both failed.", original, cleanup);
            }
            throw;
        }
    }

    /// <summary>Authenticates one final bootstrap destination through the existing native acquisition owner.</summary>
    public Task AuthenticateAsync(RestAuthenticationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (closed || retirement is not null)
            {
                context.FailWithSecurity(new(SecurityFailureKind.CredentialUnavailable,
                    "The bootstrap workload credential is no longer accepting calls."));
                return Task.CompletedTask;
            }
        }
        return bootstrap.AuthenticateAsync(context, cancellationToken);
    }

    /// <summary>Closes bootstrap authentication and joins its actual SDK calls before final runtime activation.</summary>
    /// <remarks>Drain standalone REST first. This mandatory cleanup has no cancellation that can abandon an actual SDK owner.</remarks>
    public Task RetireBootstrapAsync()
    {
        TaskCompletionSource? begin = null;
        Task work;
        lock (gate)
        {
            if (retirement is null)
            {
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                retirement = begin.Task;
            }
            work = retirement;
        }
        if (begin is not null)
        {
            _ = RetireCoreAsync(begin);
        }
        return work;
    }

    private async Task RetireCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            await bootstrap.DisposeAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    internal string Profile => options.Profile.Alias;

    internal string? AuthenticationScheme => authenticationScheme;

    internal EntraOptionsSnapshot CaptureForPolicy(SecurityPolicySnapshot policy)
    {
        Binding.ValidateForPolicy(policy);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
        }
        return options;
    }

    internal EntraPreparedProfile ClaimForProvider(SecurityPolicySnapshot policy, object provider)
    {
        Binding.ValidateForPolicy(policy);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (hostedProvider is not null && !ReferenceEquals(hostedProvider, provider))
            {
                throw new InvalidOperationException("Adopt a prepared workload credential into exactly one final provider.");
            }
            hostedProvider = provider;
            return this;
        }
    }

    internal EntraProfileRuntime CreateHostedRuntime(SecurityPolicySnapshot policy,
        ISecurityProfileStatusReporter reporter, TimeProvider clock, IHealthMonitorFactory? health,
        IHeartbeatOwnerFactory? heartbeat, ILogger<EntraProfileRuntime>? logger, IHostApplicationLifetime? lifetime,
        CoordinationHostLifetime? coordination)
    {
        Binding.ValidateForPolicy(policy);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (hosted is not null)
            {
                throw new InvalidOperationException("Adopt a prepared workload credential into exactly one final provider.");
            }
            hosted = new(options, reporter, clock, health, heartbeat, logger, lifetime, () => credential, coordination,
                beforeActivation: RequireBootstrapRetired);
            return hosted;
        }
    }

    /// <summary>Activates only this preparation's claimed final keyed runtime after actual bootstrap drain.</summary>
    /// <remarks>Receiving options are validated as in ordinary Startup; later Startup joins the same native owner.</remarks>
    public Task ActivateFinalAsync(IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();
        RequireBootstrapRetired();
        if (!ReferenceEquals(provider.GetRequiredKeyedService<EntraPreparedProfile>(Profile), this)
            || !ReferenceEquals(provider.GetRequiredService<SecurityPolicySnapshot>(), Policy)
            || !ReferenceEquals(provider.GetRequiredService<TimeProvider>(), TimeProvider))
        {
            throw new InvalidOperationException("Activate the exact prepared workload profile, immutable policy and clock in its one final provider.");
        }
        if (authenticationScheme is not null)
        {
            _ = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(authenticationScheme);
        }
        return provider.GetRequiredKeyedService<EntraProfileRuntime>(Profile).StartAsync(cancellationToken);
    }

    private void RequireBootstrapRetired()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (retirement is not { IsCompletedSuccessfully: true })
            {
                throw new InvalidOperationException("Join bootstrap retirement before activating the final workload profile.");
            }
        }
    }

    /// <summary>Joins both actual runtime owners, then disposes a constructed SDK credential exactly once.</summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? begin = null;
        Task work;
        lock (gate)
        {
            if (disposal is null)
            {
                closed = true;
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
        ExceptionDispatchInfo? fault = null;
        try
        {
            await RetireBootstrapAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            fault = ExceptionDispatchInfo.Capture(error);
        }
        EntraProfileRuntime? final;
        lock (gate)
        {
            final = hosted;
        }
        try
        {
            if (final is not null)
            {
                await final.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            fault ??= ExceptionDispatchInfo.Capture(error);
        }
        try
        {
            if (ownsCredential && credential is IDisposable owned)
            {
                owned.Dispose();
            }
        }
        catch (Exception error)
        {
            fault ??= ExceptionDispatchInfo.Capture(error);
        }
        if (fault is not null)
        {
            completion.TrySetException(fault.SourceException);
        }
        else
        {
            completion.TrySetResult();
        }
    }

    private sealed class BootstrapStatus : ISecurityProfileStatusReporter
    {
        public void Publish(SecurityProfileStatus status)
        {
        }

        public void RecordCertificateReload(string profile, SecurityCertificateReloadOutcome outcome)
        {
            throw new InvalidOperationException("Workload preparation cannot publish certificate renewal observations.");
        }
    }
}
