using System.Data.Common;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using CIS.Messaging.Hosting;
using CIS.Security;
using CIS.Startup;
using CIS.Transactions;
using Microsoft.Extensions.Logging;

namespace CIS.Messaging;

// The existing host invokes this one module owner. It knows neither CISApplication nor collector SDKs.
internal sealed class MessagingRuntime : IStartable, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly MessagingSnapshot snapshot;
    private readonly IMessagingStore store;
    private readonly ILocalTransactionFactory transactions;
    private readonly IMessageTransport transport;
    private readonly MessagingResourceBudget resources;
    private readonly MessageOutbox outbox;
    private readonly MessageOutboxPublisher publisher;
    private readonly MessageDispatcher dispatcher;
    private readonly MessagingInstrumentation observations;
    private readonly LocalApplicationIdentity identity;
    private readonly IHealthMonitorFactory? health;
    private readonly ILogger<MessagingRuntime>? logger;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource startupStopping = new();
    private readonly CancellationTokenSource loopStopping = new();
    private readonly string database;
    private readonly bool requireTransport;
    private MessageRequestCoordinator? requests;
    private Task? starting;
    private Task? stopping;
    private Task? cutoff;
    private Task? loop;
    private Task? disposal;
    private IConditionMonitor? readiness;
    private ExceptionDispatchInfo? loopFault;
    private bool closing;
    private bool started;
    private bool storeAvailable;
    private long? lastDiagnostic;
    private int diagnosticsRemaining = 8;

    internal MessagingRuntime(MessagingSnapshot snapshot, MessagingComposition composition, IMessagingStore store,
        ILocalTransactionFactory transactions, IMessageTransport transport, MessagingResourceBudget resources,
        MessageOutbox outbox, MessageOutboxPublisher publisher, MessageDispatcher dispatcher,
        MessagingInstrumentation observations, LocalApplicationIdentity identity, IHealthMonitorFactory? health = null,
        ILogger<MessagingRuntime>? logger = null, TimeProvider? timeProvider = null)
    {
        snapshot.Validate();
        if (!ReferenceEquals(snapshot.RegistrationIdentity, composition.RegistrationIdentity)
            || !ReferenceEquals(snapshot.RegistrationIdentity, observations.RegistrationIdentity))
        {
            throw new InvalidOperationException("Register Messaging hosting after its complete exact definition snapshot.");
        }
        this.snapshot = snapshot;
        this.store = store;
        this.transactions = transactions;
        this.transport = transport;
        this.resources = resources;
        this.outbox = outbox;
        this.publisher = publisher;
        this.dispatcher = dispatcher;
        this.observations = observations;
        this.identity = identity;
        this.health = health;
        this.logger = logger;
        clock = timeProvider ?? TimeProvider.System;
        database = snapshot.Stores.Single(item => item.Alias == snapshot.Settings.StoreAlias).DatabaseName;
        requireTransport = snapshot.Settings.RequireLiveBrokerForReadiness
            && (snapshot.Subscriptions.Count != 0 || snapshot.Requests.Count != 0);
    }

    internal bool IsReady
    {
        get
        {
            lock (gate)
            {
                return started && !closing && storeAvailable && loopFault is null && (!requireTransport || transport.IsReady);
            }
        }
    }

    internal void BindRequests(MessageRequestCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        lock (gate)
        {
            if (starting is not null || closing || requests is not null)
            {
                throw new InvalidOperationException("Bind the single request owner before Messaging startup.");
            }
            requests = coordinator;
        }
    }

    internal bool IsAccepting
    {
        get
        {
            lock (gate)
            {
                return !closing && disposal is null;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (starting is not null)
            {
                return starting;
            }
            ObjectDisposedException.ThrowIf(closing || disposal is not null, this);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            starting = completion.Task;
        }
        _ = StartCoreAsync(completion, cancellationToken);
        return completion.Task;
    }

    private async Task StartCoreAsync(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, startupStopping.Token);
            readiness = health?.CreateCondition(new()
            {
                Name = "Messaging",
                IsCritical = true,
                InitiallyFlagged = true,
                InitialDiagnostic = Diagnostic("messaging.starting")
            });
            await ObserveStoreAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            await transport.StartAsync(async (delivery, token) =>
            {
                _ = await dispatcher.DispatchAsync(delivery, token).ConfigureAwait(false);
            }, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(closing, this);
                started = true;
            }
            UpdateReadiness();
            loop = RunPublisherAsync();
            completion.TrySetResult();
        }
        catch (OperationCanceledException error)
        {
            completion.TrySetCanceled(error.CancellationToken);
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private async Task RunPublisherAsync()
    {
        CancellationToken token = loopStopping.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                _ = await publisher.PumpAsync(token).ConfigureAwait(false);
                await ObserveStoreAsync(token).ConfigureAwait(false);
                UpdateReadiness();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (DbException)
            {
                SetStoreUnavailable();
                ReportFailure("messaging.store_unavailable");
            }
            catch (Exception error)
            {
                lock (gate)
                {
                    loopFault ??= ExceptionDispatchInfo.Capture(error);
                }
                SetStoreUnavailable();
                ReportFailure("messaging.publisher_failed");
                return;
            }
            try
            {
                await publisher.WaitForWorkAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ObserveStoreAsync(CancellationToken token)
    {
        ILocalTransaction owner = await transactions.BeginAsync(database,
            new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, token).ConfigureAwait(false);
        MessageStoreObservation observed = await MessagingTransaction.UseAsync(owner, database, async current =>
        {
            MessageStoreObservation value = await store.ObserveAsync(current.Context,
                new(identity.Application, identity.Environment), token).ConfigureAwait(false);
            if ((await current.RollbackAsync().ConfigureAwait(false)).Outcome != TransactionOutcome.NotCommitted)
            {
                throw new InvalidOperationException("The messaging startup/observation read did not settle definitively.");
            }
            return value;
        }).ConfigureAwait(false);
        observations.Observe(observed);
        lock (gate)
        {
            storeAvailable = true;
        }
    }

    private void SetStoreUnavailable()
    {
        lock (gate)
        {
            storeAvailable = false;
        }
        observations.Observe(null);
        UpdateReadiness();
    }

    private void UpdateReadiness()
    {
        if (IsReady)
        {
            readiness?.Clear();
        }
        else
        {
            readiness?.Flag(Diagnostic("messaging.unavailable"));
        }
    }

    internal void BeginStopping()
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (cutoff is not null)
            {
                return;
            }
            closing = true;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cutoff = completion.Task;
        }
        _ = CloseAdmissionAsync(completion);
    }

    private async Task CloseAdmissionAsync(TaskCompletionSource completion)
    {
        var failures = new List<Exception>();
        Task requestDrain = StartDrain(() => requests?.StopAsync() ?? Task.CompletedTask, failures);
        Task callerDrain = StartDrain(outbox.StopAsync, failures);
        Task dispatchDrain = StartDrain(dispatcher.StopAsync, failures);
        Task consumerDrain = StartDrain(transport.StopConsumersAsync, failures);
        await CaptureAsync(startupStopping.CancelAsync, failures).ConfigureAwait(false);
        await CaptureAsync(() => Task.WhenAll(requestDrain, callerDrain, dispatchDrain, consumerDrain), failures).ConfigureAwait(false);
        Complete(completion, failures);
    }

    internal Task StopAsync()
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (stopping is not null)
            {
                return stopping;
            }
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            stopping = completion.Task;
        }
        BeginStopping();
        _ = StopCoreAsync(completion);
        return completion.Task;
    }

    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
        var failures = new List<Exception>();
        if (starting is { } startup)
        {
            try
            {
                await startup.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The original startup caller retains its exact failure; cleanup still joins its work.
            }
        }
        await CaptureAsync(() => cutoff!, failures).ConfigureAwait(false);
        await CaptureAsync(loopStopping.CancelAsync, failures).ConfigureAwait(false);
        await CaptureAsync(publisher.StopAsync, failures).ConfigureAwait(false);
        if (loop is { } running)
        {
            await CaptureAsync(() => running, failures).ConfigureAwait(false);
        }
        await CaptureAsync(transport.StopAsync, failures).ConfigureAwait(false);
        await CaptureAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        if (loopFault is { } failure)
        {
            failures.Add(failure.SourceException);
        }
        observations.Observe(null);
        Complete(completion, failures);
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (disposal is not null)
            {
                return new(disposal);
            }
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            disposal = completion.Task;
        }
        _ = DisposeCoreAsync(completion);
        return new(completion.Task);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        var failures = new List<Exception>();
        await CaptureAsync(StopAsync, failures).ConfigureAwait(false);
        if (readiness is { } monitor)
        {
            await CaptureAsync(() => monitor.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        DisposeCancellation(startupStopping, failures);
        DisposeCancellation(loopStopping, failures);
        Complete(completion, failures);
    }

    private static void DisposeCancellation(CancellationTokenSource source, List<Exception> failures)
    {
        try
        {
            source.Dispose();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
    }

    private void ReportFailure(string code)
    {
        MessagingInstrumentation.Attempt(() =>
        {
            long now = clock.GetTimestamp();
            if (diagnosticsRemaining == 0 || lastDiagnostic is { } last && clock.GetElapsedTime(last, now) < TimeSpan.FromSeconds(30))
            {
                return;
            }
            diagnosticsRemaining--;
            lastDiagnostic = now;
            logger?.LogWarning(new EventId(18000, "messaging.unavailable"),
                "Messaging background responsibility is unavailable ({Code}).", code);
        });
    }

    private static HealthDiagnostic Diagnostic(string code) => new(code, "The selected messaging responsibility is not ready.");

    private static async Task CaptureAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
    }

    private static Task StartDrain(Func<Task> operation, List<Exception> failures)
    {
        try
        {
            return operation() ?? throw new InvalidOperationException("A messaging drain returned no actual operation.");
        }
        catch (Exception error)
        {
            failures.Add(error);
            return Task.CompletedTask;
        }
    }

    private static void Complete(TaskCompletionSource completion, List<Exception> failures)
    {
        if (failures.Count == 0)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(new AggregateException("Messaging stopped after actual owned work settlement.", failures));
        }
    }
}
