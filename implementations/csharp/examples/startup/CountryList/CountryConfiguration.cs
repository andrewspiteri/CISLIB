using System.Globalization;
using System.Text.Json;
using CIS.Configuration;

namespace CIS.Startup.Examples.CountryList;

// Passive retained data; Configuration supplies the event and synchronized reads.
internal sealed class CountryListSettings : ConfigurationObject
{
    public CountryListSettings() { }
    public int ResultLimit { get; set; } = 3;
}

// A local demonstration adapter, not a selected production storage backend.
// It supplies one settings definition, conditional reads and reliable local signals.
internal sealed class CountryConfigurationStore(string hostName) : IConfigurationStore
{
    private readonly object gate = new();
    private ConfigurationReadResult common = Record(2, "common:1");
    private ConfigurationReadResult application = Record(3, "application:1");
    private string? definition;
    private Action<ConfigurationInvalidation>? changed;
    private TaskCompletionSource? commonRead;
    private TaskCompletionSource? maskedValidated;
    private int commonVersion = 1, applicationVersion = 1, expectedCommonVersion, subscriptions, disposals;

    public ConfigurationNotificationMode NotificationMode => ConfigurationNotificationMode.Reliable;
    internal int Subscriptions => Volatile.Read(ref subscriptions);
    internal bool WatchDetached
    {
        get { lock (gate) { return subscriptions == 1 && disposals == 1 && changed is null; } }
    }

    public ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? sourceToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfigurationReadResult result;
        TaskCompletionSource? observed = null;
        lock (gate)
        {
            definition ??= key.Definition;
            if (key.Definition != definition) { return ValueTask.FromResult(ConfigurationReadResult.Failure()); }
            if (key.Scope == "CommonConfiguration")
            {
                result = common;
                if (commonVersion >= expectedCommonVersion) { observed = commonRead; }
            }
            else if (key.Scope == hostName) { result = application; }
            else { return ValueTask.FromResult(ConfigurationReadResult.Failure()); }
        }
        observed?.TrySetResult();
        return ValueTask.FromResult(sourceToken is not null && sourceToken == result.SourceToken
            ? ConfigurationReadResult.Unchanged() : result);
    }

    public ValueTask<IAsyncDisposable> WatchAsync(IReadOnlyList<ConfigurationRecordKey> keys,
        Action<ConfigurationInvalidation> invalidated, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (changed is not null || keys.Count != 2 || keys.Any(key => key.Definition != definition))
            { throw new InvalidOperationException("The demonstration store supports one active settings subscription."); }
            changed = invalidated;
            subscriptions++;
        }
        return ValueTask.FromResult<IAsyncDisposable>(new Subscription(this));
    }

    internal Task ChangeCommonLimitAsync(int value)
    {
        Action<ConfigurationInvalidation>? notify;
        string? selected;
        TaskCompletionSource observed;
        TaskCompletionSource validated;
        lock (gate)
        {
            commonVersion++;
            common = Record(value, "common:" + commonVersion.ToString(CultureInfo.InvariantCulture));
            expectedCommonVersion = commonVersion;
            observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            commonRead = observed;
            validated = new(TaskCreationOptions.RunContinuationsAsynchronously);
            maskedValidated = validated;
            notify = changed;
            selected = definition;
        }
        notify?.Invoke(new("CommonConfiguration", selected));
        return Task.WhenAll(observed.Task, validated.Task);
    }

    internal void ValidateCandidate(IConfigurationObject candidate)
    {
        if (candidate is not CountryListSettings settings || settings.ResultLimit is < 1 or > 3)
        { throw new InvalidOperationException("The country result limit must be from one through three."); }
        TaskCompletionSource? observed;
        lock (gate)
        {
            observed = commonRead?.Task.IsCompleted == true && settings.ResultLimit == 3 ? maskedValidated : null;
        }
        observed?.TrySetResult();
    }

    internal void RemoveApplicationOverride()
    {
        Action<ConfigurationInvalidation>? notify;
        string? selected;
        lock (gate)
        {
            application = ConfigurationReadResult.Missing("application:" + (++applicationVersion).ToString(CultureInfo.InvariantCulture));
            notify = changed;
            selected = definition;
        }
        notify?.Invoke(new(hostName, selected));
    }

    private static ConfigurationReadResult Record(int value, string token)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new { ResultLimit = value }));
        return ConfigurationReadResult.Found(document.RootElement, token);
    }

    private sealed class Subscription(CountryConfigurationStore owner) : IAsyncDisposable
    {
        private int disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                lock (owner.gate) { owner.changed = null; owner.disposals++; }
            }
            return ValueTask.CompletedTask;
        }
    }
}
