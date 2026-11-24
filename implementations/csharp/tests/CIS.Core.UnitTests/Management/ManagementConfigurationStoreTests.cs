using System.Text;
using CIS.CommonTesting;
using CIS.Configuration;
using CIS.Management;
using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Tests.UnitTests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Management.ManagementConfigurationClientTestSupport;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementConfigurationStoreTests
{
    [TestMethod]
    public async Task ExactWhitespaceAndParsedJsonShareTheOneConfigurationCacheThroughConditionalNative304AndActualStop()
    {
        ManagementConfigurationSnapshot original = Value<ManagementConfigurationSnapshot>("configuration-snapshot-valid");
        string exact = " \n" + original.CommonSource.PayloadJson + " \t";
        ManagementConfigurationSnapshot snapshot = original with
        {
            CommonSource = original.CommonSource with
            {
                PayloadJson = exact,
                Sha256 = ManagementConfigurationWire.ComputePayloadSha256(exact)
            }
        };
        int reads = 0;
        ManagementConfigurationClientOptions clientOptions = Options();
        await using var rest = new HttpRestClient(Native(clientOptions, new Handler((_, _) =>
            ValueTask.FromResult<IRestResponse>(Interlocked.Increment(ref reads) == 1
                ? new Response(200, new MemoryStream(ManagementConfigurationWire.EncodeResponse(snapshot)))
                : new Response(304, new MemoryStream(), snapshot.ETag)))));
        var adapter = new ManagementConfigurationStore(new ManagementConfigurationClient(rest, clientOptions));
        ConfigurationCatalog catalog = Catalog(adapter);
        ConfigurationEntry entry = catalog.Entries.Single();
        long defaults = catalog.Settings.ByteBudget.OwnedBytes;
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        Assert.AreEqual("Café — 日本語", ((CentralSettings)entry.Instance).Greeting);
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(exact);
        long ownedSources = Encoding.UTF8.GetByteCount(exact) + Encoding.UTF8.GetByteCount(document.RootElement.GetRawText());
        Assert.AreEqual(defaults + ownedSources, catalog.Settings.ByteBudget.OwnedBytes);
        ConfigurationSnapshotProvenance? accepted = entry.Acceptance!.LastAccepted;
        Assert.IsTrue(await coordinator.ReconcileAsync(entry), "Retained source text must preserve the digest's surrounding whitespace through the native conditional check.");
        Assert.AreSame(accepted, entry.Acceptance!.LastAccepted);
        Assert.AreEqual(0L, entry.Revision);
        Assert.AreEqual(2, reads);
        await coordinator.StopAsync();
        Assert.AreEqual(defaults, catalog.Settings.ByteBudget.OwnedBytes);
    }

    [TestMethod]
    public async Task TypedSubstitutionRetainsIdentifiedRejectionAndFinalClientAdoptionIsBorrowedAndSingleUse()
    {
        ManagementConfigurationSnapshot snapshot = Value<ManagementConfigurationSnapshot>("configuration-snapshot-valid");
        var bootstrap = new ScriptedManagementConfigurationClient();
        bootstrap.EnqueueResult(ScriptedManagementConfigurationCallKind.Snapshot, Success(snapshot));
        var adapter = new ManagementConfigurationStore(bootstrap);
        ConfigurationCatalog catalog = Catalog(adapter);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        ConfigurationSnapshotProvenance? accepted = entry.Acceptance!.LastAccepted;
        var final = new ScriptedManagementConfigurationClient();
        var failed = RestResult<ManagementConfigurationSnapshot>.Failed("client.invalid_response", "Invalid content",
            new(200, failureKind: RestFailureKind.InvalidResponse));
        ManagementConfigurationDesiredContext desired = snapshot.Desired with
        {
            Generation = "9007199254740994",
            ReleaseId = Source
        };
        final.EnqueueResult(ScriptedManagementConfigurationCallKind.Snapshot,
            new ManagementConfigurationCallResult<ManagementConfigurationSnapshot>(failed,
                contentRejection: new(desired, "configuration.content.digest")));
        adapter.AdoptHostedClient(final);
        Assert.ThrowsExactly<InvalidOperationException>(() => adapter.AdoptHostedClient(bootstrap));
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(ConfigurationAcceptanceOutcome.Rejected, entry.Acceptance!.Outcome);
        Assert.AreSame(accepted, entry.Acceptance.LastAccepted);
        Assert.AreEqual(desired.Generation, entry.Acceptance.Desired!.AssignmentGeneration);
        Assert.IsTrue(entry.Acceptance.HasIntactAcceptedState);
        Assert.AreEqual(1L, bootstrap.TotalCalls);
        Assert.AreEqual(1L, final.TotalCalls);
        Assert.IsTrue(final.Calls.Single().HasRetainedSnapshot);
    }

    [TestMethod]
    public async Task RoutedStorePreservesReliableLegacyWatchesAndNeverDisposesTheBorrowedAdapter()
    {
        var legacy = new ConfigurationCentralTestSupport.CentralStore { NotificationMode = ConfigurationNotificationMode.Reliable };
        var client = new ScriptedManagementConfigurationClient();
        ManagementConfigurationSnapshot snapshot = Value<ManagementConfigurationSnapshot>("configuration-snapshot-valid");
        client.EnqueueResult(ScriptedManagementConfigurationCallKind.Snapshot, Success(snapshot));
        client.EnqueueResult(ScriptedManagementConfigurationCallKind.Snapshot, Success(snapshot));
        var adapter = new ManagementConfigurationStore(client, legacy);
        Assert.AreEqual(ConfigurationNotificationMode.RequiresReconciliation, adapter.NotificationMode);
        ConfigurationCatalog catalog = ConfigurationCatalog.Create([typeof(CentralSettings), typeof(OtherRefreshSettings)], ConfigurationOptions(adapter));
        ConfigurationEntry ordinary = catalog.Entries.Single(entry => entry.Type == typeof(OtherRefreshSettings));
        legacy.Legacy[new("ExampleHost", ordinary.Identity)] = ConfigurationRefreshTestSupport.Record(12, "legacy");
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        await coordinator.ActivateAsync(CancellationToken.None);
        Assert.AreEqual(12, ((OtherRefreshSettings)ordinary.Instance).Value);
        IReadOnlyList<ConfigurationRecordKey> watched = legacy.WatchedKeys ?? throw new AssertFailedException("The legacy subscription must be retained.");
        Assert.HasCount(2, watched);
        Assert.IsTrue(watched.All(key => key.Definition == ordinary.Identity));
        await coordinator.StopAsync();
        Assert.AreEqual(1, legacy.SubscriptionDisposals);
        Assert.AreEqual(0, legacy.StoreDisposals);
    }

    [TestMethod]
    [DataRow("configuration.integrity", "Configuration.SourceIntegrity")]
    [DataRow("configuration.content.schema", "Configuration.SourceSchema")]
    [DataRow("configuration.content.scope", "Configuration.SourceScope")]
    [DataRow("configuration.content.invalid", "Configuration.SourceFormat")]
    [DataRow("configuration.content.tooLarge", "Configuration.SourceSize")]
    [DataRow("configuration.content.fetch", "Configuration.SourceFetch")]
    [DataRow("future:unrecognized_code", "Configuration.ContentRejected")]
    public async Task IdentifiedContentReasonsHaveFiniteDistinctLocalCodesAndUnknownCodesUseOneSafeFallback(string code, string expected)
    {
        var script = new ScriptedManagementConfigurationClient();
        ManagementConfigurationSnapshot snapshot = Value<ManagementConfigurationSnapshot>("configuration-snapshot-valid");
        script.EnqueueResult(ScriptedManagementConfigurationCallKind.Snapshot, Success(snapshot));
        script.EnqueueResult(ScriptedManagementConfigurationCallKind.Snapshot,
            new ManagementConfigurationCallResult<ManagementConfigurationSnapshot>(RestResult<ManagementConfigurationSnapshot>.Failed(
                "client.invalid_response", "Invalid content", new(200, failureKind: RestFailureKind.InvalidResponse)),
                contentRejection: new(snapshot.Desired, code)));
        ConfigurationCatalog catalog = Catalog(new ManagementConfigurationStore(script));
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(expected, entry.Acceptance!.FailureCode);
        Assert.AreEqual(snapshot.Desired.ReleaseId, entry.Acceptance.Desired!.ReleaseId);
        Assert.IsTrue(entry.Acceptance.HasIntactAcceptedState);
    }

    private static ConfigurationCatalog Catalog(IConfigurationStore adapter)
        => ConfigurationCatalog.Create([typeof(CentralSettings)], ConfigurationOptions(adapter));

    private static ConfigurationOptions ConfigurationOptions(IConfigurationStore adapter)
        => new("ExampleHost", adapter)
        {
            PollingInterval = TimeSpan.FromSeconds(1),
            Enrollments = [new(typeof(CentralSettings), "sample.settings", "1", ConfigurationUpdateMode.HotReload)],
            SnapshotTarget = new(Authority, AppId, "Development", Registration, "ExampleHost", "canary")
        };

    private static ManagementConfigurationCallResult<ManagementConfigurationSnapshot> Success(ManagementConfigurationSnapshot snapshot)
        => new(RestResult<ManagementConfigurationSnapshot>.FromValue(snapshot, new(200, hasValue: true)));

    private sealed class CentralSettings : ConfigurationObject
    {
        public CentralSettings()
        {
        }
        public string Greeting { get; set; } = "default";
        public bool Enabled { get; set; } = true;
    }
}
