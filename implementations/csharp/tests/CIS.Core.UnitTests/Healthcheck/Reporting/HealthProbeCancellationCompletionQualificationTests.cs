using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using CIS.Healthcheck.Pipes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

// This one receiving-boundary witness uses an actual pre-canceled caller token
// and genuinely pending error I/O. It establishes no universal context policy.
[TestClass]
public sealed class HealthProbeCancellationCompletionQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task ReleasingTheActualCanceledCallErrorWriteCompletesTheCallerOwnedOperation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new MemoryStream();
        using var error = new GatedErrorWriter();
        var context = new QueuedCallerContext();
        string pipe = "hc-canceled-context-" + Guid.NewGuid().ToString("N");
        Task<int> running = InvokeUnderContext(context, () => HealthProbeClient.RunAsync(
            ["--pipe", pipe, "--timeout-ms", "60000"], output, error, cancellation.Token));
        Exception? failure = null;
        try
        {
            // The valid settings reach ConnectAsync with an already-canceled
            // linked token. Its completed canceled Task throws synchronously at
            // the await, so this real error write begins in the caller context.
            Assert.AreEqual(1, error.Writes);
            Assert.AreEqual("cis-health-probe: TimeoutOrCanceled" + Environment.NewLine, error.ToString());
            Assert.IsFalse(running.IsCompleted);
            Assert.IsFalse(context.FirstPost.IsCompleted);
            error.Release();

            _ = await Task.WhenAny(context.FirstPost, running).WaitAsync(Deadline);
            Assert.IsFalse(context.FirstPost.IsCompleted,
                "Releasing the actual canceled-call error write must permit this operation to finish without servicing a caller continuation queue.");
            Assert.IsTrue(running.IsCompletedSuccessfully);
            Assert.AreEqual(2, await running);
            Assert.AreEqual(0L, output.Length);

            // The caller retains both streams after the canceled operation.
            output.WriteByte(0x33);
            int previous = error.GetStringBuilder().Length;
            error.Write('x');
            Assert.AreEqual(previous + 1, error.GetStringBuilder().Length);
        }
        catch (Exception exception) { failure = exception; }

        await FinishPreservingFailureAsync(failure, async () =>
        {
            error.Release();
            // A selected continuation-capture mutant is asserted first; then
            // its real queued callback is serviced and its operation joined.
            await context.DrainUntilCompletedAsync(running, Deadline);
        });
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

        public override SynchronizationContext CreateCopy() => this;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            callbacks.Writer.TryWrite((callback, state));
            firstPost.TrySetResult();
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
                try { work.Callback(work.State); }
                finally { SetSynchronizationContext(previous); }
            }
        }
    }
}
