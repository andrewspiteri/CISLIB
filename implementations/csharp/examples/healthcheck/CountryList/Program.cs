using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.Healthcheck.AspNetCore;

namespace CIS.Healthcheck.Examples.CountryList;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        bool selfTest = args.Contains("--self-test", StringComparer.Ordinal);
        bool failPrime = args.Contains("--fail-prime", StringComparer.Ordinal);
        bool failRefresh = args.Contains("--fail-refresh", StringComparer.Ordinal);
        bool failRegistration = args.Contains("--fail-refresh-registration", StringComparer.Ordinal);
        bool failLaterStartup = args.Contains("--fail-later-startup", StringComparer.Ordinal);
        if (new[] { failPrime, failRegistration, failLaterStartup }.Count(value => value) > 1)
        { throw new ArgumentException("Select at most one startup failure demonstration.", nameof(args)); }
        string[] hostArgs = args.Where(arg => arg is not ("--self-test" or "--fail-prime" or "--fail-refresh"
            or "--fail-refresh-registration" or "--fail-later-startup")).ToArray();
        WebApplicationBuilder builder = WebApplication.CreateBuilder(hostArgs);
        if (selfTest)
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
        }

        HealthcheckOptions options = HealthcheckOptions.Resolve(new HealthcheckOptions
        {
            MaxMonitors = failRegistration ? 2 : 1024
        });
        await using var health = new HealthcheckService(options, new HealthApplicationIdentity
        {
            Name = "CountryList",
            InstanceId = Guid.NewGuid(),
            HostName = Environment.MachineName
        });
        await using var runner = new HealthCollectorRunner(health);
        await using var pipe = new HealthPipeServer(health, options);
        await using IConditionMonitor startupFailure = health.CreateCondition(new ConditionMonitorOptions
        {
            Name = "Application startup",
            IsCritical = true,
            InitiallyFlagged = false
        });
        await using IAvailabilityMonitor availability = health.CreateAvailability(new AvailabilityMonitorOptions
        {
            Name = "Usable country list",
            IsCritical = true
        });
        await using IHealthExecutionBinding primeBinding = HealthExecutionBinder.Bind(availability, new HealthExecutionBindingOptions
        {
            RegistrationId = Guid.NewGuid(),
            SuccessPublishesUsableData = true,
            DisplayName = "Explicit application priming"
        });
        await using var catalog = new CountryCatalog(new CountrySource(failPrime) { FailRefresh = failRefresh });
        await using WebApplication app = builder.Build();
        var shutdownRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration stopping = app.Lifetime.ApplicationStopping.Register(() =>
        {
            health.BeginStopping();
            shutdownRequested.TrySetResult();
        });
        int businessRequests = 0;
        // Install before every business-side-effect middleware. HTTP details are
        // deliberately denied here; replace with real application authorization.
        app.UseCisHealthcheck(health, options, _ => ValueTask.FromResult(false));
        app.Use(async (context, next) =>
        {
            if (!health.CaptureSnapshot().Ready)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            Interlocked.Increment(ref businessRequests);
            await next(context).ConfigureAwait(false);
        });
        app.MapGet("/countries", () => catalog.Countries);

        IConsecutiveFailureMonitor? refreshHealth = null;
        IHealthCollectionRegistration? refresh = null;
        var observations = new List<object>();
        try
        {
            // Start reporting before priming can fail. Constructors above did
            // not fetch data, invoke collectors, or open an endpoint.
            await app.StartAsync().ConfigureAwait(false);
            await pipe.StartAsync().ConfigureAwait(false);
            await runner.StartAsync().ConfigureAwait(false);
            using var client = selfTest ? new HttpClient { BaseAddress = new Uri(app.Urls.Single()) } : null;
            if (client is not null)
            {
                await ExpectStatusAsync(client, "/_health", HttpStatusCode.OK).ConfigureAwait(false);
                await ExpectStatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError).ConfigureAwait(false);
                await ExpectStatusAsync(client, "/countries", HttpStatusCode.ServiceUnavailable).ConfigureAwait(false);
                Require(businessRequests == 0, "Reporting and the startup gate bypass business side effects.");
                observations.Add(Observe("before-prime", health, catalog, refreshHealth, businessRequests));
            }

            bool primed = false;
            if (client is not null)
            {
                RefreshBlock priming = catalog.Source.BlockPrime();
                Task<bool> primeTask = PrimeAsync(catalog, primeBinding, startupFailure, app.Lifetime.ApplicationStopping);
                try
                {
                    await priming.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    Require(!primeTask.IsCompleted && !health.CaptureSnapshot().Application.StartupCompleted,
                        "Actual required priming remains incomplete behind its owner barrier.");
                    await ExpectStatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError).ConfigureAwait(false);
                    await ExpectStatusAsync(client, "/countries", HttpStatusCode.ServiceUnavailable).ConfigureAwait(false);
                    observations.Add(Observe("priming", health, catalog, refreshHealth, businessRequests));
                }
                finally
                {
                    priming.Release.TrySetResult();
                    // Even a failed assertion must finish actual explicit prime
                    // work before the enclosing owner can be disposed.
                    primed = await primeTask.ConfigureAwait(false);
                }
            }
            else
            {
                primed = await PrimeAsync(catalog, primeBinding, startupFailure, app.Lifetime.ApplicationStopping).ConfigureAwait(false);
            }
            string failureMode = primed ? "none" : "priming";
            bool startupSucceeded = primed;
            if (startupSucceeded)
            {
                if (client is not null) { observations.Add(Observe("after-prime", health, catalog, refreshHealth, businessRequests)); }
                string requiredStep = "later-startup";
                try
                {
                    await CompleteRequiredApplicationSetupAsync(catalog, failLaterStartup, app.Lifetime.ApplicationStopping).ConfigureAwait(false);
                    requiredStep = "registration";
                    refreshHealth = health.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions
                    {
                        Name = "Country refresh",
                        IsCritical = false,
                        FailureThreshold = 3
                    });
                    refresh = runner.Register(refreshHealth, catalog, new HealthCollectorOptions
                    {
                        IsRequired = true,
                        Interval = TimeSpan.FromMinutes(5)
                    });
                    // All required startup work is complete. This example has
                    // one operation, not an IStartable coordinator.
                    health.MarkStartupCompleted();
                }
                catch (Exception exception) when (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
                {
                    startupFailure.Flag(HealthDiagnostic.FromException(exception, "CountryList.RequiredStartupFailed"));
                    if (refresh is not null) { await refresh.DisposeAsync().ConfigureAwait(false); refresh = null; }
                    if (refreshHealth is not null) { await refreshHealth.DisposeAsync().ConfigureAwait(false); refreshHealth = null; }
                    startupSucceeded = false;
                    failureMode = requiredStep;
                }
            }

            if (client is not null)
            {
                if (!primed)
                {
                    // The child contribution disappears, while the separate
                    // retained application startup failure remains observable.
                    await availability.DisposeAsync().ConfigureAwait(false);
                }
                object ownerDrain = await VerifyAsync(client, health, catalog, refreshHealth, refresh,
                    primed, startupSucceeded, () => businessRequests, observations).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    kind = "countryListSelfTest",
                    passed = true,
                    primeSucceeded = primed,
                    startupSucceeded,
                    failureMode,
                    reportingBeforePriming = true,
                    refreshRegistered = refresh is not null,
                    actualOwnerDrain = startupSucceeded,
                    sourceReferenceDevelopmentExample = true,
                    runtime = RuntimeInformation.FrameworkDescription,
                    observations,
                    ownerDrain
                }));
            }
            else
            {
                refresh?.ActivatePeriodic();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    kind = "countryListStarted",
                    primeSucceeded = primed,
                    startupSucceeded,
                    failureMode,
                    ready = health.CaptureSnapshot().Ready,
                    pipeName = options.PipeEnabled ? options.PipeName : null
                }));
                // Keep reporting admission open while the explicit finally
                // sequence drains owners; then stop the host itself last.
                await shutdownRequested.Task.ConfigureAwait(false);
            }
            return 0;
        }
        finally
        {
            // Terminal readiness first; await actual collection and owner leases
            // before disposing dependencies, then close reporting admission.
            health.BeginStopping();
            refresh?.Withdraw();
            await runner.StopAsync().ConfigureAwait(false);
            if (refresh is not null) { await refresh.DisposeAsync().ConfigureAwait(false); }
            if (refreshHealth is not null) { await refreshHealth.DisposeAsync().ConfigureAwait(false); }
            await primeBinding.DisposeAsync().ConfigureAwait(false);
            await catalog.DisposeAsync().ConfigureAwait(false);
            await availability.DisposeAsync().ConfigureAwait(false);
            await startupFailure.DisposeAsync().ConfigureAwait(false);
            await pipe.StopAsync().ConfigureAwait(false);
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    private static Task CompleteRequiredApplicationSetupAsync(CountryCatalog catalog, bool fail, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // This is a concrete required application step after cache priming.
        // Its fault demonstrates that usable data alone is not full startup.
        byte[] response = JsonSerializer.SerializeToUtf8Bytes(catalog.Countries);
        using JsonDocument contract = JsonDocument.Parse(response);
        if (fail || contract.RootElement.ValueKind != JsonValueKind.Array
            || contract.RootElement.GetArrayLength() != catalog.Countries.Count)
        { throw new InvalidOperationException("Required country response contract setup failed."); }
        return Task.CompletedTask;
    }

    private static async Task<bool> PrimeAsync(CountryCatalog catalog, IHealthExecutionBinding binding,
        IConditionMonitor startupFailure, CancellationToken stopping)
    {
        HealthExecutionOutcome outcome;
        HealthDiagnostic? diagnostic = null;
        try
        {
            await catalog.PrimeAsync(stopping).ConfigureAwait(false);
            outcome = HealthExecutionOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            outcome = HealthExecutionOutcome.ExpectedCancellation;
        }
        catch (Exception exception)
        {
            outcome = HealthExecutionOutcome.Failed;
            diagnostic = HealthDiagnostic.FromException(exception, "CountryList.PrimingFailed");
            startupFailure.Flag(diagnostic);
        }

        DateTimeOffset completed = DateTimeOffset.UtcNow;
        binding.Report(new HealthExecutionFact
        {
            RegistrationId = binding.RegistrationId,
            ExecutionId = 1,
            Kind = HealthExecutionFactKind.Outcome,
            SourcePhase = HealthExecutionPhase.Callback,
            OccurredAtUtc = completed,
            WorkCompletedAtUtc = completed,
            Outcome = outcome,
            OriginalError = diagnostic
        });
        DateTimeOffset settled = DateTimeOffset.UtcNow;
        binding.Report(new HealthExecutionFact
        {
            RegistrationId = binding.RegistrationId,
            ExecutionId = 1,
            Kind = HealthExecutionFactKind.Settled,
            OccurredAtUtc = settled,
            SettledAtUtc = settled,
            ErrorHandlingStatus = HealthExecutionStepStatus.NotApplicable,
            CleanupStatus = HealthExecutionStepStatus.NotApplicable
        });
        return outcome == HealthExecutionOutcome.Succeeded;
    }

    private static async Task<object> VerifyAsync(HttpClient client, HealthcheckService health, CountryCatalog catalog,
        IConsecutiveFailureMonitor? refreshHealth, IHealthCollectionRegistration? refresh, bool primed, bool startupSucceeded,
        Func<int> businessRequests, List<object> observations)
    {
        if (!startupSucceeded)
        {
            Require(refresh is null && catalog.Countries.Count == (primed ? 3 : 0), "Failed startup creates no recurring refresh.");
            if (primed)
            {
                Require(health.CaptureSnapshot().Monitors.Single(m => m.Policy == HealthPolicyKind.FirstSuccessAvailability).State == MonitorState.Healthy,
                    "A later required step failure retains actually usable primed data.");
            }
            Require(health.CaptureSnapshot().MonitorCounts.CriticalFailing >= 1, "A retained critical monitor explains failed startup.");
            observations.Add(Observe(primed ? "startup-failed" : "prime-failed", health, catalog, refreshHealth, businessRequests()));
            await ExpectStatusAsync(client, "/_health", HttpStatusCode.InternalServerError).ConfigureAwait(false);
            await ExpectStatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError).ConfigureAwait(false);
            await ExpectStatusAsync(client, "/countries", HttpStatusCode.ServiceUnavailable).ConfigureAwait(false);
            Require(businessRequests() == 0, "Failed startup admits no business request.");
            health.BeginStopping();
            Require(!health.CaptureSnapshot().Ready, "Stopping cannot become ready.");
            observations.Add(Observe("stopping", health, catalog, refreshHealth, businessRequests()));
            return new
            {
                applicable = false,
                disposalPending = false,
                sourceDisposedBeforeRelease = catalog.Source.IsDisposed,
                sourceDisposedAfterRelease = catalog.Source.IsDisposed
            };
        }

        Require(refresh is not null && refreshHealth is not null, "Successful priming registers refresh.");
        await ExpectStatusAsync(client, "/_health/ready", HttpStatusCode.OK).ConfigureAwait(false);
        await ExpectStatusAsync(client, "/countries", HttpStatusCode.OK).ConfigureAwait(false);
        Require(catalog.Countries.Count == 3 && businessRequests() == 1, "Usable publication admits business traffic.");
        observations.Add(Observe("startup-completed", health, catalog, refreshHealth, businessRequests()));
        catalog.Source.FailRefresh = true;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            HealthCollectionAttempt result = await refresh!.CollectAsync().ConfigureAwait(false);
            Require(result.Disposition == HealthCollectionDisposition.Committed && result.EvaluationFailed,
                "Each actual failed fetch is committed as an admitted evaluation failure.");
        }
        HealthSnapshot snapshot = health.CaptureSnapshot();
        Require(snapshot.Monitors.Single(m => m.Policy == HealthPolicyKind.FirstSuccessAvailability).State == MonitorState.Healthy,
            "Usable-data availability remains explicitly satisfied after failed refreshes.");
        Require(snapshot.Monitors.Single(m => m.MonitorId == refreshHealth!.MonitorId).State == MonitorState.Unhealthy,
            "The third consecutive failed refresh flags its noncritical policy.");
        Require(snapshot.HealthStatus == HealthStatus.Healthy && snapshot.Ready && catalog.Countries.Count == 3,
            "Failed replacement preserves usable data and critical availability.");
        observations.Add(Observe("after-refresh-failures", health, catalog, refreshHealth, businessRequests()));
        await ExpectStatusAsync(client, "/_health/ready", HttpStatusCode.OK).ConfigureAwait(false);
        await using IConditionMonitor critical = health.CreateCondition(new ConditionMonitorOptions
        {
            Name = "Independent critical condition",
            IsCritical = true,
            InitiallyFlagged = false
        });
        critical.Flag(new HealthDiagnostic("CountryList.CriticalDependency"));
        observations.Add(Observe("critical-flagged", health, catalog, refreshHealth, businessRequests()));
        await ExpectStatusAsync(client, "/_health", HttpStatusCode.InternalServerError).ConfigureAwait(false);
        await ExpectStatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError).ConfigureAwait(false);
        critical.Clear();
        observations.Add(Observe("critical-cleared", health, catalog, refreshHealth, businessRequests()));
        await ExpectStatusAsync(client, "/_health/ready", HttpStatusCode.OK).ConfigureAwait(false);
        Require(businessRequests() == 1, "Health reads bypass business middleware after startup too.");

        // A real acquired lease remains outstanding after shutdown cancellation.
        RefreshBlock block = catalog.Source.BlockNextRefresh();
        Task<HealthCollectionAttempt> outstanding = refresh!.CollectAsync();
        try
        {
            await block.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            health.BeginStopping();
            Require(!health.CaptureSnapshot().Ready, "BeginStopping closes readiness immediately.");
            observations.Add(Observe("stopping", health, catalog, refreshHealth, businessRequests()));
            await ExpectStatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError).ConfigureAwait(false);
            Task ownerDisposal = catalog.DisposeAsync().AsTask();
            bool disposalPending = !ownerDisposal.IsCompleted;
            bool sourceDisposedBeforeRelease = catalog.Source.IsDisposed;
            Require(disposalPending && !sourceDisposedBeforeRelease && !outstanding.IsCompleted,
                "Owner dependencies remain alive until actual noncooperative work releases its lease.");
            block.Release.TrySetResult();
            await outstanding.ConfigureAwait(false);
            await ownerDisposal.ConfigureAwait(false);
            Require(catalog.Source.IsDisposed, "Actual lease settlement permits dependency disposal.");
            return new
            {
                applicable = true,
                disposalPending,
                sourceDisposedBeforeRelease,
                sourceDisposedAfterRelease = catalog.Source.IsDisposed
            };
        }
        finally { block.Release.TrySetResult(); }
    }

    private static object Observe(string stage, HealthcheckService health, CountryCatalog catalog,
        IConsecutiveFailureMonitor? refreshHealth, int businessRequests)
    {
        HealthSnapshot snapshot = health.CaptureSnapshot();
        HealthMonitorSnapshot? refresh = snapshot.Monitors.SingleOrDefault(m => m.MonitorId == refreshHealth?.MonitorId);
        return new
        {
            stage,
            healthStatus = snapshot.HealthStatus.ToString(),
            ready = snapshot.Ready,
            active = snapshot.MonitorCounts.Active,
            criticalFailing = snapshot.MonitorCounts.CriticalFailing,
            pending = snapshot.MonitorCounts.Pending,
            availabilityState = snapshot.Monitors.SingleOrDefault(m => m.Policy == HealthPolicyKind.FirstSuccessAvailability)?.State.ToString(),
            refreshFailed = refresh?.Outcomes?.Failed.ToString(CultureInfo.InvariantCulture),
            refreshState = refresh?.State.ToString(),
            countries = catalog.Countries.Count,
            businessRequests
        };
    }

    private static async Task ExpectStatusAsync(HttpClient client, string path, HttpStatusCode expected)
    {
        using HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
        Require(response.StatusCode == expected, $"Unexpected status for {path}: {(int)response.StatusCode}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
