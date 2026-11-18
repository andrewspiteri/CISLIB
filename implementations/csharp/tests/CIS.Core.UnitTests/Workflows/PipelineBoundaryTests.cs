using CIS.Workflows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Workflows;

[TestClass]
public sealed class PipelineBoundaryTests
{
    [TestMethod]
    [DataRow("empty")]
    [DataRow("abstract")]
    [DataRow("alias")]
    [DataRow("name")]
    [DataRow("zero")]
    [DataRow("negative")]
    [DataRow("too-long")]
    public async Task InvalidDefinitionsAndCleanupBudgetsFailBeforeActivation(string scenario)
    {
        await using var host = new PipelineTestHost();
        if (scenario == "name")
        {
            Assert.ThrowsExactly<ArgumentException>(() => host.Builder(name: "not\nprintable"));
        }
        else if (scenario == "alias")
        {
            Assert.ThrowsExactly<ArgumentException>(() => host.Builder().Add<PipelineStepA>(" "));
        }
        else if (scenario == "abstract")
        {
            Assert.ThrowsExactly<ArgumentException>(() => host.Builder().Add<AbstractPipelineStep>());
        }
        else if (scenario == "empty")
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => host.Builder().Build());
        }
        else
        {
            TimeSpan timeout = scenario switch
            {
                "zero" => TimeSpan.Zero,
                "negative" => TimeSpan.FromSeconds(-2),
                _ => TimeSpan.MaxValue
            };
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => host.Builder(new PipelineOptions { ErrorHandlingTimeout = timeout })
                .Add<PipelineStepA>().Build());
        }

        Assert.AreEqual(0, host.Activations);
        Assert.AreEqual(0, host.Lifetime.Entries);
        Assert.AreEqual(0, host.Context.Instances.Count);
    }

    [TestMethod]
    public async Task CancellationBetweenActivationsDisposesReturnedStepsWithoutEnteringForwardOrError()
    {
        await using var host = new PipelineTestHost();
        using var cancellation = new CancellationTokenSource();
        host.Context.Construct = name => { if (name == "A") { cancellation.Cancel(); } };
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Add<PipelineStepB>().Build();
        Task run = pipeline.Execute(cancellation.Token);
        Assert.IsInstanceOfType<OperationCanceledException>(await PipelineTestHost.FailureAsync(run));

        Assert.AreEqual(PipelineStatus.Cancelled, pipeline.Status);
        Assert.AreEqual(PipelinePhase.Construction, pipeline.Error?.Phase);
        Assert.AreEqual(0, pipeline.ExecutedSteps.Count);
        Assert.AreEqual(0, host.Context.HandlerCauses.Count);
        Assert.AreEqual("A", host.Context.Instances.Single().Label);
        Assert.AreEqual(1, host.Context.Instances[0].AsyncDisposals);
        Assert.IsTrue(host.Context.Dependencies.Single().Disposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationAfterASuccessfulMethodPreservesItsSuccessAndUnwindsAtTheCheckpoint(bool finalMethod)
    {
        await using var host = new PipelineTestHost();
        using var cancellation = new CancellationTokenSource();
        host.Context.Forward = (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(PipelineStepResult.Succeeded());
        };
        PipelineBuilder<PipelineTestContext> builder = host.Builder().Add<PipelineStepA>();
        if (!finalMethod) { builder.Add<PipelineStepB>(); }
        IPipeline<PipelineTestContext> pipeline = builder.Build();
        Assert.IsInstanceOfType<OperationCanceledException>(await PipelineTestHost.FailureAsync(pipeline.Execute(cancellation.Token)));

        Assert.AreEqual(PipelineStatus.Cancelled, pipeline.Status);
        Assert.AreEqual(PipelineStepExecutionStatus.Succeeded, pipeline.Steps[0].ExecutionStatus);
        Assert.AreEqual(PipelineStepErrorStatus.Succeeded, pipeline.Steps[0].ErrorStatus);
        Assert.IsNull(pipeline.FailedStep);
        Assert.IsNull(pipeline.Error?.StepPosition);
        CollectionAssert.AreEqual(new[] { "error.A" }, host.Context.Events.Where(value => value.StartsWith("error.", StringComparison.Ordinal)).ToArray());
        if (!finalMethod) { Assert.AreEqual(PipelineStepExecutionStatus.NotStarted, pipeline.Steps[1].ExecutionStatus); }
    }

    [TestMethod]
    public async Task ReturnedBusinessFailureWinsOverCancellationRequestedDuringThatMethod()
    {
        await using var host = new PipelineTestHost();
        using var cancellation = new CancellationTokenSource();
        host.Context.Forward = (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(PipelineStepResult.Failed("business.rejected"));
        };
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Build();
        Task run = pipeline.Execute(cancellation.Token);
        await run.WaitAsync(PipelineTestHost.Deadline);

        Assert.IsTrue(run.IsCompletedSuccessfully);
        Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
        Assert.AreEqual(PipelineFailureKind.ReportedFailure, pipeline.Error?.Kind);
        Assert.AreEqual(PipelineStepExecutionStatus.Failed, pipeline.Steps[0].ExecutionStatus);
        Assert.AreEqual(PipelineStepErrorStatus.Succeeded, pipeline.Steps[0].ErrorStatus);
    }

    [TestMethod]
    public async Task NotYetRequestedCancellationExceptionCannotBecomeCallerCancellationDuringHeldUnwind()
    {
        await using var host = new PipelineTestHost();
        using var cancellation = new CancellationTokenSource();
        var original = new OperationCanceledException("The token had not been requested when thrown.", cancellation.Token);
        var entered = PipelineTestHost.Signal();
        var release = PipelineTestHost.Signal();
        host.Context.Forward = (_, _) => Task.FromException<PipelineStepResult>(original);
        host.Context.Compensate = async (_, _, _) =>
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
            Assert.AreEqual(PipelineStatus.Unwinding, pipeline.Status);
            release.TrySetResult();
            Exception actual = await PipelineTestHost.FailureAsync(run);
            Assert.IsInstanceOfType<PipelineExecutionException>(actual);
            Assert.AreSame(original, actual.InnerException);
            Assert.AreSame(original, pipeline.Error?.Exception);
            Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
            Assert.IsTrue(run.IsFaulted);
        }
        finally
        {
            release.TrySetResult();
            await PipelineTestHost.SettleAsync(run);
        }
    }

    [TestMethod]
    public async Task InfiniteBudgetHasOneIndependentTokenAndNoExpiryAcrossTheWholeUnwind()
    {
        await using var host = new PipelineTestHost();
        using var execution = new CancellationTokenSource();
        var tokens = new List<CancellationToken>();
        host.Context.Forward = (name, _) => Task.FromResult(name == "B" ? PipelineStepResult.Failed("business.rejected") : PipelineStepResult.Succeeded());
        host.Context.Compensate = (_, _, token) =>
        {
            tokens.Add(token);
            host.Clock.Advance(TimeSpan.FromDays(1));
            Assert.IsFalse(token.IsCancellationRequested);
            return Task.CompletedTask;
        };
        IPipeline<PipelineTestContext> pipeline = host.Builder(new PipelineOptions { ErrorHandlingTimeout = Timeout.InfiniteTimeSpan })
            .Add<PipelineStepA>().Add<PipelineStepB>().Build();
        await pipeline.Execute(execution.Token).WaitAsync(PipelineTestHost.Deadline);

        Assert.AreEqual(2, tokens.Count);
        Assert.AreEqual(tokens[0], tokens[1]);
        Assert.AreNotEqual(execution.Token, tokens[0]);
        Assert.IsFalse(pipeline.CleanupBudgetExpired);
        Assert.IsTrue(pipeline.Steps.All(step => step.ErrorStatus == PipelineStepErrorStatus.Succeeded));
        Assert.AreEqual(0, pipeline.ErrorHandlingFailures.Count);
    }

    [TestMethod]
    public async Task NullHandlerTaskIsSecondaryAndCannotSkipEarlierEligibleHandlers()
    {
        await using var host = new PipelineTestHost();
        host.Context.Forward = (name, _) => Task.FromResult(name == "B" ? PipelineStepResult.Failed("business.rejected") : PipelineStepResult.Succeeded());
        host.Context.Compensate = (name, _, _) => name == "B" ? null! : Task.CompletedTask;
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Add<PipelineStepB>().Build();
        await pipeline.Execute().WaitAsync(PipelineTestHost.Deadline);

        Assert.AreEqual("business.rejected", pipeline.Error?.Code);
        Assert.AreEqual(PipelineFailureKind.ContractViolation, pipeline.ErrorHandlingFailures.Single().Kind);
        Assert.AreEqual(PipelineStepErrorStatus.Failed, pipeline.Steps[1].ErrorStatus);
        Assert.AreEqual(PipelineStepErrorStatus.Succeeded, pipeline.Steps[0].ErrorStatus);
        CollectionAssert.AreEqual(new[] { "error.B", "error.A" }, host.Context.Events.Where(value => value.StartsWith("error.", StringComparison.Ordinal)).ToArray());
    }

    [TestMethod]
    public async Task SynchronousOnlyStepIsDisposedBeforeItsContainerOwnedDependency()
    {
        await using var host = new PipelineTestHost();
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineSyncStep>().Build();
        await pipeline.Execute().WaitAsync(PipelineTestHost.Deadline);

        Assert.IsTrue(pipeline.Success);
        Assert.AreEqual(1, host.Context.SyncOnlyDisposals);
        CollectionAssert.AreEqual(new[] { "dispose.sync", "dependency.dispose" }, host.Context.Events);
        Assert.IsTrue(host.Context.Dependencies.Single().Disposed);
    }

    [TestMethod]
    public async Task OwnerDrainReleaseHappensOnlyAfterTheActualReturnedExecutionTaskIsComplete()
    {
        await using var host = new PipelineTestHost();
        var entered = PipelineTestHost.Signal();
        var release = PipelineTestHost.Signal();
        host.Context.Forward = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return PipelineStepResult.Succeeded();
        };
        IPipeline<PipelineTestContext> pipeline = host.Builder().Add<PipelineStepA>().Build();
        Task? run = null;
        bool observedComplete = false;
        host.Lifetime.OnExit = () =>
        {
            Assert.IsNotNull(run);
            observedComplete = run.IsCompleted;
            Assert.IsTrue(observedComplete);
            Assert.AreEqual(PipelineStatus.Succeeded, pipeline.Status);
        };
        run = pipeline.Execute();
        try
        {
            await entered.Task.WaitAsync(PipelineTestHost.Deadline);
            release.TrySetResult();
            await run.WaitAsync(PipelineTestHost.Deadline);
            Assert.AreEqual(PipelineStatus.Succeeded, pipeline.Status);
            Assert.IsTrue(observedComplete);
            Assert.AreEqual(0, host.Lifetime.Active);
        }
        finally
        {
            release.TrySetResult();
            await PipelineTestHost.SettleAsync(run);
        }
    }

    [TestMethod]
    public async Task FinalClockFaultStillPublishesOneTerminalObservationAfterActualOwnedCleanup()
    {
        await using var host = new PipelineTestHost();
        var clock = new FaultOncePipelineClock(host.Clock);
        host.Context.DisposeStep = _ =>
        {
            clock.FailNextTimestamp = true;
            return ValueTask.CompletedTask;
        };
        IPipeline<PipelineTestContext> pipeline = host.Builder(clock: clock).Add<PipelineStepA>().Build();
        Assert.AreSame(clock.Original, await PipelineTestHost.FailureAsync(pipeline.Execute()));

        Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
        Assert.AreEqual(PipelinePhase.Disposal, pipeline.Error?.Phase);
        Assert.IsNull(pipeline.Duration);
        Assert.AreEqual(PipelineStepExecutionStatus.Succeeded, pipeline.Steps[0].ExecutionStatus);
        Assert.AreEqual(PipelineStepErrorStatus.NotCalled, pipeline.Steps[0].ErrorStatus);
        Assert.AreEqual(1, host.Context.Instances.Single().AsyncDisposals);
        Assert.IsTrue(host.Context.Dependencies.Single().Disposed);
        Assert.AreEqual(1, host.Observation.StartedCount);
        Assert.AreEqual(1, host.Observation.CompletedCount);
        Assert.AreEqual(0, host.Lifetime.Active);
    }
}
