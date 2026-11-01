using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ShippingCoverageTests
{
    [TestMethod]
    public async Task NativePointsRetainClassIdentityAndUseConservativeBranchMaximum()
    {
        using var fixture = new Fixture();
        fixture.Coverage.Root!.SetAttributeValue("branches-covered", 3);
        fixture.CoreLine.SetAttributeValue("condition-coverage", "50% (1/2)");
        fixture.Save();
        string second = fixture.AddReport("second");
        JsonObject result = await fixture.Summarize(second);
        Assert.AreEqual(3L, result["aggregate"]!["branchesCoveredLowerBound"]!.GetValue<long>());
        Assert.AreEqual(4L, result["aggregate"]!["branchesTotal"]!.GetValue<long>());
        Assert.IsFalse(result["qualified"]!.GetValue<bool>(), "Two unidentified branch halves must not be summed into full coverage.");
        Assert.AreEqual(2, result["reports"]!.AsArray().Count);

        var separateClass = new XElement(fixture.CoreClass);
        separateClass.SetAttributeValue("name", "AnotherClassOnSameLine");
        fixture.CoreClass.AddAfterSelf(separateClass);
        fixture.Coverage.Root.SetAttributeValue("lines-covered", 3);
        fixture.Coverage.Root.SetAttributeValue("lines-valid", 3);
        fixture.Coverage.Root.SetAttributeValue("branches-covered", 4);
        fixture.Coverage.Root.SetAttributeValue("branches-valid", 6);
        fixture.Save();
        result = await fixture.Summarize();
        Assert.AreEqual(3L, result["aggregate"]!["linesTotal"]!.GetValue<long>());
        Assert.AreEqual(6L, result["aggregate"]!["branchesTotal"]!.GetValue<long>());
    }

    [TestMethod]
    public async Task LineExecutionIsOrMergedAndNonExecutableFilesRemainExplicit()
    {
        using var fixture = new Fixture();
        string source = fixture.AddSource("src/CIS.Core/Declaration.cs");
        fixture.Policy["assemblies"]![0]!["nonExecutable"]!["Declaration.cs"] = "Declarations only.";
        fixture.CoreLine.AddAfterSelf(new XElement("line", new XAttribute("number", 2), new XAttribute("hits", 0)));
        fixture.Coverage.Root!.SetAttributeValue("lines-valid", 3);
        fixture.Save();
        string second = fixture.AddReport("second");
        XDocument changed = XDocument.Load(Path.Combine(second, "native.cobertura.xml"));
        changed.Descendants("line").Single(line => (string?)line.Attribute("number") == "2").SetAttributeValue("hits", 1);
        changed.Root!.SetAttributeValue("lines-covered", 3);
        changed.Save(Path.Combine(second, "native.cobertura.xml"));
        JsonObject result = await fixture.Summarize(second);
        Assert.IsTrue(result["qualified"]!.GetValue<bool>());
        Assert.AreEqual(3L, result["aggregate"]!["linesCovered"]!.GetValue<long>());
        JsonNode declaration = result["assemblies"]![0]!["sources"]!.AsArray().Single(item => item!["path"]!.GetValue<string>().EndsWith("Declaration.cs", StringComparison.Ordinal))!;
        Assert.AreEqual("Declarations only.", declaration["nonExecutableReason"]!.GetValue<string>());
        Assert.AreEqual(0L, declaration["linesTotal"]!.GetValue<long>());
        Assert.IsNull(declaration["lineRate"]);
        Assert.IsTrue(File.Exists(source));
    }

    [TestMethod]
    public async Task EveryShippingAssemblyMustPassEvenWhenAggregateMeetsThreshold()
    {
        using var fixture = new Fixture();
        fixture.Policy["minimumBranchRate"] = 0.75;
        fixture.HelperLine.SetAttributeValue("condition-coverage", "50% (1/2)");
        fixture.Coverage.Root!.SetAttributeValue("branches-covered", 3);
        fixture.Save();
        JsonObject result = await fixture.Summarize();
        Assert.IsTrue(result["aggregate"]!["thresholdPassed"]!.GetValue<bool>());
        Assert.IsFalse(result["qualified"]!.GetValue<bool>());
    }

    [TestMethod]
    [DataRow("Contracts/HealthEnums.cs")]
    [DataRow("Contracts/Unreviewed.cs")]
    public async Task HealthcheckDeclarationLabelsCannotHideChangedOrUnknownExecutableSource(string relative)
    {
        using var fixture = new Fixture();
        string source = fixture.AddSource("src/CIS.Core/Healthcheck/" + relative);
        File.WriteAllText(source, "namespace CIS.Healthcheck; public static class HiddenCode { public static int Run() => 42; }");
        fixture.Policy["assemblies"]![0]!["nonExecutable"]!["Healthcheck/" + relative] = "Declaration-only label.";
        fixture.Save();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.Summarize());
    }

    [TestMethod]
    [DataRow("empty-tests")]
    [DataRow("failed-tests")]
    [DataRow("skipped-tests")]
    [DataRow("multiple-counters")]
    [DataRow("missing-attachment")]
    [DataRow("multiple-attachments")]
    [DataRow("conflicting-attachment")]
    [DataRow("unexpected-assembly")]
    [DataRow("unmapped-source")]
    [DataRow("wrong-owner")]
    [DataRow("nonexecutable-point")]
    [DataRow("duplicate-point")]
    [DataRow("missing-branch-count")]
    [DataRow("invalid-branch-count")]
    [DataRow("denominator-drift")]
    [DataRow("native-totals")]
    [DataRow("unmeasured-source")]
    [DataRow("missing-nonexecutable")]
    [DataRow("compile-inventory")]
    [DataRow("calibration")]
    [DataRow("assembly-no-branches")]
    [DataRow("outside-root")]
    [DataRow("invalid-policy")]
    public async Task InvalidEvidenceFailsClosed(string fault)
    {
        using var fixture = new Fixture();
        string? second = null;
        switch (fault)
        {
            case "empty-tests": fixture.Counter.SetAttributeValue("total", 0); fixture.Counter.SetAttributeValue("passed", 0); break;
            case "failed-tests": fixture.Counter.SetAttributeValue("passed", 1); fixture.Counter.SetAttributeValue("failed", 1); break;
            case "skipped-tests": fixture.Counter.SetAttributeValue("passed", 1); fixture.Counter.SetAttributeValue("notExecuted", 1); break;
            case "multiple-counters": fixture.Counter.AddAfterSelf(new XElement(fixture.Counter)); break;
            case "missing-attachment": fixture.Trx.Descendants("A").Single().Remove(); break;
            case "multiple-attachments": fixture.Trx.Root!.Add(new XElement("A", new XAttribute("href", "another.cobertura.xml"))); break;
            case "conflicting-attachment":
                Directory.CreateDirectory(Path.Combine(fixture.Report, "conflict"));
                File.WriteAllText(Path.Combine(fixture.Report, "conflict/native.cobertura.xml"), "different bytes"); break;
            case "unexpected-assembly": fixture.CoreClass.Parent!.Parent!.SetAttributeValue("name", "Other"); break;
            case "unmapped-source": fixture.CoreClass.SetAttributeValue("filename", "unknown.cs"); break;
            case "wrong-owner": fixture.CoreClass.SetAttributeValue("filename", "/native/implementations/csharp/eng/Helper/Task.cs"); break;
            case "nonexecutable-point": fixture.Policy["assemblies"]![0]!["nonExecutable"]!["SeverityTokenParser.cs"] = "Claimed nonexecutable"; break;
            case "duplicate-point": fixture.CoreLine.AddAfterSelf(new XElement(fixture.CoreLine)); break;
            case "missing-branch-count": fixture.CoreLine.Attribute("condition-coverage")!.Remove(); break;
            case "invalid-branch-count": fixture.CoreLine.SetAttributeValue("condition-coverage", "150% (3/2)"); break;
            case "denominator-drift":
                second = fixture.AddReport("second");
                XDocument changed = XDocument.Load(Path.Combine(second, "native.cobertura.xml"));
                changed.Descendants("line").First().SetAttributeValue("condition-coverage", "100% (3/3)");
                changed.Root!.SetAttributeValue("branches-covered", 5); changed.Root.SetAttributeValue("branches-valid", 5);
                changed.Save(Path.Combine(second, "native.cobertura.xml")); break;
            case "native-totals": fixture.Coverage.Root!.SetAttributeValue("lines-valid", 99); break;
            case "unmeasured-source": fixture.AddSource("src/CIS.Core/Unmeasured.cs"); break;
            case "missing-nonexecutable": fixture.Policy["assemblies"]![0]!["nonExecutable"]!["Missing.cs"] = "Not there"; break;
            case "compile-inventory": fixture.CompileFiles.RemoveAt(0); break;
            case "calibration": fixture.CoreLine.SetAttributeValue("condition-coverage", "0% (0/2)"); fixture.Coverage.Root!.SetAttributeValue("branches-covered", 2); break;
            case "assembly-no-branches": fixture.HelperLine.SetAttributeValue("branch", "false"); fixture.Coverage.Root!.SetAttributeValue("branches-covered", 2); fixture.Coverage.Root.SetAttributeValue("branches-valid", 2); break;
            case "outside-root": fixture.Policy["assemblies"]![0]!["sourceRoot"] = "../outside"; break;
            case "invalid-policy": fixture.Policy["minimumLineRate"] = 1.1; break;
            default: Assert.Fail("Unknown fault."); break;
        }
        fixture.Save();
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.Summarize(second));
        if (fault == "duplicate-point") { StringAssert.Contains(error.Message, "Duplicate native class/source/line identity."); }
        if (fault == "invalid-branch-count") { StringAssert.Contains(error.Message, "Invalid native branch counts."); }
    }

    [TestMethod]
    public async Task MissingReportsAndMalformedXmlCannotBecomePassingObservations()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ShippingCoverage.SummarizeAsync(fixture.Language, [], fixture.Evaluate));
        File.WriteAllText(Path.Combine(fixture.Report, "report.trx"), "<!DOCTYPE root [<!ENTITY x SYSTEM 'file:///never-read'>]><root>&x;</root>");
        await Assert.ThrowsExactlyAsync<System.Xml.XmlException>(() => fixture.Summarize());
    }

    [TestMethod]
    public async Task CommandRejectsMissingArgumentsAndNeverOverwritesAnExistingReport()
    {
        Assert.AreEqual(1, await ShippingCoverage.RunAsync([]));
        using var fixture = new Fixture();
        string output = Path.Combine(fixture.Language, "existing.json");
        File.WriteAllText(output, "retained evidence");
        Assert.AreEqual(1, await ShippingCoverage.RunAsync([fixture.Language, output, fixture.Report]));
        Assert.AreEqual("retained evidence", File.ReadAllText(output));
        fixture.Policy["assemblies"]!.AsArray().Clear();
        fixture.Counter.SetAttributeValue("total", 0);
        fixture.Counter.SetAttributeValue("passed", 0);
        fixture.Save();
        string invalidOutput = Path.Combine(fixture.Language, "invalid.json");
        Assert.AreEqual(1, await ShippingCoverage.RunAsync([fixture.Language, invalidOutput, fixture.Report]));
        Assert.IsFalse(File.Exists(invalidOutput), "Invalid evidence must not produce a success-shaped output.");
    }

    [TestMethod]
    public async Task CoverageRejectsFocusedInspectionOverrideBeforeCreatingReports()
    {
        using var directory = new TemporaryDirectory();
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "implementations/csharp/CisLib.slnx"))) { root = root.Parent; }
        Assert.IsNotNull(root);
        string output = Path.Combine(directory.Path, "untouched");
        ProcessResult result = await ProcessRunner.RunAsync("pwsh",
            ["-NoProfile", "-File", Path.Combine(root.FullName, "implementations/csharp/eng/verify-shipping-coverage.ps1"), "-OutputRoot", output,
                "-VerificationRoot", Path.Combine(directory.Path, "unprepared"), "-PackagePath", Path.Combine(directory.Path, "unprepared.nupkg")],
            root.FullName, TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["CIS_ARCHITECTURE_INSPECTION"] = "arbitrary-focused-fixture.json" });
        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.StandardError, "CIS_ARCHITECTURE_INSPECTION is only for focused tests.");
        Assert.IsFalse(Directory.Exists(output));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        internal string Language { get; }
        internal string Report { get; }
        internal List<string> CompileFiles { get; } = [];
        internal JsonObject Policy { get; }
        internal XDocument Trx { get; } = XDocument.Parse("<TestRun><Counters total='2' passed='2'/><A href='nested/native.cobertura.xml'/></TestRun>");
        internal XElement Counter => Trx.Descendants("Counters").Single();
        internal XDocument Coverage { get; } = XDocument.Parse("""
            <coverage lines-covered="2" lines-valid="2" branches-covered="4" branches-valid="4"><packages>
              <package name="CIS.Core"><classes><class name="Parser" filename="/native/implementations/csharp/src/CIS.Core/SeverityTokenParser.cs"><lines><line number="1" hits="1" branch="true" condition-coverage="100% (2/2)"/></lines></class></classes></package>
              <package name="CIS.TypeManager.Build"><classes><class name="Task" filename="/native/implementations/csharp/eng/Helper/Task.cs"><lines><line number="1" hits="1" branch="true" condition-coverage="100% (2/2)"/></lines></class></classes></package>
            </packages></coverage>
            """);
        internal XElement CoreClass => Coverage.Descendants("class").First();
        internal XElement CoreLine => CoreClass.Descendants("line").First();
        internal XElement HelperLine => Coverage.Descendants("class").Last().Descendants("line").Single();

        internal Fixture()
        {
            Language = Path.Combine(directory.Path, "implementations/csharp");
            Report = Path.Combine(directory.Path, "results");
            Directory.CreateDirectory(Path.Combine(Language, "verification/coverage"));
            Directory.CreateDirectory(Report);
            AddSource("src/CIS.Core/SeverityTokenParser.cs"); AddSource("eng/Helper/Task.cs");
            Policy = JsonNode.Parse("""
                {"minimumLineRate":0.95,"minimumBranchRate":0.9,"aggregation":"Conservative native counts","exclusions":"Explicit declarations only",
                 "assemblies":[{"name":"CIS.Core","sourceRoot":"src/CIS.Core","project":"core.csproj","nonExecutable":{}},
                               {"name":"CIS.TypeManager.Build","sourceRoot":"eng/Helper","project":"helper.csproj","nonExecutable":{}}]}
                """)!.AsObject();
            Save();
        }
        internal string AddSource(string relative)
        {
            string path = Path.Combine(Language, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "// Synthetic source inventory for coverage-parser controls.");
            CompileFiles.Add(path); return path;
        }
        internal void Save()
        {
            File.WriteAllText(Path.Combine(Language, "verification/coverage/shipping.json"), Policy.ToJsonString());
            Trx.Save(Path.Combine(Report, "report.trx")); Coverage.Save(Path.Combine(Report, "native.cobertura.xml"));
        }
        internal string AddReport(string name)
        {
            string path = Path.Combine(directory.Path, name); Directory.CreateDirectory(path);
            Trx.Save(Path.Combine(path, "report.trx")); Coverage.Save(Path.Combine(path, "native.cobertura.xml")); return path;
        }
        internal Task<string> Evaluate(string project)
        {
            string root = project == "core.csproj" ? "src/CIS.Core" : "eng/Helper";
            return Task.FromResult(new JsonObject { ["Items"] = new JsonObject { ["Compile"] = new JsonArray(CompileFiles.Where(path => path.StartsWith(Path.Combine(Language, root), StringComparison.Ordinal)).Select(path => (JsonNode)new JsonObject { ["FullPath"] = path }).ToArray()) } }.ToJsonString());
        }
        internal Task<JsonObject> Summarize(string? second = null) => ShippingCoverage.SummarizeAsync(Language, second is null ? [Report] : [Report, second], Evaluate);
        public void Dispose() => directory.Dispose();
    }
}
