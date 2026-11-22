using System.Collections.Concurrent;
using CIS.CommonTesting;
using CIS.Coordination;
using CIS.Jobs;
using CIS.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs;

internal sealed class JobRuntimeTestSupport : IAsyncDisposable
{
    private readonly ServiceProvider services;
    internal readonly JobTestControl Control = new();
    internal readonly ScriptedCoordinationService Coordination = new(64, 64);
    internal readonly ManualTimeProvider Clock = new(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero));
    internal readonly JobRuntime Runtime;
    internal readonly ContinuousJobRunner Runner;
    internal readonly JobCatalog Catalog;
    internal readonly string[] JobIds;

    internal JobRuntimeTestSupport(string[]? jobs = null, JobRuntimeLimits? limits = null, TimeProvider? runtimeClock = null,
        ICoordinationService? coordinationService = null)
    {
        JobIds = jobs ?? ["wallet.compaction"];
        JobHandlerBinding[] bindings = JobIds.Select(id => JobHandlerBinding.Continuous<JobTestHandler>(new(
            "wallet-service", id, id, "maintenance", [JobExecutionMode.Continuous], ["jobs.execute"]))).ToArray();
        Catalog = new([new("wallet-service", "maintenance", "Maintenance"), new("wallet-service", "other", "Other")], bindings, limits);
        var registration = new ServiceCollection();
        registration.AddSingleton(Control);
        registration.AddScoped<JobScopeMarker>();
        registration.AddScoped<JobTestHandler>();
        registration.AddScoped<ICapabilityAuthorizer, JobTestAuthorizer>();
        registration.AddScoped<ICoordinationTargetResolver, JobTestResolver>();
        registration.AddSingleton<ICoordinationService>(coordinationService ?? Coordination);
        services = registration.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Runtime = new(Catalog, new ScopeFactory(services, Control), Configuration(0, limits: limits), timeProvider: runtimeClock ?? Clock);
        Runner = new(Runtime);
    }

    internal JobConfigurationSnapshot Configuration(long revision, bool enabled = true, JobRuntimeLimits? limits = null,
        IEnumerable<JobCategorySettings>? categories = null, Func<string, JobSettings>? settings = null)
        => new(revision, enabled, categories, JobIds.Select(id => settings?.Invoke(id)
            ?? new JobSettings("wallet-service", id, new(JobExecutionMode.Continuous), enabled: true)), limits ?? Catalog.Limits);

    internal ManualCoordinationLease Grant(string job = "wallet.compaction", string resource = "partition")
    {
        LocalApplicationIdentity application = LocalApplicationIdentity.Capture();
        var acquisition = new CoordinationAcquisition(new(application.Environment, "wallet-service", job, resource),
            application.Application, application.InstanceId, Guid.NewGuid());
        return new(new(Guid.NewGuid(), acquisition, 1, DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    internal ManualCoordinationLease EnqueueGrant(string job = "wallet.compaction", string resource = "partition")
    {
        ManualCoordinationLease lease = Grant(job, resource);
        Coordination.Enqueue(new(CoordinationAcquireOutcome.Acquired, lease));
        return lease;
    }

    internal static async Task UntilAsync(Func<bool> condition)
    {
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(1, bound.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Control.WorkRelease.TrySetResult();
        Control.ScopeCleanupRelease.TrySetResult();
        await Runtime.DisposeAsync();
        await services.DisposeAsync();
    }

    private sealed class ScopeFactory(ServiceProvider provider, JobTestControl control) : IJobExecutionScopeFactory
    {
        public ValueTask<IJobExecutionScope> CreateAsync(JobScopeRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref control.CreatedScopes);
            AsyncServiceScope scope = provider.CreateAsyncScope();
            return ValueTask.FromResult<IJobExecutionScope>(new OwnedScope(scope, control));
        }
    }

    private sealed class OwnedScope(AsyncServiceScope scope, JobTestControl control) : IJobExecutionScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public async ValueTask DisposeAsync()
        {
            bool used = Services.GetRequiredService<JobScopeMarker>().Used;
            if (used && control.HoldCleanup)
            {
                control.ScopeCleanupEntered.TrySetResult();
                await control.ScopeCleanupRelease.Task;
            }

            await scope.DisposeAsync();
            Interlocked.Increment(ref control.DisposedScopes);
            if (used && control.ThrowCleanup)
            {
                throw new InvalidOperationException("Controlled scope cleanup fault.");
            }
        }
    }
}

internal sealed class JobTestControl
{
    internal Func<JobExecutionContext, CancellationToken, Task<ContinuousIterationResult>> Callback =
        static (_, _) => Task.FromResult(new ContinuousIterationResult(ContinuousIterationOutcome.Idle));
    internal readonly ConcurrentQueue<string> Invocations = new();
    internal readonly ConcurrentQueue<string[]> AuthorizationCapabilities = new();
    internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource WorkRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource ScopeCleanupEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource ScopeCleanupRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Authorized = true;
    internal bool HoldCleanup;
    internal bool ThrowCleanup;
    internal int CreatedScopes;
    internal int DisposedScopes;
}

internal sealed class JobScopeMarker
{
    public JobScopeMarker()
    {
    }
    internal bool Used;
}

internal sealed class JobTestHandler : IContinuousJobHandler
{
    private readonly JobTestControl control;
    private readonly JobScopeMarker marker;

    public JobTestHandler(JobTestControl control, JobScopeMarker marker)
    {
        this.control = control;
        this.marker = marker;
    }

    public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
    {
        marker.Used = true;
        control.Invocations.Enqueue(context.Definition.JobId);
        control.Entered.TrySetResult();
        return control.Callback(context, cancellationToken);
    }
}

internal sealed class JobTestAuthorizer : ICapabilityAuthorizer
{
    private readonly JobTestControl control;

    public JobTestAuthorizer(JobTestControl control)
    {
        this.control = control;
    }

    public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities, CapabilityTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        control.AuthorizationCapabilities.Enqueue(capabilities.ToArray());
        return ValueTask.FromResult(control.Authorized ? AuthorizationDecision.Allow
            : AuthorizationDecision.Deny(new(SecurityFailureKind.CapabilityDenied, "Controlled denial.")));
    }
}

internal sealed class JobTestResolver : ICoordinationTargetResolver
{
    public JobTestResolver()
    {
    }
    public ValueTask<CoordinationTarget?> ResolveAsync(CoordinationRequest request, LocalApplicationIdentity application,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<CoordinationTarget?>(CoordinationTarget.ForDuty(
            new(application.Environment, request.ExecutionScope, request.DutyId, request.ResourceId), request.ExecutionScope, ["coordination.acquire"]));
}

internal sealed class JobTestObserver : IJobRuntimeObserver
{
    internal readonly ConcurrentQueue<(JobExecutionMode Mode, string Outcome, double? Seconds)> Completed = new();
    public void InvocationStarted(JobExecutionMode mode)
    {
    }
    public void InvocationCompleted(JobExecutionMode mode, string outcome, double? durationSeconds) => Completed.Enqueue((mode, outcome, durationSeconds));
    public void Transition(JobRuntimeSnapshot observation)
    {
    }
}
