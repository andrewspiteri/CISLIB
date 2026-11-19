using CIS.CommonTesting;
using CIS.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Workflows;

internal sealed class PipelineTestHost : IAsyncDisposable
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    internal PipelineTestContext Context { get; } = new();
    internal ManualTimeProvider Clock { get; } = new(DateTimeOffset.UnixEpoch);
    internal CountingPipelineLifetime Lifetime { get; } = new();
    internal RecordingPipelineObservation Observation { get; } = new();
    internal List<PipelineRunInfo> Runs { get; } = [];
    internal ServiceProvider Provider { get; }
    internal int Activations { get; private set; }
    internal Action<string, IReadOnlyList<PipelineRegistration>> Validate { get; set; } = static (_, _) => { };

    internal PipelineTestHost(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Context);
        services.AddSingleton<PipelineSingleton>();
        services.AddScoped<PipelineDependency>();
        configure?.Invoke(services);
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    internal PipelineBuilder<PipelineTestContext> Builder(PipelineOptions? options = null, IServiceProvider? borrowed = null,
        TimeProvider? clock = null, Func<PipelineRunInfo, IPipelineObservation?>? observe = null, string name = "test-pipeline")
    {
        var environment = new PipelineEnvironment(clock ?? Clock, () =>
        {
            Activations++;
            return borrowed is null
                ? PipelineServiceActivation.CreateOwned(Provider.GetRequiredService<IServiceScopeFactory>())
                : PipelineServiceActivation.Borrowed(borrowed);
        }, Lifetime, (name, registrations) => Validate(name, registrations), observe ?? (info =>
        {
            Runs.Add(info);
            return Observation;
        }));
        return new(environment, name, Context, options);
    }

    public async ValueTask DisposeAsync()
    {
        Assert.AreEqual(0, Lifetime.Active, "Owned work must settle before the real provider is disposed.");
        await Provider.DisposeAsync();
    }

    internal static TaskCompletionSource Signal()
    {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal static async Task<Exception> FailureAsync(Task operation)
    {
        try { await operation.WaitAsync(Deadline); }
        catch (Exception exception) when (exception is not TimeoutException) { return exception; }
        throw new AssertFailedException("The pipeline unexpectedly succeeded.");
    }

    internal static async Task SettleAsync(Task? operation)
    {
        if (operation is null) { return; }
        try { await operation.WaitAsync(Deadline); }
        catch (Exception exception) when (exception is not TimeoutException) { }
    }
}

internal sealed class CountingPipelineLifetime : IPipelineLifetime
{
    internal bool Closed { get; set; }
    internal int Active { get; private set; }
    internal int Entries { get; private set; }
    internal int Exits { get; private set; }
    internal Action? OnExit { get; set; }

    public void Enter()
    {
        ObjectDisposedException.ThrowIf(Closed, this);
        Active++;
        Entries++;
    }

    public void Exit()
    {
        OnExit?.Invoke();
        Active--;
        Exits++;
    }
}

internal sealed class RecordingPipelineObservation : IPipelineObservation
{
    internal int StartedCount { get; private set; }
    internal int CompletedCount { get; private set; }
    internal int ExpiredCount { get; private set; }
    internal List<(int Position, PipelinePhase Phase, PipelineStatus Outcome, TimeSpan Duration)> Phases { get; } = [];
    internal Action? OnCallback { get; set; }

    public void Started()
    {
        StartedCount++;
        OnCallback?.Invoke();
    }
    public void PhaseStarted(PipelineRegistration registration, PipelinePhase phase)
    {
        OnCallback?.Invoke();
    }
    public void PhaseCompleted(PipelineRegistration registration, PipelinePhase phase, PipelineStatus outcome,
        TimeSpan duration, PipelineFailure? failure)
    {
        Phases.Add((registration.Position, phase, outcome, duration));
        OnCallback?.Invoke();
    }
    public void CleanupBudgetExpired()
    {
        ExpiredCount++;
        OnCallback?.Invoke();
    }
    public void LifecycleFailure(PipelineFailure failure)
    {
        OnCallback?.Invoke();
    }
    public void Completed(PipelineStatus status, TimeSpan? duration, PipelineFailure? primary, int secondaryFailureCount)
    {
        CompletedCount++;
        OnCallback?.Invoke();
    }
}

internal sealed class PipelineTestContext : IDisposable
{
    internal List<string> Events { get; } = [];
    internal List<PipelineProbeStep> Instances { get; } = [];
    internal List<PipelineDependency> Dependencies { get; } = [];
    internal List<PipelineFailure> HandlerCauses { get; } = [];
    internal Func<string, CancellationToken, Task<PipelineStepResult>> Forward { get; set; }
        = static (_, _) => Task.FromResult(PipelineStepResult.Succeeded());
    internal Func<string, PipelineFailure, CancellationToken, Task> Compensate { get; set; }
        = static (_, _, _) => Task.CompletedTask;
    internal Func<string, ValueTask> DisposeStep { get; set; } = static _ => ValueTask.CompletedTask;
    internal Action<string>? Construct { get; set; }
    internal Exception? DependencyDisposalFailure { get; set; }
    internal bool Disposed { get; private set; }
    internal int SyncOnlyDisposals { get; set; }

    public void Dispose()
    {
        Disposed = true;
    }
}

internal sealed class PipelineSingleton : IDisposable
{
    public PipelineSingleton()
    {
    }

    internal bool Disposed { get; private set; }
    public void Dispose()
    {
        Disposed = true;
    }
}

internal sealed class PipelineDependency : IDisposable
{
    private readonly PipelineTestContext context;
    internal PipelineSingleton Singleton { get; }
    internal bool Disposed { get; private set; }

    public PipelineDependency(PipelineTestContext context, PipelineSingleton singleton)
    {
        this.context = context;
        Singleton = singleton;
        context.Dependencies.Add(this);
    }

    public void Dispose()
    {
        Disposed = true;
        context.Events.Add("dependency.dispose");
        if (context.DependencyDisposalFailure is { } failure) { throw failure; }
    }
}

internal abstract class PipelineProbeStep : IPipelineStep<PipelineTestContext>, IAsyncDisposable, IDisposable
{
    protected readonly PipelineTestContext Context;
    internal string Label { get; }
    internal PipelineDependency Dependency { get; }
    internal int AsyncDisposals { get; private set; }
    internal int SyncDisposals { get; private set; }

    protected PipelineProbeStep(string label, PipelineTestContext context, PipelineDependency dependency)
    {
        Label = label;
        Context = context;
        Dependency = dependency;
        context.Construct?.Invoke(label);
        context.Instances.Add(this);
        context.Events.Add("construct." + label);
    }

    public Task<PipelineStepResult> Execute(PipelineTestContext context, CancellationToken cancellationToken)
    {
        Assert.AreSame(Context, context);
        Assert.IsFalse(Dependency.Disposed);
        Context.Events.Add("execute." + Label);
        return Context.Forward(Label, cancellationToken);
    }

    public Task Error(PipelineTestContext context, PipelineFailure failure, CancellationToken cancellationToken)
    {
        Assert.AreSame(Context, context);
        Assert.IsFalse(Dependency.Disposed);
        Context.Events.Add("error." + Label);
        Context.HandlerCauses.Add(failure);
        return Context.Compensate(Label, failure, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Assert.IsFalse(Dependency.Disposed);
        Context.Events.Add("dispose." + Label);
        AsyncDisposals++;
        await Context.DisposeStep(Label);
    }

    public void Dispose()
    {
        SyncDisposals++;
    }
}

internal sealed class PipelineStepA : PipelineProbeStep
{
    public PipelineStepA(PipelineTestContext context, PipelineDependency dependency) : base("A", context, dependency)
    {
    }
}

internal sealed class PipelineStepB : PipelineProbeStep
{
    public PipelineStepB(PipelineTestContext context, PipelineDependency dependency) : base("B", context, dependency)
    {
    }
}

internal sealed class PipelineStepC : PipelineProbeStep
{
    public PipelineStepC(PipelineTestContext context, PipelineDependency dependency) : base("C", context, dependency)
    {
    }
}

internal sealed class FaultOncePipelineClock(TimeProvider actual) : TimeProvider
{
    internal bool FailNextTimestamp { get; set; }
    internal Exception Original { get; } = new InvalidOperationException("The workflow timing clock failed once.");

    public override long TimestampFrequency => actual.TimestampFrequency;

    public override long GetTimestamp()
    {
        if (FailNextTimestamp)
        {
            FailNextTimestamp = false;
            throw Original;
        }
        return actual.GetTimestamp();
    }

    public override DateTimeOffset GetUtcNow()
    {
        return actual.GetUtcNow();
    }

    public override System.Threading.ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        return actual.CreateTimer(callback, state, dueTime, period);
    }
}

internal abstract class AbstractPipelineStep(PipelineTestContext context, PipelineDependency dependency)
    : PipelineProbeStep("abstract", context, dependency)
{
}

internal sealed class PipelineSyncStep : IPipelineStep<PipelineTestContext>, IDisposable
{
    private readonly PipelineTestContext context;
    private readonly PipelineDependency dependency;

    public PipelineSyncStep(PipelineTestContext context, PipelineDependency dependency)
    {
        this.context = context;
        this.dependency = dependency;
    }

    public Task<PipelineStepResult> Execute(PipelineTestContext supplied, CancellationToken cancellationToken)
    {
        Assert.AreSame(context, supplied);
        return Task.FromResult(PipelineStepResult.Succeeded());
    }

    public Task Error(PipelineTestContext supplied, PipelineFailure failure, CancellationToken cancellationToken)
    {
        throw new AssertFailedException("The successful synchronous-disposal step must not unwind.");
    }

    public void Dispose()
    {
        Assert.IsFalse(dependency.Disposed);
        context.SyncOnlyDisposals++;
        context.Events.Add("dispose.sync");
    }
}
