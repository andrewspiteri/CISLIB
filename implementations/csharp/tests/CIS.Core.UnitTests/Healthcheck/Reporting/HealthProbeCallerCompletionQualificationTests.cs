using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Channels;
using CIS.Healthcheck;
using CIS.Healthcheck.Pipes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

// Two finite receiving-boundary observations, not a promise about every
// context, output implementation or continuation in CIS.Core.
[TestClass]
public sealed partial class HealthProbeCallerCompletionQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task ReleasingTheActualInvalidArgumentWriteCompletesTheCallerOwnedOperation()
    {
        using var output = new MemoryStream();
        using var error = new GatedErrorWriter();
        var context = new QueuedCallerContext();
        Task<int> running = InvokeUnderContext(context, () => HealthProbeClient.RunAsync([], output, error));
        Exception? failure = null;
        try
        {
            // Invalid arguments reach this real writer before any pipe awaits.
            // Its Task is genuinely incomplete, so neither selected await can
            // disappear behind a completed-operation fast path.
            Assert.AreEqual(1, error.Writes);
            Assert.IsFalse(running.IsCompleted);
            Assert.IsFalse(context.FirstPost.IsCompleted);
            error.Release();

            _ = await Task.WhenAny(context.FirstPost, running).WaitAsync(Deadline);
            Assert.IsFalse(context.FirstPost.IsCompleted,
                "The released error write must let this operation finish without requiring the caller to service a queued continuation.");
            Assert.IsTrue(running.IsCompletedSuccessfully);
            Assert.AreEqual(2, await running);
            Assert.AreEqual(0L, output.Length);
            Assert.IsTrue(error.ToString().Length is > 0 and <= 1024);

            // Completion does not transfer or dispose the caller's streams.
            output.WriteByte(0x31);
            int previous = error.GetStringBuilder().Length;
            error.Write('x');
            Assert.AreEqual(previous + 1, error.GetStringBuilder().Length);
        }
        catch (Exception exception) { failure = exception; }

        await FinishPreservingFailureAsync(failure, async () =>
        {
            error.Release();
            // If an exact mutant posted, service its actual callback only now.
            // The direct assertion stays the failure; cleanup can still settle
            // the real operation instead of abandoning it until a test timeout.
            await context.DrainUntilCompletedAsync(running, Deadline);
        });
    }

    [TestMethod]
    public void AnActuallyPendingConnectionCompletesAfterTheRealEndpointServesItsResponse()
    {
        ReviewPendingProbeOwners(RunPendingProbeScenario());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PendingProbeObservation RunPendingProbeScenario()
    {
        var service = new HealthcheckService(new(), new HealthApplicationIdentity
        {
            Name = "caller connection",
            InstanceId = Guid.NewGuid()
        });
        service.MarkStartupCompleted();
        string pipe = "hc-context-" + Guid.NewGuid().ToString("N");
        var cancellation = new CancellationTokenSource(Deadline);
        var output = new MemoryStream();
        var error = new StringWriter();
        var context = new QueuedCallerContext();
        HealthPipeServer? server = null;
        ProbeStartupSource? source = null;
        ProbeStartupClock? clock = null;
        bool endpointStarted = false;
        Task<int>? running = null;
        Exception? failure = null;
        try
        {
            HealthSnapshot before = service.CaptureSnapshot();
            // No server object/endpoint exists for this unique name yet. The
            // synchronous prefix has returned a pending task at the first
            // ConnectAsync await, before any request/response/output awaits.
            running = InvokeUnderContext(context, () => HealthProbeClient.RunAsync(
                ["--pipe", pipe, "--timeout-ms", "60000"], output, error, cancellation.Token));
            Assert.IsFalse(running.IsCompleted, "A missing unique endpoint establishes an actual pending connection.");
            Assert.IsFalse(context.FirstPost.IsCompleted);

            source = new ProbeStartupSource(service);
            clock = new ProbeStartupClock();
            server = CreateServerWhileProbeIsPending(source, new HealthcheckOptions
            {
                PipeEnabled = true,
                PipeName = pipe,
                PipeMaxClients = 1,
                PipeRequestTimeoutMs = 5000
            }, clock, context, running, cancellation);
            endpointStarted = true;
            _ = JoinPendingProbe(Task.WhenAny(context.FirstPost, running));
            Assert.IsFalse(context.FirstPost.IsCompleted,
                "Making the actual endpoint available must permit this pending exchange to complete without servicing a caller continuation queue.");
            Assert.IsTrue(running.IsCompletedSuccessfully);
            Assert.AreEqual(0, JoinPendingProbe(running));
            Assert.AreEqual(string.Empty, error.ToString());
            byte[] written = output.ToArray();
            Assert.IsTrue(written.Length > 1);
            Assert.AreEqual((byte)'\n', written[^1]);
            using (JsonDocument snapshot = JsonDocument.Parse(written))
            {
                Assert.AreEqual("Healthy", snapshot.RootElement.GetProperty("healthStatus").GetString());
                Assert.IsTrue(snapshot.RootElement.GetProperty("ready").GetBoolean());
                Assert.AreEqual("caller connection", snapshot.RootElement.GetProperty("application").GetProperty("name").GetString());
            }
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(before.HealthStatus, after.HealthStatus);
            Assert.AreEqual(before.Ready, after.Ready);
            Assert.AreEqual(before.Application.IsStopping, after.Application.IsStopping);
            output.WriteByte(0x32);
            error.Write('x');
        }
        catch (Exception exception) { failure = exception; }

        return ReleasePendingProbeOwners(server, service, source, clock, context, running, endpointStarted, cancellation, output, error, failure);
    }

    private static Task<int> InvokeUnderContext(QueuedCallerContext context, Func<Task<int>> operation)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try { return operation(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static async Task FinishPreservingFailureAsync(Exception? failure, Func<Task> cleanup)
    {
        try { await cleanup(); }
        catch (Exception cleanupFailure)
        {
            if (failure is not null)
            {
                // An unsettled cleanup is explicitly additional failure, never
                // silently hidden behind an otherwise creditable assertion.
                throw new AggregateException("The assertion and actual owned-operation cleanup both failed.", failure, cleanupFailure);
            }
            throw;
        }
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private sealed class GatedErrorWriter : StringWriter
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int writes;
        internal int Writes => Volatile.Read(ref writes);
        public override Task WriteLineAsync(string? value)
        {
            base.WriteLine(value);
            Interlocked.Increment(ref writes);
            return release.Task;
        }
        internal void Release() => release.TrySetResult();
    }

    private sealed class QueuedCallerContext : SynchronizationContext
    {
        private readonly Channel<(SendOrPostCallback Callback, object? State)> callbacks
            = Channel.CreateUnbounded<(SendOrPostCallback, object?)>();
        private readonly TaskCompletionSource firstPost = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task FirstPost => firstPost.Task;
        internal PendingProbeContextPhase Phase { get; } = new();
        internal bool HasQueuedWork => callbacks.Reader.TryPeek(out _);
        internal WeakReference[] WeakGraph() => [new(this), new(callbacks), new(firstPost)];

        public override SynchronizationContext CreateCopy() => this;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            // A real queue, not a throwing trap. Every callback remains available
            // for cleanup and is invoked with the context it was posted to.
            Interlocked.Increment(ref Phase.PostEntries);
            try
            {
                if (callbacks.Writer.TryWrite((callback, state))) { Interlocked.Increment(ref Phase.Enqueued); }
                firstPost.TrySetResult();
            }
            catch (Exception error) { Phase.RecordFailure(error); throw; }
            finally { Interlocked.Increment(ref Phase.PostReturns); }
        }

        internal async Task DrainUntilCompletedAsync(Task running, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (!running.IsCompleted)
            {
                DrainAvailable();
                if (running.IsCompleted) { break; }
                Task<bool> available = callbacks.Reader.WaitToReadAsync(cancellation.Token).AsTask();
                Task winner = await Task.WhenAny(running, available).WaitAsync(cancellation.Token);
                if (winner == running)
                {
                    cancellation.Cancel();
                    try { _ = await available; }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                    break;
                }
                Assert.IsTrue(await available, "The owned callback queue cannot close before its operation settles.");
            }
            DrainAvailable();
            await running;
        }

        private void DrainAvailable()
        {
            while (callbacks.Reader.TryRead(out var work))
            {
                SynchronizationContext? previous = Current;
                SetSynchronizationContext(this);
                Interlocked.Increment(ref Phase.CallbackEntries);
                try { work.Callback(work.State); }
                catch (Exception error) { Phase.RecordFailure(error); throw; }
                finally { Interlocked.Increment(ref Phase.CallbackReturns); SetSynchronizationContext(previous); }
            }
        }
    }
}
