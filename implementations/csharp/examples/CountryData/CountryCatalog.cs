using CIS.Configuration;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Healthcheck;
using CIS.Metrics;
using CIS.RestClient;
using CIS.Startup;

namespace CIS.Examples.CountryData;

public sealed class CountryCatalog : IStartable, IHeartbeatSubscriber, IAsyncDisposable
{
    private static readonly EventId Loading = new(2100, "country.initial_load.started");
    private static readonly EventId Published = new(2103, "country.initial_load.published");
    private static readonly EventId Stopped = new(2104, "country.catalog.stopped");

    private readonly IRestClient client;
    private readonly IHeartbeatOwnerFactory heartbeat;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<CountryCatalog> logger;
    private readonly TimeProvider clock;
    private readonly CountryRefreshDiagnostics diagnostics;
    private readonly string source;
    private readonly TimeSpan interval;
    private readonly TimeSpan timeout;
    private readonly RestRetrySelection retry;
    private readonly IAvailabilityMonitor availability;
    private readonly object gate = new();

    private CountrySnapshot snapshot = CountrySnapshot.Empty;
    private IHeartbeatOwner? owner;
    private Task? disposal;

    public CountryCatalog(
        [FromKeyedServices("countries")] IRestClient client,
        CountrySettings settings,
        IConfigurationReader reader,
        IHeartbeatOwnerFactory heartbeat,
        IHealthMonitorFactory health,
        IMetrics metrics,
        IHostApplicationLifetime lifetime,
        ILogger<CountryCatalog> logger,
        TimeProvider clock,
        IEnumerable<RestRetryPolicy> retryPolicies)
    {
        this.client = client;
        this.heartbeat = heartbeat;
        this.lifetime = lifetime;
        this.logger = logger;
        this.clock = clock;

        (source, interval, timeout, retry) = CaptureSettings(settings, reader, retryPolicies);
        availability = health.CreateAvailability(new AvailabilityMonitorOptions
        {
            Name = "Usable country data",
            IsCritical = true
        });

        diagnostics = new CountryRefreshDiagnostics(logger, clock);
        RegisterCacheMetrics(metrics);
    }

    private static (string Source, TimeSpan Interval, TimeSpan Timeout, RestRetrySelection Retry) CaptureSettings(
        CountrySettings settings,
        IConfigurationReader reader,
        IEnumerable<RestRetryPolicy> retryPolicies)
    {
        var captured = reader.Read(settings,
            value => (value.SourceUrl, value.RefreshInterval, value.RequestTimeout, value.RetryPolicyName));

        if (!Uri.TryCreate(captured.SourceUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("Country SourceUrl must be an absolute HTTP(S) URL without user information.");
        }

        if (captured.RefreshInterval <= TimeSpan.Zero
            || captured.RefreshInterval > HeartbeatRegistrationOptions.MaximumDuration
            || captured.RequestTimeout <= TimeSpan.Zero
            || captured.RequestTimeout > HeartbeatRegistrationOptions.MaximumDuration)
        {
            throw new ArgumentException("Country refresh and request durations must be positive supported timer durations.");
        }

        string? policyName = captured.RetryPolicyName;
        if (policyName is not null && !retryPolicies.Any(policy => policy.Name == policyName))
        {
            throw new ArgumentException("The configured country retry policy is not registered.");
        }

        RestRetrySelection selection = policyName is null
            ? RestRetrySelection.None
            : RestRetrySelection.Named(policyName);
        return (captured.SourceUrl, captured.RefreshInterval, captured.RequestTimeout, selection);
    }

    private void RegisterCacheMetrics(IMetrics metrics)
    {
        metrics.RegisterObservableGauge(
            new MetricDefinition("app.country_cache.entries", MetricUnit.Count,
                "Distinct countries in the published snapshot."),
            () => [new MetricObservation(Volatile.Read(ref snapshot).Count)]);

        metrics.RegisterObservableGauge(
            new MetricDefinition("app.country_cache.last_success_timestamp", MetricUnit.Seconds,
                "Unix timestamp of the current successful publication."),
            () =>
            {
                DateTimeOffset? published = Volatile.Read(ref snapshot).PublishedAt;
                return published.HasValue
                    ? [new MetricObservation(published.Value.ToUnixTimeMilliseconds() / 1000d)]
                    : [];
            });
    }

    public bool TryGet(string identifier, out Country? country)
        => Volatile.Read(ref snapshot).Index.TryGetValue(identifier.Trim(), out country);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(Loading, "Loading initial country data.");
        if (!await RefreshAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lifetime.ApplicationStopping.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Initial country data could not be published; readiness remains closed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        lifetime.ApplicationStopping.ThrowIfCancellationRequested();
        availability.ReportPublished();
        logger.LogInformation(Published, "Published {CountryCount} countries.", Volatile.Read(ref snapshot).Count);

        owner = heartbeat.CreateOwner();
        owner.Service.Register(this,
            new HeartbeatRegistrationOptions
            {
                Name = "Country data refresh",
                InitialDelay = interval,
                Interval = interval
            },
            new HeartbeatHealthcheckOptions
            {
                Monitor = availability,
                SuccessPublishesUsableData = true
            });
    }

    public Task<bool> OnElapsedAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    private async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, lifetime.ApplicationStopping);
        Task<RestResult<CountriesEnvelope>>? request = null;

        try
        {
            request = client.Get<CountriesEnvelope>(source,
                new RestRequestOptions
                {
                    OperationName = "refresh",
                    Timeout = timeout,
                    RetryPolicy = retry
                },
                operation.Token);

            RestResult<CountriesEnvelope> result = await request.ConfigureAwait(false);
            if (operation.IsCancellationRequested)
            {
                return false;
            }

            if (!result.Success || !result.Context.HasValue || result.Value is null)
            {
                diagnostics.ExpectedFailure(result.ErrorCode ?? "country.invalid_data");
                return false;
            }

            if (!CountrySnapshot.TryCreate(result.Value.Data, clock.GetUtcNow(), out CountrySnapshot replacement))
            {
                diagnostics.ExpectedFailure("country.invalid_data");
                return false;
            }

            if (operation.IsCancellationRequested)
            {
                return false;
            }

            Volatile.Write(ref snapshot, replacement);
            diagnostics.Succeeded();
            return true;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested && request?.IsCanceled == true)
        {
            return false;
        }
    }

    public Task OnErrorAsync(Exception exception)
    {
        diagnostics.UnexpectedFailure(exception);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task operation;

        lock (gate)
        {
            if (disposal is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
            }

            operation = disposal;
        }

        if (completion is not null)
        {
            _ = DisposeCoreAsync(completion);
        }

        return new(operation);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            try
            {
                if (owner is not null)
                {
                    await owner.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await availability.DisposeAsync().ConfigureAwait(false);
            }

            logger.LogInformation(Stopped, "Country catalog stopped.");
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }
}
