using CIS.Logging.Events;
using CIS.Logging.Rendering;
using CIS.Logging.Runtime;
using CIS.Logging.Scopes;
using CIS.Text;

namespace CIS.Logging;
/// <summary>Owns logger context, independent output admission and one shared bounded stop operation.</summary>
public sealed partial class CoreLogRuntime : IAsyncDisposable
{
    private readonly object admissionGate;
    private readonly OutputQueues? queuedOutputs;
    private readonly bool recoveredPartialSegment;
    private readonly EventMetadata metadata;
    private readonly TimeProvider timeProvider;
    private readonly long timestampFrequency;
    private readonly int maximumEntryBytes;
    private readonly TimeSpan shutdownTimeout;
    private Task<LogStopResult>? stopTask;
    private LogStopResult? lastStopResult;
    private readonly Output[] outputs;
    private bool admissionClosed;
    private DateTimeOffset? shutdownStartedAtUtc;
    private long invalidEventCount;
    private long truncatedEventCount;
    // The direct internal admission seam remains available for existing focused qualification.
    internal CoreLogRuntime(EventMetadata metadata, LoggingOptions options, ILogOutputAdmission? console, ILogOutputAdmission? file, TimeProvider? timeProvider = null, object? sharedAdmissionGate = null, OutputQueues? queuedOutputs = null, IReadOnlyList<OutputWriterBinding>? writers = null, bool recoveredPartialSegment = false)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        CoreLogLevel consoleLevel = options.EffectiveConsoleLevel;
        CoreLogLevel fileLevel = options.EffectiveFileLevel;
        bool consoleEnabled = consoleLevel != CoreLogLevel.None;
        bool fileEnabled = !string.IsNullOrWhiteSpace(options.FilePath) && fileLevel != CoreLogLevel.None;
        int maximumBytes = options.MaxEntryBytes;
        ValidateSettings(consoleLevel, fileLevel, maximumBytes);
        ValidateShutdownTimeout(options.ShutdownTimeoutMilliseconds);
        admissionGate = sharedAdmissionGate ?? new object();
        this.queuedOutputs = queuedOutputs;
        this.recoveredPartialSegment = recoveredPartialSegment;
        this.metadata = metadata;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        timestampFrequency = this.timeProvider.TimestampFrequency;
        maximumEntryBytes = maximumBytes;
        shutdownTimeout = options.ShutdownTimeout;
        var configured = new List<Output>(2);
        if (writers is null)
        {
            Add("console", consoleEnabled, consoleLevel, console);
            Add("file", fileEnabled, fileLevel, file);
        }
        else
        {
            foreach (var registration in writers)
            { configured.Add(new(registration.Name, registration.Level, queuedOutputs!.Get(registration.Name), registration.Prepare)); }
        }
        outputs = configured.ToArray();
        void Add(string name, bool enabled, CoreLogLevel threshold, ILogOutputAdmission? admission)
        {
            if (enabled != (admission is not null))
            {
                throw new ArgumentException("Output composition must match enabled settings.", name);
            }

            if (admission is not null)
            {
                configured.Add(new Output(name, threshold, admission));
            }
        }
    }

    /// <summary>Creates an application-owned runtime using the prepared static environment settings and automatically captured facts.</summary>
    /// <remarks>Configure EnvironmentOptions.Logging before creation; preparation normalizes its FilePath in place. Concurrent mutation of those settings is unsupported. Each runtime owns only its own workers and file group. DisposeAsync joins its bounded shutdown.</remarks>
    /// <exception cref = "CIS.Configuration.CoreConfigurationException">Module settings or captured metadata are invalid.</exception>
    /// <exception cref = "TypeInitializationException">Static environment initialization failed.</exception>
    /// <exception cref = "CoreLogInitializationException">An enabled output cannot initialize.</exception>
    public static CoreLogRuntime Create()
    {
        LoggingOptions options = EnvironmentOptions.Logging;
        options.Prepare(Directory.GetCurrentDirectory());
        return QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), options, incomingPipeline: true);
    }

    /// <summary>Creates a runtime with additional writers whose registrations are captured at startup.</summary>
    /// <remarks>Built-in destinations retain their configured behavior. Each custom writer has an independent
    /// queue using the captured QueueCapacity. Registration changes after startup do not affect this runtime.</remarks>
    public static CoreLogRuntime Create(IEnumerable<LogWriterRegistration> writers)
    {
        ArgumentNullException.ThrowIfNull(writers);
        LoggingOptions options = EnvironmentOptions.Logging;
        options.Prepare(Directory.GetCurrentDirectory());
        return QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), options, writers: writers, incomingPipeline: true);
    }

    /// <summary>Creates a runtime with startup-captured processing policies and optional application writers.</summary>
    public static CoreLogRuntime Create(LogProcessingOptions processing, IEnumerable<LogWriterRegistration>? writers = null)
    {
        ArgumentNullException.ThrowIfNull(processing);
        LoggingOptions options = EnvironmentOptions.Logging;
        options.Prepare(Directory.GetCurrentDirectory());
        return QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), options, writers: writers, incomingPipeline: true, processing: processing);
    }

    /// <summary>Settles after incoming processing, writer operations and owned cleanup finish, including late cleanup after stop timeout.</summary>
    /// <remarks>Unlike StopAsync this task has no timeout and may fault when a writer or cleanup fails.</remarks>
    public Task Completion
    {
        get { lock (admissionGate) { return pipelineCompletion ??= incoming is null ? queuedOutputs?.Completion ?? Task.CompletedTask : Task.WhenAll(incoming.Completion, queuedOutputs!.Completion); } }
    }

    // Output-stage qualification seams deliberately bypass incoming admission. Public creation uses both stages.
    internal static CoreLogRuntime CreateQueued(EventMetadata metadata, LoggingOptions options, Func<AdmittedEvent, ValueTask>? consoleWrite, Func<AdmittedEvent, ValueTask>? fileWrite, TimeProvider? timeProvider = null, Func<ValueTask>? consoleFlush = null, Func<ValueTask>? fileFlush = null) => QueuedRuntimeFactory.CreateQueued(metadata, options, consoleWrite, fileWrite, timeProvider, consoleFlush, fileFlush);
    internal static CoreLogRuntime CreateConsoleQueued(EventMetadata metadata, LoggingOptions options, Func<AdmittedEvent, ValueTask>? fileWrite = null, TimeProvider? timeProvider = null, Func<ConsoleOutputStream, Stream>? acquireConsole = null, Func<Func<Task>, Task>? launchWorker = null) => QueuedRuntimeFactory.CreateConsoleQueued(metadata, options, fileWrite, timeProvider, acquireConsole, launchWorker);
    internal static CoreLogRuntime CreateConfiguredQueued(EventMetadata metadata, LoggingOptions options, TimeProvider? timeProvider = null, Func<ConsoleOutputStream, Stream>? acquireConsole = null, Func<Func<Task>, Task>? launchWorker = null) => QueuedRuntimeFactory.CreateConfiguredQueued(metadata, options, timeProvider, acquireConsole, launchWorker);
    internal OutputQueues? QueuedOutputs => queuedOutputs;

    internal static void ValidateShutdownTimeout(int milliseconds)
    {
        if (milliseconds is < LoggingOptions.MinimumShutdownTimeoutMilliseconds or > LoggingOptions.MaximumShutdownTimeoutMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "Use the configured shutdown-timeout range.");
        }
    }

    /// <summary>Starts or joins one shared bounded stop operation through its original accepted boundaries.</summary>
    /// <param name = "cancellationToken">Cancels only this caller's wait; even an already-cancelled first caller starts shutdown.</param>
    /// <returns>The same owned immutable observation retained for all joining callers.</returns>
    /// <exception cref = "OperationCanceledException">The caller cancels its own wait.</exception>
    /// <remarks>The configured overall timeout is frozen at creation. UTC is an observation label; elapsed time is monotonic. At expiry, unstarted accepted records are abandoned. Cancellation of active writes/flushes is requested but is not guaranteed, and no new flush, retry or replacement writer starts. Completed means terminal dispositions, not successful delivery or resource release. A timed-out observation can retain pending I/O; its later completion does not change the cached result.</remarks>
    public Task<LogStopResult> StopAsync(CancellationToken cancellationToken)
    {
        if (EventProcessing.IsCallbackActive) { throw new InvalidOperationException("Processing callbacks cannot stop a logging runtime."); }
        if (incoming is not null) { return StopIncomingAsync(cancellationToken); }
        if (queuedOutputs is null)
        {
            throw new InvalidOperationException("Stop requires actual queued output composition.");
        }

        Task<LogStopResult>? shared;
        lock (admissionGate)
        {
            shared = stopTask;
        }

        if (shared is null)
        {
            // Caller clock code is never invoked under admission synchronization.
            // Preliminary concurrent samples are harmless; only the winning caller owns the deadline.
            long started = timeProvider.GetTimestamp();
            RuntimeFlushBoundary boundary = default;
            TaskCompletionSource<LogStopResult>? completion = null;
            lock (admissionGate)
            {
                if (stopTask is null)
                {
                    completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    stopTask = completion.Task;
                    try
                    {
                        // Register final controls before a closing idle worker is allowed to exit.
                        boundary = RegisterFlushBoundary(started);
                        shutdownStartedAtUtc ??= boundary.BoundaryCapturedAtUtc;
                        admissionClosed = true;
                        queuedOutputs.SetStopBudget(new ElapsedBudget(started, shutdownTimeout, timestampFrequency));
                        queuedOutputs.Complete();
                    }
                    catch (Exception error)
                    {
                        // A failed setup must not leave the already-published task pending forever.
                        completion.TrySetException(error);
                        throw;
                    }
                }

                shared = stopTask;
            }

            if (completion is not null)
            {
                // Start deadline acquisition directly, without a delayed ThreadPool launch.
                // Only continuations/timers outlive this call; they do not retain the caller's flow.
                if (ExecutionContext.IsFlowSuppressed())
                {
                    _ = RunStopAsync(boundary, started, completion);
                }
                else
                {
                    using (ExecutionContext.SuppressFlow())
                    {
                        _ = RunStopAsync(boundary, started, completion);
                    }
                }
            }
        }

        // WaitAsync can prefer an already-completed task; an already-cancelled caller must still cancel.
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<LogStopResult>(cancellationToken);
        }

        return cancellationToken.CanBeCanceled ? shared!.WaitAsync(cancellationToken) : shared!;
    }

    /// <summary>Starts or joins the original bounded stop operation without a new drain window.</summary>
    /// <returns>A wait on the same shared operation; unsuccessful draining remains visible in LastStopResult.</returns>
    /// <remarks>Repeated disposal is harmless. At the original deadline this wait may complete while actual I/O and owned cleanup remain pending; no resource is disposed beneath active I/O. Inspect GetDiagnostics().LastStopResult for the immutable drain outcome.</remarks>
    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));
    private async Task RunStopAsync(RuntimeFlushBoundary boundary, long started, TaskCompletionSource<LogStopResult> completion)
    {
        try
        {
            if (!boundary.Entries.Any())
            {
                PublishStop(CaptureFlushResult(boundary), completion);
                return;
            }

            await using var deadline = new FlushDeadline(timeProvider, started, shutdownTimeout, () => CaptureFlushResult(boundary, new ElapsedBudget(started, shutdownTimeout, timestampFrequency)), () => CaptureStopDeadline(boundary, new ElapsedBudget(started, shutdownTimeout, timestampFrequency)));
            try
            {
                await boundary.WaitForWriterFlushAsync(deadline.Token).ConfigureAwait(false);
                foreach (var entry in boundary.Entries)
                { await queuedOutputs!.Get(entry.Name).WaitForCoveredRecordsAsync(entry.Name, entry.Boundary, deadline.Token).ConfigureAwait(false); }

                try
                {
                    await queuedOutputs!.Completion.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (Exception) when ((queuedOutputs!.Completion.IsFaulted || queuedOutputs.Completion.IsCanceled) && queuedOutputs.Entries.Any(entry => entry.Queue.IsFaulted))
                { /* Terminal writer faults remain safe output results, not business-call I/O exceptions. */
                }
            }
            catch (OperationCanceledException) when (deadline.Token.IsCancellationRequested)
            {
            }

            // Publish the frozen result before asynchronous deadline-timer cleanup can wait on a callback join.
            PublishStop(deadline.ObserveCompletion(), completion);
        }
        catch (Exception error)
        {
            if (completion.Task.IsCompletedSuccessfully)
            {
                // Publication precedes deadline-timer cleanup; later failure belongs to live diagnostics.
                queuedOutputs!.RecordCleanupFault("ShutdownTimerCleanupFailed");
            }
            else
            {
                completion.TrySetException(error);
            }
        }
    }

    private LogFlushResult CaptureStopDeadline(RuntimeFlushBoundary boundary, ElapsedBudget budget)
    {
        long observedAt = timeProvider.GetTimestamp();
        LogFlushResult observation;
        lock (admissionGate)
        {
            queuedOutputs!.AbandonUnstarted(observedAt);
            observation = CaptureFlushResult(boundary, budget);
        }

        // Freeze actual pending work before cooperative cancellation can settle it.
        queuedOutputs.RequestIoCancellation();
        return observation;
    }

    private void PublishStop(LogFlushResult observation, TaskCompletionSource<LogStopResult> completion)
    {
        var result = new LogStopResult(observation);
        lock (admissionGate)
        {
            lastStopResult = result;
        }

        completion.TrySetResult(result);
    }

    /// <summary>Waits through fixed accepted boundaries and confirms each enabled writer's buffer flush.</summary>
    /// <param name = "timeout">One positive finite overall wait budget; every positive TimeSpan is supported.</param>
    /// <param name = "cancellationToken">Cancels only this caller's wait, preserving shared worker requests.</param>
    /// <returns>An owned immutable observation, including unresolved work at the deadline.</returns>
    /// <exception cref = "ArgumentOutOfRangeException">The timeout is zero, negative or infinite.</exception>
    /// <exception cref = "OperationCanceledException">The caller token is cancelled, including after boundary registration.</exception>
    /// <remarks>UTC labels are system observations; elapsed deadlines use the runtime TimeProvider. Flush does not stop admission or guarantee disk durability. After closure it cannot reopen a writer; a terminal writer returns an unavailable/failed flush even if the original stop flushed successfully. Inspect LastStopResult for that original shutdown outcome.</remarks>
    public Task<LogFlushResult> FlushAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (EventProcessing.IsCallbackActive) { throw new InvalidOperationException("Processing callbacks cannot flush a logging runtime."); }
        if (timeout.Ticks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        long started = timeProvider.GetTimestamp();
        if (incoming is not null) { return FlushIncomingAsync(timeout, started, cancellationToken); }
        RuntimeFlushBoundary boundary = RegisterFlushBoundary(started);
        return ObserveFlushAsync(boundary, started, timeout, cancellationToken);
    }

    private async Task<LogFlushResult> ObserveFlushAsync(RuntimeFlushBoundary boundary, long started, TimeSpan timeout, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        if (!boundary.Entries.Any())
        {
            return CaptureFlushResult(boundary);
        }

        await using var deadline = new FlushDeadline(timeProvider, started, timeout, () => CaptureFlushResult(boundary, new ElapsedBudget(started, timeout, timestampFrequency)));
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
        try
        {
            await boundary.WaitForWriterFlushAsync(wait.Token).ConfigureAwait(false);
            // A terminal unavailable flush can precede the disposition of an already-started write.
            foreach (var entry in boundary.Entries)
            { await queuedOutputs!.Get(entry.Name).WaitForCoveredRecordsAsync(entry.Name, entry.Boundary, wait.Token).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            caller.ThrowIfCancellationRequested();
        }

        caller.ThrowIfCancellationRequested();
        return deadline.ObserveCompletion();
    }

    private LogFlushResult CaptureFlushResult(RuntimeFlushBoundary boundary, ElapsedBudget? budget = null)
    {
        lock (admissionGate)
        {
            var observations = boundary.Entries.Select(entry => queuedOutputs!.Get(entry.Name).CaptureDrain(entry.Name, entry.Boundary)).ToArray();
            return new LogFlushResult(boundary.BoundaryCapturedAtUtc, DateTimeOffset.UtcNow, boundary.InvalidEventCount, boundary.TruncatedEventCount, observations,
                deadlineExceeded: budget is { } selected && boundary.Entries.Any(entry => entry.Boundary.Registration.SettledAt is not { } settled || !selected.Contains(settled)));
        }
    }

    internal RuntimeFlushBoundary RegisterFlushBoundary(long? observedAt = null)
    {
        long timestamp = observedAt ?? timeProvider.GetTimestamp();
        if (queuedOutputs is null)
        {
            throw new InvalidOperationException("Flush boundaries require actual queued output composition.");
        }

        lock (admissionGate)
        {
            // Pure system UTC sampling here does not invoke caller factories, payloads or the event clock.
            DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
            OutputFlushBoundary? console = queuedOutputs.Console?.RegisterFlushBoundary(timestamp);
            OutputFlushBoundary? file = queuedOutputs.File?.RegisterFlushBoundary(timestamp);
            var additional = queuedOutputs.Entries.Where(entry => entry.Name is not ("console" or "file"))
                .Select(entry => new NamedOutputFlushBoundary(entry.Name, entry.Queue.RegisterFlushBoundary(timestamp))).ToArray();
            return new RuntimeFlushBoundary(capturedAt, InvalidEventCount, TruncatedEventCount, console, file, Array.AsReadOnly(additional));
        }
    }

    internal RuntimeAccounting CaptureAccounting()
    {
        if (queuedOutputs is null)
        {
            throw new InvalidOperationException("Accounting requires actual queued output composition.");
        }

        lock (admissionGate)
        {
            // Output/disposition/truncation/closure changes share this gate. Invalid increments
            // independently, so its atomic read chooses the observation point while the rest is stable.
            return new RuntimeAccounting(admissionClosed, InvalidEventCount, TruncatedEventCount, queuedOutputs.Console?.CaptureAccounting(), queuedOutputs.File?.CaptureAccounting());
        }
    }

    /// <summary>Returns an owned immutable live observation without routing a log or performing output I/O.</summary>
    /// <remarks>Available after faults and admission closure. This does not register a fixed flush target.</remarks>
    public CoreLogDiagnostics GetDiagnostics()
    {
        lock (admissionGate)
        {
            RuntimeAccounting counts = CaptureAccounting();
            var values = queuedOutputs!.Entries.Select(entry => new LogOutputDiagnostics(entry.Name, entry.Queue.CaptureAccounting(),
                entry.Name == "file" && recoveredPartialSegment)).ToArray();

            CoreLogState state = !counts.AdmissionClosed ? CoreLogState.Running : values.All(value => value.State == LogOutputState.Stopped) ? CoreLogState.Stopped : CoreLogState.Stopping;
            if (incoming is not null && counts.AdmissionClosed && !incoming.Completion.IsCompleted) { state = CoreLogState.Stopping; }
            return new CoreLogDiagnostics(state, counts.AdmissionClosed, DateTimeOffset.UtcNow, shutdownStartedAtUtc, counts.InvalidEventCount, counts.TruncatedEventCount, values, lastStopResult,
                incoming is null ? null : new LogIncomingSnapshot(incoming.Capture()));
        }
    }

    internal OperationScopeStore Scopes { get; } = new();
    internal long InvalidEventCount => Interlocked.Read(ref invalidEventCount);
    internal long TruncatedEventCount => Interlocked.Read(ref truncatedEventCount);

    /// <summary>Creates a logger borrowing this runtime, including after admission closes.</summary>
    /// <exception cref = "ArgumentNullException">Component is null.</exception>
    /// <exception cref = "ArgumentException">Component is blank, malformed, contains controls or exceeds 255 scalars.</exception>
    public CoreLogger CreateLogger(string component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (string.IsNullOrWhiteSpace(component) || !UnicodeScalarText.IsValid(component, 255, allowControls: false))
        {
            throw new ArgumentException("Use a component of 1-255 valid Unicode scalars without controls.", nameof(component));
        }

        return new CoreLogger(this, component);
    }

    /// <summary>Copies a bounded operation scope into this runtime's current asynchronous flow.</summary>
    /// <remarks>The creating flow disposes its handle in LIFO order; valid repeated disposal is harmless.
    /// Existing handles remain disposable after stop. State is checked before copying; a call already copying may finish during closure.
    /// Inherited execution contexts cannot reliably identify every prohibited cross-flow disposal.</remarks>
    /// <exception cref = "ArgumentNullException">Context is null.</exception>
    /// <exception cref = "ArgumentException">Context contains invalid identifiers or attributes.</exception>
    /// <exception cref = "InvalidOperationException">Admission is closed or the scope depth is exhausted.</exception>
    public IDisposable BeginScope(OperationContextData context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsAdmissionClosed)
        {
            throw new InvalidOperationException("Cannot begin a scope after runtime admission closes.");
        }

        return Scopes.BeginScope(context);
    }

    internal bool IsAdmissionClosed
    {
        get
        {
            lock (admissionGate)
            {
                return admissionClosed;
            }
        }
    }

    internal bool IsEnabled(CoreLogLevel level)
    {
        ValidateLevel(level);
        if (EventProcessing.IsCallbackActive) { return false; }
        lock (admissionGate)
        {
            return !admissionClosed && outputs.Any(output => Interested(output, level) && !output.Admission.IsFaulted);
        }
    }

    internal LogWriteResult Write(string component, CoreLogLevel level, LogEventData? data, Func<LogEventData>? factory)
    {
        ValidateLevel(level);
        if (EventProcessing.IsCallbackActive) { return Invalid("RecursiveProcessing"); }
        Output[] preparing;
        lock (admissionGate)
        {
            if (admissionClosed)
            {
                return Stopped(isTruncated: false);
            }

            if (!outputs.Any(output => Interested(output, level)))
            {
                return FinishWithoutCapture(level, LogWriteOutcome.Filtered);
            }

            if (!outputs.Any(output => Interested(output, level) && !output.Admission.IsFaulted))
            {
                return FinishWithoutCapture(level, LogWriteOutcome.Rejected);
            }

            preparing = incoming is null ? CapturePreparingOutputs(level) : [];

        }

        // No gate is held around caller factories, clock, scopes or payload callbacks.
        if (factory is not null)
        {
            data = factory();
        }

        if (data is null)
        {
            return Invalid("InvalidFactoryResult");
        }

        string? eventName = data.EventName;
        string? message = data.Message;
        string? template = data.MessageTemplate;
        string? operationId = data.OperationId;
        CapturedEventFacts facts;
        try
        {
            facts = EventProjection.Capture(metadata, Scopes.Capture(), component, level, data, timeProvider)!;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            return Invalid(CaptureError(eventName, message, template, operationId));
        }

        if (incoming is not null)
        {
            lock (admissionGate)
            {
                if (facts.IsTruncated) { IncrementLossCounter(ref truncatedEventCount); }
                if (admissionClosed) { return Stopped(facts.IsTruncated); }
                long? sequence = incoming.TryAdmit(level, facts);
                return new LogWriteResult(sequence is null ? LogWriteOutcome.Rejected : LogWriteOutcome.Accepted, [],
                    sequence is null ? "IncomingQueueFull" : null, facts.IsTruncated, sequence);
            }
        }
        try { return PrepareCapturedEvent(facts, level, preparing, fromIncoming: false)(); }
        catch (EventEnvelopeTooLargeException) { return Invalid("EnvelopeTooLarge"); }
    }

    private Output[] CapturePreparingOutputs(CoreLogLevel level)
    {
        lock (admissionGate)
        { return outputs.Where(output => output.Prepare is not null && Interested(output, level) && !output.Admission.IsFaulted).ToArray(); }
    }

    private Func<LogWriteResult> PrepareCapturedEvent(CapturedEventFacts facts, CoreLogLevel level, Output[] interested, bool fromIncoming, IReadOnlyList<string>? destinations = null, bool originalWasTruncated = false)
    {
        var prepared = new Dictionary<string, RenderedEvent>(StringComparer.Ordinal);
        var failed = new List<Output>();
        // Other owned encoding/rendering consistency errors are Core defects, not invalid caller content.
        EncodedEvent captured = EventEncoder.Encode(facts, maximumEntryBytes);
        foreach (Output output in interested)
        {
            try { prepared.Add(output.Name, output.Prepare!(captured)); }
            catch (EventEnvelopeTooLargeException) { failed.Add(output); }
        }

        long observedAt = timeProvider.GetTimestamp();
        bool isTruncated = captured.IsTruncated || prepared.Values.Any(value => value.IsTruncated);
        return () =>
        {
            lock (admissionGate)
            {
                if (isTruncated && (!fromIncoming || !originalWasTruncated))
                {
                    IncrementLossCounter(ref truncatedEventCount);
                    if (fromIncoming) { IncrementLossCounter(ref workerTruncated); }
                }

                if (admissionClosed && !fromIncoming)
                {
                    return Stopped(isTruncated);
                }

                foreach (Output output in failed) { ((OutputEventQueue)output.Admission).FaultForRendering(observedAt); }

                return Admit(level, captured, prepared, isTruncated, observedAt, destinations);
            }
        };
    }

    // Primitive internal qualification closure: no final flush or bounded shutdown is promised.
    // Supported applications use StopAsync/DisposeAsync; a later flush cannot reopen an exited worker.
    internal void CloseAdmission()
    {
        if (incoming is not null) { _ = StopAsync(CancellationToken.None); return; }
        lock (admissionGate)
        {
            shutdownStartedAtUtc ??= DateTimeOffset.UtcNow;
            admissionClosed = true;
            queuedOutputs?.Complete();
        }
    }

    private LogWriteResult Invalid(string code)
    {
        lock (admissionGate)
        {
            IncrementLossCounter(ref invalidEventCount);
            if (processing?.IsOwnCallbackActive == true) { IncrementLossCounter(ref workerInvalid); }
        }
        return new LogWriteResult(LogWriteOutcome.InvalidEvent, [], code);
    }

    // Loss counters retain a nonnegative lower bound once Int64 can no longer represent the total.
    private static void IncrementLossCounter(ref long counter)
    {
        long current = Interlocked.Read(ref counter);
        while (current != long.MaxValue)
        {
            long observed = Interlocked.CompareExchange(ref counter, current + 1, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private LogWriteResult Stopped(bool isTruncated) => new(LogWriteOutcome.Stopped, outputs.Select(output => new LogOutputWriteResult(output.Name, LogOutputWriteStatus.Stopped, null)), isTruncated: isTruncated);
    private LogWriteResult FinishWithoutCapture(CoreLogLevel level, LogWriteOutcome outcome)
    {
        var observations = new List<LogOutputWriteResult>(outputs.Length);
        foreach (Output output in outputs)
        {
            LogOutputWriteStatus status;
            if (Interested(output, level))
            {
                output.Admission.RecordUnavailable();
                if (output.CallerUnavailable != long.MaxValue) { output.CallerUnavailable++; }
                status = LogOutputWriteStatus.Faulted;
            }
            else
            {
                output.Admission.RecordFiltered();
                if (output.CallerFiltered != long.MaxValue) { output.CallerFiltered++; }
                status = LogOutputWriteStatus.Filtered;
            }

            observations.Add(new LogOutputWriteResult(output.Name, status, null));
        }

        return new LogWriteResult(outcome, observations);
    }

    private LogWriteResult Admit(CoreLogLevel level, EncodedEvent captured, IReadOnlyDictionary<string, RenderedEvent> prepared, bool isTruncated, long observedAt, IReadOnlyList<string>? destinations)
    {
        var observations = new List<LogOutputWriteResult>(outputs.Length);
        int accepted = 0;
        int interested = 0;
        foreach (Output output in outputs)
        {
            long? sequence = null;
            LogOutputWriteStatus status;
            bool routed = Interested(output, level) && (destinations is null || destinations.Contains(output.Name, StringComparer.Ordinal));
            if (!routed)
            {
                output.Admission.RecordFiltered();
                status = LogOutputWriteStatus.Filtered;
            }
            else
            {
                interested++;
                if (output.Admission.IsFaulted)
                {
                    output.Admission.RecordUnavailable();
                    status = LogOutputWriteStatus.Faulted;
                }
                else
                {
                    status = output.Admission is OutputEventQueue queue
                        ? queue.TryAdmit(captured, prepared.TryGetValue(output.Name, out var rendered) ? rendered.Line : (ReadOnlyMemory<byte>?)null, out sequence, observedAt)
                        : output.Admission.TryAdmit(captured, out sequence);
                }

                if (status == LogOutputWriteStatus.Accepted)
                {
                    accepted++;
                }
            }

            if (routed && status is not (LogOutputWriteStatus.Accepted or LogOutputWriteStatus.QueueFull or LogOutputWriteStatus.Faulted))
            {
                throw new InvalidOperationException("Output admission returned an invalid status.");
            }

            if (status == LogOutputWriteStatus.Accepted ? sequence is null or <= 0 : sequence is not null)
            {
                throw new InvalidOperationException("Output admission returned an invalid ticket.");
            }

            observations.Add(new LogOutputWriteResult(output.Name, status, sequence));
        }

        LogWriteOutcome outcome = LogWriteOutcome.Rejected;
        if (interested == 0) { outcome = LogWriteOutcome.Filtered; }
        else if (accepted == interested)
        {
            outcome = LogWriteOutcome.Accepted;
        }
        else if (accepted > 0)
        {
            outcome = LogWriteOutcome.PartiallyAccepted;
        }

        return new LogWriteResult(outcome, observations, isTruncated: isTruncated);
    }

    private static string CaptureError(string? name, string? message, string? template, string? operationId)
    {
        foreach (string? text in new[]
        {
            name,
            message,
            template,
            operationId
        }

        )
        {
            if (text is not null && !UnicodeScalarText.IsValid(text, int.MaxValue, allowControls: true))
            {
                return "InvalidUnicode";
            }
        }

        if (string.IsNullOrEmpty(name) || !UnicodeScalarText.IsValid(name, 255, allowControls: false))
        {
            return "InvalidEventName";
        }

        if (message is null)
        {
            return "InvalidMessage";
        }

        string? operation = operationId?.Trim();
        if (!string.IsNullOrEmpty(operation) && !UnicodeScalarText.IsValid(operation, 255, allowControls: false))
        {
            return "InvalidOperationId";
        }

        // Arbitrary cooperative callback/unsupported payload failures expose only this fixed code.
        return "UnsupportedValue";
    }

    private static bool Interested(Output output, CoreLogLevel level) => level != CoreLogLevel.None && level >= output.Threshold;
    internal static void ValidateSettings(CoreLogLevel console, CoreLogLevel file, int maximumBytes)
    {
        ValidateLevel(console);
        ValidateLevel(file);
        if (maximumBytes is < LoggingOptions.MinimumMaxEntryBytes or > LoggingOptions.MaximumMaxEntryBytes)
        {
            throw new ArgumentOutOfRangeException("options");
        }
    }

    private static void ValidateLevel(CoreLogLevel level)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Use a declared logging level.");
        }
    }

    private sealed record Output(string Name, CoreLogLevel Threshold, ILogOutputAdmission Admission, Func<EncodedEvent, RenderedEvent>? Prepare = null)
    {
        internal long CallerFiltered { get; set; }
        internal long CallerUnavailable { get; set; }
    }
}
