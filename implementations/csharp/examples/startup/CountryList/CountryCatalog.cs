using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Healthcheck;
using CIS.Startup;
using CIS.TypeManagement;

namespace CIS.Startup.Examples.CountryList;

internal sealed record Country(string Code, string Name);

// Constructors capture dependencies. Required asynchronous work belongs in StartAsync.
internal sealed class CountryCatalog(CountrySource source, ITypeManager typeManager,
    HeartbeatOwner owner, IAvailabilityMonitor availability) : IStartable, IHeartbeatSubscriber
{
    private IReadOnlyList<Country> countries = Array.Empty<Country>();
    private readonly TaskCompletionSource firstRefreshCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int refreshCount;

    internal ITypeManager TypeManager => typeManager;
    internal IReadOnlyList<Country> Countries => Volatile.Read(ref countries);
    internal int RefreshCount => Volatile.Read(ref refreshCount);
    internal Task FirstRefreshCompleted => firstRefreshCompleted.Task;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Country> primed = await source.ReadPrimeAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref countries, primed);
        availability.ReportPublished();

        // This is required setup, but registration stays inert until the entire
        // sequential startup operation succeeds and activates Heartbeat.
        owner.Service.Register(this, new HeartbeatRegistrationOptions
        {
            Name = "Country list refresh",
            Interval = source.RefreshInterval
        }, new HeartbeatHealthcheckOptions
        {
            Monitor = availability,
            SuccessPublishesUsableData = true
        });
    }

    public async Task<bool> OnElapsedAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Country> refreshed = await source.ReadRefreshAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref countries, refreshed);
        Interlocked.Increment(ref refreshCount);
        firstRefreshCompleted.TrySetResult();
        return true;
    }

    // Healthcheck's existing availability policy records refresh failure and
    // keeps already published data usable. Real applications can await recovery here.
    public Task OnErrorAsync(Exception exception) => Task.CompletedTask;
}

internal sealed class CountryReader(CountryCatalog catalog, ITypeManager typeManager) : IStartable
{
    internal bool Started { get; private set; }
    internal bool UsesPreparedTypeManager { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UsesPreparedTypeManager = ReferenceEquals(catalog.TypeManager, typeManager)
            && typeManager.GetDefinedTypes(typeof(CountryReader).Assembly).Contains(typeof(CountryCatalog));
        if (!UsesPreparedTypeManager || catalog.Countries.Count == 0)
        {
            throw new InvalidOperationException("CountryReader requires the prepared catalog and completed country priming.");
        }
        Started = true;
        return Task.CompletedTask;
    }

    internal IReadOnlyList<Country> ReadCountries()
        => Started ? catalog.Countries : throw new InvalidOperationException("CountryReader has not started.");
}

// A small local asynchronous source keeps the example runnable without an
// external API. The self-test holds the same real startup and refresh methods.
internal sealed class CountrySource
{
    private static readonly IReadOnlyList<Country> Data = Array.AsReadOnly(new[]
    {
        new Country("MT", "Malta"), new Country("IT", "Italy"), new Country("FR", "France")
    });
    private readonly TaskCompletionSource primeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource primeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource refreshEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource refreshRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int refreshReads;

    internal CountrySource(bool holdForSelfTest)
    {
        RefreshInterval = holdForSelfTest ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromSeconds(15);
        if (!holdForSelfTest) { ReleaseAll(); }
    }

    internal TimeSpan RefreshInterval { get; }
    internal Task PrimeEntered => primeEntered.Task;
    internal Task FirstRefreshEntered => refreshEntered.Task;
    internal void ReleasePrime() => primeRelease.TrySetResult();
    internal void ReleaseRefresh() => refreshRelease.TrySetResult();
    internal void ReleaseAll() { ReleasePrime(); ReleaseRefresh(); }

    internal async Task<IReadOnlyList<Country>> ReadPrimeAsync(CancellationToken cancellationToken)
    {
        primeEntered.TrySetResult();
        await primeRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return Data;
    }

    internal async Task<IReadOnlyList<Country>> ReadRefreshAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref refreshReads) == 1)
        {
            refreshEntered.TrySetResult();
            await refreshRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return Data;
    }
}
