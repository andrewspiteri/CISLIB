using System.Diagnostics;
using CIS.Healthcheck;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CIS.Startup;

// One actual root and one retained startup operation. No participant code runs under gate.
internal sealed class StartupCoordinator : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IServiceProvider root;
    private readonly ServiceDescriptor[] descriptors;
    private readonly HealthcheckService? health;
    private readonly ILogger<StartupCoordinator>? logger;
    private readonly Func<CancellationToken, bool>? confirmStartupCompletion;
    private readonly CancellationTokenSource stoppingCancellation = new();
    private Task? operation, runner, disposal, disposalRunner;
    private IConditionMonitor? failureMonitor;
    private int stopRequested;
    private bool stopping;

    internal StartupCoordinator(IServiceProvider root, IReadOnlyList<ServiceDescriptor> descriptors,
        HealthcheckService? health = null, ILogger<StartupCoordinator>? logger = null,
        Func<CancellationToken, bool>? confirmStartupCompletion = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(descriptors);
        this.root = root;
        this.descriptors = descriptors.ToArray();
        this.health = health;
        this.logger = logger;
        this.confirmStartupCompletion = confirmStartupCompletion;
    }

    internal Task StartAsync(CancellationToken startupCancellation, CancellationToken applicationStopping = default)
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (operation is not null) { return operation; }
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            operation = completion.Task;
            if (stopping)
            {
                completion.TrySetCanceled(new CancellationToken(canceled: true));
                return operation;
            }
        }
        // The public-facing operation is retained before resolution or any synchronous user prologue.
        // The runner is observed too; its completion is never substituted by a canceled wait.
        Task started = RunOperationAsync(completion, startupCancellation, applicationStopping);
        lock (gate) { runner = started; }
        return completion.Task;
    }

    private async Task RunOperationAsync(TaskCompletionSource completion, CancellationToken startupCancellation,
        CancellationToken applicationStopping)
    {
        long started = Stopwatch.GetTimestamp();
        Exception? failure = null;
        bool canceled = false;
        CancellationToken token = default;
        int currentIndex = 0;
        string stage = "Construction";
        string? participantType = null;
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                startupCancellation, applicationStopping, stoppingCancellation.Token);
            token = cancellation.Token;
            ThrowIfStopping(token);
            try
            {
                failureMonitor = health?.CreateCondition(new ConditionMonitorOptions
                { IsCritical = true, InitiallyFlagged = false, Name = "Application startup" });
            }
            catch (InvalidOperationException) when (Volatile.Read(ref stopRequested) != 0)
            { throw new OperationCanceledException(new CancellationToken(canceled: true)); }
            Log(LogLevel.Information, "Construction", "Started", 0, null, started);
            foreach (ServiceDescriptor descriptor in descriptors)
            {
                if (descriptor.ServiceType == typeof(IStartable) && !descriptor.IsKeyedService
                    && descriptor.Lifetime != ServiceLifetime.Singleton)
                { throw new InvalidOperationException("Every unkeyed IStartable registration must use the root singleton lifetime."); }
            }
            IStartable[] scheduled = root.GetServices<IStartable>().ToArray();
            if (scheduled.Any(participant => participant is null))
            { throw new InvalidOperationException("An IStartable registration resolved to null."); }
            var seen = new HashSet<IStartable>(ReferenceEqualityComparer.Instance);
            IStartable[] participants = scheduled.Where(seen.Add).ToArray();
            Log(LogLevel.Information, "Construction", "Completed", 0, null, started);
            for (int index = 0; index < participants.Length;)
            {
                ThrowIfStopping(token);
                IStartable participant = participants[index++]; // Invocation is recorded before entering user code.
                string name = participant.GetType().FullName ?? participant.GetType().Name;
                stage = "Initialization";
                currentIndex = index;
                participantType = name;
                Log(LogLevel.Information, "Initialization", "Invoking", index, name, started);
                Task? task = participant.StartAsync(token);
                if (task is null) { throw new InvalidOperationException("An IStartable returned a null startup task."); }
                await task.ConfigureAwait(false);
                Log(LogLevel.Information, "Initialization", "Completed", index, name, started);
            }
            lock (gate)
            {
                stage = "Completion";
                ThrowIfStopping(token);
                bool confirmed = confirmStartupCompletion?.Invoke(token) ?? true;
                ThrowIfStopping(token); // Cancellation during completion must never publish readiness.
                if (!confirmed) { throw new InvalidOperationException("Required startup completion could not be confirmed."); }
                health?.MarkStartupCompleted();
            }
            Log(LogLevel.Information, "Startup", "Completed", participants.Length, null, started);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || Volatile.Read(ref stopRequested) != 0)
        {
            canceled = true;
            Log(LogLevel.Information, stage, "Canceled", currentIndex, participantType, started);
        }
        catch (Exception error)
        {
            failure = error;
            try
            {
                failureMonitor?.Flag(HealthDiagnostic.FromException(error, "Startup.Failed",
                    "Required application startup failed. Inspect the retained startup exception."));
            }
            catch (Exception) { /* Reporting cannot replace the originating initialization failure. */ }
            Log(LogLevel.Error, stage, "Failed", currentIndex, participantType, started);
        }
        if (failure is not null) { completion.TrySetException(failure); }
        else if (canceled) { completion.TrySetCanceled(token.IsCancellationRequested ? token : new CancellationToken(canceled: true)); }
        else { completion.TrySetResult(); }
    }

    private void ThrowIfStopping(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref stopRequested) != 0) { throw new OperationCanceledException(new CancellationToken(canceled: true)); }
    }

    // Publish stop intent without waiting for the completion gate. The hosting
    // adapter can then close external dispatch before joining this operation.
    internal void RequestStopping() => Interlocked.Exchange(ref stopRequested, 1);

    internal void BeginStopping()
    {
        RequestStopping();
        lock (gate) { stopping = true; }
        health?.BeginStopping();
        try { stoppingCancellation.Cancel(); }
        catch (AggregateException) { Log(LogLevel.Error, "Stopping", "CancellationCallbackFailed", 0, null, Stopwatch.GetTimestamp()); }
        catch (ObjectDisposedException) { /* Repeated stopping after proved disposal is already complete. */ }
    }

    internal async Task StopAsync()
    {
        BeginStopping();
        Task? startup;
        lock (gate) { startup = operation; }
        if (startup is not null)
        {
            try { await startup.ConfigureAwait(false); }
            catch (Exception) { /* Its terminal outcome remains available through StartAsync. */ }
        }
        Task? actualRunner;
        lock (gate) { actualRunner = runner; }
        if (actualRunner is not null) { await actualRunner.ConfigureAwait(false); }
    }

    public async ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task shared;
        lock (gate)
        {
            if (disposal is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
            }
            shared = disposal;
        }
        if (completion is not null)
        {
            Task started = DisposeCoreAsync(completion);
            lock (gate) { disposalRunner = started; }
        }
        await shared.ConfigureAwait(false);
        Task? actualRunner;
        lock (gate) { actualRunner = disposalRunner; }
        if (actualRunner is not null) { await actualRunner.ConfigureAwait(false); }
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
            if (failureMonitor is not null) { await failureMonitor.DisposeAsync().ConfigureAwait(false); }
            stoppingCancellation.Dispose();
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private void Log(LogLevel level, string stage, string outcome, int index, string? participantType, long origin)
    {
        try
        {
            logger?.Log(level, "Startup {Stage} {Outcome}; participant {Index} {ParticipantType}; elapsed {ElapsedMilliseconds} ms",
                stage, outcome, index, participantType, Stopwatch.GetElapsedTime(origin).TotalMilliseconds);
        }
        catch (Exception) { /* Logging is observational and cannot alter startup or replace its exception. */ }
    }
}
