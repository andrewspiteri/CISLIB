namespace CIS.Coordination;

public sealed partial class CoordinationRuntime
{
    private async Task<PhysicalReply<T>> RequestAsync<T>(Entry entry, CoordinationRuntimeOperation operation,
        Func<CancellationToken, Task<T>> invoke, TimeSpan maximumWait, CancellationToken caller)
        where T : class
    {
        long started = time.GetTimestamp();
        if (maximumWait <= TimeSpan.Zero)
        {
            return new(null, started, false);
        }

        lock (gate)
        {
            bool renewal = operation == CoordinationRuntimeOperation.Renew;
            if ((renewal ? renewalRequests : acquisitionRequests)
                >= (renewal ? options.MaximumConcurrentRenewals : options.MaximumConcurrentAcquisitions))
            {
                return new(null, started, true);
            }

            entry.Requests++;
            if (renewal)
            {
                renewalRequests++;
                entry.RenewalRequests++;
            }
            else
            {
                acquisitionRequests++;
            }
        }

        using var waitCancelled = operation is CoordinationRuntimeOperation.Acquire or CoordinationRuntimeOperation.Resolve
            ? CancellationTokenSource.CreateLinkedTokenSource(caller, admissionClosed.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(caller);
        var timeout = new CancellationTokenSource(maximumWait, time);
        var physicalCancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, timeout.Token);
        using var delayCancelled = new CancellationTokenSource();
        using var localWait = CancellationTokenSource.CreateLinkedTokenSource(waitCancelled.Token, delayCancelled.Token);
        Task delay = Task.Delay(maximumWait, time, localWait.Token);
        // A synchronous provider implementation cannot occupy the scheduler or the scoped caller's continuation.
        var timing = new PhysicalTiming();
        Task<T> actual = Task.Run(async () =>
        {
            timing.Observer = CaptureOperationObserver();
            long invoked = time.GetTimestamp();
            Task<T>? providerCall = null;
            try
            {
                providerCall = invoke(physicalCancellation.Token);
                return await providerCall.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timing.Cancelled = providerCall?.IsCanceled == true || physicalCancellation.IsCancellationRequested;
                throw;
            }
            finally
            {
                try
                {
                    timing.Elapsed = time.GetElapsedTime(invoked, time.GetTimestamp());
                    timing.HasDuration = true;
                }
                catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error))
                {
                    // Missing observation time is omitted, never a fabricated zero or replacement provider result.
                }
            }
        }, CancellationToken.None);
        _ = Task.Run(() => ObservePhysicalAsync(entry, operation, actual, timing, physicalCancellation, timeout), CancellationToken.None);
        Task winner = await Task.WhenAny(actual, delay).ConfigureAwait(false);
        if (winner == actual)
        {
            delayCancelled.Cancel();
            try
            {
                return new(await actual.ConfigureAwait(false), started, false);
            }
            catch
            {
                return new(null, started, false);
            }
        }

        return new(null, started, false);
    }

    private async Task ObservePhysicalAsync<T>(Entry entry, CoordinationRuntimeOperation operation, Task<T> actual,
        PhysicalTiming timing, CancellationTokenSource physicalCancellation, CancellationTokenSource timeout)
        where T : class
    {
        T? result = null;
        CoordinationRuntimeOperationOutcome outcome = CoordinationRuntimeOperationOutcome.Faulted;
        try
        {
            result = await actual.ConfigureAwait(false);
            outcome = Outcome(result);
        }
        catch (OperationCanceledException)
        {
            outcome = timing.Cancelled ? CoordinationRuntimeOperationOutcome.Cancelled : CoordinationRuntimeOperationOutcome.Faulted;
        }
        catch
        {
            // The original physical task is observed even after the local request stopped waiting.
        }
        finally
        {
            physicalCancellation.Dispose();
            timeout.Dispose();
        }

        CoordinationGrant? orphan = null;
        lock (gate)
        {
            entry.Requests--;
            if (operation == CoordinationRuntimeOperation.Renew)
            {
                renewalRequests--;
                entry.RenewalRequests--;
                if (!entry.RenewalCoordinator)
                {
                    entry.RenewalInFlight = false;
                }
            }
            else
            {
                acquisitionRequests--;
            }

            authorityAvailable = outcome == CoordinationRuntimeOperationOutcome.Cancelled ? null
                : outcome is not (CoordinationRuntimeOperationOutcome.Faulted
                    or CoordinationRuntimeOperationOutcome.Unavailable or CoordinationRuntimeOperationOutcome.Indeterminate);
            entry.Notifications++;
            notifications++;
            CoordinationGrant? known = result switch
            {
                CoordinationAcquireResult { Outcome: CoordinationAcquireOutcome.Acquired } acquired => acquired.Grant,
                CoordinationObservation { Outcome: CoordinationObservationOutcome.FoundLive } observed => observed.Grant,
                _ => null
            };
            if (known is not null && IsExact(entry, known))
            {
                entry.KnownGrant = known;
                if (!entry.Active && entry.Lease is null && !entry.CleanupScheduled)
                {
                    entry.Coordinators++;
                    orphan = known;
                }
            }

            RemoveSettledLocked(entry);
        }

        if (timing.HasDuration)
        {
            ObserveOperation(timing.Observer, operation, outcome, timing.Elapsed);
        }
        EndNotification(entry);
        if (orphan is not null)
        {
            try
            {
                await CleanupKnownGrantAsync(entry, orphan).ConfigureAwait(false);
            }
            finally
            {
                EndCoordinator(entry);
            }
        }
    }

    private async Task CleanupKnownGrantAsync(Entry entry, CoordinationGrant grant)
    {
        lock (gate)
        {
            if (entry.CleanupScheduled || !IsExact(entry, grant))
            {
                return;
            }

            entry.CleanupScheduled = true;
        }

        await RequestAsync(entry, CoordinationRuntimeOperation.Release,
            token => provider.ReleaseAsync(grant, token), options.RequestTimeout, CancellationToken.None).ConfigureAwait(false);
    }

    private bool IsExact(Entry entry, CoordinationGrant grant) => grant.AuthorityId == provider.AuthorityId
        && grant.Acquisition == entry.Acquisition;

    private static CoordinationRuntimeOperationOutcome Outcome<T>(T result) => result switch
    {
        CoordinationAcquireResult acquired => acquired.Outcome switch
        {
            CoordinationAcquireOutcome.Acquired => CoordinationRuntimeOperationOutcome.Succeeded,
            CoordinationAcquireOutcome.Busy => CoordinationRuntimeOperationOutcome.Busy,
            CoordinationAcquireOutcome.Unavailable => CoordinationRuntimeOperationOutcome.Unavailable,
            CoordinationAcquireOutcome.Indeterminate => CoordinationRuntimeOperationOutcome.Indeterminate,
            _ => CoordinationRuntimeOperationOutcome.Refused
        },
        CoordinationMutationResult mutation => mutation.Outcome switch
        {
            CoordinationMutationOutcome.Succeeded => CoordinationRuntimeOperationOutcome.Succeeded,
            CoordinationMutationOutcome.NotCurrent => CoordinationRuntimeOperationOutcome.Refused,
            CoordinationMutationOutcome.Unavailable => CoordinationRuntimeOperationOutcome.Unavailable,
            _ => CoordinationRuntimeOperationOutcome.Indeterminate
        },
        CoordinationObservation observation => observation.Outcome switch
        {
            CoordinationObservationOutcome.FoundLive => CoordinationRuntimeOperationOutcome.Succeeded,
            CoordinationObservationOutcome.NotObserved => CoordinationRuntimeOperationOutcome.NotObserved,
            _ => CoordinationRuntimeOperationOutcome.Unavailable
        },
        _ => CoordinationRuntimeOperationOutcome.Faulted
    };

    private ICoordinationRuntimeObserver? CaptureOperationObserver()
    {
        ICoordinationRuntimeObserver? target;
        lock (gate)
        {
            target = observer;
        }

        try
        {
            return target?.CaptureOperationObserver();
        }
        catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error))
        {
            return null;
        }
    }

    private static void ObserveOperation(ICoordinationRuntimeObserver? target, CoordinationRuntimeOperation operation,
        CoordinationRuntimeOperationOutcome outcome, TimeSpan elapsed)
    {
        try
        {
            target?.OperationCompleted(operation, outcome, elapsed);
        }
        catch
        {
            // Optional trusted telemetry does not change authority or business results.
        }
    }

    private void EndCoordinator(Entry entry)
    {
        lock (gate)
        {
            entry.Coordinators--;
            RemoveSettledLocked(entry);
        }
    }

    private readonly record struct PhysicalReply<T>(T? Result, long Started, bool CapacityExceeded) where T : class;

    private sealed class PhysicalTiming
    {
        internal TimeSpan Elapsed;
        internal bool Cancelled;
        internal bool HasDuration;
        internal ICoordinationRuntimeObserver? Observer;
    }
}
