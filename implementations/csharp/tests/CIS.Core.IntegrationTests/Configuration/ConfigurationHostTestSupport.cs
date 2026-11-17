using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using CIS.Configuration;
using CIS.Startup;
using CIS.TestSupport;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Configuration;

internal static class ConfigurationHostTestSupport
{
    internal const string HostName = "ConfigurationIntegrationHost";
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static async Task<ITypeManager> ManagerAsync(TemporaryDirectory directory)
    {
        Assembly host = typeof(ConfigurationHostTestSupport).Assembly;
        File.Copy(host.Location, Path.Combine(directory.Path, "Host.dll"));
        string manifest = Path.Combine(directory.Path, "host.types.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            hostAssembly = host.FullName,
            assemblies = new[] { new { assemblyName = host.FullName, relativePath = "Host.dll" } }
        }));
        using var cancellation = new CancellationTokenSource(Deadline);
        return await TypeManager.CreateAsync(new TypeManagerOptions(host)
        { AssemblyManifestPath = manifest, PrecomputedContracts = [typeof(IConfigurationObject)] }, cancellation.Token);
    }

    internal static ConfigurationOptions Options(ConfigurationHostStore store, bool? critical = true, TimeSpan? interval = null)
        => new(HostName, store) { IsHealthCritical = critical, PollingInterval = interval };

    // Expected persisted identity for these ordinary unsigned, neutral, nongeneric types.
    // No production identity helper or post-query filtering is used by this fixture.
    internal static string Definition<T>()
        => typeof(T).FullName + ", CIS.Core.IntegrationTests, Culture=neutral, PublicKeyToken=null";

    internal static ConfigurationEntryRefreshStatus Entry(ConfigurationRuntime runtime)
        => runtime.CaptureStatus().Entries.Single(entry => entry.ConfigurationType == typeof(ConfigurationHostSettings));

    internal static async Task<Exception> FailureAsync(Task operation)
    {
        try { await operation.WaitAsync(Deadline); }
        catch (Exception error) when (operation.IsCompleted) { return error; }
        throw new AssertFailedException("The real operation unexpectedly succeeded.");
    }

    internal static async Task ObserveStopAsync(Task stop)
    {
        try { await stop.WaitAsync(Deadline); }
        catch (OperationCanceledException) when (stop.IsCanceled) { }
    }

    internal static async Task ObserveAsync(Func<bool> observation, string explanation)
        => Assert.IsTrue(await Task.Run(() => SpinWait.SpinUntil(observation, Deadline)).WaitAsync(Deadline), explanation);
}

// The real Type Manager sees every valid marker in this assembly. Negative marker
// fixtures belong to UnitTests and are never filtered out of this discovery path.
internal sealed class ConfigurationHostSettings : ConfigurationObject
{
    public ConfigurationHostSettings() { }
    public int Count { get; set; } = 3;
    public string Label { get; set; } = "default";
}

internal sealed class ConfigurationHostDirectSettings : IConfigurationObject
{
    public ConfigurationHostDirectSettings() { }
    public bool Enabled { get; set; } = true;
    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged
    {
        add => ConfigurationEvents.Add(this, value);
        remove => ConfigurationEvents.Remove(this, value);
    }
}

internal sealed class ConfigurationHostConsumer : IStartable
{
    private readonly IConfigurationReader reader;
    private int calls;
    internal ConfigurationHostSettings Settings { get; }
    internal ConfigurationHostDirectSettings Direct { get; }
    internal (int Count, bool Enabled) ConstructorValues { get; }
    internal (int Count, bool Enabled)? StartedValues { get; private set; }
    internal int Calls => Volatile.Read(ref calls);

    public ConfigurationHostConsumer(ConfigurationHostSettings settings, ConfigurationHostDirectSettings direct, IConfigurationReader reader)
    {
        Settings = settings;
        Direct = direct;
        this.reader = reader;
        ConstructorValues = (settings.Count, direct.Enabled);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        StartedValues = (reader.Read(Settings, value => value.Count), reader.Read(Direct, value => value.Enabled));
        return Task.CompletedTask;
    }
}

// A controlled bootstrap-capable adapter. It is deliberately not a production backend.
internal sealed class ConfigurationHostStore : IConfigurationStore, IAsyncDisposable
{
    internal readonly ConcurrentDictionary<ConfigurationRecordKey, ConfigurationReadResult> Records = new();
    internal readonly ConcurrentQueue<(ConfigurationRecordKey Key, string? Token)> Requests = new();
    internal Func<ConfigurationRecordKey, string?, CancellationToken, ValueTask<ConfigurationReadResult>>? Read;
    internal Func<CancellationToken, ValueTask<IAsyncDisposable>>? Watch;
    internal Func<ValueTask>? DisposeWatch;
    private Action<ConfigurationInvalidation>? invalidated;
    private int reads, watches, watchDisposals, disposals;
    internal int Reads => Volatile.Read(ref reads);
    internal int Watches => Volatile.Read(ref watches);
    internal int WatchDisposals => Volatile.Read(ref watchDisposals);
    internal int Disposals => Volatile.Read(ref disposals);
    internal TaskCompletionSource WatchEntered { get; } = ConfigurationHostTestSupport.Signal();
    internal TaskCompletionSource WatchDisposalEntered { get; } = ConfigurationHostTestSupport.Signal();
    internal IReadOnlyList<ConfigurationRecordKey>? WatchedKeys { get; private set; }
    public ConfigurationNotificationMode NotificationMode { get; init; } = ConfigurationNotificationMode.Reliable;

    internal static ConfigurationReadResult Json(string json, string token)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return ConfigurationReadResult.Found(document.RootElement, token);
    }

    internal void Set<T>(string json, string token, bool application = false)
        => Records[new(application ? ConfigurationHostTestSupport.HostName : "CommonConfiguration",
            ConfigurationHostTestSupport.Definition<T>())] = Json(json, token);

    internal ConfigurationReadResult Current(ConfigurationRecordKey key, string? token)
    {
        ConfigurationReadResult result = Records.TryGetValue(key, out ConfigurationReadResult? found)
            ? found : ConfigurationReadResult.Missing();
        return token is not null && token == result.SourceToken ? ConfigurationReadResult.Unchanged() : result;
    }

    public async ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? sourceToken, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref reads);
        Requests.Enqueue((key, sourceToken));
        return Read is null ? Current(key, sourceToken) : await Read(key, sourceToken, cancellationToken);
    }

    public async ValueTask<IAsyncDisposable> WatchAsync(IReadOnlyList<ConfigurationRecordKey> keys,
        Action<ConfigurationInvalidation> callback, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref watches);
        WatchedKeys = keys.ToArray();
        Volatile.Write(ref invalidated, callback);
        WatchEntered.TrySetResult();
        return Watch is null ? Subscription() : await Watch(cancellationToken);
    }

    internal void Signal() => Volatile.Read(ref invalidated)?.Invoke(new());
    internal IAsyncDisposable Subscription() => new WatchHandle(this);
    public ValueTask DisposeAsync() { Interlocked.Increment(ref disposals); return ValueTask.CompletedTask; }

    private sealed class WatchHandle(ConfigurationHostStore store) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref store.watchDisposals);
            Volatile.Write(ref store.invalidated, null);
            store.WatchDisposalEntered.TrySetResult();
            if (store.DisposeWatch is not null) { await store.DisposeWatch(); }
        }
    }
}
