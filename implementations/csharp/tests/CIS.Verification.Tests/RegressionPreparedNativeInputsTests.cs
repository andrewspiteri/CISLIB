using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Tests.Verification;

[TestClass]
public sealed class RegressionPreparedNativeInputsTests
{
    [TestMethod]
    public void OnlyIntegrationUsesAnOwnedEightWorkerCopyWithoutChangingOtherSettings()
    {
        using var directory = new TemporaryDirectory();
        string language = directory.Path;
        Directory.CreateDirectory(Path.Combine(language, "verification"));
        string canonical = Path.Combine(language, "verification/regression.runsettings");
        const string settings = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<RunSettings><!-- retained -->"
            + "<RunConfiguration><MaxCpuCount>3</MaxCpuCount></RunConfiguration><MSTest><Parallelize>"
            + "<Workers>4</Workers><Scope>MethodLevel</Scope></Parallelize><TestTimeout>12345</TestTimeout></MSTest></RunSettings>\r\n";
        var utf8 = new System.Text.UTF8Encoding(true, true);
        byte[] original = [.. utf8.GetPreamble(), .. utf8.GetBytes(settings)];
        File.WriteAllBytes(canonical, original);
        string integration = Directory.CreateDirectory(Path.Combine(language, "integration-results")).FullName;
        string copy = RegressionTests.WriteSettings(language, integration, "CIS.Core.IntegrationTests");
        Assert.AreEqual(Path.Combine(integration, "integration.runsettings"), copy);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(canonical));
        CollectionAssert.AreEqual(new byte[] { 239, 187, 191 }.Concat(utf8.GetBytes(settings.Replace("<Workers>4</Workers>", "<Workers>8</Workers>", StringComparison.Ordinal))).ToArray(),
            File.ReadAllBytes(copy));
        Assert.AreEqual(8, RegressionTests.MethodWorkers("CIS.Core.IntegrationTests"));

        foreach (string project in new[] { "CIS.Core.UnitTests", "CIS.Core.ArchitectureTests", "CIS.Core.CompatibilityTests", "CIS.Verification.Tests", "CIS.Core.BusinessCases" })
        {
            string unused = Path.Combine(language, project);
            Assert.AreEqual(canonical, RegressionTests.WriteSettings(language, unused, project));
            Assert.IsFalse(Directory.Exists(unused), "A default host must use the maintained settings without a new copy.");
            Assert.AreEqual(4, RegressionTests.MethodWorkers(project));
        }
        string fastCanonical = Path.Combine(language, "verification/fast.runsettings");
        File.WriteAllBytes(fastCanonical, original);
        foreach ((string project, int expected) in new[] { ("CIS.Core.FastTests", 22), ("CIS.Core.FastLoggingTests", 4), ("CIS.Core.FastIsolationTests", 8), ("CIS.Core.FastArchitectureTests", 4) })
        {
            string results = Directory.CreateDirectory(Path.Combine(language, project)).FullName;
            string fast = RegressionTests.WriteSettings(language, results, project);
            Assert.AreEqual(Path.Combine(results, "fast.runsettings"), fast);
            StringAssert.Contains(File.ReadAllText(fast), "<Workers>" + expected + "</Workers>");
            Assert.AreEqual(expected, RegressionTests.MethodWorkers(project));
        }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(fastCanonical));
        File.WriteAllBytes(canonical, [.. utf8.GetPreamble(), .. utf8.GetBytes(settings.Replace("<Workers>4</Workers>", "<Workers>4</Workers><Workers>4</Workers>", StringComparison.Ordinal))]);
        string rejected = Directory.CreateDirectory(Path.Combine(language, "rejected-results")).FullName;
        Assert.ThrowsExactly<InvalidDataException>(() => RegressionTests.WriteSettings(language, rejected, "CIS.Core.IntegrationTests"));
        Assert.IsFalse(File.Exists(Path.Combine(rejected, "integration.runsettings")));
    }

    [TestMethod]
    [DataRow(true, 290d, 300, 295d, true, true, true)]
    [DataRow(true, 290d, 300, 305d, true, true, false)]
    [DataRow(true, 320d, 1500, 335d, true, false, false)]
    [DataRow(true, 305d, 300, 315d, false, false, false)]
    [DataRow(false, 100d, 300, 115d, true, false, false)]
    public void CorrectnessBudgetAndBothPerformanceTargetsHaveIndependentOutcomes(bool correct, double nativeSeconds,
        int budgetSeconds, double commandSeconds, bool budgetMet, bool nativeTargetMet, bool commandTargetMet)
    {
        var summary = new JsonObject();
        RegressionTests.RecordMeasurements(summary, correct, nativeSeconds, budgetSeconds, commandSeconds);
        Assert.AreEqual(correct, (bool)summary["correctnessComplete"]!);
        Assert.AreEqual(budgetMet, (bool)summary["nativeBudgetMet"]!);
        Assert.AreEqual(nativeTargetMet, (bool)summary["nativeTargetMet"]!);
        Assert.AreEqual(commandTargetMet, (bool)summary["commandTargetMet"]!);
        Assert.AreEqual(commandSeconds, (double)summary["commandSeconds"]!);
    }

    [TestMethod]
    public void PreparationBindsEveryNativeOutputIncludingRuntimeConfigurationAndNestedDependencies()
    {
        using var fixture = new Fixture();
        SourceInventoryEntry[] files = fixture.Verify();
        Assert.HasCount(9 * 4 + 1, files);
        Assert.IsTrue(files.Any(pin => pin.Path.EndsWith("native/libfixture.so", StringComparison.Ordinal)));
        Assert.IsTrue(files.Any(pin => pin.Path.EndsWith(".runtimeconfig.json", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(files.Select(pin => pin.Path).Order(StringComparer.Ordinal).ToArray(), files.Select(pin => pin.Path).ToArray());
    }

    [TestMethod]
    [DataRow("missing-pdb")]
    [DataRow("changed-runtimeconfig")]
    [DataRow("changed-nested-dependency")]
    [DataRow("added-dependency")]
    [DataRow("source-manifest")]
    [DataRow("omitted-file")]
    [DataRow("mutation-cohort")]
    [DataRow("omitted-root")]
    public void ReuseRejectsChangedOrIncompletePreparedNativeInputs(string change)
    {
        using var fixture = new Fixture();
        JsonObject manifest = Read(fixture.Manifest);
        string first = Path.Combine(fixture.Root, RegressionTests.NativeInputRoots[0], "net10.0/CIS.Core.UnitTests");
        switch (change)
        {
            case "missing-pdb": File.Delete(first + ".pdb"); break;
            case "changed-runtimeconfig": Write(first + ".runtimeconfig.json", "changed runtime configuration"); break;
            case "changed-nested-dependency": Write(fixture.Dependency, "changed nested runtime dependency"); break;
            case "added-dependency": Write(Path.Combine(Path.GetDirectoryName(first)!, "Additional.dll"), "new dependency"); break;
            case "source-manifest": File.AppendAllText(fixture.SourceManifest, "\n"); break;
            case "omitted-file": manifest["files"]!.AsArray().RemoveAt(0); break;
            case "mutation-cohort": manifest["buildProperties"]!["CisHealthcheckMutationCohort"] = "true"; break;
            case "omitted-root": manifest["roots"]!.AsArray().RemoveAt(0); break;
            default: throw new InvalidOperationException(change);
        }
        if (change is "omitted-file" or "mutation-cohort" or "omitted-root") { WriteJson(fixture.Manifest, manifest); }
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Verify());
    }

    [TestMethod]
    public void CaptureRejectsStaleSourceBeforeWritingAnyReusableNativeManifest()
    {
        using var fixture = new Fixture();
        Write(fixture.Source, "changed source after preparation");
        string rejected = Path.Combine(fixture.Root, ".artifacts/rejected-native-inputs.json");
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Capture(rejected));
        Assert.IsFalse(File.Exists(rejected));
    }

    [TestMethod]
    public void PartialCopiesKeepAdditionalOriginalMethodsInTheirOriginalHost()
    {
        RegressionTests.TestMethodShape shared = Shape("Partial", "Shared"), extra = Shape("Partial", "FullOnly"), marker = Marker();
        RegressionTests.TestMethodShape[] original = [shared, extra, marker], fast = [shared, marker];
        string[] selected = RegressionTests.SelectFastMethods(original, new Dictionary<string, RegressionTests.TestMethodShape[]> { ["Fast"] = fast })["Fast"];
        CollectionAssert.AreEqual(new[] { marker.Method, shared.Method }.Order(StringComparer.Ordinal).ToArray(), selected);
        string residual = RegressionTests.PartitionFilter(original, selected.Where(method => method != RegressionTests.SchedulingMethod), include: false);
        StringAssert.Contains(residual, "FullyQualifiedName!=" + shared.Method);
        Assert.IsFalse(residual.Contains("FullyQualifiedName!~" + shared.Type + ".", StringComparison.Ordinal));
        Assert.IsFalse(residual.Contains(marker.Method, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("signature")]
    [DataRow("data-row")]
    [DataRow("class-attribute")]
    [DataRow("overload")]
    [DataRow("duplicate-fast-owner")]
    public void DifferentOrAmbiguousMethodsStayFullAndAreExcludedFromFast(string difference)
    {
        RegressionTests.TestMethodShape full = Shape("Partial", "Shared"), marker = Marker();
        RegressionTests.TestMethodShape copy = difference switch
        {
            "signature" => full with { Signature = "different-parameter" },
            "data-row" => full with { Attributes = "different-real-row-blob" },
            "class-attribute" => full with { TypeAttributes = "different-class-control" },
            _ => full
        };
        RegressionTests.TestMethodShape[] original = difference == "overload" ? [full, full with { Signature = "overload" }, marker] : [full, marker];
        var fast = new Dictionary<string, RegressionTests.TestMethodShape[]> { ["Fast"] = [copy, marker] };
        if (difference == "duplicate-fast-owner") { fast["OtherFast"] = [copy, marker]; }
        foreach (string[] selected in RegressionTests.SelectFastMethods(original, fast).Values)
        {
            CollectionAssert.AreEqual(new[] { marker.Method }, selected);
            string filter = RegressionTests.PartitionFilter([copy, marker], selected, include: true);
            Assert.IsFalse(filter.Contains(copy.Method, StringComparison.Ordinal));
            StringAssert.Contains(filter, "CIS.Tests.TestAssemblySetup.");
        }
    }

    [TestMethod]
    public void IdenticalClassesUseCompactFiltersAndKeepEveryHostIdentityControl()
    {
        RegressionTests.TestMethodShape[] methods = [Shape("Complete", "First"), Shape("Complete", "Second"), Marker()];
        string[] selected = RegressionTests.SelectFastMethods(methods, new Dictionary<string, RegressionTests.TestMethodShape[]> { ["Fast"] = methods })["Fast"];
        string fast = RegressionTests.PartitionFilter(methods, selected, include: true);
        string residual = RegressionTests.PartitionFilter(methods, selected.Where(method => method != RegressionTests.SchedulingMethod), include: false);
        StringAssert.Contains(fast, "FullyQualifiedName~CIS.Tests.Partition.Complete.");
        StringAssert.Contains(fast, "CIS.Tests.TestAssemblySetup.");
        StringAssert.Contains(residual, "FullyQualifiedName!~CIS.Tests.Partition.Complete.");
        Assert.IsFalse(residual.Contains("TestAssemblySetup", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CompiledMetadataPreservesRealDataRowBlobsWithoutLoadingAnotherTestAssembly()
    {
        RegressionTests.TestMethodShape method = RegressionTests.ReadTestMethods(typeof(RegressionPreparedNativeInputsTests).Assembly.Location)
            .Single(method => method.Type == typeof(RegressionPreparedNativeInputsTests).FullName && method.Name == nameof(DifferentOrAmbiguousMethodsStayFullAndAreExcludedFromFast));
        Assert.AreEqual(5, method.Attributes.Split('\n').Count(line => line.StartsWith("Microsoft.VisualStudio.TestTools.UnitTesting.DataRowAttribute:", StringComparison.Ordinal)));
        Assert.IsTrue(method.CanPartition);
        Assert.AreNotEqual("", method.Signature);
    }

    [TestMethod]
    public void OptionalFastInputsBindFourRootsWithoutChangingTheOriginalNine()
    {
        using var fixture = new Fixture();
        fixture.AddFastOutputs();
        string manifest = Path.Combine(fixture.Root, ".artifacts/preparation/with-fast.json");
        fixture.Capture(manifest, includeFast: true);
        Assert.HasCount(9 * 4 + 1, RegressionTests.VerifyPreparedNativeInputs(fixture.Root, manifest, fixture.SourceManifest));
        Assert.HasCount(4 * 4, RegressionTests.VerifyPreparedFastInputs(fixture.Root, manifest, fixture.SourceManifest));
        Assert.HasCount(9, RegressionTests.NativeInputRoots);
        Assert.HasCount(4, RegressionTests.FastInputRoots);
    }

    [TestMethod]
    [DataRow("missing-pdb")]
    [DataRow("changed-runtimeconfig")]
    [DataRow("added-dependency")]
    public void ChangedFastBytesRejectSplitReuseWhileOriginalNineInputsRemainValid(string change)
    {
        using var fixture = new Fixture();
        fixture.AddFastOutputs();
        string manifest = Path.Combine(fixture.Root, ".artifacts/preparation/with-fast.json");
        fixture.Capture(manifest, includeFast: true);
        string fast = Path.Combine(fixture.Root, RegressionTests.FastInputRoots[0], "net10.0/CIS.Core.FastTests");
        if (change == "missing-pdb") { File.Delete(fast + ".pdb"); }
        else if (change == "changed-runtimeconfig") { Write(fast + ".runtimeconfig.json", "changed Fast runtime"); }
        else { Write(Path.Combine(Path.GetDirectoryName(fast)!, "Extra.dll"), "new dependency"); }
        Assert.ThrowsExactly<InvalidDataException>(() => RegressionTests.VerifyPreparedFastInputs(fixture.Root, manifest, fixture.SourceManifest));
        Assert.HasCount(9 * 4 + 1, RegressionTests.VerifyPreparedNativeInputs(fixture.Root, manifest, fixture.SourceManifest));
    }

    private static RegressionTests.TestMethodShape Shape(string type, string name) => new("CIS.Tests.Partition." + type, name, "void()", "original-row-blob", "original-test-class");
    private static RegressionTests.TestMethodShape Marker() => new("CIS.Tests.TestAssemblySetup", "SchedulingMatchesRequestedMode", "void()", "host-marker", "original-test-class");

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory owned = new();
        internal string Root => owned.Path;
        internal string Source => Path.Combine(Root, "implementations/csharp/src/CIS.Core/Fixture.cs");
        internal string SourceManifest => Path.Combine(Root, ".artifacts/preparation/source-inputs.json");
        internal string Manifest => Path.Combine(Root, ".artifacts/preparation/native-inputs.json");
        internal string Dependency => Path.Combine(Root, RegressionTests.NativeInputRoots[0], "net10.0/native/libfixture.so");

        internal Fixture()
        {
            Write(Source, "maintained parser fixture input");
            foreach (string relative in RegressionTests.NativeInputRoots)
            {
                string project = relative.Split('/')[3];
                foreach (string extension in new[] { ".dll", ".pdb", ".deps.json", ".runtimeconfig.json" })
                { Write(Path.Combine(Root, relative, "net10.0", project + extension), project + extension); }
            }
            // Opaque bytes exercise inventory/pin mechanics, not a built or passing native candidate.
            Write(Dependency, "actual nested fixture dependency bytes");
            Write(SourceManifest, JsonSerializer.Serialize(SourceInventory.CaptureVerificationInputs(Root)));
            Assert.AreEqual(0, Capture(Manifest));
        }

        internal int Capture(string path, bool includeFast = false) => RegressionTests.CapturePreparedNativeInputs(
            ["--repo-root", Root, "--source-inputs", SourceManifest, "--output-path", path, "--include-fast", includeFast.ToString()]);
        internal void AddFastOutputs()
        {
            foreach (string relative in RegressionTests.FastInputRoots)
            {
                string project = relative.Split('/')[3];
                foreach (string extension in new[] { ".dll", ".pdb", ".deps.json", ".runtimeconfig.json" })
                { Write(Path.Combine(Root, relative, "net10.0", project + extension), project + extension); }
            }
        }
        internal SourceInventoryEntry[] Verify() => RegressionTests.VerifyPreparedNativeInputs(Root, Manifest, SourceManifest);
        public void Dispose() => owned.Dispose();
    }
}
