using System.Text.Json;
using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Configuration.ConfigurationCentralTestSupport;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationCentralBoundaryTests
{
    [TestMethod]
    [DataRow("duplicate-type")]
    [DataRow("duplicate-id-other-schema")]
    [DataRow("unknown-type")]
    [DataRow("missing-target")]
    [DataRow("mismatched-host")]
    [DataRow("missing-polling")]
    public void InvalidCentralSelectionRejectsBeforeAnyRootConstruction(string scenario)
    {
        var store = new CentralStore();
        int factories = 0;
        var first = new ConfigurationEnrollment(typeof(RefreshSettings), "settings", "1", ConfigurationUpdateMode.HotReload);
        IReadOnlyList<ConfigurationEnrollment> enrollments = scenario switch
        {
            "duplicate-type" => [first, new(typeof(RefreshSettings), "other", "1", ConfigurationUpdateMode.HotReload)],
            "duplicate-id-other-schema" => [first, new(typeof(OtherRefreshSettings), "settings", "2", ConfigurationUpdateMode.HotReload)],
            "unknown-type" => [new(typeof(OtherRefreshSettings), "settings", "1", ConfigurationUpdateMode.HotReload)],
            _ => [first]
        };
        var options = new ConfigurationOptions("CentralHost", store)
        {
            PollingInterval = scenario == "missing-polling" ? null : TimeSpan.FromSeconds(1),
            SnapshotTarget = scenario == "missing-target" ? null : scenario == "mismatched-host"
                ? new(Authority, ConfigurationCentralTestSupport.Application, "test", Registration, "OtherHost") : Target(),
            Enrollments = enrollments,
            Factories = new Dictionary<Type, Func<IConfigurationObject>>
            {
                [typeof(RefreshSettings)] = () =>
                {
                    factories++;
                    return new RefreshSettings();
                }
            }
        };
        Assert.ThrowsExactly<CoreConfigurationException>(() => ConfigurationCatalog.Create([typeof(RefreshSettings)], options));
        Assert.AreEqual(0, factories);
        Assert.AreEqual(0, store.SnapshotReads);
    }

    [TestMethod]
    public void OrdinaryLegacyStoreDoesNotBecomeCentralBySupplyingDiscoveryOrATarget()
    {
        var store = new ConfigurationRefreshStore();
        var ordinary = new ConfigurationOptions("CentralHost", store) { SnapshotTarget = Target() };
        ConfigurationCatalog catalog = ConfigurationCatalog.Create([typeof(RefreshSettings)], ordinary);
        Assert.IsNull(catalog.Entries.Single().Enrollment);
        var central = new ConfigurationOptions("CentralHost", store)
        {
            SnapshotTarget = Target(),
            PollingInterval = TimeSpan.FromSeconds(1),
            Enrollments = [new(typeof(RefreshSettings), "settings", "1", ConfigurationUpdateMode.HotReload)]
        };
        Assert.ThrowsExactly<CoreConfigurationException>(() => ConfigurationCatalog.Create([typeof(RefreshSettings)], central));
    }

    [TestMethod]
    public async Task CapturedEnrollmentDoesNotFollowLaterMutationOfTheHostList()
    {
        var store = new CentralStore();
        var enrollments = new List<ConfigurationEnrollment>
        {
            new(typeof(RefreshSettings), "settings", "1", ConfigurationUpdateMode.HotReload)
        };
        var options = new ConfigurationOptions("CentralHost", store)
        {
            SnapshotTarget = Target(),
            PollingInterval = TimeSpan.FromSeconds(1),
            Enrollments = enrollments
        };
        ConfigurationCatalog catalog = ConfigurationCatalog.Create([typeof(RefreshSettings)], options);
        enrollments.Clear();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        Assert.IsNotNull(catalog.Entries.Single().Enrollment);
        Assert.AreEqual("settings", store.Requests.Single().Enrollment!.ConfigurationId);
        Assert.AreEqual(0, store.LegacyReads);
    }

    [TestMethod]
    [DataRow("scalar")]
    [DataRow("collection")]
    [DataRow("depth")]
    [DataRow("duplicate-decoded-name")]
    public async Task BoundedCentralSourcesRejectBeforeLocalValidationAndRetainAcceptedFacts(string boundary)
    {
        var store = new CentralStore();
        int validations = 0;
        Type selected = boundary == "collection" ? typeof(W1Settings) : typeof(RefreshSettings);
        var validators = new Dictionary<Type, Action<IConfigurationObject>> { [selected] = _ => validations++ };
        ConfigurationCatalog catalog = Catalog(store, selected: selected, validators: validators);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        int baselineValidations = validations;
        ConfigurationSnapshotProvenance? accepted = Acceptance(coordinator).LastAccepted;
        string json = boundary switch
        {
            "scalar" => JsonSerializer.Serialize(new { Unknown = new string('a', 64 * 1024 + 1) }),
            "collection" => "{\"Values\":[" + string.Join(',', Enumerable.Repeat("1", 4097)) + "]}",
            "depth" => "{\"Unknown\":" + string.Concat(Enumerable.Repeat("[", 17)) + "0" + string.Concat(Enumerable.Repeat("]", 17)) + "}",
            _ => "{\"Value\":2,\"\\u0056alue\":3}"
        };
        store.Current = Snapshot(2, application: json);
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(baselineValidations, validations);
        Assert.AreSame(accepted, Acceptance(coordinator).LastAccepted);
        Assert.AreEqual(ConfigurationAcceptanceOutcome.Rejected, Acceptance(coordinator).Outcome);
        Assert.IsTrue(Acceptance(coordinator).HasIntactAcceptedState);
        Assert.AreEqual(0L, entry.Revision);
    }

    [TestMethod]
    public void CentralDefaultCollectionsAreCheckedBeforeTheyAreSerializedOrFetched()
    {
        var store = new CentralStore();
        ConfigurationOptions original = Options(store, selected: typeof(W1Settings));
        var options = new ConfigurationOptions(original.HostApplicationName, store)
        {
            SnapshotTarget = original.SnapshotTarget,
            PollingInterval = original.PollingInterval,
            Enrollments = original.Enrollments,
            Factories = new Dictionary<Type, Func<IConfigurationObject>>
            {
                [typeof(W1Settings)] = () => new W1Settings { Values = Enumerable.Range(0, 4097).ToList() }
            }
        };
        Assert.ThrowsExactly<CoreConfigurationException>(() => ConfigurationCatalog.Create([typeof(W1Settings)], options));
        Assert.AreEqual(0, store.SnapshotReads);
    }

    [TestMethod]
    public async Task CompletionCountsRepeatedChildDefaultsBeforeMaterializingTheirReplacementGraphs()
    {
        var store = new CentralStore();
        ConfigurationCatalog catalog = Catalog(store, selected: typeof(ExpandedSettings));
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        int before = ExpandedChild.Constructions;
        store.Current = Snapshot(2, application: "{\"Children\":[" + string.Join(',', Enumerable.Repeat("{}", 17)) + "]}");
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(before, ExpandedChild.Constructions, "The completed logical node ceiling wins before child replacement construction.");
        Assert.IsEmpty(((ExpandedSettings)entry.Instance).Children);
        Assert.AreEqual(ConfigurationAcceptanceOutcome.Rejected, Acceptance(coordinator).Outcome);
    }

    [TestMethod]
    public async Task SharedByteAdmissionRefusesBeforeFetchingAndReleasesOwnedSourcesAfterActualStop()
    {
        var store = new CentralStore { Current = Snapshot(application: "{\"Value\":2}") };
        ConfigurationCatalog catalog = Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        ConfigurationByteBudget budget = catalog.Settings.ByteBudget;
        long defaults = budget.OwnedBytes;
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        Assert.IsGreaterThan(defaults, budget.OwnedBytes);
        int reads = store.SnapshotReads;
        using (ConfigurationByteBudget.Reservation occupied = budget.Reserve(ConfigurationByteBudget.MaximumBytes - budget.OwnedBytes))
        {
            store.Current = Snapshot(2, application: "{\"Value\":3}");
            Assert.IsFalse(await coordinator.ReconcileAsync(entry));
            Assert.AreEqual(reads, store.SnapshotReads);
            Assert.AreEqual(2, ((RefreshSettings)entry.Instance).Value);
            Assert.IsTrue(coordinator.CaptureCentralReadiness().IsReady);
        }
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(3, ((RefreshSettings)entry.Instance).Value);
        await coordinator.StopAsync();
        Assert.AreEqual(defaults, budget.OwnedBytes);
        Assert.AreEqual(0, store.StoreDisposals);
    }

    private sealed class ExpandedSettings : ConfigurationObject
    {
        public ExpandedSettings()
        {
        }
        public List<ExpandedChild> Children { get; set; } = [];
    }

    private sealed class ExpandedChild
    {
        internal static int Constructions;
        public ExpandedChild()
        {
            Constructions++;
        }
        public List<int> Values { get; set; } = Enumerable.Range(0, 4096).ToList();
    }
}
