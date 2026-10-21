using System.IO.Pipes;
using CIS.Healthcheck.Pipes;
using CIS.Healthcheck.Reporting;

namespace CIS.Healthcheck;

/// <summary>Serves explicitly requested detached health reports on a protected local pipe.</summary>
/// <remarks>Construction is inert. The application explicitly starts and stops this server; stopping never mutates application health.</remarks>
public sealed class HealthPipeServer : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IHealthSnapshotSource source;
    private readonly HealthcheckOptions options;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim endpointChanges = new(1, 1);
    private readonly CancellationTokenSource stoppingSource = new();
    private readonly TaskCompletionSource startCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource stopCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Slot[] slots = [];
    private bool started, starting, stopping, disposed;
    private int remaining, closing;

    /// <summary>Copies and validates transport settings without opening an endpoint or starting any work.</summary>
    public HealthPipeServer(IHealthSnapshotSource source, HealthcheckOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        this.source = source;
        this.options = options.ValidatedCopy();
        clock = timeProvider ?? TimeProvider.System;
        if (clock.TimestampFrequency <= 0)
        {
            throw new ArgumentException("The clock must have a positive timestamp frequency.", nameof(timeProvider));
        }
    }

    /// <summary>Opens the configured endpoint once, or does nothing when pipe reporting is disabled.</summary>
    /// <remarks>A pre-canceled call leaves the server unstarted. Repeated running starts are idempotent; stop is terminal.</remarks>
    public Task StartAsync(CancellationToken waitCancellation = default)
    {
        if (waitCancellation.IsCancellationRequested) { return Task.FromCanceled(waitCancellation); }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (stopping) { throw new InvalidOperationException("The Healthcheck pipe server is stopped."); }
            if (started) { return startCompletion.Task.WaitAsync(waitCancellation); }
            started = starting = true;
        }
        StartCore();
        return startCompletion.Task.WaitAsync(waitCancellation);
    }

    /// <summary>Closes admission and awaits actual accepting and connected handlers.</summary>
    /// <remarks>Cancellation affects only the caller's wait. A synchronous snapshot source remains accounted until it actually returns.</remarks>
    public Task StopAsync(CancellationToken waitCancellation = default)
    {
        BeginStop();
        return stopCompletion.Task.WaitAsync(waitCancellation);
    }

    /// <summary>Performs terminal stop and releases server resources after actual handler settlement.</summary>
    public async ValueTask DisposeAsync()
    {
        BeginStop();
        await stopCompletion.Task.ConfigureAwait(false);
        bool release;
        lock (gate) { release = !disposed; disposed = true; }
        if (release) { stoppingSource.Dispose(); endpointChanges.Dispose(); }
    }

    private void StartCore()
    {
        var created = new List<Slot>(options.PipeEnabled ? options.PipeMaxClients : 0);
        bool published = false;
        int queued = 0;
        try
        {
            if (options.PipeEnabled)
            {
                for (int index = 0; index < options.PipeMaxClients; index++)
                {
                    created.Add(new Slot(HealthPipeEndpoint.Create(options.PipeName!, options.PipeMaxClients, first: index == 0)));
                }
            }
            lock (gate)
            {
                if (!stopping)
                {
                    slots = created.ToArray();
                    remaining = slots.Length;
                    published = true;
                }
            }
            if (published)
            {
                // There are exactly PipeMaxClients long-lived async slot loops.
                // No connected client creates another queued Task or capacity waiter.
                foreach (var slot in created)
                {
                    bool accepted = ThreadPool.UnsafeQueueUserWorkItem(static value =>
                    {
                        _ = value.Owner.RunSlotAsync(value.Slot);
                    }, (Owner: this, Slot: slot), preferLocal: false);
                    if (!accepted) { throw new InvalidOperationException("The Healthcheck pipe worker could not be queued."); }
                    queued++;
                }
            }
            startCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            startCompletion.TrySetException(exception);
            BeginStop();
        }
        finally
        {
            if (!published)
            {
                foreach (var slot in created) { slot.Stream!.Dispose(); }
            }
            else if (queued != created.Count)
            {
                for (int index = queued; index < created.Count; index++) { created[index].Stream!.Dispose(); }
                lock (gate) { remaining -= created.Count - queued; }
            }
            lock (gate) { starting = false; }
            SignalProgress();
        }
    }

    private async Task RunSlotAsync(Slot slot)
    {
        try
        {
            while (!stoppingSource.IsCancellationRequested)
            {
                NamedPipeServerStream stream = slot.Stream!;
                try
                {
                    await stream.WaitForConnectionAsync(stoppingSource.Token).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException)
                {
                    // Unix CurrentUserOnly rejects a peer after accepting its socket.
                    // That rejected socket is closed by the runtime; this slot remains available.
                    continue;
                }
                catch (IOException) when (!stoppingSource.IsCancellationRequested)
                {
                    // A peer may connect and close before accept is observed. In
                    // particular Windows reports ERROR_NO_DATA for this race.
                    // Recreate only this slot; ordinary peer churn cannot stop
                    // unrelated accepting/connected clients.
                    if (!await RecoverSlotAsync(slot).ConfigureAwait(false)) { break; }
                    continue;
                }
                stoppingSource.Token.ThrowIfCancellationRequested();
                try
                {
                    using var budget = new HealthPipeBudget(clock, options.PipeRequestTimeoutMs, stoppingSource.Token);
                    await HandleClientAsync(stream, budget).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or FormatException or OperationCanceledException or ObjectDisposedException)
                {
                    // The one connection failed. It cannot change health or manufacture a successful report.
                }
                if (stoppingSource.IsCancellationRequested) { break; }
                try { stream.Disconnect(); }
                catch (IOException) when (!stoppingSource.IsCancellationRequested)
                {
                    if (!await RecoverSlotAsync(slot).ConfigureAwait(false)) { break; }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Stop disposes both accepting and connected streams, which completes
            // their actual I/O before this slot can leave the drain accounting.
            if (!stoppingSource.IsCancellationRequested) { BeginStop(); }
        }
        catch (Exception)
        {
            // A failed local endpoint/clock closes all admission instead of silently
            // leaving a server running with less capacity than its configured bound.
            BeginStop();
        }
        finally
        {
            NamedPipeServerStream? close;
            lock (gate) { close = slot.Stream; slot.Stream = null; }
            close?.Dispose();
            lock (gate) { remaining--; }
            SignalProgress();
        }
    }

    private async Task<bool> RecoverSlotAsync(Slot slot)
    {
        // At most the fixed slot workers can wait here. Serializing removal and
        // creation lets the last stream reclaim its name with first-instance
        // protection, and never creates more than PipeMaxClients OS instances.
        await endpointChanges.WaitAsync(stoppingSource.Token).ConfigureAwait(false);
        try
        {
            NamedPipeServerStream? previous;
            lock (gate) { previous = slot.Stream; slot.Stream = null; }
            previous?.Dispose();
            stoppingSource.Token.ThrowIfCancellationRequested();
            bool first;
            lock (gate) { first = !slots.Any(item => item.Stream is not null); }
            // Creation/permission failures deliberately escape and terminally
            // close admission. They are infrastructure errors, not peer churn.
            var replacement = HealthPipeEndpoint.Create(options.PipeName!, options.PipeMaxClients, first);
            bool publish;
            lock (gate)
            {
                publish = !stopping;
                if (publish) { slot.Stream = replacement; }
            }
            if (!publish) { replacement.Dispose(); }
            return publish;
        }
        finally { endpointChanges.Release(); }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, HealthPipeBudget budget)
    {
        HealthReportBuffer response;
        HealthReportView? selected = null;
        try
        {
            byte[] request = await HealthReportProtocol.ReadFrameAsync(stream, 512, budget.Token).ConfigureAwait(false);
            budget.ThrowIfExpired();
            if (!HealthReportProtocol.TryParseRequest(request, out var view, out string? errorCode))
            {
                response = HealthReportFormatter.FormatError(errorCode!, pipe: true);
            }
            else
            {
                selected = view;
                if (view == HealthReportView.Details && !options.DetailsEnabled)
                {
                    response = HealthReportFormatter.FormatError("DetailsDisabled", pipe: true, view);
                }
                else
                {
                    try
                    {
                        var snapshot = source.CaptureSnapshot();
                        budget.ThrowIfExpired();
                        response = HealthReportFormatter.FormatPipeResponse(snapshot, view, options);
                    }
                    catch (OperationCanceledException) when (budget.Token.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        response = HealthReportFormatter.FormatError("ReportingUnavailable", pipe: true, view);
                    }
                }
            }
        }
        catch (FormatException)
        {
            response = HealthReportFormatter.FormatError("InvalidFrame", pipe: true, selected);
        }
        budget.ThrowIfExpired();
        await HealthReportProtocol.WriteFrameAsync(stream, response.Utf8Bytes, options.MaxReportBytes, budget.Token).ConfigureAwait(false);
        budget.ThrowIfExpired();
        // DisconnectNamedPipe discards unread response bytes on Windows. Keep
        // this actual slot until the matched client closes after its complete
        // read, sends unexpected extra input, or exhausts the same deadline.
        // This avoids an uncancelable synchronous WaitForPipeDrain call.
        await stream.ReadAsync(new byte[1], budget.Token).ConfigureAwait(false);
    }

    private void BeginStop()
    {
        NamedPipeServerStream[] close;
        lock (gate)
        {
            if (stopping) { return; }
            stopping = true;
            closing++;
            close = slots.Select(item => item.Stream).OfType<NamedPipeServerStream>().ToArray();
        }
        try
        {
            stoppingSource.Cancel();
            foreach (var stream in close) { stream.Dispose(); }
        }
        finally
        {
            lock (gate) { closing--; }
            SignalProgress();
        }
    }

    private void SignalProgress()
    {
        bool settled;
        lock (gate) { settled = stopping && !starting && remaining == 0 && closing == 0; }
        if (settled) { stopCompletion.TrySetResult(); }
    }

    private sealed class Slot(NamedPipeServerStream stream)
    {
        internal NamedPipeServerStream? Stream { get; set; } = stream;
    }
}
