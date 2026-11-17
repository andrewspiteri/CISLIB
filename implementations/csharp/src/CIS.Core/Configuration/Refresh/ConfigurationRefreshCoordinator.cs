namespace CIS.Configuration;

// Owns real refresh work. Periodic scheduling, DI and application readiness are
// deliberately supplied by the existing integrations rather than recreated here.
internal sealed class ConfigurationRefreshCoordinator : IAsyncDisposable
{
    private sealed class EntryState(ConfigurationEntry entry, string host)
    {
        internal readonly ConfigurationEntry Entry = entry;
        internal readonly object Gate = new();
        internal readonly ConfigurationRecordKey CommonKey = new("CommonConfiguration", entry.Identity);
        internal readonly ConfigurationRecordKey ApplicationKey = new(host, entry.Identity);
        internal readonly ConfigurationSourceCache Common = new();
        internal readonly ConfigurationSourceCache Application = new();
        internal TaskCompletionSource<bool>? Completion;
        internal bool Running, Pending, IsValid, Disabled;
        internal long Revision;
        internal DateTimeOffset? LastSuccess, LastChange;
        internal string? FailureCode, NotificationFailureCode;
    }

    private readonly object gate = new();
    private readonly ConfigurationCatalog catalog;
    private readonly IConfigurationValuePipeline pipeline;
    private readonly EntryState[] entries;
    private readonly Dictionary<ConfigurationEntry, EntryState> byEntry;
    private readonly CancellationTokenSource providerCancellation = new();
    private Task? load, activation, stopping;
    private TaskCompletionSource<bool>? cancellationCompletion;
    private TaskCompletionSource<IAsyncDisposable?>? watchAcquisition;
    private IAsyncDisposable? watch;
    private ConfigurationWatchState watchState;
    private string? watchFailure;
    private bool loaded, activated, disposed;
    private int stopRequested;
    private Action<ConfigurationRefreshStatus>? observer;

    internal ConfigurationRefreshCoordinator(ConfigurationCatalog catalog, IConfigurationValuePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(pipeline);
        this.catalog = catalog;
        this.pipeline = pipeline;
        entries = catalog.Entries.Select(entry => new EntryState(entry, catalog.Settings.HostApplicationName)).ToArray();
        byEntry = entries.ToDictionary(state => state.Entry);
        watchState = catalog.Settings.NotificationMode == ConfigurationNotificationMode.None
            ? ConfigurationWatchState.NotRequired : ConfigurationWatchState.Inactive;
    }

    internal bool IsStopping => Volatile.Read(ref stopRequested) != 0;

    // The hosting health adapter may install one observer after bootstrap. Its
    // synchronous execution remains inside the tracked attempt, outside gates.
    internal Action<ConfigurationRefreshStatus>? Observer
    {
        get => Volatile.Read(ref observer);
        set => Volatile.Write(ref observer, value);
    }

    internal ConfigurationRefreshStatus CaptureStatus()
    {
        bool completed;
        ConfigurationWatchState state;
        string? failure;
        lock (gate) { completed = activated; state = watchState; failure = watchFailure; }
        var observations = new ConfigurationEntryRefreshStatus[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            EntryState entry = entries[index];
            lock (entry.Gate)
            {
                observations[index] = new(entry.Entry.Type, entry.IsValid, entry.Disabled, entry.Revision,
                    entry.LastSuccess, entry.LastChange, entry.FailureCode, entry.NotificationFailureCode);
            }
        }
        return new(completed, IsStopping, state, failure, Array.AsReadOnly(observations));
    }

    internal Task LoadAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (load is not null) { return load; }
            if (IsStopping) { return Task.FromCanceled(new CancellationToken(canceled: true)); }
            completion = NewCompletion();
            load = completion.Task;
        }
        _ = LoadCoreAsync(completion, cancellationToken);
        return completion.Task;
    }

    private async Task LoadCoreAsync(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
                static value => ((ConfigurationRefreshCoordinator)value!).BeginStopping(), this);
            foreach (EntryState entry in entries)
            {
                ThrowIfStopping();
                if (!await RequestAsync(entry, initial: true).ConfigureAwait(false))
                {
                    ThrowIfStopping();
                    throw EntryFailure(entry);
                }
            }
            lock (gate)
            {
                ThrowIfStopping();
                loaded = true;
                completion.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (IsStopping) { completion.TrySetCanceled(new CancellationToken(canceled: true)); }
        catch (CoreConfigurationException error) { completion.TrySetException(error); }
        catch (Exception) { completion.TrySetException(Error("Configuration.InitialLoad", "Initial configuration loading failed.")); }
    }

    internal Task ActivateAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (activation is not null) { return activation; }
            if (IsStopping) { return Task.FromCanceled(new CancellationToken(canceled: true)); }
            if (!loaded) { return Task.FromException(Error("Configuration.Activation", "Complete initial loading before activating refresh.")); }
            completion = NewCompletion();
            activation = completion.Task;
            if (entries.Length != 0 && catalog.Settings.NotificationMode != ConfigurationNotificationMode.None)
            { watchAcquisition = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        }
        _ = ActivateCoreAsync(completion, cancellationToken);
        return completion.Task;
    }

    private async Task ActivateCoreAsync(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
                static value => ((ConfigurationRefreshCoordinator)value!).BeginStopping(), this);
            ThrowIfStopping();
            if (watchAcquisition is not null)
            {
                lock (gate) { watchState = ConfigurationWatchState.Activating; }
                ConfigurationRecordKey[] keys = entries.SelectMany(entry => new[] { entry.CommonKey, entry.ApplicationKey }).ToArray();
                IAsyncDisposable acquired;
                try
                {
                    acquired = await catalog.Settings.Store.WatchAsync(Array.AsReadOnly(keys), Signal,
                        providerCancellation.Token).ConfigureAwait(false);
                    if (acquired is null) { throw new InvalidOperationException(); }
                }
                catch (OperationCanceledException) when (IsStopping) { throw; }
                catch (Exception)
                {
                    lock (gate) { watchState = ConfigurationWatchState.Failed; watchFailure = "Configuration.WatchActivation"; }
                    throw Error("Configuration.WatchActivation", "Establishing configuration observation failed.");
                }
                lock (gate) { watch = acquired; watchState = ConfigurationWatchState.Active; }
                watchAcquisition.TrySetResult(acquired);
            }
            else if (entries.Length == 0) { lock (gate) { watchState = ConfigurationWatchState.NotRequired; } }

            // Before activation, signals only mark pending. Reconcile sequentially
            // until every invalidation received during subscription/read is covered.
            bool first = true;
            while (true)
            {
                foreach (EntryState entry in entries)
                {
                    bool pending;
                    lock (entry.Gate) { pending = entry.Pending; }
                    if (!first && !pending) { continue; }
                    ThrowIfStopping();
                    if (!await RequestAsync(entry).ConfigureAwait(false))
                    {
                        ThrowIfStopping();
                        throw EntryFailure(entry);
                    }
                }
                first = false;
                lock (gate)
                {
                    ThrowIfStopping();
                    if (entries.Any(HasPending)) { continue; }
                    activated = true;
                    break;
                }
            }
            ReportStatus();
            completion.TrySetResult();
        }
        catch (OperationCanceledException) when (IsStopping) { completion.TrySetCanceled(new CancellationToken(canceled: true)); }
        catch (CoreConfigurationException error) { completion.TrySetException(error); }
        catch (Exception) { completion.TrySetException(Error("Configuration.Activation", "Activating configuration refresh failed.")); }
        finally { watchAcquisition?.TrySetResult(null); }
    }

    private static bool HasPending(EntryState entry)
    {
        lock (entry.Gate) { return entry.Pending; }
    }

    internal Task<bool> ReconcileAsync(ConfigurationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!byEntry.TryGetValue(entry, out EntryState? selected)) { throw new ArgumentException("The entry belongs to another configuration runtime.", nameof(entry)); }
        lock (gate)
        {
            if (IsStopping) { return Task.FromResult(false); }
            if (!loaded) { throw new InvalidOperationException("Complete initial loading before requesting reconciliation."); }
        }
        return RequestAsync(selected);
    }

    internal async Task<bool> ReconcileAsync()
    {
        lock (gate)
        {
            if (IsStopping) { return false; }
            if (!loaded) { throw new InvalidOperationException("Complete initial loading before requesting reconciliation."); }
        }
        bool valid = true;
        foreach (EntryState entry in entries) { valid &= await ReconcileAsync(entry.Entry).ConfigureAwait(false); }
        return valid;
    }

    private Task<bool> RequestAsync(EntryState entry, bool initial = false, bool queued = false)
    {
        TaskCompletionSource<bool> completion;
        lock (entry.Gate)
        {
            if (IsStopping || entry.Disabled) { return Task.FromResult(false); }
            entry.Pending = true;
            if (entry.Running) { return entry.Completion!.Task; }
            entry.Running = true;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            entry.Completion = completion;
        }
        if (!queued) { _ = RunEntryAsync(entry, completion, initial); }
        else if (!ThreadPool.UnsafeQueueUserWorkItem(static work =>
            { _ = work.Coordinator.RunEntryAsync(work.Entry, work.Completion, work.Initial); },
            (Coordinator: this, Entry: entry, Completion: completion, Initial: initial), preferLocal: false))
        {
            SetFailure(entry, "Configuration.Dispatch");
            ReportStatus();
            lock (entry.Gate) { entry.Pending = entry.Running = false; completion.TrySetResult(false); }
        }
        return completion.Task;
    }

    private async Task RunEntryAsync(EntryState entry, TaskCompletionSource<bool> completion, bool initial)
    {
        bool valid = false;
        try
        {
            while (true)
            {
                lock (entry.Gate) { entry.Pending = false; }
                valid = await AttemptAsync(entry, initial).ConfigureAwait(false);
                initial = false;
                ReportStatus();
                lock (entry.Gate)
                {
                    if (entry.Pending && !IsStopping && !entry.Disabled) { continue; }
                    entry.Running = false;
                    completion.TrySetResult(valid);
                    return;
                }
            }
        }
        catch (Exception)
        {
            SetFailure(entry, "Configuration.Refresh");
            ReportStatus();
            lock (entry.Gate) { entry.Pending = entry.Running = false; completion.TrySetResult(false); }
        }
    }

    private async Task<bool> AttemptAsync(EntryState state, bool initial)
    {
        try
        {
            ThrowIfStopping();
            bool disabled;
            lock (state.Entry.ValueGate) { disabled = state.Entry.Disabled; }
            if (disabled) { SetFailure(state, "Configuration.CommitDisabled", disabled: true); return false; }
            string? commonFailure = await state.Common.ReadAsync(catalog.Settings.Store, state.CommonKey, providerCancellation.Token).ConfigureAwait(false);
            string? applicationFailure = await state.Application.ReadAsync(catalog.Settings.Store, state.ApplicationKey, providerCancellation.Token).ConfigureAwait(false);
            if (commonFailure is not null || applicationFailure is not null)
            { SetFailure(state, commonFailure ?? applicationFailure!); return false; }
            ConfigurationPreparedValues prepared = pipeline.Prepare(state.Entry, state.Common.Payload, state.Application.Payload);
            bool changed = pipeline.Apply(state.Entry, prepared, providerCancellation.Token, () => IsStopping, initial);
            long revision;
            lock (state.Entry.ValueGate) { revision = state.Entry.Revision; disabled = state.Entry.Disabled; }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (state.Gate)
            {
                state.IsValid = true;
                state.Disabled = disabled;
                state.Revision = revision;
                state.LastSuccess = now;
                if (changed) { state.LastChange = now; state.NotificationFailureCode = null; }
                state.FailureCode = null;
            }
            // There is deliberately no cancellation check after commit. An
            // accepted revision includes its ordered synchronous notification.
            if (changed)
            {
                ConfigurationEvents.Publish(state.Entry.Instance, revision, _ =>
                {
                    lock (state.Gate) { state.NotificationFailureCode = "Configuration.Notification"; }
                });
            }
            return true;
        }
        catch (OperationCanceledException) when (IsStopping) { return false; }
        catch (Exception)
        {
            bool disabled;
            lock (state.Entry.ValueGate) { disabled = state.Entry.Disabled; }
            SetFailure(state, disabled ? "Configuration.CommitDisabled" : "Configuration.Refresh", disabled);
            return false;
        }
    }

    private static void SetFailure(EntryState entry, string code, bool disabled = false)
    {
        lock (entry.Gate) { entry.IsValid = false; entry.Disabled = disabled; entry.FailureCode = code; }
    }

    private void Signal(ConfigurationInvalidation invalidation)
    {
        if (invalidation is null || IsStopping) { return; }
        var dispatch = new List<EntryState>();
        lock (gate)
        {
            if (IsStopping) { return; }
            foreach (EntryState entry in entries)
            {
                if (invalidation.Scope is not null && invalidation.Scope != entry.CommonKey.Scope && invalidation.Scope != entry.ApplicationKey.Scope
                    || invalidation.Definition is not null && invalidation.Definition != entry.Entry.Identity) { continue; }
                if (!activated) { lock (entry.Gate) { entry.Pending = true; } }
                else { dispatch.Add(entry); }
            }
        }
        foreach (EntryState entry in dispatch) { _ = RequestAsync(entry, queued: true); }
    }

    internal void BeginStopping()
    {
        TaskCompletionSource<bool> completion;
        lock (gate)
        {
            if (IsStopping) { return; }
            Volatile.Write(ref stopRequested, 1);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationCompletion = completion;
        }
        _ = CancelProviderAsync(completion);
    }

    private async Task CancelProviderAsync(TaskCompletionSource<bool> completion)
    {
        try { await providerCancellation.CancelAsync().ConfigureAwait(false); completion.TrySetResult(true); }
        catch (Exception) { completion.TrySetResult(false); }
    }

    internal Task StopAsync()
    {
        BeginStopping();
        TaskCompletionSource completion;
        lock (gate)
        {
            if (stopping is not null) { return stopping; }
            completion = NewCompletion();
            stopping = completion.Task;
        }
        _ = StopCoreAsync(completion);
        return completion.Task;
    }

    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
        bool cleanupFailed = false;
        Task? initial, starting;
        Task<IAsyncDisposable?>? acquiring;
        IAsyncDisposable? subscription;
        lock (gate) { initial = load; starting = activation; acquiring = watchAcquisition?.Task; subscription = watch; }
        if (acquiring is not null) { subscription = await acquiring.ConfigureAwait(false); }
        if (subscription is not null)
        {
            try { await subscription.DisposeAsync().ConfigureAwait(false); }
            catch (Exception)
            {
                cleanupFailed = true;
                lock (gate) { watchFailure = "Configuration.WatchDisposal"; }
            }
        }
        await ObserveAsync(initial).ConfigureAwait(false);
        await ObserveAsync(starting).ConfigureAwait(false);
        foreach (EntryState entry in entries)
        {
            Task? actual;
            lock (entry.Gate) { actual = entry.Completion?.Task; }
            await ObserveAsync(actual).ConfigureAwait(false);
        }
        Task<bool> canceled;
        lock (gate) { canceled = cancellationCompletion!.Task; }
        cleanupFailed |= !await canceled.ConfigureAwait(false);
        lock (gate) { watchState = ConfigurationWatchState.Stopped; }
        ReportStatus();
        if (cleanupFailed) { completion.TrySetException(Error("Configuration.Stop", "Owned configuration observation or cancellation cleanup failed after actual work settled.")); }
        else { completion.TrySetResult(); }
    }

    private static async Task ObserveAsync(Task? operation)
    {
        if (operation is null) { return; }
        try { await operation.ConfigureAwait(false); }
        catch (Exception) { /* Original phase outcomes remain available to their callers. */ }
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        finally
        {
            bool release;
            lock (gate) { release = !disposed; disposed = true; }
            if (release) { providerCancellation.Dispose(); }
        }
    }

    private void ThrowIfStopping()
    {
        if (IsStopping) { throw new OperationCanceledException(new CancellationToken(canceled: true)); }
    }

    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static CoreConfigurationException Error(string code, string explanation)
        => ConfigurationDiagnostics.Error(typeof(ConfigurationRefreshCoordinator), code, explanation);
    private static CoreConfigurationException EntryFailure(EntryState entry)
    {
        lock (entry.Gate)
        { return ConfigurationDiagnostics.Error(entry.Entry.Type, entry.FailureCode ?? "Configuration.Refresh", "Configuration reconciliation failed; inspect its bounded status and passive data contract."); }
    }

    private void ReportStatus()
    {
        try { Observer?.Invoke(CaptureStatus()); }
        catch (Exception) { /* Health/diagnostic observation cannot replace real refresh or cleanup outcomes. */ }
    }
}
