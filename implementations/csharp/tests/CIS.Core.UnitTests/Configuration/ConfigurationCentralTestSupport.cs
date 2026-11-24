using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

internal static class ConfigurationCentralTestSupport
{
    internal const string Authority = "11111111-1111-1111-1111-111111111111";
    internal const string Application = "22222222-2222-2222-2222-222222222222";
    internal const string Registration = "33333333-3333-3333-3333-333333333333";
    internal const string NewRegistration = "77777777-7777-7777-7777-777777777777";
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    internal static ConfigurationSnapshotTarget Target(string registration = Registration, string? cohort = null)
        => new(Authority, Application, "test", registration, "CentralHost", cohort);

    internal static ConfigurationOptions Options(CentralStore store, ConfigurationUpdateMode mode = ConfigurationUpdateMode.HotReload,
        Type? selected = null, ConfigurationSnapshotTarget? target = null,
        IReadOnlyDictionary<Type, Action<IConfigurationObject>>? validators = null)
        => new("CentralHost", store)
        {
            PollingInterval = TimeSpan.FromSeconds(1),
            SnapshotTarget = target ?? Target(),
            Enrollments = [new(selected ?? typeof(RefreshSettings), "settings", "1", mode)],
            Validators = validators ?? new Dictionary<Type, Action<IConfigurationObject>>()
        };

    internal static ConfigurationCatalog Catalog(CentralStore store, ConfigurationUpdateMode mode = ConfigurationUpdateMode.HotReload,
        Type? selected = null, ConfigurationSnapshotTarget? target = null,
        IReadOnlyDictionary<Type, Action<IConfigurationObject>>? validators = null)
        => ConfigurationCatalog.Create([selected ?? typeof(RefreshSettings)], Options(store, mode, selected, target, validators));

    internal static ConfigurationDesiredIdentity Desired(long generation = 1, ConfigurationSnapshotTarget? target = null,
        int release = 1)
        => new(target ?? Target(), "settings", "1", Id(release), generation.ToString(CultureInfo.InvariantCulture));

    internal static ConfigurationSnapshotReadResult Snapshot(long generation = 1, string? common = null,
        string? application = null, ConfigurationSnapshotTarget? target = null, int? release = null)
    {
        ConfigurationDesiredIdentity desired = Desired(generation, target, release ?? checked((int)generation));
        ConfigurationPublicationReference? commonReference = Reference(common, 10 + checked((int)generation));
        ConfigurationPublicationReference? applicationReference = Reference(application, 20 + checked((int)generation));
        return ConfigurationSnapshotReadResult.Complete(Record(common), Record(application),
            "etag-" + generation.ToString(CultureInfo.InvariantCulture) + "-" + desired.ReleaseId,
            new(desired, commonReference, applicationReference));
    }

    internal static string Id(int value) => new Guid(value, 0, 0, new byte[8]).ToString("D");

    private static ConfigurationPublicationReference? Reference(string? json, int publication)
        => json is null ? null : new(Id(99), Id(publication), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))));

    private static ConfigurationReadResult Record(string? json)
    {
        if (json is null)
        {
            return ConfigurationReadResult.Missing();
        }
        using JsonDocument document = JsonDocument.Parse(json);
        return ConfigurationReadResult.Found(document.RootElement);
    }

    internal sealed class CentralStore : IConfigurationStore, IConfigurationSnapshotStore, IAsyncDisposable
    {
        internal ConfigurationSnapshotReadResult Current { get; set; } = Snapshot();
        internal Func<ConfigurationSnapshotRequest, CancellationToken, ValueTask<ConfigurationSnapshotReadResult>>? Read;
        internal readonly ConcurrentQueue<ConfigurationSnapshotRequest> Requests = new();
        internal readonly ConcurrentDictionary<ConfigurationRecordKey, ConfigurationReadResult> Legacy = new();
        internal IReadOnlyList<ConfigurationRecordKey>? WatchedKeys;
        internal int SnapshotReads;
        internal int LegacyReads;
        internal int StoreDisposals;
        internal int SubscriptionDisposals;
        public ConfigurationNotificationMode NotificationMode { get; init; } = ConfigurationNotificationMode.None;

        public ValueTask<ConfigurationSnapshotReadResult> ReadSnapshotAsync(ConfigurationSnapshotRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref SnapshotReads);
            Requests.Enqueue(request);
            if (Read is not null)
            {
                return Read(request, cancellationToken);
            }
            return ValueTask.FromResult(request.SnapshotToken is not null && request.SnapshotToken == Current.SnapshotToken
                ? ConfigurationSnapshotReadResult.Unchanged() : Current);
        }

        public ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? sourceToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref LegacyReads);
            return ValueTask.FromResult(Legacy.TryGetValue(key, out ConfigurationReadResult? result) ? result : ConfigurationReadResult.Missing());
        }

        public ValueTask<IAsyncDisposable> WatchAsync(IReadOnlyList<ConfigurationRecordKey> keys,
            Action<ConfigurationInvalidation> invalidated, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WatchedKeys = keys.ToArray();
            return ValueTask.FromResult<IAsyncDisposable>(new Subscription(this));
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref StoreDisposals);
            return ValueTask.CompletedTask;
        }

        private sealed class Subscription(CentralStore store) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref store.SubscriptionDisposals);
                return ValueTask.CompletedTask;
            }
        }
    }

    internal static ConfigurationAcceptanceStatus Acceptance(ConfigurationRefreshCoordinator coordinator)
        => coordinator.CaptureStatus().Entries.Single(entry => entry.Acceptance is not null).Acceptance
            ?? throw new AssertFailedException("A central entry must expose actual acceptance status.");
}
