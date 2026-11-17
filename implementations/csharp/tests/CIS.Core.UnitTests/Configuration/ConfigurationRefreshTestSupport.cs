using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

internal static class ConfigurationRefreshTestSupport
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static ConfigurationReadResult Record(int value, string token)
        => Json("{\"Value\":" + value.ToString(CultureInfo.InvariantCulture) + "}", token);
    internal static ConfigurationReadResult Json(string json, string token)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return ConfigurationReadResult.Found(document.RootElement, token);
    }
    internal static ConfigurationCatalog Catalog(ConfigurationRefreshStore store, params Type[] types)
        => ConfigurationCatalog.Create(types.Length == 0 ? new[] { typeof(RefreshSettings) } : types,
            new ConfigurationOptions("RefreshHost", store)
            { PollingInterval = store.NotificationMode == ConfigurationNotificationMode.Reliable ? null : TimeSpan.FromSeconds(1) });
    internal static async Task<Exception> FailureAsync(Task operation)
    {
        try { await operation.WaitAsync(Deadline); }
        catch (Exception error) when (operation.IsCompleted) { return error; }
        throw new AssertFailedException("The operation unexpectedly succeeded.");
    }
}

// A controlled provider fixture, not a production backend or scheduler.
internal sealed class ConfigurationRefreshStore : IConfigurationStore, IAsyncDisposable
{
    private int reads, active, maximum, watches, disposals, subscriptionDisposals;
    internal readonly ConcurrentDictionary<ConfigurationRecordKey, ConfigurationReadResult> Records = new();
    internal readonly ConcurrentQueue<(ConfigurationRecordKey Key, string? Token)> Requests = new();
    internal Func<ConfigurationRecordKey, string?, CancellationToken, ValueTask<ConfigurationReadResult>>? Read;
    internal Func<CancellationToken, ValueTask<IAsyncDisposable>>? Watch;
    internal Func<ValueTask>? DisposeSubscription;
    internal Action<ConfigurationInvalidation>? Invalidated;
    internal TaskCompletionSource WatchEntered { get; } = ConfigurationRefreshTestSupport.Signal();
    internal TaskCompletionSource SubscriptionDisposalEntered { get; } = ConfigurationRefreshTestSupport.Signal();
    public ConfigurationNotificationMode NotificationMode { get; init; } = ConfigurationNotificationMode.Reliable;
    internal int Reads => Volatile.Read(ref reads);
    internal int MaximumConcurrentReads => Volatile.Read(ref maximum);
    internal int Watches => Volatile.Read(ref watches);
    internal int Disposals => Volatile.Read(ref disposals);
    internal int SubscriptionDisposals => Volatile.Read(ref subscriptionDisposals);

    internal void Set(ConfigurationEntry entry, int value, string token, bool application = false)
        => Records[new(application ? "RefreshHost" : "CommonConfiguration", entry.Identity)] = ConfigurationRefreshTestSupport.Record(value, token);

    internal ConfigurationReadResult Current(ConfigurationRecordKey key, string? token)
    {
        ConfigurationReadResult current = Records.TryGetValue(key, out ConfigurationReadResult? result) ? result : ConfigurationReadResult.Missing();
        return token is not null && token == current.SourceToken ? ConfigurationReadResult.Unchanged() : current;
    }

    public async ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? sourceToken, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref reads);
        Requests.Enqueue((key, sourceToken));
        int count = Interlocked.Increment(ref active);
        int previous;
        do { previous = Volatile.Read(ref maximum); if (previous >= count) { break; } }
        while (Interlocked.CompareExchange(ref maximum, count, previous) != previous);
        try { return Read is null ? Current(key, sourceToken) : await Read(key, sourceToken, cancellationToken); }
        finally { Interlocked.Decrement(ref active); }
    }

    public async ValueTask<IAsyncDisposable> WatchAsync(IReadOnlyList<ConfigurationRecordKey> keys,
        Action<ConfigurationInvalidation> invalidated, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref watches);
        Invalidated = invalidated;
        WatchEntered.TrySetResult();
        return Watch is null ? Subscription() : await Watch(cancellationToken);
    }

    internal IAsyncDisposable Subscription() => new WatchHandle(this);
    internal void Signal(ConfigurationEntry? entry = null, string? scope = null)
        => Invalidated?.Invoke(new(scope, entry?.Identity));
    public ValueTask DisposeAsync() { Interlocked.Increment(ref disposals); return ValueTask.CompletedTask; }

    private sealed class WatchHandle(ConfigurationRefreshStore store) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref store.subscriptionDisposals);
            store.Invalidated = null;
            store.SubscriptionDisposalEntered.TrySetResult();
            if (store.DisposeSubscription is not null) { await store.DisposeSubscription(); }
        }
    }
}

// This deliberately small value seam tests refresh coordination independently
// of W2's real merge/conversion implementation; it is not shipped mapping code.
internal sealed class ConfigurationRefreshPipeline : IConfigurationValuePipeline
{
    private int prepares;
    internal int Prepares => Volatile.Read(ref prepares);
    internal Action<ConfigurationEntry>? BeforeApply;
    internal Action? AfterAcceptedCommit;
    public ConfigurationPreparedValues Prepare(ConfigurationEntry entry, JsonElement? common, JsonElement? application)
    {
        Interlocked.Increment(ref prepares);
        int value = entry.Defaults.GetProperty("Value").GetInt32();
        foreach (JsonElement? source in new[] { common, application })
        {
            if (source is not { } record) { continue; }
            if (record.TryGetProperty("invalid", out _)) { throw new InvalidOperationException("PRIVATE invalid value"); }
            if (record.TryGetProperty("Value", out JsonElement supplied)) { value = supplied.GetInt32(); }
        }
        return new(value.ToString(CultureInfo.InvariantCulture), new[]
        { new ConfigurationAssignment(entry.Type.GetProperty("Value")!, value) });
    }
    public bool Apply(ConfigurationEntry entry, ConfigurationPreparedValues prepared, CancellationToken cancellationToken,
        Func<bool> stopping, bool initial = false)
    {
        bool changed;
        lock (entry.ValueGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopping()) { throw new OperationCanceledException(new CancellationToken(canceled: true)); }
            BeforeApply?.Invoke(entry);
            if (!initial && entry.AcceptedChecksum == prepared.Checksum) { return false; }
            foreach (ConfigurationAssignment assignment in prepared.Assignments) { assignment.Property.SetValue(entry.Instance, assignment.Value); }
            entry.AcceptedChecksum = prepared.Checksum;
            changed = !initial;
            if (changed) { entry.Revision = checked(entry.Revision + 1); }
        }
        if (changed) { AfterAcceptedCommit?.Invoke(); }
        return changed;
    }
}

internal sealed class RefreshSettings : ConfigurationObject
{
    public RefreshSettings() { }
    public int Value { get; set; } = 1;
}

internal sealed class OtherRefreshSettings : ConfigurationObject
{
    public OtherRefreshSettings() { }
    public int Value { get; set; } = 1;
}
