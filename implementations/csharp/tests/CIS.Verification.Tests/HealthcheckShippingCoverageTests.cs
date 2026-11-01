using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Tests.Verification;

// Small receipt/parser controls use explicit opaque fixture bytes, not native
// coverage observations or completed candidate qualification evidence.
[TestClass]
public sealed class HealthcheckShippingCoverageTests
{
    [TestMethod]
    public void SharedCoverageDeadlineCapsEachChildAndConsumesElapsedPreparationTime()
    {
        Assert.AreEqual(1800d, ShippingCoverageCollection.FamilyTimeoutSeconds(3600, 300));
        Assert.AreEqual(0.25d, ShippingCoverageCollection.FamilyTimeoutSeconds(1800, 1799.75));
    }

    [TestMethod]
    public void NoCoverageChildMayLaunchAtOrAfterTheSharedDeadline()
    {
        Assert.ThrowsExactly<TimeoutException>(() => ShippingCoverageCollection.FamilyTimeoutSeconds(1800, 1800));
        Assert.ThrowsExactly<TimeoutException>(() => ShippingCoverageCollection.FamilyTimeoutSeconds(1800, 1800.01));
    }

    [TestMethod]
    public void ACurrentReceiptCannotAcceptLegacyOrDowngradedProducerAdmission()
    {
        bool current = HealthcheckCoverageVerification.ValidateCollectionProfile(CurrentProfile());
        foreach (JsonObject producer in new[] { new JsonObject(), new JsonObject { ["schema"] = "cis.healthcheck.prepared-inputs/v999" } })
        { Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateProducerProfile(current, producer)); }
        HealthcheckCoverageVerification.ValidateProducerProfile(false, new JsonObject());
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateProducerProfile(false,
            new JsonObject { ["schema"] = HealthcheckPackageVerification.PreparedInputSchema }));
        HealthcheckPackageVerification.ValidatePreparedProducer(new JsonObject(), "unused-legacy-root", "unused-legacy-package");
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("schema")]
    [DataRow("profile")]
    [DataRow("missing-family")]
    [DataRow("extra-family")]
    [DataRow("duplicate-family")]
    [DataRow("unfinished")]
    [DataRow("failed")]
    [DataRow("undrained")]
    [DataRow("missing-trx-pin")]
    public void CurrentCollectionProfileRequiresItsExactFourCompletedFamilies(string change)
    {
        JsonObject receipt = CurrentProfile();
        switch (change)
        {
            case "schema": receipt["schema"] = "other"; break;
            case "profile": receipt["profile"] = "core-only"; break;
            case "missing-family": receipt["families"]!.AsArray().RemoveAt(0); break;
            case "extra-family": receipt["families"]!.AsArray().Add(receipt["families"]![0]!.DeepClone()); break;
            case "duplicate-family": receipt["families"]![1]!["name"] = "unit"; break;
            case "unfinished": receipt["complete"] = false; break;
            case "failed": receipt["families"]![0]!["exitCode"] = 1; break;
            case "undrained": receipt["families"]![0]!["outputDrained"] = false; break;
            case "missing-trx-pin": receipt["families"]![0]!.AsObject().Remove("trxSha256"); break;
        }
        if (change == "valid") { Assert.IsTrue(HealthcheckCoverageVerification.ValidateCollectionProfile(receipt)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateCollectionProfile(receipt)); }
        var legacy = new JsonObject { ["families"] = new JsonArray(new[] { "unit", "integration", "business" }.Select(name => (JsonNode)new JsonObject { ["name"] = name }).ToArray()) };
        Assert.IsFalse(HealthcheckCoverageVerification.ValidateCollectionProfile(legacy));
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing-selection")]
    [DataRow("missing-filter")]
    [DataRow("category-filter")]
    [DataRow("other-family-filter")]
    [DataRow("observation-claim")]
    [DataRow("missing-command-pin")]
    [DataRow("missing-schema")]
    public void SelectedCoverageCannotBroadenTheOmissionOrClaimUninstrumentedObservations(string change)
    {
        JsonObject receipt = CurrentProfile();
        if (change == "missing-selection") { receipt.Remove("testSelection"); }
        if (change == "missing-filter") { receipt["families"]![0]!.AsObject().Remove("filter"); }
        if (change == "category-filter") { receipt["families"]![0]!["filter"] = "TestCategory!=PerformanceObservation"; }
        if (change == "other-family-filter") { receipt["families"]![1]!["filter"] = ShippingCoverageCollection.UnitFilter; }
        if (change == "observation-claim") { receipt["uninstrumentedObservationsExecuted"] = true; }
        if (change == "missing-command-pin") { receipt.Remove("commands"); }
        if (change == "missing-schema") { receipt.Remove("schema"); receipt.Remove("profile"); }
        if (change == "valid") { Assert.IsTrue(HealthcheckCoverageVerification.ValidateCollectionProfile(receipt)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateCollectionProfile(receipt)); }
    }

    [TestMethod]
    public void OriginalV1RemainsUnfilteredAndCannotStandInForTheNewPinnedSelection()
    {
        JsonObject original = CurrentProfile();
        original["schema"] = ShippingCoverageCollection.LegacyReceiptSchema;
        original["profile"] = ShippingCoverageCollection.LegacyReceiptProfile;
        original.Remove("testSelection"); original.Remove("uninstrumentedObservationsExecuted"); original.Remove("commands");
        foreach (JsonObject family in original["families"]!.AsArray().OfType<JsonObject>()) { family.Remove("filter"); }
        Assert.IsTrue(HealthcheckCoverageVerification.ValidateCollectionProfile(original));
        ShippingCoverageCollection.ValidateSelection(original, new JsonObject());
        Assert.ThrowsExactly<InvalidDataException>(() => ShippingCoverageCollection.ValidateSelection(original, SelectionPolicy()));
        original["families"]![0]!["filter"] = ShippingCoverageCollection.UnitFilter;
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateCollectionProfile(original));
    }

    [TestMethod]
    [DataRow("missing-marker")]
    [DataRow("missing-selection")]
    [DataRow("different-method")]
    [DataRow("category-filter")]
    [DataRow("different-count")]
    public void CurrentPolicySelectionCannotSilentlyDowngradeOrOmitOtherTests(string change)
    {
        JsonObject policy = SelectionPolicy();
        if (change == "missing-marker") { policy.Remove("collectionProfile"); }
        if (change == "missing-selection") { policy.Remove("testSelection"); }
        if (change == "different-method") { policy["testSelection"]!["excludedMethod"] = "CIS.Tests.UnitTests.IncomingMeasurementsTests.HeldIndexAndRotatingSlotsRetainAtMostTheirExplicitOccurrenceBounds"; }
        if (change == "category-filter") { policy["testSelection"]!["unitFilter"] = "TestCategory!=PerformanceObservation"; }
        if (change == "different-count") { policy["testSelection"]!["expectedExcludedRows"] = 22; }
        Assert.ThrowsExactly<InvalidDataException>(() => ShippingCoverageCollection.PolicyProfile(policy));
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("empirical-present")]
    [DataRow("ownership-missing")]
    [DataRow("polling-missing")]
    [DataRow("wrong-source")]
    [DataRow("relative-source")]
    public void ActualUnitSelectionRetainsGcOwnershipAndPollingWhileOmittingOnlyTheEmpiricalMethod(string change)
    {
        const string prefix = "CIS.Tests.UnitTests.IncomingMeasurementsTests.";
        string expectedAssembly = Path.GetFullPath("coverage-language/tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll");
        var rows = new List<FocusedTests.NativeCase>
        {
            Native(prefix + "HeldIndexAndRotatingSlotsRetainAtMostTheirExplicitOccurrenceBounds", "ownership"),
            Native(prefix + "RecordActualRuntimeIdlePollingAndQuietWriterLatency", "poll-1"),
            Native(prefix + "RecordActualRuntimeIdlePollingAndQuietWriterLatency", "poll-5"),
            Native(prefix + "RecordActualRuntimeIdlePollingAndQuietWriterLatency", "poll-10")
        };
        if (change == "empirical-present") { rows.Add(Native(ShippingCoverageCollection.ExcludedMeasurementMethod, "cost")); }
        if (change == "ownership-missing") { rows.RemoveAt(0); }
        if (change == "polling-missing") { rows.RemoveAt(rows.Count - 1); }
        if (change == "wrong-source") { rows[0] = rows[0] with { Source = Path.Combine(Path.GetDirectoryName(expectedAssembly)!, "CIS.Core.BusinessCases.dll") }; }
        if (change == "relative-source") { rows[0] = rows[0] with { Source = "CIS.Core.UnitTests.dll" }; }
        if (change == "valid") { ShippingCoverageCollection.ValidateUnitSelection(rows, expectedAssembly); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => ShippingCoverageCollection.ValidateUnitSelection(rows, expectedAssembly)); }

        FocusedTests.NativeCase Native(string method, string id) => new(id, id, id, method, expectedAssembly, "Passed");
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("category-filter")]
    [DataRow("missing-unit-filter")]
    [DataRow("other-family-filter")]
    [DataRow("duplicate-family")]
    [DataRow("failed-command")]
    public void ActualNativeCommandLogMustMatchTheDeclaredSelection(string change)
    {
        using var temporary = new TemporaryDirectory();
        string language = Path.Combine(temporary.Path, "language"), reports = Path.Combine(temporary.Path, "reports");
        const string settings = "verification/coverage/shipping.runsettings";
        var commands = new JsonArray(ShippingCoverageCollection.ReceiptFamilies.Select(family => (JsonNode)new JsonObject
        {
            ["family"] = family.Key,
            ["executable"] = "dotnet",
            ["workingDirectory"] = language,
            ["exitCode"] = 0,
            ["argv"] = JsonSerializer.SerializeToNode(ShippingCoverageCollection.CollectorArguments(family.Value, settings, Path.Combine(reports, family.Key)))
        }).ToArray());
        JsonArray unit = commands[0]!["argv"]!.AsArray();
        if (change == "category-filter") { unit[unit.Count - 1] = "TestCategory!=PerformanceObservation"; }
        if (change == "missing-unit-filter") { unit.RemoveAt(unit.Count - 1); unit.RemoveAt(unit.Count - 1); }
        if (change == "other-family-filter") { commands[1]!["argv"]!.AsArray().Add("--filter"); commands[1]!["argv"]!.AsArray().Add(ShippingCoverageCollection.UnitFilter); }
        if (change == "duplicate-family") { commands[1]!["family"] = "unit"; }
        if (change == "failed-command") { commands[0]!["exitCode"] = 1; }
        if (change == "valid") { HealthcheckCoverageVerification.ValidateShippingCommands(commands, language, reports, settings); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateShippingCommands(commands, language, reports, settings)); }
    }

    private static JsonObject SelectionPolicy() => new()
    {
        ["collectionProfile"] = ShippingCoverageCollection.ReceiptProfile,
        ["testSelection"] = ShippingCoverageCollection.TestSelection()
    };

    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing-helper")]
    [DataRow("extra-module")]
    [DataRow("duplicate-core")]
    [DataRow("source-exclusion")]
    public void ShippingSettingsRetainTheActualTwoModulesWithoutExecutableExclusions(string change)
    {
        XDocument settings = XDocument.Load(Path.Combine(LanguageRoot(), "verification/coverage/shipping.runsettings"));
        XElement include = settings.Descendants("ModulePaths").Single().Element("Include")!;
        if (change == "missing-helper") { include.Elements().Last().Remove(); }
        if (change == "extra-module") { include.Add(new XElement("ModulePath", "Other.dll")); }
        if (change == "duplicate-core") { include.Elements().Last().Value = include.Elements().First().Value; }
        if (change == "source-exclusion") { settings.Descendants("Sources").Single().Element("Exclude")!.Value = "Hidden.cs"; }
        if (change == "valid")
        {
            HealthcheckCoverageVerification.ValidateSettings(settings, shipping: true);
            Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateSettings(settings));
        }
        else { Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateSettings(settings, shipping: true)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing-pin")]
    [DataRow("missing-archive")]
    [DataRow("stale-pin")]
    [DataRow("wrong-version")]
    public void CollectorReceiptUsesTheActualRestoredArchiveAndRejectsMissingOrChangedPins(string change)
    {
        using var fixture = new ReceiptFixture();
        string archive = ShippingCoverageCollection.FindCollectorPackage(fixture.Language, ShippingCoverageCollection.ReceiptFamilies.Values);
        Assert.AreEqual(fixture.Collector, archive);
        JsonObject receipt = fixture.Create();
        if (change == "missing-pin") { receipt.Remove("collectorPackage"); }
        if (change == "missing-archive") { File.Delete(archive); }
        if (change == "stale-pin") { File.AppendAllText(archive, "changed bytes"); }
        if (change == "wrong-version")
        {
            File.Delete(archive); fixture.CollectorArchive("18.10.2");
            receipt["collectorPackage"]!["sha256"] = Hash(archive);
        }
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        if (change == "valid") { Assert.AreEqual(archive, HealthcheckCoverageVerification.ValidateCollectorReference(receipt, fixture.Root, pins)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateCollectorReference(receipt, fixture.Root, pins)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("module-name")]
    [DataRow("source-root")]
    [DataRow("missing-source")]
    [DataRow("changed-helper-source")]
    [DataRow("added-helper-source")]
    [DataRow("changed-helper-binary")]
    public void TheSupersetKeepsActualHelperPackageBinaryAndSourceIdentity(string change)
    {
        using var fixture = new ReceiptFixture();
        JsonObject receipt = fixture.Create();
        Assert.AreEqual(false, (bool?)receipt["complete"]);
        Assert.IsTrue(receipt["families"]!.AsArray().All(family => (string?)family!["status"] == "NotRun" && family["exitCode"] is null));
        JsonObject helper = receipt["modules"]!.AsArray().OfType<JsonObject>().Single(module => (string?)module["name"] == "CIS.TypeManager.Build");
        if (change == "module-name") { helper["name"] = "Other"; }
        if (change == "source-root") { helper["sourceRoot"] = "../outside"; }
        if (change == "missing-source") { helper["sourceInputs"]!.AsArray().Clear(); }
        if (change == "changed-helper-source") { File.AppendAllText(fixture.HelperSource, " changed"); }
        if (change == "added-helper-source") { Write(Path.Combine(Path.GetDirectoryName(fixture.HelperSource)!, "Added.cs"), "extra helper source"); }
        if (change == "changed-helper-binary") { File.AppendAllText(fixture.Helper, " changed"); }
        if (change == "valid") { Assert.HasCount(2, HealthcheckCoverageVerification.ValidateShippingModules(fixture.Language, fixture.Package, receipt, fixture.Policy)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateShippingModules(fixture.Language, fixture.Package, receipt, fixture.Policy)); }
    }

    [TestMethod]
    public void HealthcheckSelectionSumsDisjointRecomputedSourcesAndKeepsOtherModulesSeparate()
    {
        const string path = "src/CIS.Core/Healthcheck/Measured.cs";
        string hash = new('a', 64);
        var sources = new Dictionary<string, HealthcheckCoverageVerification.Source>(StringComparer.Ordinal) { [path] = new(path, hash, true) };
        var source = new JsonObject
        {
            ["path"] = "implementations/csharp/" + path,
            ["sha256"] = hash,
            ["nonExecutableReason"] = null,
            ["linesCovered"] = 19L,
            ["linesTotal"] = 20L,
            ["branchesCoveredLowerBound"] = 18L,
            ["branchesTotal"] = 20L,
            ["uncoveredLines"] = new JsonArray(20),
            ["partiallyCoveredBranchLines"] = new JsonArray(19, 20)
        };
        var shipping = new JsonObject
        {
            ["schema"] = "cis.csharp.shipping-coverage-observation/v1",
            ["assemblies"] = new JsonArray(new JsonObject { ["assembly"] = "CIS.Core", ["sources"] = new JsonArray(source) },
                new JsonObject { ["assembly"] = "CIS.TypeManager.Build", ["sources"] = new JsonArray(), ["linesCovered"] = 0L, ["linesTotal"] = 10000L })
        };
        JsonObject selected = HealthcheckCoverageVerification.SelectShippingHealthcheck(shipping, sources);
        Assert.AreEqual(true, (bool?)selected["qualified"]);
        Assert.AreEqual(19L, (long?)selected["aggregate"]!["linesCovered"]);
        Assert.AreEqual(20L, (long?)selected["aggregate"]!["linesTotal"]);
        Assert.AreEqual(18L, (long?)selected["aggregate"]!["branchesCoveredLowerBound"]);
        source["sha256"] = new string('b', 64);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.SelectShippingHealthcheck(shipping, sources));
    }

    [TestMethod]
    [DataRow("Contracts/HealthEnums.cs")]
    [DataRow("Contracts/Execution/HealthExecutionEnums.cs")]
    [DataRow("Contracts/HealthMonitorContracts.cs")]
    [DataRow("Contracts/Execution/IHealthExecutionBinding.cs")]
    [DataRow("Contracts/Collection/IHealthCollector.cs")]
    [DataRow("Internal/Contracts/IHealthStateAccess.cs")]
    [DataRow("Internal/Contracts/IHealthExecutionState.cs")]
    [DataRow("Internal/Contracts/IHealthCollectionState.cs")]
    public void SharedDeclarationAuthorityAcceptsOnlyItsReviewedBytes(string relative)
    {
        string path = "src/CIS.Core/Healthcheck/" + relative, text = File.ReadAllText(Path.Combine(LanguageRoot(), path));
        HealthcheckCoverageVerification.ValidateDeclarationOnly(path, text);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateDeclarationOnly(path, text + "\ninternal class UnreviewedExecutable {}"));
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckCoverageVerification.ValidateDeclarationOnly(path + ".unknown", text));
    }

    private static JsonObject CurrentProfile() => new()
    {
        ["schema"] = ShippingCoverageCollection.ReceiptSchema,
        ["profile"] = ShippingCoverageCollection.ReceiptProfile,
        ["testSelection"] = ShippingCoverageCollection.TestSelection(),
        ["uninstrumentedObservationsExecuted"] = false,
        ["commands"] = new JsonObject { ["path"] = "commands.json", ["sha256"] = new string('a', 64) },
        ["complete"] = true,
        ["families"] = new JsonArray(ShippingCoverageCollection.ReceiptFamilies.Keys.Select(name => (JsonNode)new JsonObject
        {
            ["name"] = name,
            ["status"] = "Passed",
            ["exitCode"] = 0,
            ["outputDrained"] = true,
            ["trxSha256"] = new string('a', 64),
            ["filter"] = name == "unit" ? ShippingCoverageCollection.UnitFilter : null
        }).ToArray())
    };

    private static string LanguageRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        { if (File.Exists(Path.Combine(directory.FullName, "CisLib.slnx"))) { return directory.FullName; } }
        throw new DirectoryNotFoundException("Run the maintained Tool tests inside the C# checkout.");
    }

    private sealed class ReceiptFixture : IDisposable
    {
        private readonly TemporaryDirectory temporary = new();
        internal string Root => temporary.Path;
        internal string Language { get; }
        internal string Package { get; }
        internal string Collector { get; }
        internal string Helper { get; }
        internal string HelperSource { get; }
        internal JsonObject Policy { get; }
        internal ReceiptFixture()
        {
            Language = Path.Combine(Root, "implementations/csharp"); Package = Path.Combine(Root, "fixture.nupkg");
            Collector = Path.Combine(Root, "cache/microsoft.codecoverage/18.10.1/microsoft.codecoverage.18.10.1.nupkg");
            Policy = Read(Path.Combine(LanguageRoot(), "verification/coverage/shipping.json"));
            string core = Path.Combine(Language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll");
            Helper = Path.Combine(Language, "eng/CIS.TypeManager.Build/bin/Release/net10.0/CIS.TypeManager.Build.dll");
            HelperSource = Path.Combine(Language, "eng/CIS.TypeManager.Build/Fixture.cs");
            Write(core, "opaque fixture Core"); Write(Helper, "opaque fixture helper");
            Write(Path.Combine(Language, "src/CIS.Core/Healthcheck/Fixture.cs"), "Core fixture source"); Write(HelperSource, "helper fixture source");
            string settings = Path.Combine(Language, "verification/coverage/shipping.runsettings");
            Copy(Path.Combine(LanguageRoot(), "verification/coverage/shipping.runsettings"), settings);
            WriteJson(Path.Combine(Language, "verification/coverage/shipping.json"), Policy);
            using (ZipArchive archive = ZipFile.Open(Package, ZipArchiveMode.Create))
            {
                foreach ((string entry, string path) in new[] { ("lib/net10.0/CIS.Core.dll", core), ("tools/net10.0/CIS.TypeManager.Build.dll", Helper) })
                { using Stream stream = archive.CreateEntry(entry).Open(); stream.Write(File.ReadAllBytes(path)); }
            }
            CollectorArchive("18.10.1");
            foreach (string suffix in ShippingCoverageCollection.ReceiptFamilies.Values)
            {
                Copy(core, Path.Combine(Language, "tests/CIS.Core." + suffix, "bin/Release/net10.0/CIS.Core.dll"));
                WriteJson(Path.Combine(Language, "tests/CIS.Core." + suffix, "obj/project.assets.json"), new JsonObject
                {
                    ["libraries"] = new JsonObject { ["Microsoft.CodeCoverage/18.10.1"] = new JsonObject { ["type"] = "package", ["path"] = "microsoft.codecoverage/18.10.1" } },
                    ["packageFolders"] = new JsonObject { [Path.Combine(Root, "cache")] = new JsonObject() }
                });
            }
        }
        internal JsonObject Create() => ShippingCoverageCollection.CreateReceipt(Language, Policy, Package, Collector);
        internal void CollectorArchive(string version)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Collector)!);
            using ZipArchive archive = ZipFile.Open(Collector, ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry("Microsoft.CodeCoverage.nuspec").Open());
            writer.Write("<package><metadata><id>Microsoft.CodeCoverage</id><version>" + version + "</version></metadata></package>");
        }
        public void Dispose() => temporary.Dispose();
    }
}
