using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[TestCategory("FocusedLoop")]
public sealed class FocusedTestsTests
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    [TestMethod]
    public void NativeResultRetainsTheExecutedCaseAndMethodIdentity()
    {
        using var directory = new TemporaryDirectory();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        string path = Path.Combine(directory.Path, "selected.trx");
        Report(started).Save(path);
        FocusedTests.NativeCase[] cases = FocusedTests.ReadResults(path, started);
        Assert.HasCount(1, cases);
        Assert.AreEqual("Tests.SelectedCase", cases[0].Method);
        Assert.AreEqual("selected (row)", cases[0].Name);
        Assert.AreEqual("native.dll", cases[0].Source);
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("failed-run")]
    [DataRow("failed-case")]
    [DataRow("skipped")]
    [DataRow("stale")]
    [DataRow("counter")]
    [DataRow("missing-definition")]
    [DataRow("wrong-execution")]
    [DataRow("duplicate-execution")]
    [DataRow("missing-source")]
    [DataRow("wrong-schema")]
    public void InvalidNativeEvidenceCannotPass(string fault)
    {
        using var directory = new TemporaryDirectory();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        XDocument report = Report(started);
        XElement root = report.Root!;
        XElement result = root.Element(Ns + "Results")!.Element(Ns + "UnitTestResult")!;
        XElement definition = root.Element(Ns + "TestDefinitions")!.Element(Ns + "UnitTest")!;
        XElement summary = root.Element(Ns + "ResultSummary")!;
        switch (fault)
        {
            case "empty": result.Remove(); break;
            case "failed-run": summary.SetAttributeValue("outcome", "Failed"); break;
            case "failed-case": result.SetAttributeValue("outcome", "Failed"); break;
            case "skipped": result.SetAttributeValue("outcome", "NotExecuted"); break;
            case "stale": result.SetAttributeValue("startTime", started.AddMinutes(-1).ToString("O")); break;
            case "counter": summary.Element(Ns + "Counters")!.SetAttributeValue("executed", 2); break;
            case "missing-definition": definition.Remove(); break;
            case "wrong-execution": definition.Element(Ns + "Execution")!.SetAttributeValue("id", "wrong"); break;
            case "duplicate-execution":
                root.Element(Ns + "Results")!.Add(new XElement(result));
                foreach (string name in new[] { "total", "executed", "passed" }) { summary.Element(Ns + "Counters")!.SetAttributeValue(name, 2); }
                break;
            case "missing-source": definition.Element(Ns + "TestMethod")!.Attribute("codeBase")!.Remove(); break;
            case "wrong-schema": root.Name = Ns + "UnexpectedRoot"; break;
            default: Assert.Fail("Unknown control."); break;
        }
        string path = Path.Combine(directory.Path, "selected.trx");
        report.Save(path);
        Assert.Throws<InvalidDataException>(() => FocusedTests.ReadResults(path, started));
    }

    [TestMethod]
    public void MissingOrMalformedNativeReportsCannotPass()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "selected.trx");
        Assert.Throws<FileNotFoundException>(() => FocusedTests.ReadResults(path, DateTimeOffset.UtcNow));
        File.WriteAllText(path, "not xml");
        Assert.Throws<System.Xml.XmlException>(() => FocusedTests.ReadResults(path, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    [DataRow("--class", "CIS.Tests.SomeTests", "FullyQualifiedName~CIS.Tests.SomeTests.")]
    [DataRow("--category", "FocusedLoop", "TestCategory=FocusedLoop")]
    [DataRow("--filter", "Name~One|Name~Two", "Name~One|Name~Two")]
    public void SelectorsUseNativeVSTestExpressions(string option, string value, string expected)
    {
        using var directory = Policy();
        FocusedTests.Selection selection = FocusedTests.Parse([directory.Path, "--project", "UnitTests", option, value]);
        Assert.AreEqual(expected, selection.Filter);
        Assert.AreEqual("Debug", selection.Configuration);
        Assert.AreEqual("tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj", selection.Project);
    }

    [TestMethod]
    [DataRow("unknown-project")]
    [DataRow("duplicate")]
    [DataRow("selectors")]
    [DataRow("operator")]
    [DataRow("timeout")]
    [DataRow("configuration")]
    [DataRow("unknown-option")]
    public void InvalidSelectionsFailBeforeLaunchingTools(string fault)
    {
        using var directory = Policy();
        string[] options = fault switch
        {
            "unknown-project" => ["--project", "../Other.csproj"],
            "duplicate" => ["--project", "UnitTests", "--project", "UnitTests"],
            "selectors" => ["--project", "UnitTests", "--class", "One", "--filter", "Name~Two"],
            "operator" => ["--project", "UnitTests", "--category", "One|Two"],
            "timeout" => ["--project", "UnitTests", "--timeout-seconds", "0"],
            "configuration" => ["--project", "UnitTests", "--configuration", "Other"],
            _ => ["--project", "UnitTests", "--unknown", "value"]
        };
        Assert.Throws<ArgumentException>(() => FocusedTests.Parse([directory.Path, .. options]));
    }

    private static TemporaryDirectory Policy()
    {
        var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "eng"));
        File.WriteAllText(Path.Combine(directory.Path, "eng/project-boundaries.json"), """
            {"projects":[{"role":"test","path":"tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj"}]}
            """);
        return directory;
    }

    private static XDocument Report(DateTimeOffset started) => XDocument.Parse($"""
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results><UnitTestResult testId="case" executionId="execution" testName="selected (row)" outcome="Passed" startTime="{started:O}" /></Results>
          <TestDefinitions><UnitTest id="case"><Execution id="execution"/><TestMethod className="Tests" name="SelectedCase" codeBase="native.dll"/></UnitTest></TestDefinitions>
          <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0"/></ResultSummary>
        </TestRun>
        """);
}
