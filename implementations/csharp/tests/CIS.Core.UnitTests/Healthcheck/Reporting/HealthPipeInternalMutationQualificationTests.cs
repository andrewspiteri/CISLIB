using System.ComponentModel;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthPipeInternalMutationQualificationTests
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
    public async Task FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException()
    {
        var service = new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "probe-output-owner", InstanceId = Guid.NewGuid() });
        service.MarkStartupCompleted();
        string pipe = "hc-" + Guid.NewGuid().ToString("N");
        var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
        try
        {
            await server.StartAsync().WaitAsync(Deadline);
            using var output = new UnavailableStream();
            using var error = new StringWriter();
            int exit = await HealthProbeClient.RunAsync(["--pipe", pipe], output, error).WaitAsync(Deadline);
            Assert.AreEqual(2, exit);
            StringAssert.Contains(error.ToString(), "TransportOrProtocolFailure");
            Assert.IsFalse(error.ToString().Contains("private output path", StringComparison.Ordinal));
            Assert.AreEqual(1, output.Attempts);
        }
        finally
        {
            try { await StopAndDisposeAsync(server); }
            finally { await service.DisposeAsync().AsTask().WaitAsync(Deadline); }
        }
    }

    [TestMethod]
    public async Task SuccessfulProbeDeliversBufferedOutputBeforeReturningItsHealthExit()
    {
        var service = new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "buffered-probe-owner", InstanceId = Guid.NewGuid() });
        service.MarkStartupCompleted();
        string pipe = "hc-" + Guid.NewGuid().ToString("N");
        var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });
        try
        {
            await server.StartAsync().WaitAsync(Deadline);
            using var delivered = new MemoryStream();
            using var buffered = new BufferedStream(delivered, 65536);
            using var error = new StringWriter();
            Assert.AreEqual(0, await HealthProbeClient.RunAsync(["--pipe", pipe], buffered, error).WaitAsync(Deadline));
            byte[] actual = delivered.ToArray();
            Assert.IsTrue(actual.Length > 1);
            Assert.AreEqual((byte)'\n', actual[^1]);
            using var json = System.Text.Json.JsonDocument.Parse(actual.AsMemory(0, actual.Length - 1));
            Assert.AreEqual("buffered-probe-owner", json.RootElement.GetProperty("application").GetProperty("name").GetString());
            Assert.AreEqual(string.Empty, error.ToString());
        }
        finally
        {
            try { await StopAndDisposeAsync(server); }
            finally { await service.DisposeAsync().AsTask().WaitAsync(Deadline); }
        }
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

    private static async Task StopAndDisposeAsync(HealthPipeServer server)
    {
        Task stopping;
        try { stopping = server.StopAsync(); }
        catch (Exception error) { stopping = Task.FromException(error); }
        Task disposing;
        try { disposing = server.DisposeAsync().AsTask(); }
        catch (Exception error) { disposing = Task.FromException(error); }
        Task drains = Task.WhenAll(stopping, disposing);
        try { await drains.WaitAsync(Deadline); }
        catch (Exception error) when (error is not TimeoutException && drains.Exception is not null)
        {
            throw drains.Exception;
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
