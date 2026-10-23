using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Text;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthPipeCanceledResponseIsolationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARealDeadlineCallbackDuringCaptureClosesOnlyThatConnection(bool sourceThrowsAfterCancellation)
    {
        var service = new HealthcheckService(new(), new HealthApplicationIdentity
        {
            Name = "canceled response isolation",
            InstanceId = Guid.NewGuid()
        });
        service.MarkStartupCompleted();
        var clock = new CallbackControlledClock();
        var source = new CancelingSource(service, clock, sourceThrowsAfterCancellation);
        var options = new HealthcheckOptions
        {
            PipeEnabled = true,
            PipeName = "hc-cr-" + Guid.NewGuid().ToString("N"),
            PipeMaxClients = 1,
            PipeRequestTimeoutMs = 1000,
            MaxReportBytes = 4096
        };
        var server = new HealthPipeServer(source, options, clock);
        using var cancellation = new CancellationTokenSource(Deadline);
        Exception? failure = null;
        try
        {
            await server.StartAsync().WaitAsync(Deadline);
            HealthSnapshot before = service.CaptureSnapshot();
            using (var client = CreateClient(options.PipeName!))
            {
                await client.ConnectAsync(cancellation.Token);
                await SendRequestAsync(client, cancellation.Token);
                try
                {
                    int received = await client.ReadAsync(new byte[1], cancellation.Token);
                    Assert.AreEqual(0, received, "A canceled capture cannot advertise a report prefix.");
                }
                catch (IOException) { /* Actual peer closure can be reported as a broken pipe. */ }
            }
            Assert.AreEqual(1, source.Captures);
            Assert.AreEqual(1, clock.CallbacksFired);

            // Actual EOF follows the canceled handler. A connection deadline
            // must leave the public server running, including idempotent Start.
            // This checks admission, without requiring synchronous completion.
            Task? repeatedStart = null;
            InvalidOperationException? stopped = null;
            try { repeatedStart = server.StartAsync(); }
            catch (InvalidOperationException error) { stopped = error; }
            Assert.IsNull(stopped, "Canceling one response must not terminally stop the server.");
            await (repeatedStart ?? throw new InvalidOperationException("No start wait was returned.")).WaitAsync(Deadline);

            using (var client = CreateClient(options.PipeName!))
            {
                await client.ConnectAsync(cancellation.Token);
                await SendRequestAsync(client, cancellation.Token);
                byte[] response = await HealthReportProtocol.ReadFrameAsync(client, options.MaxReportBytes, cancellation.Token);
                HealthParsedResponse parsed = HealthReportProtocol.ParseResponse(response, HealthReportView.Health, options.MaxReportBytes);
                Assert.IsFalse(parsed.IsError);
                Assert.IsTrue(parsed.SelectedHealthy);
                Assert.IsFalse(parsed.SnapshotUtf8.IsEmpty);
            }
            Assert.AreEqual(2, source.Captures);
            Assert.AreEqual(1, clock.CallbacksFired);
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);
            Assert.AreEqual(before.Application.IsStopping, after.Application.IsStopping);
        }
        catch (Exception error) { failure = error; }

        try
        {
            // Start both real terminal paths before joining either. A broken
            // transition cannot strand cleanup behind its own returned wait.
            Task stop = InvokeCleanup(() => server.StopAsync());
            Task dispose = InvokeCleanup(() => server.DisposeAsync().AsTask());
            try { await Task.WhenAll(stop, dispose).WaitAsync(Deadline); }
            finally { await service.DisposeAsync().AsTask().WaitAsync(Deadline); }
        }
        catch (Exception cleanupFailure)
        {
            if (failure is not null)
            {
                throw new AggregateException("The response isolation assertion and actual transport cleanup both failed.", failure, cleanupFailure);
            }
            throw;
        }
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private static NamedPipeClientStream CreateClient(string pipe) =>
        new(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static Task SendRequestAsync(Stream client, CancellationToken cancellation) =>
        HealthReportProtocol.WriteFrameAsync(client, Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), 512, cancellation);

    private static Task InvokeCleanup(Func<Task> cleanup)
    {
        try { return cleanup(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private sealed class CancelingSource(HealthcheckService service, CallbackControlledClock clock, bool throwsAfterCancellation) : IHealthSnapshotSource
    {
        private int captures;
        internal int Captures => Volatile.Read(ref captures);

        public HealthSnapshot CaptureSnapshot()
        {
            if (Interlocked.Increment(ref captures) == 1)
            {
                // Deliver the callback supplied by CancellationTokenSource
                // through the public TimeProvider timer, at its actual due time.
                clock.FireCurrentDeadline(1000);
                if (throwsAfterCancellation) { throw new OperationCanceledException("The source observed cancellation."); }
            }
            return service.CaptureSnapshot();
        }
    }

    private sealed class CallbackControlledClock : TimeProvider
    {
        private readonly object gate = new();
        private ControlledTimer? current;
        private long timestamp;
        private int callbacksFired;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Volatile.Read(ref timestamp);
        internal int CallbacksFired => Volatile.Read(ref callbacksFired);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledTimer(callback, state);
            lock (gate) { current = timer; }
            return timer;
        }

        internal void FireCurrentDeadline(long milliseconds)
        {
            ControlledTimer timer;
            lock (gate) { timer = current ?? throw new InvalidOperationException("No actual deadline timer exists."); }
            Interlocked.Add(ref timestamp, milliseconds);
            timer.Fire();
            Interlocked.Increment(ref callbacksFired);
        }

        private sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
        {
            private int disposed;
            private int fired;
            internal void Fire()
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                if (Interlocked.Exchange(ref fired, 1) != 0) { throw new InvalidOperationException("The deadline callback already fired."); }
                callback(state);
            }
            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref disposed) == 0;
            public void Dispose() => Interlocked.Exchange(ref disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
