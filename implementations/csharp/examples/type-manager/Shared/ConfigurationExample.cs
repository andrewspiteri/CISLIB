using System.Runtime.CompilerServices;
using System.Text.Json;
using CIS.Configuration;
using CIS.Startup;
using CIS.TypeManagement.Examples.Unused;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.TypeManagement.Examples;

// Strong references to Unused belong only to these post-preparation methods.
// The top-level host can check initial loading without its JIT binding those types.
internal static class ConfigurationExample
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void AddConsumers(IServiceCollection services, LiteralConfigurationStore store)
    {
        services.AddSingleton(store);
        services.AddScoped<ConfigurationExampleConsumer>();
        services.AddSingleton<ConfigurationExampleStartable>();
        services.AddSingleton<IStartable>(provider => provider.GetRequiredService<ConfigurationExampleStartable>());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<object> VerifyAndRefreshAsync(IServiceProvider root, ITypeManager manager, LiteralConfigurationStore store)
    {
        using IServiceScope first = root.CreateScope();
        using IServiceScope second = root.CreateScope();
        var left = first.ServiceProvider.GetRequiredService<ConfigurationExampleConsumer>();
        var right = second.ServiceProvider.GetRequiredService<ConfigurationExampleConsumer>();
        var startable = root.GetRequiredService<ConfigurationExampleStartable>();
        var reader = root.GetRequiredService<IConfigurationReader>();
        var direct = root.GetRequiredService<DirectConfiguration>();
        var inherited = root.GetRequiredService<InheritedConfiguration>();
        bool sameRoots = !ReferenceEquals(left, right) && ReferenceEquals(left.Direct, right.Direct)
            && ReferenceEquals(direct, left.Direct) && ReferenceEquals(inherited, left.Inherited)
            && ReferenceEquals(left.Inherited, right.Inherited);
        if (!sameRoots || left.ConstructorGreeting != "configured-before-construction" || left.ConstructorBatchSize != 42
            || startable.ConstructorGreeting != "configured-before-construction" || startable.ConstructorBatchSize != 42
            || !startable.Started || startable.WatchActiveInConstructor || !startable.ObservedWatch || !store.WatchActivated)
        { throw new InvalidOperationException("Configuration must be loaded before constructors and activated before the next startable."); }
        IReadOnlyList<Type> discovered = manager.GetImplementations<IConfigurationObject>();
        if (discovered.Count != 2 || !discovered.Contains(typeof(DirectConfiguration)) || !discovered.Contains(typeof(InheritedConfiguration)))
        { throw new InvalidOperationException("The real complete catalog must discover the direct and inherited passive configurations."); }

        var directChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inheritedChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int changes = 0;
        EventHandler<ConfigurationChangedEventArgs> directHandler = (_, _) =>
        { Interlocked.Increment(ref changes); directChanged.TrySetResult(); };
        EventHandler<ConfigurationChangedEventArgs> inheritedHandler = (_, _) =>
        { Interlocked.Increment(ref changes); inheritedChanged.TrySetResult(); };
        direct.ConfigurationChanged += directHandler;
        inherited.ConfigurationChanged += inheritedHandler;
        try
        {
            store.Refresh();
            await Task.WhenAll(directChanged.Task, inheritedChanged.Task).WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            direct.ConfigurationChanged -= directHandler;
            inherited.ConfigurationChanged -= inheritedHandler;
        }
        string greeting = reader.Read(direct, value => value.Greeting);
        int batch = reader.Read(inherited, value => value.BatchSize);
        bool retained = ReferenceEquals(direct, second.ServiceProvider.GetRequiredService<DirectConfiguration>())
            && ReferenceEquals(inherited, first.ServiceProvider.GetRequiredService<InheritedConfiguration>());
        if (!retained || greeting != "refreshed-after-startup" || batch != 84 || changes != 2)
        { throw new InvalidOperationException("Watch refresh must update the same concrete roots and their original subscriptions once."); }
        return new
        {
            constructorGreeting = left.ConstructorGreeting,
            constructorBatchSize = left.ConstructorBatchSize,
            rootAndScopesShareConfiguration = sameRoots,
            startupReaderObservedConfiguration = startable.Started,
            startupReaderObservedWatch = startable.ObservedWatch,
            watchActiveInConstructor = startable.WatchActiveInConstructor,
            sameRootsAfterRefresh = retained,
            currentGreeting = greeting,
            currentBatchSize = batch,
            changedEvents = changes,
            watchActivated = store.WatchActivated,
            discovered = discovered.Select(type => new { type = type.FullName, assembly = type.Assembly.FullName })
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static object Capture(IServiceProvider services)
    {
        var reader = services.GetRequiredService<IConfigurationReader>();
        return new
        {
            greeting = reader.Read(services.GetRequiredService<DirectConfiguration>(), value => value.Greeting),
            batchSize = reader.Read(services.GetRequiredService<InheritedConfiguration>(), value => value.BatchSize)
        };
    }
}

internal sealed class ConfigurationExampleConsumer
{
    public ConfigurationExampleConsumer(DirectConfiguration direct, InheritedConfiguration inherited, IConfigurationReader reader)
    {
        Direct = direct;
        Inherited = inherited;
        ConstructorGreeting = reader.Read(direct, value => value.Greeting);
        ConstructorBatchSize = reader.Read(inherited, value => value.BatchSize);
    }

    internal DirectConfiguration Direct { get; }
    internal InheritedConfiguration Inherited { get; }
    internal string ConstructorGreeting { get; }
    internal int ConstructorBatchSize { get; }
}

internal sealed class ConfigurationExampleStartable(DirectConfiguration direct, InheritedConfiguration inherited,
    IConfigurationReader reader, LiteralConfigurationStore store) : IStartable
{
    internal string ConstructorGreeting { get; } = reader.Read(direct, value => value.Greeting);
    internal int ConstructorBatchSize { get; } = reader.Read(inherited, value => value.BatchSize);
    internal bool WatchActiveInConstructor { get; } = store.WatchActivated;
    internal bool ObservedWatch { get; private set; }
    internal bool Started { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!store.WatchActivated || reader.Read(direct, value => value.Greeting) != "configured-before-construction"
            || reader.Read(inherited, value => value.BatchSize) != 42)
        { throw new InvalidOperationException("The dependent startable must observe directly reconciled configuration."); }
        ObservedWatch = store.WatchActivated;
        Started = true;
        return Task.CompletedTask;
    }
}

// A finite literal demonstration adapter, not a production backend or a polling default.
internal sealed class LiteralConfigurationStore(string host) : IConfigurationStore, IAsyncDisposable
{
    private const string Direct = "CIS.TypeManagement.Examples.Unused.DirectConfiguration, CIS.TypeManager.Examples.Unused, Culture=neutral, PublicKeyToken=null";
    private const string Inherited = "CIS.TypeManagement.Examples.Unused.InheritedConfiguration, CIS.TypeManager.Examples.Unused, Culture=neutral, PublicKeyToken=null";
    private readonly object gate = new();
    private Action<ConfigurationInvalidation>? invalidated;
    private int revision, disposals;
    private bool watchActivated, watchDisposed;
    public ConfigurationNotificationMode NotificationMode => ConfigurationNotificationMode.Reliable;
    internal bool WatchActivated { get { lock (gate) { return watchActivated; } } }
    internal bool WatchDisposed { get { lock (gate) { return watchDisposed; } } }
    internal int Disposals => Volatile.Read(ref disposals);

    public ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? sourceToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            string token = key.Scope == host ? "application-" + revision : "common-0";
            if (sourceToken == token) { return ValueTask.FromResult(ConfigurationReadResult.Unchanged()); }
            if (key.Scope != host) { return ValueTask.FromResult(ConfigurationReadResult.Missing(token)); }
            JsonElement payload;
            if (key.Definition == Direct)
            { payload = JsonSerializer.SerializeToElement(new { Greeting = revision == 0 ? "configured-before-construction" : "refreshed-after-startup" }); }
            else if (key.Definition == Inherited) { payload = JsonSerializer.SerializeToElement(new { BatchSize = revision == 0 ? 42 : 84 }); }
            else { return ValueTask.FromResult(ConfigurationReadResult.Missing(token)); }
            return ValueTask.FromResult(ConfigurationReadResult.Found(payload, token));
        }
    }

    public ValueTask<IAsyncDisposable> WatchAsync(IReadOnlyList<ConfigurationRecordKey> keys,
        Action<ConfigurationInvalidation> callback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (watchActivated || keys.Count != 4) { throw new InvalidOperationException("Establish exactly one watch over both scopes of both discovered definitions."); }
            invalidated = callback;
            watchActivated = true;
        }
        return ValueTask.FromResult<IAsyncDisposable>(new Subscription(this));
    }

    internal void Refresh()
    {
        Action<ConfigurationInvalidation> callback;
        lock (gate) { revision++; callback = invalidated ?? throw new InvalidOperationException("Activate observation before refreshing."); }
        callback(new ConfigurationInvalidation(host));
    }

    private sealed class Subscription(LiteralConfigurationStore store) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            lock (store.gate) { store.invalidated = null; store.watchDisposed = true; }
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask DisposeAsync() { Interlocked.Increment(ref disposals); return ValueTask.CompletedTask; }
}
