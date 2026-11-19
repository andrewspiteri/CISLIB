using CIS.Workflows;
using CIS.Workflows.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Workflows;

[TestClass]
public sealed class StateMachineChildPipelineTests
{
    [TestMethod]
    [DataRow("child-reported")]
    [DataRow("child-success-outer-failure")]
    [DataRow("foreign-child-oce")]
    public async Task FreshRealChildPipelineOwnsItsUnwindWhileTheOuterCompensatesOnlyItsOwnWork(string mode)
    {
        using var caller = new CancellationTokenSource();
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        var original = new OperationCanceledException("Unrelated child work ended.", foreign.Token);
        var context = new ChildContext(mode, original);
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
        builder.Services.AddSingleton(context);
        builder.Services.AddScoped<SharedDependency>();
        builder.Services.AddCisWorkflows();
        builder.Services.AddCisStateMachines();
        IHost host = builder.Build();
        AsyncServiceScope scope = host.Services.CreateAsyncScope();
        bool scopeDisposed = false;
        Task<StateTransitionResult<OuterState, OuterTrigger>>? run = null;
        try
        {
            await host.StartAsync().WaitAsync(StateMachineTestHost.Deadline);
            SharedDependency dependency = scope.ServiceProvider.GetRequiredService<SharedDependency>();
            context.ExpectedDependency = dependency;
            IStateMachineFactory factory = host.Services.GetRequiredService<IStateMachineFactory>();
            StateMachineBuilder<OuterState, OuterTrigger, ChildContext> definition = factory
                .CreateBuilder<OuterState, OuterTrigger, ChildContext>("nested-outer", context,
                    new StateMachineOptions { LogCalls = false });
            definition.State(OuterState.Pending).On<OuterHandler>(OuterTrigger.Resolve, OuterState.Resolved, "outer");
            IStateMachine<OuterState, OuterTrigger> machine = definition.Build(OuterState.Pending);
            run = machine.TriggerInScope(OuterTrigger.Resolve, scope.ServiceProvider, caller.Token);
            if (mode == "foreign-child-oce")
            {
                await context.ChildErrorEntered.Task.WaitAsync(StateMachineTestHost.Deadline);
                caller.Cancel();
                Assert.IsFalse(run.IsCompleted);
                Assert.AreEqual(OuterState.Pending, machine.CurrentState);
                Assert.IsFalse(dependency.Disposed);
                context.ReleaseChildError.TrySetResult();
            }
            StateTransitionResult<OuterState, OuterTrigger> result = await run.WaitAsync(StateMachineTestHost.Deadline);
            Assert.IsTrue(run.IsCompletedSuccessfully);
            Assert.AreEqual(StateTransitionOutcome.Failed, result.Outcome);
            Assert.AreEqual(OuterState.Pending, machine.CurrentState);
            Assert.AreSame(result, machine.LastTransition);
            Assert.AreEqual(1, context.Children.Count);
            Assert.AreEqual(2, context.ChildDisposals);
            Assert.AreEqual(1, context.OuterDisposals);
            Assert.AreEqual(1, context.OuterCompensations);
            Assert.AreEqual(0, context.OuterEffect);
            Assert.AreEqual(PipelineStepErrorStatus.Succeeded, result.HandlerReport?.ErrorStatus);
            Assert.IsFalse(dependency.Disposed);
            Assert.AreEqual(0, dependency.Disposals);
            IPipeline<ChildContext> child = context.Children.Single();
            if (mode == "child-success-outer-failure")
            {
                Assert.IsTrue(child.Success);
                Assert.AreEqual("outer.rejected", result.ErrorCode);
                Assert.AreEqual("retained-result-42", context.CompensatedChildResult);
                Assert.AreEqual(0, context.ChildErrors.Count);
                Assert.IsTrue(child.Steps.All(step => step.ErrorStatus == PipelineStepErrorStatus.NotCalled));
            }
            else
            {
                Assert.IsFalse(child.Success);
                CollectionAssert.AreEqual(new[] { "second", "first" }, context.ChildErrors);
                Assert.IsTrue(child.Steps.All(step => step.ErrorStatus == PipelineStepErrorStatus.Succeeded));
                if (mode == "child-reported")
                {
                    Assert.AreEqual("child.rejected", result.ErrorCode);
                    Assert.AreEqual(PipelineFailureKind.ReportedFailure, child.Error?.Kind);
                }
                else
                {
                    Assert.IsInstanceOfType<PipelineExecutionException>(result.Error?.Exception);
                    Assert.AreSame(original, result.Error?.Exception?.InnerException);
                    Assert.AreSame(original, child.Error?.Exception);
                    Assert.AreEqual(StateTransitionFailureKind.Exception, result.Error?.Kind);
                }
            }
            Assert.IsTrue(context.Events.IndexOf("child.dispose.first") < context.Events.IndexOf("outer.error"));
            await scope.DisposeAsync();
            scopeDisposed = true;
            Assert.IsTrue(dependency.Disposed);
            Assert.AreEqual(1, dependency.Disposals);
        }
        finally
        {
            context.ReleaseChildError.TrySetResult();
            await StateMachineTestHost.SettleAsync(run);
            if (!scopeDisposed)
            {
                await scope.DisposeAsync();
            }
            await host.StopAsync(CancellationToken.None).WaitAsync(StateMachineTestHost.Deadline);
            if (host is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
    }

    private enum OuterState
    {
        Pending,
        Resolved
    }

    private enum OuterTrigger
    {
        Resolve
    }

    private sealed class ChildContext(string mode, OperationCanceledException original)
    {
        internal string Mode { get; } = mode;
        internal OperationCanceledException Original { get; } = original;
        internal SharedDependency? ExpectedDependency
        {
            get; set;
        }
        internal List<IPipeline<ChildContext>> Children { get; } = [];
        internal List<string> ChildErrors { get; } = [];
        internal List<string> Events { get; } = [];
        internal TaskCompletionSource ChildErrorEntered { get; } = StateMachineTestHost.Signal();
        internal TaskCompletionSource ReleaseChildError { get; } = StateMachineTestHost.Signal();
        internal int ChildDisposals
        {
            get; set;
        }
        internal int OuterDisposals
        {
            get; set;
        }
        internal int OuterCompensations
        {
            get; set;
        }
        internal int OuterEffect
        {
            get; set;
        }
        internal string? ChildBusinessResult
        {
            get; set;
        }
        internal string? CompensatedChildResult
        {
            get; set;
        }
    }

    private sealed class SharedDependency : IAsyncDisposable
    {
        public SharedDependency()
        {
        }
        internal bool Disposed
        {
            get; private set;
        }
        internal int Disposals
        {
            get; private set;
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OuterHandler : IStateTransitionHandler<OuterState, OuterTrigger, ChildContext>, IAsyncDisposable
    {
        private readonly IPipelineFactory pipelines;
        private readonly IServiceProvider scope;
        private readonly SharedDependency dependency;
        private readonly ChildContext context;

        public OuterHandler(IPipelineFactory pipelines, IServiceProvider scope, SharedDependency dependency, ChildContext context)
        {
            this.pipelines = pipelines;
            this.scope = scope;
            this.dependency = dependency;
            this.context = context;
        }
        public async Task<TransitionActionResult> Execute(ChildContext supplied,
            TransitionInfo<OuterState, OuterTrigger> transition, CancellationToken cancellationToken)
        {
            Assert.AreSame(context, supplied);
            Assert.AreSame(context.ExpectedDependency, dependency);
            context.OuterEffect = 1;
            IPipeline<ChildContext> child = pipelines.CreateBuilderInScope("nested-child", context, scope,
                new PipelineOptions { LogCalls = false }).Add<FirstChild>("first").Add<SecondChild>("second").Build();
            context.Children.Add(child);
            await child.Execute(cancellationToken);
            return child.Success
                ? TransitionActionResult.Failed("outer.rejected", "The child succeeded; later outer work was rejected.")
                : TransitionActionResult.Failed(child.Error!.Code, "The child already attempted its compensation.");
        }
        public Task Error(ChildContext supplied, TransitionInfo<OuterState, OuterTrigger> transition,
            StateTransitionFailure failure, CancellationToken cancellationToken)
        {
            Assert.IsFalse(dependency.Disposed);
            Assert.AreEqual(2, context.ChildDisposals);
            context.Events.Add("outer.error");
            context.OuterCompensations++;
            context.OuterEffect = 0;
            context.CompensatedChildResult = context.ChildBusinessResult;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Assert.IsFalse(dependency.Disposed);
            context.OuterDisposals++;
            return ValueTask.CompletedTask;
        }
    }

    private abstract class ChildStep(ChildContext context, SharedDependency dependency, string name)
        : IPipelineStep<ChildContext>, IAsyncDisposable
    {
        public Task<PipelineStepResult> Execute(ChildContext supplied, CancellationToken cancellationToken)
        {
            Assert.AreSame(context, supplied);
            Assert.AreSame(context.ExpectedDependency, dependency);
            Assert.IsFalse(dependency.Disposed);
            if (name == "second")
            {
                if (context.Mode == "child-reported")
                {
                    return Task.FromResult(PipelineStepResult.Failed("child.rejected"));
                }
                if (context.Mode == "foreign-child-oce")
                {
                    return Task.FromException<PipelineStepResult>(context.Original);
                }
                context.ChildBusinessResult = "retained-result-42";
            }
            return Task.FromResult(PipelineStepResult.Succeeded());
        }
        public async Task Error(ChildContext supplied, PipelineFailure failure, CancellationToken cancellationToken)
        {
            Assert.IsFalse(dependency.Disposed);
            context.ChildErrors.Add(name);
            if (name == "second" && context.Mode == "foreign-child-oce")
            {
                context.ChildErrorEntered.TrySetResult();
                await context.ReleaseChildError.Task;
            }
        }
        public ValueTask DisposeAsync()
        {
            Assert.IsFalse(dependency.Disposed);
            context.ChildDisposals++;
            context.Events.Add("child.dispose." + name);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FirstChild(ChildContext context, SharedDependency dependency) : ChildStep(context, dependency, "first")
    {
    }

    private sealed class SecondChild(ChildContext context, SharedDependency dependency) : ChildStep(context, dependency, "second")
    {
    }
}
