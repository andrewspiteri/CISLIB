using CIS.Healthcheck;

namespace CIS.Healthcheck.Examples.CountryList;

// The application owns this component and its source. Healthcheck may only use
// the source while an actual lease is held; weak registration is not a lease.
internal sealed class CountryCatalog(CountrySource source) : IHealthCollector, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string[] countries = [];
    private int leases;
    private bool closing;
    private Task? disposal;

    internal IReadOnlyList<string> Countries => Array.AsReadOnly(Volatile.Read(ref countries));
    internal CountrySource Source => source;

    internal async Task PrimeAsync(CancellationToken cancellation)
    {
        string[] replacement = await source.FetchAsync(isPrime: true, cancellation).ConfigureAwait(false);
        Publish(replacement);
    }

    public bool TryAcquire(out IHealthCollectionLease? lease)
    {
        lock (gate)
        {
            if (closing) { lease = null; return false; }
            leases++;
            lease = new Lease(this);
            return true;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closing = true;
            if (leases == 0) { drained.TrySetResult(); }
            disposal ??= DisposeAfterDrainAsync();
            return new(disposal);
        }
    }

    private async Task DisposeAfterDrainAsync()
    {
        await drained.Task.ConfigureAwait(false);
        await source.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<HealthCollectionResult> RefreshAsync(CancellationToken cancellation)
    {
        string[] replacement = await source.FetchAsync(isPrime: false, cancellation).ConfigureAwait(false);
        Publish(replacement);
        return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
    }

    private void Publish(string[] replacement)
    {
        if (replacement.Length == 0 || replacement.Any(string.IsNullOrWhiteSpace))
        { throw new InvalidOperationException("The fetched country list is unusable."); }
        // Replace only after successful fetching and validation. A failed refresh
        // leaves the previous usable array and its availability fact intact.
        Volatile.Write(ref countries, (string[])replacement.Clone());
    }

    private void Release()
    {
        lock (gate)
        {
            leases--;
            if (closing && leases == 0) { drained.TrySetResult(); }
        }
    }

    private sealed class Lease(CountryCatalog owner) : IHealthCollectionLease
    {
        private int released;

        public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation) =>
            owner.RefreshAsync(lifetimeCancellation);

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) { owner.Release(); }
            return ValueTask.CompletedTask;
        }
    }
}

// A deterministic stand-in for application I/O. Replace this dependency with the
// real data client in an application, keeping the same lease/disposal protocol.
internal sealed class CountrySource(bool failPrime) : IAsyncDisposable
{
    private RefreshBlock? primeBlock;
    private RefreshBlock? nextBlock;
    private int disposed;

    internal bool FailRefresh { get; set; }
    internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

    internal RefreshBlock BlockPrime()
    {
        var block = new RefreshBlock();
        if (Interlocked.CompareExchange(ref primeBlock, block, null) is not null)
        { throw new InvalidOperationException("Priming is already blocked."); }
        return block;
    }

    internal RefreshBlock BlockNextRefresh()
    {
        var block = new RefreshBlock();
        if (Interlocked.CompareExchange(ref nextBlock, block, null) is not null)
        { throw new InvalidOperationException("A refresh is already blocked."); }
        return block;
    }

    internal async Task<string[]> FetchAsync(bool isPrime, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        RefreshBlock? block = isPrime ? Interlocked.Exchange(ref primeBlock, null) : Interlocked.Exchange(ref nextBlock, null);
        if (block is not null)
        {
            block.Entered.TrySetResult();
            // The finite self-test releases this explicitly. Cancellation alone
            // cannot be treated as actual completion of noncooperative I/O.
            await block.Release.Task.ConfigureAwait(false);
        }
        await Task.Yield();
        cancellation.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if ((isPrime && failPrime) || (!isPrime && FailRefresh))
        { throw new InvalidOperationException(isPrime ? "Initial country fetch failed." : "Country refresh failed."); }
        return ["Malta", "France", "Japan"];
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref disposed, 1);
        return ValueTask.CompletedTask;
    }
}

internal sealed class RefreshBlock
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
