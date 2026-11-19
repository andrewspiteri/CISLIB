using CIS.CommonTesting;
using CIS.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Workflows;

internal enum MachineState
{
    Idle = -7, Ready = 18, Done = 91
}
internal enum MachineTrigger
{
    Start = 3, Finish = 14, Reset = 58, invalid = 90
}

internal sealed class StateMachineTestHost : IAsyncDisposable
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    internal StateMachineTestContext Context { get; } = new();
    internal ManualTimeProvider Clock { get; } = new(DateTimeOffset.UnixEpoch);
    internal StateMachineTestLifetime Lifetime { get; } = new();
    internal StateMachineTestObservation Observation { get; } = new();
    internal ServiceProvider Provider
    {
        get;
    }
    internal List<StateMachineDefinitionMetadata> Definitions { get; } = [];
    internal int Scopes
    {
        get; private set;
    }
    internal Action<StateMachineCallInfo>? ObserveCall
    {
        get; set;
    }

    internal StateMachineTestHost(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Context);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<StateMachineTestSingleton>();
        services.AddScoped<StateMachineTestDependency>();
        configure?.Invoke(services);
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
    internal StateMachineBuilder<TState, TTrigger, StateMachineTestContext> Builder<TState, TTrigger>(
        StateMachineOptions? options = null, string name = "test-machine") where TState : struct, Enum where TTrigger : struct, Enum
    {
        var environment = new StateMachineEnvironment(Clock, () =>
        {
            Scopes++;
            return PipelineServiceActivation.CreateOwned(Provider.GetRequiredService<IServiceScopeFactory>());
        }, Lifetime, metadata => Definitions.Add(metadata), info =>
        {
            ObserveCall?.Invoke(info);
            return Observation;
        });
        return new(environment, name, Context, options);
    }
    internal StateMachineBuilder<MachineState, MachineTrigger, StateMachineTestContext> Builder(StateMachineOptions? options = null)
    {
        return Builder<MachineState, MachineTrigger>(options);
    }
    public async ValueTask DisposeAsync()
    {
        await Lifetime.CloseAndDrain().WaitAsync(Deadline);
        Assert.AreEqual(0, Lifetime.Active, "All public calls must settle before the provider is disposed.");
        await Provider.DisposeAsync();
    }
    internal static TaskCompletionSource Signal()
    {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    internal static async Task SettleAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }
        try
        {
            await task.WaitAsync(Deadline);
        }
        catch (Exception exception) when (exception is not TimeoutException) { }
    }
}

internal sealed class StateMachineTestLifetime : IPipelineLifetime
{
    private readonly object gate = new();
    private TaskCompletionSource? drained;
    internal bool Closed
    {
        get; set;
    }
    internal int Active
    {
        get; private set;
    }
    internal int Entries
    {
        get; private set;
    }
    internal int Exits
    {
        get; private set;
    }
    internal Action? ExitProbe
    {
        get; set;
    }
    public void Enter()
    {
        lock (gate)
        {
            if (Closed)
            {
                throw new InvalidOperationException("The test host is stopping.");
            }
            if (Active == 0)
            {
                drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            Active++;
            Entries++;
        }
    }
    public void Exit()
    {
        lock (gate)
        {
            ExitProbe?.Invoke();
            Active--;
            Exits++;
            if (Active == 0)
            {
                drained?.TrySetResult();
            }
        }
    }
    internal Task CloseAndDrain()
    {
        lock (gate)
        {
            Closed = true;
            return Active == 0 ? Task.CompletedTask : drained!.Task;
        }
    }
}

internal sealed class StateMachineTestObservation : IStateMachineObservation
{
    internal int Calls
    {
        get; private set;
    }
    internal int Admissions
    {
        get; private set;
    }
    internal int Expiries
    {
        get; private set;
    }
    internal List<StateMachineCompletion> Completions { get; } = [];
    internal List<(PipelinePhase Phase, PipelineStatus Outcome, TimeSpan? Duration)> Phases { get; } = [];
    internal Action? Callback
    {
        get; set;
    }
    public void Started()
    {
        Calls++;
        Callback?.Invoke();
    }
    public void GateAcquired()
    {
        Admissions++;
        Callback?.Invoke();
    }
    public void PhaseStarted(StateMachineHandlerRegistration handler, PipelinePhase phase)
    {
        Callback?.Invoke();
    }
    public void PhaseCompleted(StateMachineHandlerRegistration handler, PipelinePhase phase, PipelineStatus outcome,
        TimeSpan? duration, StateTransitionFailure? failure)
    {
        Phases.Add((phase, outcome, duration));
        Callback?.Invoke();
    }
    public void CleanupBudgetExpired()
    {
        Expiries++;
        Callback?.Invoke();
    }
    public void LifecycleFailure(StateTransitionFailure failure)
    {
        Callback?.Invoke();
    }
    public void Completed(StateMachineCompletion completion)
    {
        Completions.Add(completion);
        Callback?.Invoke();
    }
}

internal sealed class StateMachineTestContext
{
    internal List<string> Events { get; } = [];
    internal List<StateMachineTestHandler> Instances { get; } = [];
    internal List<StateMachineTestDependency> Dependencies { get; } = [];
    internal List<StateTransitionFailure> HandlerCauses { get; } = [];
    internal Action? Construct
    {
        get; set;
    }
    internal Func<TransitionInfo<MachineState, MachineTrigger>, CancellationToken, Task<TransitionActionResult>> Forward
    {
        get; set;
    }
        = static (_, _) => Task.FromResult(TransitionActionResult.Succeeded());
    internal Func<TransitionInfo<MachineState, MachineTrigger>, StateTransitionFailure, CancellationToken, Task> Compensate
    {
        get; set;
    }
        = static (_, _, _) => Task.CompletedTask;
    internal Func<ValueTask> DisposeHandler { get; set; } = static () => ValueTask.CompletedTask;
    internal Exception? ScopeDisposalFailure
    {
        get; set;
    }
}

internal sealed class StateMachineTestSingleton : IDisposable
{
    public StateMachineTestSingleton()
    {
    }
    internal bool Disposed
    {
        get; private set;
    }
    public void Dispose()
    {
        Disposed = true;
    }
}

internal sealed class StateMachineTestDependency : IDisposable
{
    private readonly StateMachineTestContext context;
    internal StateMachineTestSingleton Singleton
    {
        get;
    }
    internal bool Disposed
    {
        get; private set;
    }
    public StateMachineTestDependency(StateMachineTestContext context, StateMachineTestSingleton singleton)
    {
        this.context = context;
        Singleton = singleton;
        context.Dependencies.Add(this);
    }
    public void Dispose()
    {
        Disposed = true;
        context.Events.Add("scope.dispose");
        if (context.ScopeDisposalFailure is { } failure)
        {
            throw failure;
        }
    }
}

internal sealed class StateMachineTestHandler : IStateTransitionHandler<MachineState, MachineTrigger, StateMachineTestContext>,
    IAsyncDisposable, IDisposable
{
    private readonly StateMachineTestContext context;
    internal StateMachineTestDependency Dependency
    {
        get;
    }
    internal int AsyncDisposals
    {
        get; private set;
    }
    internal int SyncDisposals
    {
        get; private set;
    }
    public StateMachineTestHandler(StateMachineTestContext context, StateMachineTestDependency dependency)
    {
        this.context = context;
        Dependency = dependency;
        context.Construct?.Invoke();
        context.Instances.Add(this);
        context.Events.Add("construct");
    }
    public Task<TransitionActionResult> Execute(StateMachineTestContext supplied,
        TransitionInfo<MachineState, MachineTrigger> transition, CancellationToken cancellationToken)
    {
        Assert.AreSame(context, supplied);
        Assert.IsFalse(Dependency.Disposed);
        context.Events.Add("execute");
        return context.Forward(transition, cancellationToken);
    }
    public Task Error(StateMachineTestContext supplied, TransitionInfo<MachineState, MachineTrigger> transition,
        StateTransitionFailure failure, CancellationToken cancellationToken)
    {
        Assert.AreSame(context, supplied);
        Assert.IsFalse(Dependency.Disposed);
        context.Events.Add("error");
        context.HandlerCauses.Add(failure);
        return context.Compensate(transition, failure, cancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        Assert.IsFalse(Dependency.Disposed);
        context.Events.Add("handler.dispose");
        AsyncDisposals++;
        await context.DisposeHandler();
    }
    public void Dispose()
    {
        SyncDisposals++;
    }
}
