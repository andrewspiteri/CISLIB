using System.ComponentModel;
using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed partial class HealthPipeInternalMutationQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public void FirstInstanceProtectionRejectsAnExistingEndpointEvenWhenItHasSpareCapacity()
    {
        string pipe = "hc-" + Guid.NewGuid().ToString("N");
        using var first = HealthPipeEndpoint.Create(pipe, maximumClients: 2, first: true);
        using (HealthPipeEndpoint.Create(pipe, maximumClients: 2, first: false)) { }
        void OpenCollision() { using var collision = HealthPipeEndpoint.Create(pipe, maximumClients: 2, first: true); }
        if (OperatingSystem.IsWindows()) { Assert.ThrowsExactly<Win32Exception>(OpenCollision); }
        else { Assert.ThrowsExactly<UnauthorizedAccessException>(OpenCollision); }
        Assert.IsFalse(first.IsConnected);
    }

    [TestMethod]
    [DataRow("arguments")]
    [DataRow("standardOutput")]
    [DataRow("standardError")]
    public async Task ProbeRejectsMissingCallerResourcesBeforeConnecting(string missing)
    {
        using var output = new MemoryStream();
        using var error = new StringWriter();
        ArgumentNullException failure = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
            HealthProbeClient.RunAsync(missing == "arguments" ? null! : [],
                missing == "standardOutput" ? null! : output, missing == "standardError" ? null! : error));
        Assert.AreEqual(missing, failure.ParamName);
        Assert.AreEqual(0L, output.Length);
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task ProbeCancellationProducesOnlyTheBoundedErrorAndKeepsCallerStreamsOpen()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new MemoryStream();
        using var error = new StringWriter();
        int exit = await HealthProbeClient.RunAsync(["--pipe", "hc-" + Guid.NewGuid().ToString("N")], output, error, cancellation.Token);
        Assert.AreEqual(2, exit);
        StringAssert.Contains(error.ToString(), "TimeoutOrCanceled");
        Assert.AreEqual(0L, output.Length);
        output.WriteByte(1);
        error.Write("still-owned-by-caller");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BrokenErrorDestinationCannotReplaceTheDocumentedErrorExit(bool disposed)
    {
        using var output = new MemoryStream();
        using var error = new UnavailableWriter(disposed);
        Assert.AreEqual(2, await HealthProbeClient.RunAsync([], output, error));
        Assert.AreEqual(1, error.Attempts);
        Assert.AreEqual(0L, output.Length);
    }

    [TestMethod]
    public void FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException()
    {
        ReviewInternalProbeOwners(RunFailedOutputScenario());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static InternalProbeObservation RunFailedOutputScenario()
    {
        var service = new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "probe-output-owner", InstanceId = Guid.NewGuid() });
        service.MarkStartupCompleted();
        string pipe = "hc-" + Guid.NewGuid().ToString("N");
        var source = new StartupCountingSource(service);
        var options = new HealthcheckOptions { PipeEnabled = true, PipeName = pipe };
        var clock = new InternalBudgetClock();
        HealthPipeServer? server = null;
        Task<int>? probe = null;
        Exception? failure = null;
        try
        {
            server = CreateStartedServer(source, options, clock);
            using var output = new UnavailableStream();
            using var error = new StringWriter();
            probe = HealthProbeClient.RunAsync(["--pipe", pipe], output, error);
            int exit = JoinInternal(probe);
            Assert.AreEqual(2, exit);
            StringAssert.Contains(error.ToString(), "TransportOrProtocolFailure");
            Assert.IsFalse(error.ToString().Contains("private output path", StringComparison.Ordinal));
            Assert.AreEqual(1, output.Attempts);
        }
        catch (Exception error) { failure = error; }
        return ReleaseInternalProbeOwners(server, service, source, clock, options, probe, failure);
    }

    [TestMethod]
    public void SuccessfulProbeDeliversBufferedOutputBeforeReturningItsHealthExit()
    {
        ReviewInternalProbeOwners(RunBufferedOutputScenario());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static InternalProbeObservation RunBufferedOutputScenario()
    {
        var service = new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "buffered-probe-owner", InstanceId = Guid.NewGuid() });
        service.MarkStartupCompleted();
        string pipe = "hc-" + Guid.NewGuid().ToString("N");
        var source = new StartupCountingSource(service);
        var options = new HealthcheckOptions { PipeEnabled = true, PipeName = pipe };
        var clock = new InternalBudgetClock();
        HealthPipeServer? server = null;
        Task<int>? probe = null;
        Exception? failure = null;
        try
        {
            server = CreateStartedServer(source, options, clock);
            using var delivered = new MemoryStream();
            using var buffered = new BufferedStream(delivered, 65536);
            using var error = new StringWriter();
            probe = HealthProbeClient.RunAsync(["--pipe", pipe], buffered, error);
            Assert.AreEqual(0, JoinInternal(probe));
            byte[] actual = delivered.ToArray();
            Assert.IsTrue(actual.Length > 1);
            Assert.AreEqual((byte)'\n', actual[^1]);
            using var json = System.Text.Json.JsonDocument.Parse(actual.AsMemory(0, actual.Length - 1));
            Assert.AreEqual("buffered-probe-owner", json.RootElement.GetProperty("application").GetProperty("name").GetString());
            Assert.AreEqual(string.Empty, error.ToString());
        }
        catch (Exception failureValue) { failure = failureValue; }
        return ReleaseInternalProbeOwners(server, service, source, clock, options, probe, failure);
    }

    [TestMethod]
    [DataRow(49, false)]
    [DataRow(50, true)]
    [DataRow(51, true)]
    public void MonotonicDeadlineRejectsTheExactBoundaryEvenWhenTimerCallbacksAreDelayed(int elapsed, bool expired)
    {
        var clock = new DelayedTimerClock();
        using var budget = new HealthPipeBudget(clock, 50, CancellationToken.None);
        clock.Timestamp = elapsed;
        Assert.IsFalse(budget.Token.IsCancellationRequested);
        if (expired)
        {
            OperationCanceledException failure = Assert.ThrowsExactly<OperationCanceledException>(budget.ThrowIfExpired);
            Assert.AreEqual(budget.Token, failure.CancellationToken);
        }
        else { budget.ThrowIfExpired(); }
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), clock.DueTime);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, clock.Period);
    }

    [TestMethod]
    public void ExternalCancellationIsLinkedAndBudgetDisposalReleasesOnlyItsOwnedResources()
    {
        var clock = new DelayedTimerClock();
        using var caller = new CancellationTokenSource();
        using WaitHandle callerWait = caller.Token.WaitHandle;
        var budget = new HealthPipeBudget(clock, 50, caller.Token);
        CancellationToken token = budget.Token;
        WaitHandle ownedWait = token.WaitHandle;
        caller.Cancel();
        Assert.IsTrue(token.IsCancellationRequested);
        Assert.ThrowsExactly<OperationCanceledException>(budget.ThrowIfExpired);
        budget.Dispose();
        Assert.IsTrue(ownedWait.SafeWaitHandle.IsClosed);
        Assert.IsTrue(clock.TimerDisposed);
        Assert.IsFalse(callerWait.SafeWaitHandle.IsClosed);
    }

    private sealed class StartupCountingSource(HealthcheckService service) : IHealthSnapshotSource
    {
        internal InternalSourcePhase Phase { get; } = new();
        internal int Captures => Volatile.Read(ref Phase.Captures);
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref Phase.Captures);
            try { return service.CaptureSnapshot(); }
            catch (Exception error) { Phase.RecordFailure(error); throw; }
            finally { Interlocked.Increment(ref Phase.Exits); }
        }
    }

    private sealed class UnavailableWriter(bool disposed) : StringWriter
    {
        internal int Attempts { get; private set; }
        public override Task WriteLineAsync(string? value)
        {
            Attempts++;
            return Task.FromException(disposed ? new ObjectDisposedException("caller-stderr") : new IOException("private destination detail"));
        }
    }

    private sealed class UnavailableStream : MemoryStream
    {
        internal int Attempts { get; private set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return ValueTask.FromException(new IOException("private output path"));
        }
    }

    private sealed class DelayedTimerClock : TimeProvider
    {
        internal long Timestamp { get; set; }
        internal TimeSpan DueTime { get; private set; }
        internal TimeSpan Period { get; private set; }
        internal bool TimerDisposed { get; private set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Timestamp;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime;
            Period = period;
            return new DelayedTimer(this);
        }
        private sealed class DelayedTimer(DelayedTimerClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => owner.TimerDisposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
