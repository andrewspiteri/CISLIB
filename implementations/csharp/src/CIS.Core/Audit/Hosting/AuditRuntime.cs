using CIS.Coordination.Hosting;
using CIS.Messaging;

namespace CIS.Audit.Hosting;

// One known-module barrier: jobs retain final Audit access, then actual Audit work drains before dependencies.
internal sealed class AuditRuntime : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly AuditCaptureEngine capture;
    private readonly AuditWriter writer;
    private readonly MessagingRuntime messaging;
    private readonly CoordinationHostLifetime? coordination;
    private readonly CancellationTokenSource dependencyStopping = new();
    private readonly CancellationToken dependencyToken;
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? stop;
    private bool started;
    private int disposed;

    internal AuditRuntime(AuditCaptureEngine capture, AuditWriter writer, MessagingRuntime messaging,
        CoordinationHostLifetime? coordination)
    {
        this.capture = capture;
        this.writer = writer;
        this.messaging = messaging;
        this.coordination = coordination;
        dependencyToken = dependencyStopping.Token;
    }

    internal CancellationToken DependencyStopping => dependencyToken;
    internal bool IsDrained => drained.Task.IsCompleted;

    internal void PreparePublishing()
    {
        lock (gate)
        {
            if (stop is not null)
            {
                throw new InvalidOperationException("Audit cannot open publication after stopping begins.");
            }
            // Consumer authentication can run during Messaging's sequential native Start.
            // Its existing outbox owns publication; no Audit worker or I/O starts here.
            writer.BeginAccepting();
        }
    }

    internal Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            if (stop is not null)
            {
                throw new InvalidOperationException("Audit cannot start after admission closes.");
            }
            if (!messaging.IsAccepting)
            {
                throw new InvalidOperationException("The selected Messaging runtime no longer accepts work.");
            }
            if (!started)
            {
                writer.BeginAccepting();
                started = true;
            }
        }
        return Task.CompletedTask;
    }

    internal void BeginStopping() => _ = StopAsync();

    internal Task StopAsync()
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (stop is not null)
            {
                return stop;
            }
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            stop = completion.Task;
        }
        _ = CompleteStopAsync(completion);
        return completion.Task;
    }

    private async Task CompleteStopAsync(TaskCompletionSource completion)
    {
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception failure)
        {
            completion.TrySetException(failure);
        }
    }

    private async Task StopCoreAsync()
    {
        Exception? failure = await CoordinationHostLifetime.CaptureStopAsync(coordination).ConfigureAwait(false);
        capture.BeginClose();
        writer.BeginClose();
        try
        {
            await Task.WhenAll(capture.DrainAsync(), writer.DrainAsync()).ConfigureAwait(false);
        }
        finally
        {
            drained.TrySetResult();
            dependencyStopping.Cancel();
        }
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal static async Task<Exception?> CaptureStopAsync(AuditRuntime? audit, CoordinationHostLifetime? coordination)
    {
        if (audit is null)
        {
            return await CoordinationHostLifetime.CaptureStopAsync(coordination).ConfigureAwait(false);
        }
        try
        {
            await audit.StopAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                dependencyStopping.Dispose();
            }
        }
    }
}
