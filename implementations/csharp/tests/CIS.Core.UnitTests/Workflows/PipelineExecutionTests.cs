using CIS.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Workflows;

[TestClass]
public sealed class PipelineExecutionTests
{
    [TestMethod]
    public async Task ActivationPrecedesHeldOrderedWorkAndSnapshotsRetainActualPhaseDurations()
    {
        await using var host = new PipelineTestHost();
        var entered = PipelineTestHost.Signal();
        var release = PipelineTestHost.Signal();
        host.Context.Forward = async (name, _) =>
        {
            if (name == "A")
            {
                entered.TrySetResult();
                await release.Task;
            }
            else
            {
                host.Clock.Advance(TimeSpan.FromSeconds(name == "B" ? 2 : 3));
            }
            return PipelineStepResult.Succeeded();
        };
        host.Context.DisposeStep = _ =>
        {
            host.Clock.Advance(TimeSpan.FromMilliseconds(100));
            return ValueTask.CompletedTask;
        };
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>()
            .Add<PipelineStepB>().Add<PipelineStepC>().Build();
        Task run = pipeline.Execute();
        try
        {
            await entered.Task.WaitAsync(PipelineTestHost.Deadline);
            CollectionAssert.AreEqual(new[] { "construct.A", "construct.B", "construct.C", "execute.A" }, host.Context.Events);
            Assert.AreEqual(PipelineStatus.Running, pipeline.Status);
            IReadOnlyList<PipelineStepReport> before = pipeline.Steps;
            Assert.AreEqual(PipelineStepExecutionStatus.Running, before[0].ExecutionStatus);
            Assert.IsNull(before[1].ExecutionDuration);
            host.Clock.Advance(TimeSpan.FromSeconds(1));
            release.TrySetResult();
            await run.WaitAsync(PipelineTestHost.Deadline);

            Assert.AreEqual(PipelineStatus.Succeeded, pipeline.Status);
            Assert.IsTrue(pipeline.Success);
            Assert.AreSame(host.Context, pipeline.Context);
            Assert.AreEqual(TimeSpan.FromSeconds(6.3), pipeline.Duration);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3) },
                pipeline.Steps.Select(step => step.ExecutionDuration!.Value).ToArray());
            CollectionAssert.AreEqual(new[] { "dispose.C", "dispose.B", "dispose.A", "dependency.dispose" },
                host.Context.Events.Where(value => value.StartsWith("dispose.", StringComparison.Ordinal) || value == "dependency.dispose").ToArray());
            Assert.IsTrue(pipeline.Steps.All(step => step.ErrorStatus == PipelineStepErrorStatus.NotCalled && step.ErrorDuration is null));
            Assert.AreEqual(PipelineStepExecutionStatus.Running, before[0].ExecutionStatus);
            Assert.AreEqual(1, host.Observation.StartedCount);
            Assert.AreEqual(1, host.Observation.CompletedCount);
            CollectionAssert.AreEqual(pipeline.Steps.Select(step => step.ExecutionDuration!.Value).ToArray(),
                host.Observation.Phases.Select(phase => phase.Duration).ToArray());
        }
        finally
        {
            release.TrySetResult();
            await PipelineTestHost.SettleAsync(run);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task ExpectedFailureUnwindsExactlyTheEnteredRegistrationsInReverse(int failedPosition)
    {
        await using var host = new PipelineTestHost();
        string failed = new[] { "A", "B", "C" }[failedPosition];
        host.Context.Forward = (name, _) => Task.FromResult(name == failed
            ? PipelineStepResult.Failed("business.rejected", "The request was rejected.") : PipelineStepResult.Succeeded());
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Add<PipelineStepB>().Add<PipelineStepC>().Build();
        await pipeline.Execute().WaitAsync(PipelineTestHost.Deadline);

        Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
        Assert.IsFalse(pipeline.Success);
        Assert.AreEqual("business.rejected", pipeline.Error?.Code);
        Assert.AreEqual(failedPosition, pipeline.FailedStep?.Position);
        Assert.AreEqual(failedPosition + 1, pipeline.ExecutedSteps.Count);
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }.Take(failedPosition + 1).Reverse().Select(name => "error." + name).ToArray(),
            host.Context.Events.Where(value => value.StartsWith("error.", StringComparison.Ordinal)).ToArray());
        Assert.IsTrue(host.Context.HandlerCauses.All(cause => ReferenceEquals(cause, pipeline.Error)));
        Assert.IsTrue(pipeline.Steps.Skip(failedPosition + 1).All(step => step.ExecutionStatus == PipelineStepExecutionStatus.NotStarted
            && step.ErrorStatus == PipelineStepErrorStatus.NotCalled && step.ExecutionDuration is null && step.ErrorDuration is null));
        Assert.IsTrue(host.Context.Instances.All(step => step.AsyncDisposals == 1 && step.SyncDisposals == 0));
    }

    [TestMethod]
    [DataRow("sync")]
    [DataRow("task")]
    [DataRow("null-task")]
    [DataRow("null-result")]
    public async Task ForwardFaultsAndNullContractsUnwindAndPreserveTheirPrimaryCause(string mode)
    {
        await using var host = new PipelineTestHost();
        var original = new InvalidOperationException("The forward extension failed.");
        host.Context.Forward = (name, _) => name != "B" ? Task.FromResult(PipelineStepResult.Succeeded()) : mode switch
        {
            "sync" => throw original,
            "task" => Task.FromException<PipelineStepResult>(original),
            "null-task" => null!,
            _ => Task.FromResult<PipelineStepResult>(null!)
        };
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Add<PipelineStepB>().Add<PipelineStepC>().Build();
        Exception actual = await PipelineTestHost.FailureAsync(pipeline.Execute());

        Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
        Assert.AreEqual(1, pipeline.FailedStep?.Position);
        if (mode is "sync" or "task")
        {
            Assert.AreSame(original, actual);
            Assert.AreSame(original, pipeline.Error?.Exception);
        }
        else
        {
            Assert.IsInstanceOfType<InvalidOperationException>(actual);
            Assert.AreEqual(PipelineFailureKind.ContractViolation, pipeline.Error?.Kind);
        }
        CollectionAssert.AreEqual(new[] { "error.B", "error.A" }, host.Context.Events.Where(value => value.StartsWith("error.", StringComparison.Ordinal)).ToArray());
        Assert.AreEqual(PipelineStepExecutionStatus.NotStarted, pipeline.Steps[2].ExecutionStatus);
    }

    [TestMethod]
    public async Task HandlerFaultsKeepObservedReverseOrderAndCannotPromoteAnExpectedFailure()
    {
        await using var host = new PipelineTestHost();
        var first = new OperationCanceledException("Not the cleanup token.");
        var second = new InvalidOperationException("An earlier compensation failed.");
        host.Context.Forward = (name, _) => Task.FromResult(name == "B" ? PipelineStepResult.Failed("business.rejected") : PipelineStepResult.Succeeded());
        host.Context.Compensate = (name, _, _) => Task.FromException(name == "B" ? first : second);
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Add<PipelineStepB>().Add<PipelineStepC>().Build();
        await pipeline.Execute().WaitAsync(PipelineTestHost.Deadline);

        Assert.AreEqual("business.rejected", pipeline.Error?.Code);
        Assert.AreEqual(2, pipeline.ErrorHandlingFailures.Count);
        Assert.AreSame(first, pipeline.ErrorHandlingFailures[0].Exception);
        Assert.AreSame(second, pipeline.ErrorHandlingFailures[1].Exception);
        Assert.AreEqual(PipelineStepExecutionStatus.Succeeded, pipeline.Steps[0].ExecutionStatus);
        Assert.AreEqual(PipelineStepErrorStatus.Failed, pipeline.Steps[0].ErrorStatus);
        Assert.AreEqual(PipelineStepErrorStatus.Failed, pipeline.Steps[1].ErrorStatus);
        Assert.AreEqual(PipelineStepErrorStatus.NotCalled, pipeline.Steps[2].ErrorStatus);
        Assert.AreEqual(0, pipeline.LifecycleFailures.Count);
    }

    [TestMethod]
    public async Task ConcurrentReentrantAndRepeatedCallsCannotAlterTheAcceptedExecution()
    {
        await using var host = new PipelineTestHost();
        var entered = PipelineTestHost.Signal();
        var release = PipelineTestHost.Signal();
        IPipeline<PipelineTestContext>? pipeline = null;
        host.Context.Forward = async (_, _) =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => { _ = pipeline!.Execute(); });
            entered.TrySetResult();
            await release.Task;
            return PipelineStepResult.Succeeded();
        };
        pipeline = host.Builder().Add<PipelineStepA>().Build();
        Task run = pipeline.Execute();
        try
        {
            await entered.Task.WaitAsync(PipelineTestHost.Deadline);
            Assert.ThrowsExactly<InvalidOperationException>(() => { _ = pipeline.Execute(); });
            Assert.AreEqual(1, host.Lifetime.Active);
            release.TrySetResult();
            await run.WaitAsync(PipelineTestHost.Deadline);
            Assert.ThrowsExactly<InvalidOperationException>(() => { _ = pipeline.Execute(); });
            Assert.AreEqual(PipelineStatus.Succeeded, pipeline.Status);
            Assert.AreEqual(1, host.Lifetime.Entries);
            Assert.AreEqual(1, host.Lifetime.Exits);
            Assert.AreEqual(1, host.Observation.StartedCount);
            Assert.AreEqual(1, host.Observation.CompletedCount);
        }
        finally
        {
            release.TrySetResult();
            await PipelineTestHost.SettleAsync(run);
        }
    }

    [TestMethod]
    public async Task PreCancelledAcceptedRunAcquiresNoScopeAndStillConsumesTheInstance()
    {
        await using var host = new PipelineTestHost();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Build();
        Task run = pipeline.Execute(cancellation.Token);
        Assert.IsInstanceOfType<OperationCanceledException>(await PipelineTestHost.FailureAsync(run));
        Assert.IsTrue(run.IsCanceled);
        Assert.AreEqual(PipelineStatus.Cancelled, pipeline.Status);
        Assert.AreEqual(0, host.Activations);
        Assert.AreEqual(0, pipeline.ExecutedSteps.Count);
        Assert.AreEqual(1, host.Observation.StartedCount);
        Assert.AreEqual(1, host.Observation.CompletedCount);
        Assert.ThrowsExactly<InvalidOperationException>(() => { _ = pipeline.Execute(); });
    }

    [TestMethod]
    public async Task LaterConstructionFailureRunsNoForwardOrHandlerButDisposesEveryReturnedInstance()
    {
        await using var host = new PipelineTestHost();
        var original = new InvalidOperationException("B construction failed.");
        host.Context.Construct = name => { if (name == "B") { throw original; } };
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Add<PipelineStepB>().Add<PipelineStepC>().Build();
        Assert.AreSame(original, await PipelineTestHost.FailureAsync(pipeline.Execute()));

        Assert.AreEqual(PipelinePhase.Construction, pipeline.Error?.Phase);
        Assert.AreEqual(1, pipeline.Error?.StepPosition);
        Assert.AreSame(pipeline.Error, pipeline.LifecycleFailures.Single());
        Assert.IsNull(pipeline.FailedStep);
        Assert.AreEqual(0, pipeline.ExecutedSteps.Count);
        Assert.IsTrue(pipeline.Steps.All(step => step.ExecutionStatus == PipelineStepExecutionStatus.NotStarted && step.ExecutionDuration is null));
        Assert.AreEqual(1, host.Context.Instances.Single().AsyncDisposals);
        Assert.IsTrue(host.Context.Dependencies.Single().Disposed);
        Assert.AreEqual(0, host.Context.HandlerCauses.Count);
    }

    [TestMethod]
    public async Task BuilderSnapshotsAndDuplicateRegistrationsCreateFreshInstancesInsteadOfResolvingStepSingletons()
    {
        await using var host = new PipelineTestHost(services => services.AddSingleton<PipelineStepA>(_ =>
            throw new AssertFailedException("The registered step singleton must never be resolved.")));
        var options = new PipelineOptions();
        PipelineBuilder<PipelineTestContext> builder = host.Builder(options).Add<PipelineStepA>("first");
        IPipeline<PipelineTestContext> first = builder.Build();
        IPipeline<PipelineTestContext> second = builder.Add<PipelineStepA>("second").Build();
        options.ErrorHandlingTimeout = TimeSpan.Zero;
        options.LogCalls = false;
        Assert.AreEqual(0, host.Activations);
        await first.Execute().WaitAsync(PipelineTestHost.Deadline);
        await second.Execute().WaitAsync(PipelineTestHost.Deadline);

        Assert.AreEqual(1, first.Steps.Count);
        Assert.AreEqual(2, second.Steps.Count);
        Assert.AreEqual(3, host.Context.Instances.Count);
        Assert.AreNotSame(host.Context.Instances[1], host.Context.Instances[2]);
        Assert.AreNotSame(host.Context.Instances[0].Dependency, host.Context.Instances[1].Dependency);
        Assert.AreSame(host.Context.Instances[1].Dependency, host.Context.Instances[2].Dependency);
        Assert.IsTrue(host.Runs.All(info => info.LogCalls));
        CollectionAssert.AreEqual(new[] { 0, 1 }, second.Steps.Select(step => step.Position).ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OwnedAndBorrowedScopesKeepInjectedDependenciesAliveThroughAsyncStepDisposal(bool borrowed)
    {
        await using var host = new PipelineTestHost();
        await using AsyncServiceScope scope = host.Provider.CreateAsyncScope();
        IPipeline<PipelineTestContext> pipeline = host.Builder(borrowed: borrowed ? scope.ServiceProvider : null)
            .Add<PipelineStepA>().Add<PipelineStepB>().Build();
        await pipeline.Execute().WaitAsync(PipelineTestHost.Deadline);
        PipelineDependency dependency = host.Context.Dependencies.Single();

        Assert.AreEqual(!borrowed, dependency.Disposed);
        Assert.IsFalse(dependency.Singleton.Disposed);
        Assert.IsFalse(host.Context.Disposed);
        Assert.IsTrue(host.Context.Instances.All(instance => instance.AsyncDisposals == 1 && instance.SyncDisposals == 0));
        if (borrowed)
        {
            Assert.AreSame(dependency, scope.ServiceProvider.GetRequiredService<PipelineDependency>());
        }
    }

    [TestMethod]
    public async Task DisposalFaultAfterSuccessPropagatesTheFirstFaultAndAttemptsRemainingObjectsWithoutUnwind()
    {
        await using var host = new PipelineTestHost();
        var first = new InvalidOperationException("C disposal failed.");
        var second = new InvalidOperationException("B disposal failed.");
        host.Context.DisposeStep = name => name switch
        {
            "C" => ValueTask.FromException(first),
            "B" => ValueTask.FromException(second),
            _ => ValueTask.CompletedTask
        };
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Add<PipelineStepB>().Add<PipelineStepC>().Build();
        Assert.AreSame(first, await PipelineTestHost.FailureAsync(pipeline.Execute()));

        Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
        Assert.AreEqual(PipelinePhase.Disposal, pipeline.Error?.Phase);
        Assert.AreEqual(2, pipeline.Error?.StepPosition);
        Assert.AreEqual(2, pipeline.LifecycleFailures.Count);
        Assert.IsTrue(pipeline.Steps.All(step => step.ExecutionStatus == PipelineStepExecutionStatus.Succeeded && step.ErrorStatus == PipelineStepErrorStatus.NotCalled));
        Assert.IsTrue(host.Context.Instances.All(instance => instance.AsyncDisposals == 1));
        Assert.IsTrue(host.Context.Dependencies.Single().Disposed);
    }

    [TestMethod]
    public async Task ExistingBusinessFailureReturnsNormallyEvenWhenOwnedScopeDisposalFails()
    {
        await using var host = new PipelineTestHost();
        var secondary = new InvalidOperationException("The scope-owned dependency failed during disposal.");
        host.Context.DependencyDisposalFailure = secondary;
        host.Context.Forward = (_, _) => Task.FromResult(PipelineStepResult.Failed("business.rejected"));
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Build();
        await pipeline.Execute().WaitAsync(PipelineTestHost.Deadline);

        Assert.AreEqual("business.rejected", pipeline.Error?.Code);
        Assert.AreSame(secondary, pipeline.LifecycleFailures.Single().Exception);
        Assert.IsNull(pipeline.LifecycleFailures[0].StepPosition);
        Assert.AreEqual(PipelineStepErrorStatus.Succeeded, pipeline.Steps[0].ErrorStatus);
    }

    [TestMethod]
    public async Task LateCancellationDuringHeldDisposalCannotReopenCommittedForwardSuccess()
    {
        await using var host = new PipelineTestHost();
        using var cancellation = new CancellationTokenSource();
        var entered = PipelineTestHost.Signal();
        var release = PipelineTestHost.Signal();
        host.Context.DisposeStep = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Build();
        Task run = pipeline.Execute(cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(PipelineTestHost.Deadline);
            cancellation.Cancel();
            Assert.IsFalse(run.IsCompleted);
            Assert.AreEqual(PipelineStatus.Running, pipeline.Status);
            Assert.IsNull(pipeline.Duration);
            Assert.IsFalse(host.Context.Dependencies.Single().Disposed);
            release.TrySetResult();
            await run.WaitAsync(PipelineTestHost.Deadline);
            Assert.IsTrue(pipeline.Success);
            Assert.IsNull(pipeline.Error);
            Assert.AreEqual(PipelineStepErrorStatus.NotCalled, pipeline.Steps[0].ErrorStatus);
        }
        finally
        {
            release.TrySetResult();
            await PipelineTestHost.SettleAsync(run);
        }
    }

    [TestMethod]
    public async Task CallerCancellationWaitsForRealForwardSettlementAndUsesIndependentHeldCleanup()
    {
        await using var host = new PipelineTestHost();
        using var cancellation = new CancellationTokenSource();
        var forwardEntered = PipelineTestHost.Signal();
        var cleanupEntered = PipelineTestHost.Signal();
        var release = PipelineTestHost.Signal();
        host.Context.Forward = async (_, token) =>
        {
            forwardEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return PipelineStepResult.Succeeded();
        };
        host.Context.Compensate = async (_, _, token) =>
        {
            Assert.AreNotEqual(cancellation.Token, token);
            Assert.IsFalse(token.IsCancellationRequested);
            cleanupEntered.TrySetResult();
            await release.Task;
        };
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Add<PipelineStepB>().Build();
        Task run = pipeline.Execute(cancellation.Token);
        try
        {
            await forwardEntered.Task.WaitAsync(PipelineTestHost.Deadline);
            cancellation.Cancel();
            await cleanupEntered.Task.WaitAsync(PipelineTestHost.Deadline);
            Assert.AreEqual(PipelineStatus.Unwinding, pipeline.Status);
            Assert.IsFalse(run.IsCompleted);
            Assert.IsFalse(host.Context.Dependencies.Single().Disposed);
            release.TrySetResult();
            Assert.IsInstanceOfType<OperationCanceledException>(await PipelineTestHost.FailureAsync(run));
            Assert.AreEqual(PipelineStatus.Cancelled, pipeline.Status);
            Assert.AreEqual(PipelineStepExecutionStatus.Cancelled, pipeline.Steps[0].ExecutionStatus);
            Assert.AreEqual(PipelineStepExecutionStatus.NotStarted, pipeline.Steps[1].ExecutionStatus);
            Assert.IsTrue(run.IsCanceled);
        }
        finally
        {
            cancellation.Cancel();
            release.TrySetResult();
            await PipelineTestHost.SettleAsync(run);
        }
    }

    [TestMethod]
    public async Task ExpiredWholeUnwindBudgetStillInvokesRemainingHandlersAndNeverAbandonsAnIgnoringHandler()
    {
        await using var host = new PipelineTestHost();
        var entered = PipelineTestHost.Signal();
        var release = PipelineTestHost.Signal();
        host.Context.Forward = (name, _) => Task.FromResult(name == "B" ? PipelineStepResult.Failed("business.rejected") : PipelineStepResult.Succeeded());
        host.Context.Compensate = async (name, _, token) =>
        {
            if (name == "B")
            {
                entered.TrySetResult();
                await release.Task;
            }
            else
            {
                Assert.IsTrue(token.IsCancellationRequested);
                token.ThrowIfCancellationRequested();
            }
        };
        IPipeline<PipelineTestContext> pipeline = host.Builder(new PipelineOptions { ErrorHandlingTimeout = TimeSpan.FromMilliseconds(20) })
            .Add<PipelineStepA>().Add<PipelineStepB>().Build();
        Task run = pipeline.Execute();
        try
        {
            await entered.Task.WaitAsync(PipelineTestHost.Deadline);
            host.Clock.Advance(TimeSpan.FromMilliseconds(30));
            Assert.IsTrue(pipeline.CleanupBudgetExpired);
            Assert.AreEqual(1, host.Observation.ExpiredCount);
            Assert.AreEqual(PipelineStatus.Unwinding, pipeline.Status);
            Assert.IsFalse(run.IsCompleted);
            Assert.AreEqual(0, host.Context.Instances.Sum(instance => instance.AsyncDisposals));
            release.TrySetResult();
            await run.WaitAsync(PipelineTestHost.Deadline);
            Assert.AreEqual("business.rejected", pipeline.Error?.Code);
            Assert.AreEqual(PipelineStepErrorStatus.Succeeded, pipeline.Steps[1].ErrorStatus);
            Assert.AreEqual(PipelineStepErrorStatus.Cancelled, pipeline.Steps[0].ErrorStatus);
            Assert.AreEqual(PipelineFailureKind.CleanupTimeout, pipeline.ErrorHandlingFailures.Single().Kind);
            Assert.AreEqual(1, host.Observation.ExpiredCount);
        }
        finally
        {
            release.TrySetResult();
            await PipelineTestHost.SettleAsync(run);
        }
    }

    [TestMethod]
    [DataRow(PipelinePhase.Construction)]
    [DataRow(PipelinePhase.Execute)]
    [DataRow(PipelinePhase.Disposal)]
    public async Task FailedPrimaryCancellationExceptionsRemainFaultedWithTheirOriginalDiagnosticCause(PipelinePhase phase)
    {
        await using var host = new PipelineTestHost();
        using var caller = new CancellationTokenSource();
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        var original = new OperationCanceledException("The phase fault was not accepted cancellation.",
            phase == PipelinePhase.Execute ? foreign.Token : caller.Token);
        if (phase == PipelinePhase.Construction)
        {
            host.Context.Construct = _ => { caller.Cancel(); throw original; };
        }
        else if (phase == PipelinePhase.Execute)
        {
            host.Context.Forward = (_, _) => { caller.Cancel(); return Task.FromException<PipelineStepResult>(original); };
        }
        else
        {
            host.Context.DisposeStep = _ => { caller.Cancel(); return ValueTask.FromException(original); };
        }
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Build();
        Task run = pipeline.Execute(caller.Token);
        Exception actual = await PipelineTestHost.FailureAsync(run);

        Assert.IsInstanceOfType<PipelineExecutionException>(actual);
        Assert.AreSame(original, actual.InnerException);
        Assert.AreSame(original, pipeline.Error?.Exception);
        Assert.AreEqual(phase, pipeline.Error?.Phase);
        Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
        Assert.IsTrue(run.IsFaulted);
    }

    [TestMethod]
    public async Task ClosedOwnerRejectsBeforeClaimScopeAndObservation()
    {
        await using var host = new PipelineTestHost();
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Build();
        host.Lifetime.Closed = true;
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = pipeline.Execute(); });
        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = pipeline.Execute(); });
        Assert.AreEqual(PipelineStatus.NotStarted, pipeline.Status);
        Assert.AreEqual(0, host.Activations);
        Assert.AreEqual(0, host.Lifetime.Entries);
        Assert.AreEqual(0, host.Observation.StartedCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnavailableOrThrowingObservationCannotChangeBusinessFailureOrCleanup(bool factoryThrows)
    {
        await using var host = new PipelineTestHost();
        host.Observation.OnCallback = () => throw new InvalidOperationException("Observation failed.");
        host.Context.Forward = (_, _) => Task.FromResult(PipelineStepResult.Failed("business.rejected"));
        IPipeline<PipelineTestContext> pipeline = host.Builder(observe: factoryThrows
            ? _ => throw new InvalidOperationException("Attachment failed.") : _ => host.Observation).Add<PipelineStepA>().Build();
        await pipeline.Execute().WaitAsync(PipelineTestHost.Deadline);

        Assert.AreEqual("business.rejected", pipeline.Error?.Code);
        Assert.AreEqual(PipelineStepErrorStatus.Succeeded, pipeline.Steps[0].ErrorStatus);
        Assert.AreEqual(1, host.Context.Instances.Single().AsyncDisposals);
        Assert.AreEqual(0, host.Lifetime.Active);
        Assert.AreEqual(0, pipeline.LifecycleFailures.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AClockFaultAfterEnteredWorkStillUnwindsOnceAndDisposesAllConstructedObjects(bool atNextAdmission)
    {
        await using var host = new PipelineTestHost();
        var clock = new FaultOncePipelineClock(host.Clock);
        bool armAfterPhase = false;
        host.Context.Forward = (_, _) =>
        {
            if (atNextAdmission)
            {
                armAfterPhase = true;
            }
            else
            {
                clock.FailNextTimestamp = true;
            }
            return Task.FromResult(PipelineStepResult.Succeeded());
        };
        host.Observation.OnCallback = () =>
        {
            if (armAfterPhase)
            {
                armAfterPhase = false;
                clock.FailNextTimestamp = true;
            }
        };
        IPipeline<PipelineTestContext> pipeline = host.Builder(clock: clock).Add<PipelineStepA>().Add<PipelineStepB>().Build();
        Assert.AreSame(clock.Original, await PipelineTestHost.FailureAsync(pipeline.Execute()));

        Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
        Assert.AreEqual(PipelinePhase.Execute, pipeline.Error?.Phase);
        Assert.AreEqual(PipelineStepExecutionStatus.Succeeded, pipeline.Steps[0].ExecutionStatus);
        Assert.AreEqual(PipelineStepExecutionStatus.NotStarted, pipeline.Steps[1].ExecutionStatus);
        CollectionAssert.AreEqual(new[] { "error.A" }, host.Context.Events.Where(value => value.StartsWith("error.", StringComparison.Ordinal)).ToArray());
        Assert.IsTrue(host.Context.Instances.All(instance => instance.AsyncDisposals == 1));
        Assert.IsTrue(host.Context.Dependencies.Single().Disposed);
        Assert.AreEqual(1, host.Observation.CompletedCount);
    }
}
