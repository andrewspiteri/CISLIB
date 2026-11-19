using CIS.Hosting;
using CIS.Startup;
using CIS.TestSupport;
using CIS.Workflows;
using CIS.Workflows.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Application;

[TestClass]
public sealed class StateMachineHostingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FacadeJoinsSelectedHandlerErrorAndDisposalBeforeBusinessTeardown(bool web)
    {
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        var context = new HeldContext();
        var business = new BusinessDependency();

        void Configure(CISApplicationBuilder builder)
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.AddSingleton(_ => business);
            builder.Services.AddSingleton<IHostedService>(root => root.GetRequiredService<BusinessDependency>());
            builder.Services.AddScoped<ScopedDependency>();
            builder.Services.AddCisStateMachines();
            builder.Services.AddCisStateMachines();
        }

        CISApplication app = web ? CISApplication.CreateWeb([], Configure, _ => { }) : new CISApplication(Configure);
        Task<StateTransitionResult<State, Trigger>>? execution = null;
        Task? disposal = null;
        try
        {
            await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
            IStateMachineFactory factory = app.Services.GetRequiredService<IStateMachineFactory>();
            IStateMachine<State, Trigger> machine = Create(factory, context);
            execution = machine.Trigger(Trigger.Go);
            await context.ErrorEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);

            using var wait = new CancellationTokenSource();
            Task stop = app.StopAsync(wait.Token);
            wait.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => stop);
            disposal = app.DisposeAsync().AsTask();
            Assert.IsFalse(disposal.IsCompleted);
            Assert.AreEqual(State.Source, machine.CurrentState);
            Assert.IsNull(machine.LastTransition);
            Assert.IsFalse(context.ExecutionToken.IsCancellationRequested);
            business.Use();
            Assert.Throws<InvalidOperationException>(() => machine.Trigger(Trigger.Go));

            context.ReleaseError.TrySetResult();
            await context.DisposalEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.IsFalse(execution.IsCompleted);
            Assert.IsFalse(disposal.IsCompleted);
            business.Use();

            context.ReleaseDisposal.TrySetResult();
            StateTransitionResult<State, Trigger> result = await execution.WaitAsync(ApplicationTestSupport.Deadline);
            await disposal.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreEqual(StateTransitionOutcome.Failed, result.Outcome);
            Assert.AreSame(result, machine.LastTransition);
            Assert.AreEqual(State.Source, machine.CurrentState);
            Assert.AreEqual(1, context.HandlerDisposals);
            Assert.AreEqual(1, context.ScopeDisposals);
            Assert.AreEqual(1, business.Stops);
            Assert.AreEqual(1, business.Disposals);
        }
        finally
        {
            context.ReleaseError.TrySetResult();
            context.ReleaseDisposal.TrySetResult();
            await ApplicationTestSupport.SettleAsync(execution);
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task FailedBuildOrStartupDrainsTheActualSharedOwnerBeforeDisposingDependencies(bool web, bool duringBuild)
    {
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        var context = new HeldContext();
        var business = new BusinessDependency();
        var original = new InvalidOperationException("Failure after a State Machine attempt was accepted.");
        Task<StateTransitionResult<State, Trigger>>? execution = null;
        IStateMachine<State, Trigger>? machine = null;

        void Configure(CISApplicationBuilder builder)
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.AddSingleton(_ => business);
            builder.Services.AddScoped<ScopedDependency>();
            builder.Services.AddCisStateMachines();

            void StartAttempt(IServiceProvider root)
            {
                machine = Create(root.GetRequiredService<IStateMachineFactory>(), context);
                execution = machine.Trigger(Trigger.Go);
            }

            if (duringBuild)
            {
                builder.Services.Replace(ServiceDescriptor.Singleton<IHostLifetime>(root =>
                {
                    StartAttempt(root);
                    throw original;
                }));
            }
            else
            {
                builder.Services.AddSingleton<IStartable>(root => new ApplicationTestSupport.Participant(_ =>
                {
                    StartAttempt(root);
                    return Task.FromException(original);
                }));
            }
        }

        CISApplication app = web ? CISApplication.CreateWeb([], Configure, _ => { }) : new CISApplication(Configure);
        Task? disposal = null;
        try
        {
            Assert.AreSame(original, await ApplicationTestSupport.FailureAsync(app.StartAsync()));
            await context.ErrorEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            disposal = app.DisposeAsync().AsTask();
            Assert.IsFalse(disposal.IsCompleted);
            business.Use();
            Assert.Throws<InvalidOperationException>(() => machine!.Trigger(Trigger.Go));
            context.ReleaseError.TrySetResult();
            await context.DisposalEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.IsFalse(disposal.IsCompleted);
            business.Use();
            context.ReleaseDisposal.TrySetResult();

            Assert.AreEqual(StateTransitionOutcome.Failed,
                (await execution!.WaitAsync(ApplicationTestSupport.Deadline)).Outcome);
            await disposal.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreSame(original, app.Failure);
            Assert.AreEqual(1, business.Disposals);
            Assert.AreEqual(1, context.ScopeDisposals);
        }
        finally
        {
            context.ReleaseError.TrySetResult();
            context.ReleaseDisposal.TrySetResult();
            await ApplicationTestSupport.SettleAsync(execution);
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BothOptInOrdersInstallOneBridgeAndStateOnlyDoesNotRegisterPipelineFactory(bool pipelineFirst)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddCisStateMachines();
        Assert.IsFalse(builder.Services.Any(item => item.ServiceType == typeof(IPipelineFactory)));

        if (pipelineFirst)
        {
            builder = Host.CreateApplicationBuilder();
            builder.Services.AddCisWorkflows();
            builder.Services.AddCisStateMachines();
        }
        else
        {
            builder.Services.AddCisWorkflows();
        }

        builder.Services.AddCisStateMachines();
        builder.Services.AddCisWorkflows();
        Assert.AreEqual(1, builder.Services.Count(item => item.ImplementationType == typeof(PipelineHostedService)));
        Assert.AreEqual(1, builder.Services.Count(item => item.ServiceType == typeof(PipelineHostingRegistration)));
        using IHost host = builder.Build();
        await host.StartAsync();

        StateMachineBuilder<State, Trigger, object> definition = host.Services.GetRequiredService<IStateMachineFactory>()
            .CreateBuilder<State, Trigger, object>("Direct", new object());
        definition.State(State.Source).On(Trigger.Go, State.Target);
        IStateMachine<State, Trigger> machine = definition.Build(State.Source);
        Assert.IsTrue((await machine.Trigger(Trigger.Go)).Success);
        Assert.IsNotNull(host.Services.GetRequiredService<IPipelineFactory>());
        await host.StopAsync();
        Assert.Throws<InvalidOperationException>(() => machine.Trigger(Trigger.Go));
    }

    [TestMethod]
    public async Task OneNativeHostDrainJoinsBothPipelineAndStateMachineWork()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        var business = new BusinessDependency();
        builder.Services.AddSingleton(_ => business);
        builder.Services.AddSingleton<IHostedService>(root => root.GetRequiredService<BusinessDependency>());
        builder.Services.AddScoped<ScopedDependency>();
        builder.Services.AddCisWorkflows();
        builder.Services.AddCisStateMachines();
        using IHost host = builder.Build();
        await host.StartAsync();
        var stateContext = new HeldContext();
        var pipelineContext = new PipelineHold();
        Task<StateTransitionResult<State, Trigger>> state = Create(host.Services.GetRequiredService<IStateMachineFactory>(), stateContext)
            .Trigger(Trigger.Go);
        Task pipeline = host.Services.GetRequiredService<IPipelineFactory>().CreateBuilder("SharedPipeline", pipelineContext)
            .Add<WaitingStep>().Build().Execute();
        Task? stop = null;
        try
        {
            await stateContext.ErrorEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            await pipelineContext.Entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            stop = host.StopAsync();
            Assert.IsFalse(stop.IsCompleted);
            pipelineContext.Release.TrySetResult();
            await pipeline.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted, "Settling Pipeline alone must not release the shared owner.");
            business.Use();
            stateContext.ReleaseError.TrySetResult();
            await stateContext.DisposalEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted);
            stateContext.ReleaseDisposal.TrySetResult();
            await state.WaitAsync(ApplicationTestSupport.Deadline);
            await stop.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreEqual(1, business.Stops);
        }
        finally
        {
            pipelineContext.Release.TrySetResult();
            stateContext.ReleaseError.TrySetResult();
            stateContext.ReleaseDisposal.TrySetResult();
            await ApplicationTestSupport.SettleAsync(pipeline);
            await ApplicationTestSupport.SettleAsync(state);
            await (stop ?? host.StopAsync()).WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task PerCallBorrowedScopesShareTheFreshChildPipelineAndNeverPersistOnTheMachine()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<ScopedDependency>();
        builder.Services.AddCisWorkflows();
        builder.Services.AddCisStateMachines();
        using IHost host = builder.Build();
        var context = new ScopeContext();
        await host.StartAsync();
        StateMachineBuilder<State, Trigger, ScopeContext> definition = host.Services.GetRequiredService<IStateMachineFactory>()
            .CreateBuilder<State, Trigger, ScopeContext>("ScopedChild", context);
        definition.State(State.Source).On<ChildHandler>(Trigger.Go, State.Source);
        IStateMachine<State, Trigger> machine = definition.Build(State.Source);
        ScopedDependency? first = null;

        for (int index = 0; index < 2; index++)
        {
            await using AsyncServiceScope caller = host.Services.CreateAsyncScope();
            ScopedDependency expected = caller.ServiceProvider.GetRequiredService<ScopedDependency>();
            Assert.IsTrue((await machine.TriggerInScope(Trigger.Go, caller.ServiceProvider)).Success);
            Assert.AreSame(expected, context.HandlerDependency);
            Assert.AreSame(expected, context.ChildDependency);
            Assert.AreEqual(0, expected.Disposals);
            if (first is not null)
            {
                Assert.AreNotSame(expected, first);
                Assert.AreEqual(1, first.Disposals);
            }

            first = expected;
        }

        Assert.AreEqual(1, first!.Disposals);
        Assert.AreEqual(2, context.HandlerDisposals);
        Assert.AreEqual(2, context.ChildDisposals);
        await host.StopAsync();
    }

    [TestMethod]
    public async Task NativeWebListenerAbortPrecedesStateMachineDrainAndActualScopedCleanup()
    {
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        var context = new HeldContext();
        var business = new BusinessDependency();
        Task<StateTransitionResult<State, Trigger>>? execution = null;
        CISApplication app = CISApplication.CreateWeb([], builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.Zero);
            builder.Services.AddSingleton(_ => business);
            builder.Services.AddSingleton<IHostedService>(root => root.GetRequiredService<BusinessDependency>());
            builder.Services.AddScoped<ScopedDependency>();
            builder.Services.AddCisStateMachines();
        }, application => application.MapGet("/held-state", async (HttpContext request, IStateMachineFactory factory) =>
        {
            execution = Create(factory, context).TriggerInScope(Trigger.Go, request.RequestServices, request.RequestAborted);
            await execution;
            return Results.Ok();
        }));

        Task? disposal = null;
        try
        {
            await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
            using HttpClient client = ApplicationTestSupport.Client(app);
            Task<HttpResponseMessage> response = client.GetAsync("/held-state");
            await context.ErrorEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            disposal = app.DisposeAsync().AsTask();
            await Assert.ThrowsAsync<HttpRequestException>(async () => await response.WaitAsync(ApplicationTestSupport.Deadline));
            business.Use();
            Assert.IsFalse(disposal.IsCompleted);
            context.ReleaseError.TrySetResult();
            await context.DisposalEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            business.Use();
            Assert.IsFalse(disposal.IsCompleted);
            context.ReleaseDisposal.TrySetResult();
            Assert.AreEqual(StateTransitionOutcome.Failed,
                (await execution!.WaitAsync(ApplicationTestSupport.Deadline)).Outcome);
            await disposal.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreEqual(1, business.Stops);
            Assert.AreEqual(1, context.ScopeDisposals);
        }
        finally
        {
            context.ReleaseError.TrySetResult();
            context.ReleaseDisposal.TrySetResult();
            await ApplicationTestSupport.SettleAsync(execution);
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeHostRejectsUnsupportedOrderOrConcurrentTeardown(bool concurrent)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddCisStateMachines();
        if (concurrent)
        {
            builder.Services.Configure<HostOptions>(options => options.ServicesStopConcurrently = true);
        }
        else
        {
            builder.Services.AddHostedService<BusinessDependency>();
        }

        using IHost host = builder.Build();
        Exception failure = await ApplicationTestSupport.FailureAsync(host.StartAsync());
        StringAssert.Contains(failure.Message, "ordinary business hosted services");
        await host.StopAsync();
    }

    private static IStateMachine<State, Trigger> Create(IStateMachineFactory factory, HeldContext context)
    {
        StateMachineBuilder<State, Trigger, HeldContext> builder = factory.CreateBuilder<State, Trigger, HeldContext>("HeldState", context);
        builder.State(State.Source).On<HeldHandler>(Trigger.Go, State.Target, "held");
        return builder.Build(State.Source);
    }

    private enum State
    {
        Source,
        Target
    }

    private enum Trigger
    {
        Go
    }

    private sealed class PipelineHold
    {
        internal TaskCompletionSource Entered { get; } = ApplicationTestSupport.Signal();
        internal TaskCompletionSource Release { get; } = ApplicationTestSupport.Signal();
    }

    private sealed class WaitingStep : IPipelineStep<PipelineHold>
    {
        public async Task<PipelineStepResult> Execute(PipelineHold value, CancellationToken cancellationToken)
        {
            value.Entered.TrySetResult();
            await value.Release.Task;
            return PipelineStepResult.Succeeded();
        }

        public Task Error(PipelineHold value, PipelineFailure failure, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HeldContext
    {
        internal TaskCompletionSource ErrorEntered { get; } = ApplicationTestSupport.Signal();
        internal TaskCompletionSource ReleaseError { get; } = ApplicationTestSupport.Signal();
        internal TaskCompletionSource DisposalEntered { get; } = ApplicationTestSupport.Signal();
        internal TaskCompletionSource ReleaseDisposal { get; } = ApplicationTestSupport.Signal();
        internal CancellationToken ExecutionToken
        {
            get; set;
        }
        internal int HandlerDisposals
        {
            get; set;
        }
        internal int ScopeDisposals
        {
            get; set;
        }
    }

    private sealed class HeldHandler(BusinessDependency business, ScopedDependency scoped)
        : IStateTransitionHandler<State, Trigger, HeldContext>, IAsyncDisposable
    {
        private HeldContext? context;

        public Task<TransitionActionResult> Execute(HeldContext value, TransitionInfo<State, Trigger> transition,
            CancellationToken cancellationToken)
        {
            context = value;
            scoped.Context = value;
            value.ExecutionToken = cancellationToken;
            business.Use();
            scoped.Use();
            return Task.FromResult(TransitionActionResult.Failed("Expected"));
        }

        public async Task Error(HeldContext value, TransitionInfo<State, Trigger> transition,
            StateTransitionFailure failure, CancellationToken cancellationToken)
        {
            value.ErrorEntered.TrySetResult();
            await value.ReleaseError.Task;
            business.Use();
            scoped.Use();
        }

        public async ValueTask DisposeAsync()
        {
            context!.DisposalEntered.TrySetResult();
            await context.ReleaseDisposal.Task;
            business.Use();
            scoped.Use();
            context.HandlerDisposals++;
        }
    }

    private sealed class BusinessDependency : IHostedService, IAsyncDisposable
    {
        public BusinessDependency()
        {
        }
        internal int Stops
        {
            get; private set;
        }
        internal int Disposals
        {
            get; private set;
        }

        internal void Use()
        {
            Assert.AreEqual(0, Stops);
            Assert.AreEqual(0, Disposals);
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stops++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposals = 1;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScopedDependency : IDisposable
    {
        public ScopedDependency()
        {
        }
        internal HeldContext? Context
        {
            get; set;
        }
        internal int Disposals
        {
            get; private set;
        }

        internal void Use()
        {
            Assert.AreEqual(0, Disposals);
        }

        public void Dispose()
        {
            Disposals++;
            if (Context is { } value)
            {
                value.ScopeDisposals++;
            }
        }
    }

    private sealed class ScopeContext
    {
        internal ScopedDependency? HandlerDependency
        {
            get; set;
        }
        internal ScopedDependency? ChildDependency
        {
            get; set;
        }
        internal int HandlerDisposals
        {
            get; set;
        }
        internal int ChildDisposals
        {
            get; set;
        }
    }

    private sealed class ChildHandler(IPipelineFactory factory, IServiceProvider provider, ScopedDependency dependency)
        : IStateTransitionHandler<State, Trigger, ScopeContext>, IDisposable
    {
        private ScopeContext? context;

        public async Task<TransitionActionResult> Execute(ScopeContext value, TransitionInfo<State, Trigger> transition,
            CancellationToken cancellationToken)
        {
            context = value;
            value.HandlerDependency = dependency;
            IPipeline<ScopeContext> child = factory.CreateBuilderInScope("ScopedStateChild", value, provider)
                .Add<ChildStep>().Build();
            await child.Execute(cancellationToken);
            return child.Success ? TransitionActionResult.Succeeded() : TransitionActionResult.Failed("Child");
        }

        public Task Error(ScopeContext value, TransitionInfo<State, Trigger> transition,
            StateTransitionFailure failure, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
            dependency.Use();
            context!.HandlerDisposals++;
        }
    }

    private sealed class ChildStep(ScopedDependency dependency) : IPipelineStep<ScopeContext>, IDisposable
    {
        private ScopeContext? context;

        public Task<PipelineStepResult> Execute(ScopeContext value, CancellationToken cancellationToken)
        {
            context = value;
            value.ChildDependency = dependency;
            return Task.FromResult(PipelineStepResult.Succeeded());
        }

        public Task Error(ScopeContext value, PipelineFailure failure, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
            dependency.Use();
            context!.ChildDisposals++;
        }
    }
}
