using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization;
using CIS.Configuration;
using CIS.TestSupport;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationCatalogTests
{
    [TestMethod]
    public async Task RealPreparedTypeManagerQuerySuppliesTheExactCandidateToTheRetainedObjectPrimitive()
    {
        Assembly host = typeof(ConfigurationCatalogTests).Assembly;
        using var directory = new TemporaryDirectory();
        File.Copy(host.Location, Path.Combine(directory.Path, "Host.dll"));
        string manifest = Path.Combine(directory.Path, "host.types.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            hostAssembly = host.FullName,
            assemblies = new[] { new { assemblyName = host.FullName, relativePath = "Host.dll" } }
        }));
        using var cancellation = new CancellationTokenSource(ConfigurationObjectTestSupport.Deadline);
        ITypeManager manager = await TypeManager.CreateAsync(new TypeManagerOptions(host)
        { AssemblyManifestPath = manifest, PrecomputedContracts = [typeof(IConfigurationObject)] }, cancellation.Token);
        // The Unit assembly deliberately includes invalid negative-test markers.
        // Exercise one actual query result; full production discovery belongs to its later fixture.
        Type selected = manager.GetImplementations<IConfigurationObject>().Single(type => type == typeof(W1Settings));
        ConfigurationCatalog catalog = ConfigurationCatalog.Create(new[] { selected }, ConfigurationObjectTestSupport.Options());
        Assert.AreSame(selected, catalog.Entries.Single().Type);
        var services = new ServiceCollection();
        catalog.Register(services);
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        { ValidateScopes = true, ValidateOnBuild = true });
        Assert.AreSame(catalog.Entries.Single().Instance, provider.GetRequiredService<W1Settings>());
    }

    [TestMethod]
    public async Task DiscoveryQueriesOnceAndFactoriesDoNotRunAgainForConsumersOrScopes()
    {
        int constructions = 0;
        var manager = new ConfigurationObjectTestSupport.SelectedTypes(typeof(W1Settings));
        ConfigurationCatalog catalog = ConfigurationCatalog.Create(manager, ConfigurationObjectTestSupport.Options(factories:
            new Dictionary<Type, Func<IConfigurationObject>>
            { [typeof(W1Settings)] = () => { constructions++; return new W1Settings(); } }));
        var services = new ServiceCollection();
        catalog.Register(services);
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        { ValidateScopes = true, ValidateOnBuild = true });
        using IServiceScope firstScope = provider.CreateScope();
        using IServiceScope secondScope = provider.CreateScope();
        W1Settings root = provider.GetRequiredService<W1Settings>();
        Assert.AreSame(root, firstScope.ServiceProvider.GetRequiredService<W1Settings>());
        Assert.AreSame(root, secondScope.ServiceProvider.GetRequiredService<W1Settings>());
        Assert.AreSame(root, catalog.Entries.Single().Instance);
        Assert.AreSame(catalog.Reader, provider.GetRequiredService<IConfigurationReader>());
        root.First = 31;
        Assert.AreEqual(31, secondScope.ServiceProvider.GetRequiredService<W1Settings>().First);
        Assert.AreEqual(1, manager.Queries);
        Assert.AreEqual(1, constructions);
    }

    [TestMethod]
    public void SeparateHostsOwnDifferentRootsAndGatesForTheSameType()
    {
        ConfigurationCatalog first = ConfigurationCatalog.Create(new[] { typeof(W1Settings) }, ConfigurationObjectTestSupport.Options("HostA"));
        ConfigurationCatalog second = ConfigurationCatalog.Create(new[] { typeof(W1Settings) }, ConfigurationObjectTestSupport.Options("HostB"));
        ConfigurationEntry firstEntry = first.Entries.Single(), secondEntry = second.Entries.Single();
        Assert.AreNotSame(firstEntry.Instance, secondEntry.Instance);
        Assert.AreNotSame(firstEntry.ValueGate, secondEntry.ValueGate);
        Assert.AreNotSame(firstEntry.Instance, firstEntry.ValueGate);
        Assert.AreEqual(firstEntry.Identity, secondEntry.Identity, "The host scope is separate from the type's persisted definition.");
        ((W1Settings)firstEntry.Instance).First = 41;
        Assert.AreEqual(10, ((W1Settings)secondEntry.Instance).First);
    }

    [TestMethod]
    public void CapturedDefaultsRemainDetachedAfterLiveScalarsCollectionsAndChildrenChange()
    {
        ConfigurationEntry entry = ConfigurationCatalog.Create(new[] { typeof(W1Settings) }, ConfigurationObjectTestSupport.Options()).Entries.Single();
        var root = (W1Settings)entry.Instance;
        root.First = 99;
        root.Values[0] = 88;
        root.Values.Add(66);
        root.Child.Label = "changed";
        Assert.AreEqual(10, entry.Defaults.GetProperty(nameof(W1Settings.First)).GetInt32());
        CollectionAssert.AreEqual(new[] { 2, 4 }, entry.Defaults.GetProperty(nameof(W1Settings.Values)).EnumerateArray().Select(value => value.GetInt32()).ToArray());
        Assert.AreEqual("original", entry.Defaults.GetProperty(nameof(W1Settings.Child)).GetProperty(nameof(W1Child.Label)).GetString());
    }

    [TestMethod]
    public void AUsableConstructorRunsOnceEvenForANonpublicConfigurationClass()
    {
        int before = CountedSettings.Constructions;
        ConfigurationCatalog catalog = ConfigurationCatalog.Create(new[] { typeof(CountedSettings) }, ConfigurationObjectTestSupport.Options());
        Assert.IsFalse(typeof(CountedSettings).IsVisible);
        Assert.IsInstanceOfType<CountedSettings>(catalog.Entries.Single().Instance);
        int expectedConstructions = before + 1;
        Assert.AreEqual(expectedConstructions, CountedSettings.Constructions);
    }

    [TestMethod]
    public void AnExceptionalExactTypeFactorySuppliesDefaultsWithoutAParameterlessConstructor()
    {
        int calls = 0;
        ConfigurationEntry entry = ConfigurationCatalog.Create(new[] { typeof(W1NeedsFactorySettings) }, ConfigurationObjectTestSupport.Options(
            factories: new Dictionary<Type, Func<IConfigurationObject>>
            { [typeof(W1NeedsFactorySettings)] = () => { calls++; return new W1NeedsFactorySettings(37); } })).Entries.Single();
        Assert.AreEqual(1, calls);
        Assert.AreEqual(37, ((W1NeedsFactorySettings)entry.Instance).Value);
        Assert.AreEqual(37, entry.Defaults.GetProperty(nameof(W1NeedsFactorySettings.Value)).GetInt32());
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("unrelated")]
    [DataRow("subclass")]
    [DataRow("throws")]
    public void FactoryMustReturnTheExactFreshDiscoveredType(string result)
    {
        int calls = 0;
        var factories = new Dictionary<Type, Func<IConfigurationObject>>
        {
            [typeof(W1Settings)] = () =>
            {
                calls++;
                return result switch
                {
                    "null" => null!, "unrelated" => new W1OtherSettings(), "subclass" => new W1DerivedSettings(),
                    _ => throw new InvalidOperationException("Private factory detail.")
                };
            }
        };
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Factory", () =>
            ConfigurationCatalog.Create(new[] { typeof(W1Settings) }, ConfigurationObjectTestSupport.Options(factories: factories)));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingOrThrowingConstructorsFailInsteadOfSilentlySkippingDiscoveredTypes(bool throws)
    {
        Type type = throws ? typeof(ThrowingSettings) : typeof(W1NeedsFactorySettings);
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Construction", () =>
            ConfigurationCatalog.Create(new[] { type }, ConfigurationObjectTestSupport.Options()));
    }

    [TestMethod]
    [DataRow("interface")]
    [DataRow("abstract")]
    [DataRow("open-generic")]
    [DataRow("not-marker")]
    [DataRow("readonly-data")]
    public void IneligibleDiscoveredShapesFailUsefullyRatherThanDisappearing(string shape)
    {
        Type type = shape switch
        {
            "interface" => typeof(IConfigurationObject), "abstract" => typeof(AbstractSettings),
            "open-generic" => typeof(OpenSettings<>), "not-marker" => typeof(W1Child), _ => typeof(ReadonlySettings)
        };
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Shape", () =>
            ConfigurationCatalog.Create(new[] { type }, ConfigurationObjectTestSupport.Options()));
    }

    [TestMethod]
    [DataRow("concrete-singleton")]
    [DataRow("concrete-scoped")]
    [DataRow("concrete-keyed")]
    [DataRow("reader-singleton")]
    [DataRow("reader-keyed")]
    public void ExistingConcreteOrReaderRegistrationsIncludingKeyedOnesAreNotSilentlyOverridden(string conflict)
    {
        ConfigurationCatalog catalog = ConfigurationCatalog.Create(new[] { typeof(W1Settings), typeof(W1OtherSettings) }, ConfigurationObjectTestSupport.Options());
        var services = new ServiceCollection();
        switch (conflict)
        {
            case "concrete-singleton": services.AddSingleton(new W1OtherSettings()); break;
            case "concrete-scoped": services.AddScoped<W1OtherSettings>(); break;
            case "concrete-keyed": services.AddKeyedSingleton("existing", new W1OtherSettings()); break;
            case "reader-singleton": services.AddSingleton(catalog.Reader); break;
            case "reader-keyed": services.AddKeyedSingleton("existing", catalog.Reader); break;
        }
        ServiceDescriptor[] before = services.ToArray();
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Registration", () => catalog.Register(services));
        CollectionAssert.AreEqual(before, services.ToArray(), "Registration conflicts must be detected before publishing any partial Configuration registration set.");
    }

    [TestMethod]
    public void PersistedIdentityOmitsOnlyAssemblyVersionWhileRuntimeTypesRemainDistinct()
    {
        string assemblyName = "ConfigurationVersion_" + Guid.NewGuid().ToString("N");
        Type first = IdentityType(assemblyName, new Version(1, 2, 3, 4));
        Type second = IdentityType(assemblyName, new Version(9, 8, 7, 6));
        Assert.AreNotSame(first, second);
        Assert.AreNotEqual(first.AssemblyQualifiedName, second.AssemblyQualifiedName);
        Assert.AreEqual(ConfigurationIdentity.Create(first), ConfigurationIdentity.Create(second));
        StringAssert.Contains(ConfigurationIdentity.Create(first), first.FullName!);
        StringAssert.Contains(ConfigurationIdentity.Create(first), assemblyName);
        Assert.IsFalse(ConfigurationIdentity.Create(first).Contains("Version=", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SameTypeNameInDifferentAssembliesHasDifferentPersistedIdentity()
    {
        Type first = IdentityType("ConfigurationA_" + Guid.NewGuid().ToString("N"), new Version(1, 0));
        Type second = IdentityType("ConfigurationB_" + Guid.NewGuid().ToString("N"), new Version(1, 0));
        Assert.AreEqual(first.FullName, second.FullName);
        Assert.AreNotEqual(ConfigurationIdentity.Create(first), ConfigurationIdentity.Create(second));
    }

    [TestMethod]
    public void DistinctDiscoveredTypesCollidingUnderTheVersionlessKeyFailInsteadOfGuessingFallbackKeys()
    {
        string assemblyName = "ConfigurationCollision_" + Guid.NewGuid().ToString("N");
        Type first = IdentityType(assemblyName, new Version(1, 0));
        Type second = IdentityType(assemblyName, new Version(2, 0));
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Identity", () =>
            ConfigurationCatalog.Create(new[] { first, second }, ConfigurationObjectTestSupport.Options()));
    }

    [TestMethod]
    public void HostIdentityIsExplicitAndTheCommonScopeCannotBeUsedAsAnApplication()
    {
        ConfigurationOptions options = ConfigurationObjectTestSupport.Options("ExplicitHostProject");
        Assert.AreEqual("ExplicitHostProject", options.HostApplicationName);
        Assert.IsNull(options.PollingInterval, "Object preparation must not invent a polling default.");
        Assert.IsNull(options.IsHealthCritical, "Health criticality belongs to explicit host policy.");
        Assert.ThrowsExactly<ArgumentException>(() => ConfigurationObjectTestSupport.Options("CommonConfiguration"));
    }

    [TestMethod]
    public void SerializedPropertyNamesAreOrdinalAndAnExactDuplicateIsRejected()
    {
        ConfigurationEntry entry = ConfigurationCatalog.Create(new[] { typeof(OrdinalNamesSettings) }, ConfigurationObjectTestSupport.Options()).Entries.Single();
        Assert.AreEqual(3, entry.Defaults.GetProperty("Value").GetInt32());
        Assert.AreEqual(8, entry.Defaults.GetProperty("value").GetInt32());
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Shape", () =>
            ConfigurationCatalog.Create(new[] { typeof(DuplicateNamesSettings) }, ConfigurationObjectTestSupport.Options()));
    }

    [TestMethod]
    [DataRow("default")]
    [DataRow("ordinal")]
    [DataRow("ignore-case")]
    public void DictionaryDefaultsKeepOrdinalKeysAndRejectAComparerThatChangesTheirMeaning(string comparer)
    {
        var factories = new Dictionary<Type, Func<IConfigurationObject>>
        {
            [typeof(DictionarySettings)] = () => new DictionarySettings
            {
                Values = comparer == "ignore-case"
                    ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1 }
                    : comparer == "ordinal"
                        ? new Dictionary<string, int>(StringComparer.Ordinal) { ["A"] = 1, ["a"] = 2 }
                        : new Dictionary<string, int> { ["A"] = 1, ["a"] = 2 }
            }
        };
        if (comparer == "ignore-case")
        {
            ConfigurationObjectTestSupport.AssertIssue("Configuration.Shape", () =>
                ConfigurationCatalog.Create(new[] { typeof(DictionarySettings) }, ConfigurationObjectTestSupport.Options(factories: factories)));
            return;
        }
        ConfigurationEntry entry = ConfigurationCatalog.Create(new[] { typeof(DictionarySettings) },
            ConfigurationObjectTestSupport.Options(factories: factories)).Entries.Single();
        JsonElement values = entry.Defaults.GetProperty(nameof(DictionarySettings.Values));
        Assert.AreEqual(1, values.GetProperty("A").GetInt32());
        Assert.AreEqual(2, values.GetProperty("a").GetInt32());
    }

    // Dynamic types exercise identity/collision policy only; Type Manager's actual
    // default-context query is exercised separately above, without dynamic input.
    private static Type IdentityType(string assemblyName, Version version)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName) { Version = version }, AssemblyBuilderAccess.Run);
        TypeBuilder type = assembly.DefineDynamicModule(assemblyName).DefineType("Shared.Settings", TypeAttributes.Public, typeof(ConfigurationObject));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        return type.CreateType()!;
    }

    private sealed class CountedSettings : ConfigurationObject
    {
        private static int constructions;
        public CountedSettings() { Interlocked.Increment(ref constructions); }
        internal static int Constructions => Volatile.Read(ref constructions);
        public int Value { get; set; } = 13;
    }
    private sealed class ThrowingSettings : ConfigurationObject
    {
        public ThrowingSettings() { throw new InvalidOperationException("Private constructor detail."); }
        public int Value { get; set; }
    }
    private abstract class AbstractSettings : ConfigurationObject { }
    private sealed class OpenSettings<T> : ConfigurationObject
    {
        public OpenSettings() { }
        public int Value { get; set; }
    }
    private sealed class ReadonlySettings : ConfigurationObject
    {
        public ReadonlySettings() { }
        public int Value => 12;
    }
    private sealed class OrdinalNamesSettings : ConfigurationObject
    {
        public OrdinalNamesSettings() { }
        [JsonPropertyName("Value")]
        public int First { get; set; } = 3;
        [JsonPropertyName("value")]
        public int Second { get; set; } = 8;
    }
    private sealed class DuplicateNamesSettings : ConfigurationObject
    {
        public DuplicateNamesSettings() { }
        [JsonPropertyName("same")]
        public int First { get; set; }
        [JsonPropertyName("same")]
        public int Second { get; set; }
    }
    private sealed class DictionarySettings : ConfigurationObject
    {
        public DictionarySettings() { }
        public Dictionary<string, int> Values { get; set; } = [];
    }
}
