using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportFrameCallerCompletionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static readonly byte[] Payload = [0x11, 0x22, 0x33];
    private static readonly byte[] Frame = [3, 0, 0, 0, 0x11, 0x22, 0x33];

    [TestMethod]
    [DataRow(true, 1)]
    [DataRow(true, 2)]
    [DataRow(false, 1)]
    [DataRow(false, 2)]
    public async Task ReleasingAnActualFrameOperationCompletesWithoutPumpingTheCallerQueue(bool reading, int pendingCall)
    {
        using var stream = new GatedFrameStream(reading, pendingCall);
        var context = new QueuedCallerContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        Task operation;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            operation = reading
                ? HealthReportProtocol.ReadFrameAsync(stream, 512, CancellationToken.None)
                : HealthReportProtocol.WriteFrameAsync(stream, Payload, 512, CancellationToken.None);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        Exception? failure = null;
        try
        {
            Assert.AreEqual(pendingCall, stream.Calls);
            Assert.IsTrue(stream.Entered.IsCompletedSuccessfully);
            Assert.IsFalse(operation.IsCompleted);
            Assert.IsFalse(context.FirstPost.IsCompleted);
            stream.Release();

            _ = await Task.WhenAny(context.FirstPost, operation).WaitAsync(Deadline);
            Assert.IsFalse(context.FirstPost.IsCompleted,
                "Completing actual frame I/O must permit this operation to finish without servicing the caller continuation queue.");
            Assert.IsTrue(operation.IsCompletedSuccessfully);
            await operation;
            Assert.AreEqual(2, stream.Calls);
            if (reading) { CollectionAssert.AreEqual(Payload, await (Task<byte[]>)operation); }
            else { CollectionAssert.AreEqual(Frame, stream.Written); }
            Assert.IsTrue(stream.CanRead && stream.CanWrite, "The caller retains its stream.");
        }
        catch (Exception exception) { failure = exception; }

        try
        {
            stream.Release();
            // A captured continuation is asserted before cleanup services its
            // real callback. Cleanup joins the owned operation in either case.
            await context.DrainUntilCompletedAsync(operation);
        }
        catch (Exception cleanupFailure)
        {
            if (failure is not null)
            {
                throw new AggregateException("The assertion and owned frame cleanup both failed.", failure, cleanupFailure);
            }
            throw;
        }
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private sealed class GatedFrameStream(bool reading, int pendingCall) : Stream
    {
        private readonly MemoryStream input = new(Frame);
        private readonly MemoryStream output = new();
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        internal int Calls => Volatile.Read(ref calls);
        internal Task Entered => entered.Task;
        internal byte[] Written => output.ToArray();
        internal void Release() => release.TrySetResult();
        public override bool CanRead => input.CanRead;
        public override bool CanWrite => output.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Assert.IsTrue(reading);
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref calls) != pendingCall) { return ValueTask.FromResult(input.Read(buffer.Span)); }
            entered.TrySetResult();
            return new ValueTask<int>(CompleteReadAsync(buffer, cancellationToken));
        }

        private async Task<int> CompleteReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return input.Read(buffer.Span);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Assert.IsFalse(reading);
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref calls) != pendingCall)
            {
                output.Write(buffer.Span);
                return ValueTask.CompletedTask;
            }
            entered.TrySetResult();
            return new ValueTask(CompleteWriteAsync(buffer, cancellationToken));
        }

        private async Task CompleteWriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            output.Write(buffer.Span);
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { input.Dispose(); output.Dispose(); }
            base.Dispose(disposing);
        }
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

        internal async Task DrainUntilCompletedAsync(Task operation)
        {
            using var cancellation = new CancellationTokenSource(Deadline);
            while (!operation.IsCompleted)
            {
                DrainAvailable();
                if (operation.IsCompleted) { break; }
                Task<bool> available = callbacks.Reader.WaitToReadAsync(cancellation.Token).AsTask();
                if (await Task.WhenAny(operation, available).WaitAsync(cancellation.Token) == operation)
                {
                    cancellation.Cancel();
                    try { _ = await available; }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                    break;
                }
                Assert.IsTrue(await available);
            }
            DrainAvailable();
            await operation;
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
