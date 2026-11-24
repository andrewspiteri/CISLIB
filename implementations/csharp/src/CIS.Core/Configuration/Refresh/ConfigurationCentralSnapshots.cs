using System.Globalization;

namespace CIS.Configuration;

internal sealed record ConfigurationCentralReadiness(ConfigurationSnapshotTarget? Target, long SessionEpoch,
    long RequiredEpoch, long CompletedEpoch, bool IsReady, bool IsReconciling);

internal sealed class ConfigurationSnapshotCache(ConfigurationSnapshotReadResult result, long sessionEpoch,
    ConfigurationByteBudget.Reservation ownership) : IDisposable
{
    internal ConfigurationSnapshotReadResult Result { get; } = result;
    internal long SessionEpoch { get; } = sessionEpoch;
    public void Dispose() => ownership.Dispose();
}

internal sealed partial class ConfigurationRefreshCoordinator
{
    private readonly object centralGate = new();
    private readonly ConfigurationSnapshotAdmission centralAdmission = new();
    private CancellationTokenSource centralCancellation = new();
    private ConfigurationSnapshotTarget? centralTarget;
    private long sessionEpoch = 1;
    private long requiredEpoch;
    private long completedEpoch;
    private Task<bool>? centralReconciliation;
    private Task<bool>? centralRebinding;
    private bool centralReady = true;
    private Task<bool>? centralRecoveryRetryOwner;

    private void InitializeCentral()
    {
        centralTarget = catalog.Settings.SnapshotTarget;
        foreach (EntryState state in entries.Where(state => state.Entry.Enrollment is not null))
        {
            state.Entry.SnapshotAdmission = centralAdmission;
            state.Entry.Acceptance = new(ConfigurationAcceptanceOutcome.Pending, null, null, 0, false);
            requiredEpoch = 1;
            centralReady = false;
        }
    }

    internal ConfigurationCentralReadiness CaptureCentralReadiness()
    {
        lock (centralGate)
        {
            bool ready = centralReady && !IsStopping && !centralAdmission.IsFaulted
                && completedEpoch >= requiredEpoch;
            return new(centralTarget, sessionEpoch, requiredEpoch, completedEpoch, ready,
                centralReconciliation is { IsCompleted: false } || centralRebinding is { IsCompleted: false });
        }
    }

    // Called only for an actual authority/connectivity gap. Repeated requests
    // while that gap is incomplete share its one epoch and existing owner.
    internal long RequireCentralReconciliation()
    {
        lock (centralGate)
        {
            if (IsStopping)
            {
                return requiredEpoch;
            }
            if (requiredEpoch == completedEpoch)
            {
                requiredEpoch = checked(requiredEpoch + 1);
                centralReady = false;
                centralReconciliation = null;
            }
            return requiredEpoch;
        }
    }

    internal Task<bool> ReconcileCentralAsync()
    {
        TaskCompletionSource<bool> completion;
        long ownedSession;
        long ownedEpoch;
        lock (centralGate)
        {
            if (IsStopping)
            {
                return Task.FromResult(false);
            }
            if (centralReconciliation is { IsCompleted: false })
            {
                return centralReconciliation;
            }
            if (completedEpoch >= requiredEpoch)
            {
                return Task.FromResult(CaptureCentralReadiness().IsReady);
            }
            if (centralReconciliation is not null)
            {
                return centralReconciliation;
            }
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            centralReconciliation = completion.Task;
            ownedSession = sessionEpoch;
            ownedEpoch = requiredEpoch;
        }
        // The short hosting callback only publishes/coalesces this actual owner.
        // Synchronously completing providers/mapping must not run its full sweep
        // on a control or admission observation thread.
        if (!ThreadPool.UnsafeQueueUserWorkItem(static work =>
            {
                _ = work.Coordinator.ReconcileCentralCoreAsync(work.Completion, work.Session, work.Epoch);
            }, (Coordinator: this, Completion: completion, Session: ownedSession, Epoch: ownedEpoch), preferLocal: false))
        {
            completion.TrySetResult(false);
        }
        return completion.Task;
    }

    // A newly validated native control may retry one offline failed sweep in the SAME epoch.
    // Ordinary polling/control observers keep the existing cached-result behavior.
    internal Task<bool> ReconcileCentralAfterRecoveryAsync()
    {
        lock (centralGate)
        {
            if (IsStopping || completedEpoch >= requiredEpoch)
            {
                return Task.FromResult(CaptureCentralReadiness().IsReady);
            }
            if (centralReconciliation is { IsCompleted: false })
            {
                centralRecoveryRetryOwner = centralReconciliation;
                return centralReconciliation;
            }
            if (centralReconciliation is { IsCompletedSuccessfully: true } failed && !failed.Result)
            {
                centralReconciliation = null;
                centralRecoveryRetryOwner = null;
            }
        }
        return ReconcileCentralAsync();
    }

    private async Task ReconcileCentralCoreAsync(TaskCompletionSource<bool> completion, long ownedSession, long ownedEpoch)
    {
        while (true)
        {
            bool ready = false;
            try
            {
                foreach (EntryState state in entries.Where(state => state.Entry.Enrollment is not null))
                {
                    if (!OwnsCentralReconciliation(completion.Task, ownedSession, ownedEpoch))
                    {
                        break;
                    }
                    await RequestAsync(state).ConfigureAwait(false);
                }
                CompleteCentralEpochIfReady(ownedSession, ownedEpoch);
                ready = CaptureCentralReadiness().IsReady;
            }
            catch (Exception)
            {
                // Preserve the existing safe failed-reconciliation outcome.
            }
            lock (centralGate)
            {
                // Publishing completion and consuming recovery are atomic: a valid control cannot
                // arrive between a failed worker's last check and completion and lose its one retry.
                bool current = OwnsCentralReconciliation(completion.Task, ownedSession, ownedEpoch);
                if (!ready && current && ReferenceEquals(centralRecoveryRetryOwner, completion.Task))
                {
                    centralRecoveryRetryOwner = null;
                    continue;
                }
                if (ReferenceEquals(centralRecoveryRetryOwner, completion.Task))
                {
                    centralRecoveryRetryOwner = null;
                }
                completion.TrySetResult(ready && current);
                return;
            }
        }
    }

    private bool OwnsCentralReconciliation(Task<bool> owner, long ownedSession, long ownedEpoch)
    {
        lock (centralGate)
        {
            return !IsStopping && ownedSession == sessionEpoch && ownedEpoch == requiredEpoch
                && ReferenceEquals(centralReconciliation, owner);
        }
    }

    // Only the authenticated hosting owner can call this after a new admission.
    // Remote content never chooses this target, and fixed app/env/authority/host
    // cannot silently move. Session equality state resets; map high-water does not.
    internal Task<bool> RebindSnapshotTargetAsync(ConfigurationSnapshotTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        TaskCompletionSource<bool> completion;
        CancellationTokenSource previous;
        Task[] oldWork;
        lock (centralGate)
        {
            if (IsStopping)
            {
                return Task.FromResult(false);
            }
            if (centralTarget is null || target.AuthorityId != centralTarget.AuthorityId
                || target.ApplicationId != centralTarget.ApplicationId || target.Environment != centralTarget.Environment
                || target.HostApplicationName != centralTarget.HostApplicationName)
            {
                throw new InvalidOperationException("A trusted rebind must preserve the established authority, application, environment and fixed host.");
            }
            if (target == centralTarget)
            {
                return centralRebinding ?? ReconcileCentralAsync();
            }
            if (centralRebinding is { IsCompleted: false })
            {
                throw new InvalidOperationException("Join the current authenticated target handoff before selecting another registration.");
            }
            sessionEpoch = checked(sessionEpoch + 1);
            requiredEpoch = checked(requiredEpoch + 1);
            centralReady = false;
            centralTarget = target;
            Task<bool>? previousReconciliation = centralReconciliation;
            centralReconciliation = null;
            centralRecoveryRetryOwner = null;
            previous = centralCancellation;
            centralCancellation = new();
            var running = new List<Task>();
            if (previousReconciliation is { IsCompleted: false })
            {
                running.Add(previousReconciliation);
            }
            foreach (EntryState state in entries.Where(state => state.Entry.Enrollment is not null))
            {
                lock (state.Entry.ValueGate)
                {
                    state.Entry.SessionEpoch = sessionEpoch;
                    state.Entry.SnapshotTarget = target;
                    state.Entry.ObservedDesired = null;
                    state.Entry.ObservedProvenance = null;
                }
                lock (state.Gate)
                {
                    if (state.Running)
                    {
                        running.Add(state.Completion!.Task);
                    }
                }
            }
            oldWork = running.ToArray();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            centralRebinding = completion.Task;
        }
        _ = RebindCoreAsync(previous, oldWork, completion);
        return completion.Task;
    }

    private async Task RebindCoreAsync(CancellationTokenSource previous, Task[] oldWork, TaskCompletionSource<bool> completion)
    {
        bool cancellationSucceeded = true;
        try
        {
            await previous.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception) { cancellationSucceeded = false; }
        try
        {
            await Task.WhenAll(oldWork).ConfigureAwait(false);
            previous.Dispose();
            bool ready = await ReconcileCentralAsync().ConfigureAwait(false);
            completion.TrySetResult(cancellationSucceeded && ready);
        }
        catch (Exception)
        {
            previous.Dispose();
            completion.TrySetResult(false);
        }
    }

    private static bool HasCurrentIntactAcceptance(EntryState state)
    {
        lock (state.Entry.ValueGate)
        {
            ConfigurationAcceptanceStatus? accepted = state.Entry.Acceptance;
            return !state.Entry.Disabled && accepted is { HasIntactAcceptedState: true }
                && accepted.Desired?.Target == state.Entry.SnapshotTarget;
        }
    }

    private bool IsAuthoritativelyReady(EntryState state)
    {
        if (state.Entry.Enrollment is null)
        {
            return false;
        }
        bool reconciled;
        lock (centralGate)
        {
            reconciled = state.ReconciledEpoch >= requiredEpoch;
        }
        return reconciled && HasCurrentIntactAcceptance(state);
    }

    private void CompleteCentralEpochIfReady(long? ownedSession = null, long? ownedEpoch = null)
    {
        long completingEpoch;
        long completingSession;
        lock (centralGate)
        {
            if (ownedSession is not null && ownedSession != sessionEpoch || ownedEpoch is not null && ownedEpoch != requiredEpoch)
            {
                return;
            }
            if (centralAdmission.IsFaulted)
            {
                centralReady = false;
                return;
            }
            completingEpoch = requiredEpoch;
            completingSession = sessionEpoch;
            if (IsStopping || entries.Where(state => state.Entry.Enrollment is not null)
                .Any(state => state.ReconciledEpoch < completingEpoch))
            {
                return;
            }
        }
        // Inspect root gates only on actual completion work, never while holding
        // the short readiness-observation gate. Session changes and permanent
        // faults are checked again before publishing the retained aggregate.
        bool intact = entries.Where(state => state.Entry.Enrollment is not null).All(HasCurrentIntactAcceptance);
        lock (centralGate)
        {
            if (completingEpoch == requiredEpoch && completingSession == sessionEpoch
                && intact && !IsStopping && !centralAdmission.IsFaulted)
            {
                completedEpoch = completingEpoch;
                centralReady = true;
            }
        }
    }

    private async Task<bool> AttemptCentralAsync(EntryState state, bool initial)
    {
        ConfigurationEntry entry = state.Entry;
        long epoch;
        long reconciliationEpoch;
        ConfigurationSnapshotTarget target;
        CancellationToken sessionToken;
        lock (centralGate)
        {
            epoch = sessionEpoch;
            reconciliationEpoch = requiredEpoch;
            target = centralTarget!;
            sessionToken = centralCancellation.Token;
        }
        using var work = CancellationTokenSource.CreateLinkedTokenSource(providerCancellation.Token, sessionToken);
        ConfigurationDesiredIdentity? identified = null;
        ConfigurationPreparedValues? prepared = null;
        try
        {
            if (pipeline is not IConfigurationSnapshotValuePipeline snapshotPipeline)
            {
                throw ConfigurationCentralLimits.Error("Configuration.SnapshotPipeline", "Central enrollment requires the actual snapshot acceptance pipeline.");
            }
            work.Token.ThrowIfCancellationRequested();
            if (state.Snapshot is { } old && old.SessionEpoch != epoch)
            {
                old.Dispose();
                state.Snapshot = null;
            }
            var request = new ConfigurationSnapshotRequest(state.CommonKey, state.ApplicationKey,
                state.Snapshot?.Result.SnapshotToken, entry.Enrollment, target)
            {
                RetainedPair = state.Snapshot?.Result
            };
            ConfigurationSnapshotReadResult? response;
            using (ConfigurationByteBudget.Reservation input = entry.ByteBudget!.Reserve(ConfigurationCentralLimits.MaximumReadBytes))
            {
                response = await ((IConfigurationSnapshotStore)catalog.Settings.Store).ReadSnapshotAsync(request, work.Token).ConfigureAwait(false);
                work.Token.ThrowIfCancellationRequested();
                if (response is null)
                {
                    throw ConfigurationCentralLimits.Error("Configuration.SnapshotRead", "The paired provider returned no usable result.");
                }
                if (response.Kind == ConfigurationSnapshotReadKind.Unchanged)
                {
                    response = state.Snapshot?.Result;
                    if (response is null)
                    {
                        throw ConfigurationCentralLimits.Error("Configuration.UnchangedWithoutCache", "A whole-pair unchanged response requires the exact retained session pair.");
                    }
                }
                if (response.Desired is { } desired && request.MatchesTarget(desired))
                {
                    identified = desired;
                }
                if (identified is null || response.Kind == ConfigurationSnapshotReadKind.Complete && response.Provenance is null)
                {
                    throw ConfigurationCentralLimits.Error("Configuration.SnapshotTarget", "The snapshot lacks the independently expected complete target/schema binding.");
                }
                string? refused = ObserveDesired(entry, identified, response.Provenance, epoch);
                if (refused is not null)
                {
                    identified = null;
                    throw ConfigurationCentralLimits.Error(refused, "A stale, equal-conflicting or superseded desired snapshot was refused.");
                }
                if (response.Kind == ConfigurationSnapshotReadKind.Failure)
                {
                    throw ConfigurationCentralLimits.Error(response.FailureCode ?? "Configuration.SnapshotRead", "An identified desired snapshot failed retrieval or integrity validation.");
                }
                long sourceBytes = ConfigurationCentralLimits.RetainedSourceBytes(response.Common!)
                    + ConfigurationCentralLimits.RetainedSourceBytes(response.Application!);
                if (!ReferenceEquals(response, state.Snapshot?.Result))
                {
                    ConfigurationByteBudget.Reservation retained = entry.ByteBudget.Reserve(sourceBytes);
                    state.Snapshot?.Dispose();
                    state.Snapshot = new(response, epoch, retained);
                }
            }
            prepared = pipeline.Prepare(entry, response.Common!.Payload, response.Application!.Payload);
            ConfigurationSnapshotCommit committed = snapshotPipeline.ApplySnapshot(entry, prepared, response.Provenance!, epoch, work.Token, initial);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (state.Gate)
            {
                state.IsValid = true;
                state.Revision = committed.Acceptance.Revision;
                state.LastSuccess = now;
                state.FailureCode = null;
                if (committed.Changed)
                {
                    state.LastChange = now;
                    state.NotificationFailureCode = null;
                }
            }
            MarkCentralReconciled(state, epoch, reconciliationEpoch);
            if (committed.Changed)
            {
                ConfigurationEvents.Publish(entry.Instance, committed.Acceptance.Revision, _ => RecordCentralNotificationFailure(state));
            }
            return true;
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            RecordCentralFailure(state, "Configuration.SessionChanged", null, epoch);
            return false;
        }
        catch (CoreConfigurationException error)
        {
            string code = error.Issues.FirstOrDefault()?.Code ?? "Configuration.Refresh";
            RecordCentralFailure(state, code, identified, epoch);
            if (!initial && identified is not null)
            {
                MarkCentralReconciled(state, epoch, reconciliationEpoch);
            }
            return false;
        }
        catch (Exception)
        {
            RecordCentralFailure(state, "Configuration.SnapshotRead", identified, epoch);
            if (!initial && identified is not null)
            {
                MarkCentralReconciled(state, epoch, reconciliationEpoch);
            }
            return false;
        }
        finally
        {
            prepared?.Ownership?.Dispose();
        }
    }

    private static string? ObserveDesired(ConfigurationEntry entry, ConfigurationDesiredIdentity desired,
        ConfigurationSnapshotProvenance? provenance, long epoch)
    {
        lock (entry.ValueGate)
        {
            if (entry.SessionEpoch != epoch || entry.SnapshotTarget != desired.Target || entry.SnapshotAdmission?.IsClosed == true)
            {
                return "Configuration.SessionChanged";
            }
            long generation = long.Parse(desired.AssignmentGeneration, CultureInfo.InvariantCulture);
            if (generation < entry.HighestGeneration)
            {
                return "Configuration.StaleGeneration";
            }
            if (generation == entry.HighestGeneration && entry.ObservedDesired is { } previous
                && (previous != desired || provenance is not null && entry.ObservedProvenance is { } known && known != provenance))
            {
                return "Configuration.EqualGenerationConflict";
            }
            if (generation > entry.HighestGeneration)
            {
                entry.ObservedProvenance = null;
            }
            entry.HighestGeneration = generation;
            entry.ObservedDesired = desired;
            if (provenance is not null)
            {
                entry.ObservedProvenance = provenance;
            }
            return null;
        }
    }

    private static void RecordCentralFailure(EntryState state, string code, ConfigurationDesiredIdentity? identified, long epoch)
    {
        ConfigurationEntry entry = state.Entry;
        lock (entry.ValueGate)
        {
            if (entry.SessionEpoch != epoch)
            {
                return;
            }
            ConfigurationAcceptanceStatus? previous = entry.Acceptance;
            if (entry.Disabled)
            {
                entry.Acceptance = new(ConfigurationAcceptanceOutcome.Faulted, identified ?? previous?.Desired,
                    previous?.LastAccepted, entry.Revision, false, "Configuration.CommitDisabled",
                    previous?.NotificationFailureCode);
            }
            else if (identified is not null && entry.ObservedDesired == identified)
            {
                entry.Acceptance = new(ConfigurationAcceptanceOutcome.Rejected, identified, previous?.LastAccepted,
                    entry.Revision, previous?.HasIntactAcceptedState == true, code, previous?.NotificationFailureCode);
            }
            // Unidentified poll failure preserves desired/applied facts exactly.
            lock (state.Gate)
            {
                state.IsValid = false;
                state.Disabled = entry.Disabled;
                state.FailureCode = entry.Disabled ? "Configuration.CommitDisabled" : code;
            }
        }
    }

    private void MarkCentralReconciled(EntryState state, long epoch, long reconciliationEpoch)
    {
        lock (centralGate)
        {
            if (epoch == sessionEpoch && HasCurrentIntactAcceptance(state))
            {
                state.ReconciledEpoch = Math.Max(state.ReconciledEpoch, reconciliationEpoch);
            }
        }
    }

    private static void RecordCentralNotificationFailure(EntryState state)
    {
        lock (state.Entry.ValueGate)
        {
            ConfigurationAcceptanceStatus current = state.Entry.Acceptance!;
            state.Entry.Acceptance = new(current.Outcome, current.Desired, current.LastAccepted, current.Revision,
                current.HasIntactAcceptedState, current.FailureCode, "Configuration.Notification");
        }
        lock (state.Gate)
        {
            state.NotificationFailureCode = "Configuration.Notification";
        }
    }

    private async Task<bool> DrainCentralAsync()
    {
        Task<bool>? rebinding;
        Task<bool>? reconciliation;
        lock (centralGate)
        {
            rebinding = centralRebinding;
            reconciliation = centralReconciliation;
        }
        await ObserveAsync(rebinding).ConfigureAwait(false);
        await ObserveAsync(reconciliation).ConfigureAwait(false);
        bool succeeded = true;
        try
        {
            await centralCancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception) { succeeded = false; }
        centralCancellation.Dispose();
        foreach (EntryState state in entries)
        {
            state.Snapshot?.Dispose();
            state.Snapshot = null;
        }
        return succeeded;
    }
}
