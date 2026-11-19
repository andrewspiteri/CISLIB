using CIS.Healthcheck;
using CIS.Heartbeat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CIS.Security.Certificates.Hosting;

// One finite two-role status owner per actual profile; callbacks only observe retained dates and the clock.
internal sealed class CertificateProfileAvailability : IAsyncDisposable, IHeartbeatSubscriber
{
    private readonly object gate = new();
    private readonly SecurityProfile profile;
    private readonly TimeProvider clock;
    private readonly ISecurityProfileStatusReporter reporter;
    private readonly IHealthMonitorFactory? health;
    private readonly IHeartbeatOwnerFactory? heartbeat;
    private readonly CancellationTokenRegistration stopping;
    private readonly TaskCompletionSource cutoff = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DateTimeOffset? inboundExpiry;
    private DateTimeOffset? outboundExpiry;
    private DateTimeOffset? outboundTrustExpiry;
    private SecurityFailure? outboundFailure;
    private IConditionMonitor? condition;
    private IHeartbeatOwner? owner;
    private Task? closing;
    private Task? disposal;
    private SecurityProfileStatus? previous;
    private bool initialized;
    private bool closed;
    private Exception? cutoffFailure;

    internal CertificateProfileAvailability(IServiceProvider services, string alias)
    {
        profile = services.GetRequiredService<SecurityPolicySnapshot>().GetProfile(alias);
        clock = services.GetRequiredService<TimeProvider>();
        reporter = services.GetRequiredService<ISecurityProfileStatusReporter>();
        health = services.GetService<IHealthMonitorFactory>();
        heartbeat = services.GetService<IHeartbeatOwnerFactory>();
        stopping = services.GetService<IHostApplicationLifetime>()?.ApplicationStopping.Register(BeginStopping) ?? default;
    }

    internal static void Register(IServiceCollection services, string alias)
    {
        if (!services.Any(descriptor => descriptor.IsKeyedService && descriptor.ServiceType == typeof(CertificateProfileAvailability)
            && Equals(descriptor.ServiceKey, alias)))
        {
            services.AddKeyedSingleton<CertificateProfileAvailability>(alias, (provider, _) => new(provider, alias));
        }
    }

    internal void Initialize()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (initialized)
            {
                return;
            }
            if (profile.RequiredForReadiness)
            {
                condition = (health ?? throw new InvalidOperationException("A required certificate profile needs existing Healthcheck."))
                    .CreateCondition(new()
                    {
                        Name = "Security: " + profile.Alias,
                        IsCritical = true,
                        InitiallyFlagged = true
                    });
                _ = heartbeat ?? throw new InvalidOperationException("A required certificate profile needs existing Heartbeat for expiry metadata.");
            }
            if (heartbeat is not null)
            {
                owner = heartbeat.CreateOwner();
                owner.Service.Register(this, new()
                {
                    Name = "Security expiry: " + profile.Alias,
                    Interval = TimeSpan.FromSeconds(1),
                    InitialDelay = TimeSpan.FromSeconds(1)
                });
            }
            initialized = true;
            Publish();
        }
    }

    internal void SetInbound(DateTimeOffset expiry)
    {
        lock (gate)
        {
            inboundExpiry = expiry;
            Publish();
        }
    }

    internal void SetOutbound(DateTimeOffset? expiry, SecurityFailure? failure = null)
    {
        lock (gate)
        {
            outboundExpiry = expiry;
            outboundFailure = failure;
            Publish();
        }
    }

    internal void SetOutboundTrust(DateTimeOffset expiry)
    {
        lock (gate)
        {
            outboundTrustExpiry = outboundTrustExpiry is { } existing && existing < expiry ? existing : expiry;
            Publish();
        }
    }

    private void Publish()
    {
        DateTimeOffset now = clock.GetUtcNow();
        bool inbound = (profile.Roles & SecurityProfileRoles.Inbound) != 0;
        bool outbound = (profile.Roles & SecurityProfileRoles.Outbound) != 0;
        bool missing = inbound && inboundExpiry is null || outbound && outboundExpiry is null;
        bool expired = inbound && inboundExpiry <= now || outbound && (outboundExpiry <= now || outboundTrustExpiry <= now);
        SecurityProfileAvailability availability = closed ? SecurityProfileAvailability.Stopping
            : missing ? SecurityProfileAvailability.Unavailable : expired ? SecurityProfileAvailability.Expired : SecurityProfileAvailability.Available;
        SecurityFailure? failure = expired ? new(SecurityFailureKind.CredentialExpired, "An enabled certificate role's actual credential or authority is expired.")
            : missing ? outboundFailure ?? new(SecurityFailureKind.CredentialUnavailable, "An enabled certificate role has not established usable trust or credentials.")
            : outboundFailure;
        var status = new SecurityProfileStatus(profile.Alias, availability, failure, outboundExpiry);
        if (previous?.Availability == availability && previous.Failure?.Code == failure?.Code
            && previous.CredentialExpiresAtUtc == outboundExpiry)
        {
            return;
        }
        previous = status;
        if (condition is not null)
        {
            if (availability == SecurityProfileAvailability.Available)
            {
                condition.Clear();
            }
            else
            {
                condition.Flag(new(failure?.Code ?? "security.stopping", failure?.Message ?? "The certificate profile is stopping."));
            }
        }
        reporter.Publish(status);
    }

    public Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!closed)
            {
                Publish();
            }
        }
        return Task.FromResult(true);
    }

    public Task OnErrorAsync(Exception exception) => Task.CompletedTask;

    private void BeginStopping()
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }
            closed = true;
        }
        try
        {
            closing = owner?.CloseAsync();
        }
        catch (Exception error)
        {
            cutoffFailure = error;
        }
        try
        {
            lock (gate)
            {
                Publish();
            }
        }
        catch (Exception error)
        {
            cutoffFailure = cutoffFailure is null ? error : new AggregateException(cutoffFailure, error);
        }
        finally
        {
            cutoff.TrySetResult();
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task work;
        lock (gate)
        {
            if (disposal is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
            }
            work = disposal;
        }
        if (completion is not null)
        {
            _ = DisposeCoreAsync(completion);
        }
        return new(work);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        BeginStopping();
        await cutoff.Task.ConfigureAwait(false);
        var failures = new List<Exception>();
        await Capture(async () =>
        {
            if (closing is not null)
            {
                await closing.ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
        await Capture(async () =>
        {
            if (owner is not null)
            {
                await owner.DisposeAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
        await Capture(async () =>
        {
            if (condition is not null)
            {
                await condition.DisposeAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
        await Capture(() =>
        {
            stopping.Dispose();
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        if (cutoffFailure is not null)
        {
            failures.Add(cutoffFailure);
        }
        if (failures.Count == 0)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(failures.Count == 1 ? failures[0] : new AggregateException(failures));
        }

        async Task Capture(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
    }
}
