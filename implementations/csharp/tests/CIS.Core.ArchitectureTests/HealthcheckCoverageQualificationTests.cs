using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckCoverageVerification;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckCoverageQualificationTests
{
    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"complete\":true}")]
    public void LegacyAbsenceAndExplicitCompletedCollectionRemainEligibleForNativeValidation(string json)
    {
        ValidateCollectionCompletion(JsonNode.Parse(json)!.AsObject());
    }

    [TestMethod]
    [DataRow("false")]
    [DataRow("null")]
    [DataRow("\"true\"")]
    [DataRow("\"false\"")]
    [DataRow("0")]
    [DataRow("[]")]
    [DataRow("{}")]
    public void IncompleteOrMalformedCompletionCannotBeOverriddenByPassingFamilyExits(string completion)
    {
        JsonObject collection = JsonNode.Parse("{\"complete\":" + completion + "}")!.AsObject();
        var families = new JsonArray();
        foreach (string name in new[] { "unit", "integration", "business" })
        { families.Add(new JsonObject { ["name"] = name, ["exitCode"] = 0 }); }
        collection["families"] = families;
        Assert.ThrowsExactly<InvalidDataException>(() => ValidateCollectionCompletion(collection));
    }

    [TestMethod]
    public void NativeTotalsIncludeUnrelatedCoreBeforeHealthcheckIsSelected()
    {
        using var fixture = new NativeFixture();
        NativeReport native = ParseCoverage(fixture.Document, fixture.Language, fixture.Sources);
        Assert.AreEqual(3L, native.LinesTotal);
        Assert.AreEqual(2L, native.LinesCovered);
        Assert.AreEqual(4L, native.BranchesTotal);
        Assert.AreEqual(3L, native.BranchesCovered);
        JsonObject summary = Summarize(fixture.Sources, Aggregate([native]));
        Assert.AreEqual(2L, (long?)summary["aggregate"]!["linesTotal"]);
        Assert.AreEqual(1L, (long?)summary["aggregate"]!["linesCovered"]);
        Assert.AreEqual(2L, (long?)summary["aggregate"]!["branchesTotal"]);
        Assert.AreEqual(1L, (long?)summary["aggregate"]!["branchesCoveredLowerBound"]);
        Assert.AreEqual(false, (bool?)summary["qualified"]);
        Assert.HasCount(2, summary["rawGaps"]!.AsArray());
    }

    [TestMethod]
    [DataRow("unfiltered-line-total")]
    [DataRow("unfiltered-branch-total")]
    [DataRow("covered-total")]
    [DataRow("extra-assembly")]
    [DataRow("wrong-assembly")]
    [DataRow("duplicate-class")]
    [DataRow("duplicate-line")]
    [DataRow("unknown-source")]
    [DataRow("relative-origin")]
    [DataRow("outside-origin")]
    [DataRow("outside-file-healthcheck-name")]
    [DataRow("healthcheck-file-outside-name")]
    [DataRow("negative-hits")]
    [DataRow("zero-line")]
    [DataRow("invalid-branch-flag")]
    [DataRow("missing-branch-count")]
    [DataRow("covered-over-total")]
    [DataRow("zero-branch-denominator")]
    [DataRow("nonbranch-counts")]
    [DataRow("declaration-sequence-point")]
    public void MalformedOrMasqueradingNativeReportsCannotShrinkTheDenominator(string mode)
    {
        using var fixture = new NativeFixture();
        _ = ParseCoverage(fixture.Document, fixture.Language, fixture.Sources);
        XElement root = fixture.Document.Root!, package = root.Element("packages")!.Element("package")!;
        XElement cls = package.Element("classes")!.Elements("class").First();
        XElement line = cls.Element("lines")!.Elements("line").First();
        switch (mode)
        {
            case "unfiltered-line-total": root.SetAttributeValue("lines-valid", 2); break;
            case "unfiltered-branch-total": root.SetAttributeValue("branches-valid", 2); break;
            case "covered-total": root.SetAttributeValue("lines-covered", 3); break;
            case "extra-assembly": root.Element("packages")!.Add(new XElement("package", new XAttribute("name", "Other"))); break;
            case "wrong-assembly": package.SetAttributeValue("name", "CIS.Core.Alias"); break;
            case "duplicate-class": package.Element("classes")!.Add(new XElement(cls)); break;
            case "duplicate-line": cls.Element("lines")!.Add(new XElement(line)); break;
            case "unknown-source": cls.SetAttributeValue("filename", System.IO.Path.Combine(fixture.Language, "src/CIS.Core/Healthcheck/Unknown.cs")); break;
            case "relative-origin": cls.SetAttributeValue("filename", NativeFixture.HealthPath); break;
            case "outside-origin": cls.SetAttributeValue("filename", System.IO.Path.Combine(fixture.Temporary.Path, "outside/Healthcheck.cs")); break;
            case "outside-file-healthcheck-name": package.Element("classes")!.Elements("class").Last().SetAttributeValue("name", "CIS.Healthcheck.Forged"); break;
            case "healthcheck-file-outside-name": cls.SetAttributeValue("name", "CIS.Logging.Forged"); break;
            case "negative-hits": line.SetAttributeValue("hits", -1); break;
            case "zero-line": line.SetAttributeValue("number", 0); break;
            case "invalid-branch-flag": line.SetAttributeValue("branch", "maybe"); break;
            case "missing-branch-count": line.Attribute("condition-coverage")!.Remove(); break;
            case "covered-over-total": line.SetAttributeValue("condition-coverage", "150% (3/2)"); break;
            case "zero-branch-denominator": line.SetAttributeValue("condition-coverage", "0% (0/0)"); break;
            case "nonbranch-counts": line.SetAttributeValue("branch", "False"); break;
            case "declaration-sequence-point": fixture.Sources[NativeFixture.HealthPath] = fixture.Sources[NativeFixture.HealthPath] with { NonExecutableReason = "Reviewed interface" }; break;
            default: Assert.Fail("Unknown negative control."); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => ParseCoverage(fixture.Document, fixture.Language, fixture.Sources));
    }

    [TestMethod]
    public void AnonymousBranchCountsUseMaximumWhileLineHitsUseOr()
    {
        using var fixture = new NativeFixture();
        NativeReport first = ParseCoverage(fixture.Document, fixture.Language, fixture.Sources);
        XDocument secondXml = new(fixture.Document);
        XElement cls = secondXml.Descendants("class").First();
        cls.Element("lines")!.Elements("line").Last().SetAttributeValue("hits", 1);
        secondXml.Root!.SetAttributeValue("lines-covered", 3);
        NativeReport second = ParseCoverage(secondXml, fixture.Language, fixture.Sources);
        JsonObject aggregate = Summarize(fixture.Sources, Aggregate([first, second]));
        Assert.AreEqual(2L, (long?)aggregate["aggregate"]!["linesCovered"]);
        Assert.AreEqual(2L, (long?)aggregate["aggregate"]!["linesTotal"]);
        Assert.AreEqual(1L, (long?)aggregate["aggregate"]!["branchesCoveredLowerBound"], "Two anonymous one-of-two observations cannot prove that both branches ran.");
        Assert.AreEqual(2L, (long?)aggregate["aggregate"]!["branchesTotal"]);
        Assert.AreEqual(false, (bool?)aggregate["qualified"]);
    }

    [TestMethod]
    [DataRow("missing-point")]
    [DataRow("added-point")]
    [DataRow("changed-class")]
    [DataRow("branch-denominator")]
    public void ReportAggregationRefusesChangedNativeIdentityOrBranchDenominators(string mode)
    {
        using var fixture = new NativeFixture();
        NativeReport first = ParseCoverage(fixture.Document, fixture.Language, fixture.Sources);
        var points = new Dictionary<Identity, Point>(first.Points);
        Identity identity = points.Keys.First();
        if (mode == "missing-point") { points.Remove(identity); }
        if (mode == "added-point") { points.Add(identity with { Line = 99 }, new Point(false, 0, 0)); }
        if (mode == "changed-class") { Point point = points[identity]; points.Remove(identity); points.Add(identity with { Class = "CIS.Healthcheck.Different" }, point); }
        if (mode == "branch-denominator") { points[identity] = points[identity] with { Total = 4 }; }
        var changed = first with { Points = points };
        Assert.ThrowsExactly<InvalidDataException>(() => Aggregate([first, changed]));
    }

    [TestMethod]
    public void EveryExecutableSourceNeedsNativePointsAndDeclarationsCannotHideCode()
    {
        using var fixture = new NativeFixture();
        NativeReport native = ParseCoverage(fixture.Document, fixture.Language, fixture.Sources);
        string missing = "src/CIS.Core/Healthcheck/Missing.cs";
        fixture.Sources.Add(missing, new Source(missing, "pin", true));
        Assert.ThrowsExactly<InvalidDataException>(() => Summarize(fixture.Sources, Aggregate([native])));
        fixture.Sources[missing] = fixture.Sources[missing] with { NonExecutableReason = "Reviewed declaration-only interface" };
        JsonObject observed = Summarize(fixture.Sources, Aggregate([native]));
        Assert.AreEqual(2, (int?)observed["sourceFileCount"]);
        Assert.AreEqual(1, (int?)observed["declarationOnlyFileCount"]);
    }

    [TestMethod]
    [DataRow(19, 18, true)]
    [DataRow(18, 18, false)]
    [DataRow(19, 17, false)]
    public void CoverageThresholdsRemainNinetyFiveAndNinetyPercent(int hitLines, int coveredBranches, bool passed)
    {
        const string path = "src/CIS.Core/Healthcheck/Boundary.cs";
        var source = new Dictionary<string, Source> { [path] = new(path, "pin", true) };
        var points = new Dictionary<Identity, Point>();
        for (int line = 1; line <= 20; line++) { points.Add(new Identity(path, "CIS.Healthcheck.Boundary", line), new Point(line <= hitLines, line <= coveredBranches ? 1 : 0, 1)); }
        JsonObject result = Summarize(source, points);
        Assert.AreEqual(passed, (bool?)result["qualified"]);
        Assert.AreEqual(20L, (long?)result["aggregate"]!["linesTotal"]);
        Assert.AreEqual(20L, (long?)result["aggregate"]!["branchesTotal"]);
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("failed-count")]
    [DataRow("empty")]
    [DataRow("skipped-result")]
    [DataRow("mismatched-count")]
    [DataRow("missing-attachment")]
    [DataRow("two-attachments")]
    [DataRow("missing-native-file")]
    [DataRow("conflicting-native-file")]
    public void NativeTrxMustBeNonemptyEntirelyPassedAndBoundToItsActualAttachment(string mode)
    {
        using var temporary = new TemporaryDirectory();
        string report = System.IO.Path.Combine(temporary.Path, "report.trx"), native = System.IO.Path.Combine(temporary.Path, "native.cobertura.xml");
        File.WriteAllText(native, "<coverage />");
        XDocument xml = XDocument.Parse("""
            <TestRun><Results><UnitTestResult outcome="Passed" /></Results>
              <ResultSummary><Counters total="1" passed="1" /></ResultSummary>
              <CollectorDataEntries><A href="native.cobertura.xml" /></CollectorDataEntries>
            </TestRun>
            """);
        if (mode == "failed-count") { xml.Descendants("Counters").Single().SetAttributeValue("passed", 0); }
        if (mode == "empty") { xml.Descendants("Counters").Single().SetAttributeValue("total", 0); xml.Descendants("Counters").Single().SetAttributeValue("passed", 0); }
        if (mode == "skipped-result") { xml.Descendants("UnitTestResult").Single().SetAttributeValue("outcome", "NotExecuted"); }
        if (mode == "mismatched-count") { xml.Descendants("Results").Single().Add(new XElement("UnitTestResult", new XAttribute("outcome", "Passed"))); }
        if (mode == "missing-attachment") { xml.Descendants("A").Single().Remove(); }
        if (mode == "two-attachments") { xml.Descendants("CollectorDataEntries").Single().Add(new XElement("A", new XAttribute("href", "other.cobertura.xml"))); }
        if (mode == "missing-native-file") { File.Delete(native); }
        if (mode == "conflicting-native-file")
        {
            Directory.CreateDirectory(System.IO.Path.Combine(temporary.Path, "second"));
            File.WriteAllText(System.IO.Path.Combine(temporary.Path, "second", "native.cobertura.xml"), "different bytes");
        }
        xml.Save(report);
        if (mode == "valid")
        {
            (long passed, string path) = ValidateTrx(report, temporary.Path);
            Assert.AreEqual(1L, passed); Assert.AreEqual(native, path);
        }
        else { Assert.ThrowsExactly<InvalidDataException>(() => ValidateTrx(report, temporary.Path)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("source-exclusion")]
    [DataRow("function-exclusion")]
    [DataRow("attribute-exclusion")]
    [DataRow("default-exclusions")]
    [DataRow("extra-module")]
    [DataRow("child-disabled")]
    [DataRow("skip-properties")]
    [DataRow("instrumentation-disabled")]
    public void CollectorSettingsCannotFilterExecutableHealthcheckBehavior(string mode)
    {
        XDocument settings = ValidSettings(); ValidateSettings(settings);
        XElement code = settings.Descendants("CodeCoverage").Single();
        if (mode == "source-exclusion") { code.Element("Sources")!.Element("Exclude")!.Add(new XElement("Source", ".*Healthcheck.*")); }
        if (mode == "function-exclusion") { code.Element("Functions")!.Element("Exclude")!.Add(new XElement("Function", ".*")); }
        if (mode == "attribute-exclusion") { code.Element("Attributes")!.Element("Exclude")!.Add(new XElement("Attribute", ".*CompilerGenerated.*")); }
        if (mode == "default-exclusions") { code.Element("Sources")!.SetAttributeValue("mergeDefaults", "True"); }
        if (mode == "extra-module") { code.Element("ModulePaths")!.Element("Include")!.Add(new XElement("ModulePath", ".*Other.*")); }
        if (mode == "child-disabled") { code.Element("CollectFromChildProcesses")!.Value = "False"; }
        if (mode == "skip-properties") { code.Element("SkipAutoProperties")!.Value = "True"; }
        if (mode == "instrumentation-disabled") { code.Element("EnableDynamicManagedInstrumentation")!.Value = "False"; }
        if (mode == "valid") { ValidateSettings(settings); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => ValidateSettings(settings)); }
    }

    private static XDocument ValidSettings() => XDocument.Parse("""
        <RunSettings><DataCollectionRunSettings><DataCollectors>
          <DataCollector friendlyName="Code Coverage" uri="datacollector://Microsoft/CodeCoverage/2.0"><Configuration>
            <Format>cobertura</Format><IncludeTestAssembly>false</IncludeTestAssembly><ExcludeAssembliesWithoutSources>None</ExcludeAssembliesWithoutSources>
            <CodeCoverage><ModulePaths mergeDefaults="False"><Include><ModulePath>.*[\\/]CIS\.Core\.dll$</ModulePath></Include><Exclude /></ModulePaths>
              <Sources mergeDefaults="False"><Exclude /></Sources><Functions mergeDefaults="False"><Exclude /></Functions>
              <Attributes mergeDefaults="False"><Exclude /></Attributes><CompanyNames mergeDefaults="False"><Exclude /></CompanyNames><PublicKeyTokens mergeDefaults="False"><Exclude /></PublicKeyTokens>
              <SkipAutoProperties>False</SkipAutoProperties><CollectFromChildProcesses>True</CollectFromChildProcesses><EnableDynamicManagedInstrumentation>True</EnableDynamicManagedInstrumentation>
            </CodeCoverage>
          </Configuration></DataCollector>
        </DataCollectors></DataCollectionRunSettings></RunSettings>
        """);

    private sealed class NativeFixture : IDisposable
    {
        internal const string HealthPath = "src/CIS.Core/Healthcheck/Measured.cs";
        private const string OtherPath = "src/CIS.Core/Logging/Unrelated.cs";
        internal TemporaryDirectory Temporary { get; } = new();
        internal string Language { get; }
        internal Dictionary<string, Source> Sources { get; } = new(StringComparer.Ordinal)
        {
            [HealthPath] = new(HealthPath, "health-pin", true),
            [OtherPath] = new(OtherPath, "other-pin", false)
        };
        internal XDocument Document { get; }
        internal NativeFixture()
        {
            Language = System.IO.Path.Combine(Temporary.Path, "implementations", "csharp"); Directory.CreateDirectory(Language);
            Document = XDocument.Parse("""
                <coverage lines-covered="2" lines-valid="3" branches-covered="3" branches-valid="4"><packages><package name="CIS.Core"><classes>
                  <class name="CIS.Healthcheck.Measured" filename="health"><lines>
                    <line number="10" hits="1" branch="True" condition-coverage="50% (1/2)" />
                    <line number="11" hits="0" branch="False" />
                  </lines></class>
                  <class name="CIS.Logging.Unrelated" filename="other"><lines><line number="5" hits="1" branch="True" condition-coverage="100% (2/2)" /></lines></class>
                </classes></package></packages></coverage>
                """);
            XElement[] classes = Document.Descendants("class").ToArray();
            classes[0].SetAttributeValue("filename", System.IO.Path.Combine(Language, HealthPath));
            classes[1].SetAttributeValue("filename", System.IO.Path.Combine(Language, OtherPath));
        }
        public void Dispose() => Temporary.Dispose();
    }
}
