namespace CIS.Messaging;

// One process-wide module budget. Instances own admission and drain, never private copies of capacity.
internal sealed class MessagingResourceBudget : IMessageBufferAdmission, IAsyncDisposable
{
    private static readonly object SharedGate = new();
    private static SharedState? shared;
    private readonly SharedState state;
    private readonly TaskCompletionSource drained = NewSignal();
    private bool closing;
    private int ownedLeases;
    private int ownedWaiters;

    internal MessagingResourceBudget(MessagingSettings settings, bool reserveReplyIngress = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        int replyBytes = reserveReplyIngress ? checked(4 * settings.Limits.MaximumEnvelopeBytes) : 0;
        if (reserveReplyIngress && (settings.MaximumConcurrentDeliveries < 2 || settings.MaximumEncodedBufferBytes < 3L * replyBytes))
        {
            throw new ArgumentException("RPC requires two work slots and room for incoming, response and reserved reply buffers.", nameof(settings));
        }
        var profile = new Profile(settings.MaximumConcurrentDeliveries, settings.MaximumOutstandingRequests,
            settings.PublisherConcurrency, settings.MaximumEncodedBufferBytes, replyBytes);
        lock (SharedGate)
        {
            if (shared is not null && shared.Profile != profile)
            {
                throw new InvalidOperationException("Active messaging runtimes must share the same process resource limits.");
            }
            state = shared ??= new(profile);
            state.Owners = checked(state.Owners + 1);
        }
    }

    internal bool TryAcquireDelivery(out IDisposable? lease) => TryAcquire(Kind.Delivery, 0, out lease);
    internal bool TryAcquireRequest(out IDisposable? lease) => TryAcquire(Kind.Request, 0, out lease);
    internal bool TryAcquirePublication(out IDisposable? lease) => TryAcquire(Kind.Publication, 0, out lease);
    internal bool ReplyIngressEnabled => state.Profile.ReplyBytes != 0;

    internal ValueTask<IDisposable> AcquireDeliveryAsync(CancellationToken cancellationToken = default)
        => AcquireAsync(Kind.Delivery, 0, cancellationToken);

    internal ValueTask<IDisposable> AcquireReplyDeliveryAsync(CancellationToken cancellationToken = default)
    {
        if (!ReplyIngressEnabled)
        {
            throw new InvalidOperationException("This captured process profile has no reserved reply lane.");
        }
        return AcquireAsync(Kind.ReplyDelivery, 0, cancellationToken);
    }

    internal ValueTask<IDisposable> AcquireReplyBytesAsync(int bytes, CancellationToken cancellationToken = default)
    {
        if (bytes < 1 || bytes > state.Profile.ReplyBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }
        return AcquireAsync(Kind.ReplyBytes, bytes, cancellationToken);
    }

    internal bool TryAcquireBytes(int bytes, out IDisposable? lease)
    {
        ValidateBytes(bytes);
        return TryAcquire(Kind.Bytes, bytes, out lease);
    }

    public ValueTask<IDisposable> AcquireAsync(int bytes, CancellationToken cancellationToken = default)
    {
        ValidateBytes(bytes);
        return AcquireAsync(Kind.Bytes, bytes, cancellationToken);
    }

    private ValueTask<IDisposable> AcquireAsync(Kind kind, int bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (SharedGate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            IDisposable? lease = TakeIfAvailable(kind, bytes);
            if (lease is not null)
            {
                return ValueTask.FromResult(lease);
            }
            if (state.Waiters >= state.WaiterCapacity)
            {
                throw new MessagingAdmissionCapacityException();
            }
            state.Waiters++;
            ownedWaiters++;
        }
        return WaitAsync(kind, bytes, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (SharedGate)
        {
            if (!closing)
            {
                closing = true;
                state.Owners--;
                Pulse();
                Settle();
            }
            return new(drained.Task);
        }
    }

    private bool TryAcquire(Kind kind, int bytes, out IDisposable? lease)
    {
        lock (SharedGate)
        {
            lease = closing ? null : TakeIfAvailable(kind, bytes);
            return lease is not null;
        }
    }

    private IDisposable? TakeIfAvailable(Kind kind, int bytes)
    {
        int index = (int)kind;
        int maximum = kind switch
        {
            Kind.Delivery => state.Profile.Deliveries - (ReplyIngressEnabled ? 1 : 0),
            Kind.Request => state.Profile.Requests,
            Kind.Publication => state.Profile.Publications,
            Kind.ReplyDelivery => ReplyIngressEnabled ? 1 : 0,
            _ => state.BufferLeaseCapacity
        };
        if (state.Used[index] >= maximum || state.Bytes + bytes > state.Profile.Bytes
            || kind == Kind.Bytes && state.NormalBytes + bytes > state.Profile.Bytes - state.Profile.ReplyBytes
            || kind == Kind.ReplyBytes && state.Bytes - state.NormalBytes + bytes > state.Profile.ReplyBytes)
        {
            return null;
        }
        state.Used[index]++;
        state.Bytes += bytes;
        if (kind == Kind.Bytes)
        {
            state.NormalBytes += bytes;
        }
        state.ActiveLeases++;
        ownedLeases++;
        return new Lease(this, kind, bytes);
    }

    private async ValueTask<IDisposable> WaitAsync(Kind kind, int bytes, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task changed;
                lock (SharedGate)
                {
                    ObjectDisposedException.ThrowIf(closing, this);
                    IDisposable? lease = TakeIfAvailable(kind, bytes);
                    if (lease is not null)
                    {
                        return lease;
                    }
                    changed = state.Changed.Task;
                }
                await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (SharedGate)
            {
                state.Waiters--;
                ownedWaiters--;
                Settle();
            }
        }
    }

    private void Release(Kind kind, int bytes)
    {
        lock (SharedGate)
        {
            state.Used[(int)kind]--;
            state.Bytes -= bytes;
            if (kind == Kind.Bytes)
            {
                state.NormalBytes -= bytes;
            }
            state.ActiveLeases--;
            ownedLeases--;
            Pulse();
            Settle();
        }
    }

    private void Pulse()
    {
        TaskCompletionSource previous = state.Changed;
        state.Changed = NewSignal();
        previous.TrySetResult();
    }

    private void Settle()
    {
        if (closing && ownedLeases == 0 && ownedWaiters == 0)
        {
            drained.TrySetResult();
        }
        if (state.Owners == 0 && state.ActiveLeases == 0 && state.Waiters == 0 && ReferenceEquals(shared, state))
        {
            shared = null;
        }
    }

    private void ValidateBytes(int bytes)
    {
        if (bytes < 1 || bytes > state.Profile.Bytes - state.Profile.ReplyBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "Reserve a positive byte count within the shared process budget.");
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private enum Kind { Delivery, Request, Publication, Bytes, ReplyDelivery, ReplyBytes }
    private sealed record Profile(int Deliveries, int Requests, int Publications, long Bytes, int ReplyBytes);

    private sealed class SharedState(Profile profile)
    {
        internal Profile Profile { get; } = profile;
        internal int WaiterCapacity { get; } = Math.Max(128, checked(profile.Deliveries + profile.Requests + profile.Publications));
        internal int BufferLeaseCapacity { get; } = checked((profile.Deliveries + profile.Requests + profile.Publications) * 4);
        internal int[] Used { get; } = new int[6];
        internal TaskCompletionSource Changed { get; set; } = NewSignal();
        internal int Owners;
        internal int ActiveLeases;
        internal int Waiters;
        internal long Bytes;
        internal long NormalBytes;
    }

    private sealed class Lease(MessagingResourceBudget owner, Kind kind, int bytes) : IDisposable
    {
        private MessagingResourceBudget? owner = owner;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release(kind, bytes);
    }
}

internal sealed class MessagingAdmissionCapacityException() : InvalidOperationException("The bounded messaging admission wait capacity is exhausted.");
