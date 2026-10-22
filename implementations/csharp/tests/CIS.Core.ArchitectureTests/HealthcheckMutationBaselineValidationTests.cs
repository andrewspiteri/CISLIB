using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationBaselineValidationTests
{
    [TestMethod]
    [DataRow("unit", false)]
    [DataRow("integration", false)]
    [DataRow("unit", true)]
    public void NativePassingCohortsBindEveryGuidExecutionAndFamilyWhileAllowingSharedDataRowMethods(string family, bool upperCaseIds)
    {
        using var fixture = new BaselineFixture(family);
        if (upperCaseIds)
        {
            foreach (XAttribute attribute in fixture.Document.Descendants().Attributes().Where(item => item.Name.LocalName is "testId" or "executionId" or "id"))
            { attribute.Value = attribute.Value.ToUpperInvariant(); }
        }
        string before = fixture.Document.ToString();
        string[] cohort = fixture.Read(family);
        CollectionAssert.AreEqual(new[] { FirstId + "\t" + Method, SecondId + "\t" + Method }, cohort);
        Assert.AreEqual(before, fixture.Document.ToString());
    }

    [TestMethod]
    [DataRow("false-summary")]
    [DataRow("wrong-namespace")]
    [DataRow("wrong-root")]
    [DataRow("empty-results")]
    [DataRow("failed-result")]
    [DataRow("skipped-result")]
    [DataRow("unexecuted-result")]
    [DataRow("total-disagrees")]
    [DataRow("executed-disagrees")]
    [DataRow("passed-disagrees")]
    [DataRow("missing-required-counter")]
    [DataRow("negative-counter")]
    [DataRow("noninteger-counter")]
    [DataRow("failed-counter")]
    [DataRow("error-counter")]
    [DataRow("timeout-counter")]
    [DataRow("aborted-counter")]
    [DataRow("notexecuted-counter")]
    [DataRow("unknown-nonzero-counter")]
    [DataRow("run-error")]
    [DataRow("error-details")]
    [DataRow("missing-results")]
    [DataRow("duplicate-results-section")]
    [DataRow("missing-definitions")]
    [DataRow("missing-entries")]
    [DataRow("foreign-result-child")]
    [DataRow("duplicate-result-guid")]
    [DataRow("duplicate-result-execution")]
    [DataRow("missing-result-execution")]
    [DataRow("invalid-result-guid")]
    [DataRow("empty-result-guid")]
    [DataRow("result-definition-execution-mismatch")]
    [DataRow("duplicate-definition-guid")]
    [DataRow("duplicate-definition-execution")]
    [DataRow("missing-definition-execution")]
    [DataRow("unknown-definition-guid")]
    [DataRow("entry-execution-mismatch")]
    [DataRow("duplicate-entry-guid")]
    [DataRow("duplicate-entry-execution")]
    [DataRow("missing-entry")]
    [DataRow("wrong-family")]
    [DataRow("wrong-assembly-suffix")]
    [DataRow("missing-method-name")]
    [DataRow("missing-class-name")]
    public void ContradictoryIncompleteOrMisboundNativeBaselinesCannotSupplyAValidCohort(string mode)
    {
        using var fixture = new BaselineFixture("unit");
        XElement root = fixture.Document.Root!, results = root.Element(Ns + "Results")!, definitions = root.Element(Ns + "TestDefinitions")!, entries = root.Element(Ns + "TestEntries")!;
        XElement first = results.Elements().First(), second = results.Elements().Last(), definition = definitions.Elements().First(), secondDefinition = definitions.Elements().Last();
        XElement summary = root.Element(Ns + "ResultSummary")!, counters = summary.Element(Ns + "Counters")!, method = definition.Element(Ns + "TestMethod")!;
        switch (mode)
        {
            case "false-summary": summary.SetAttributeValue("outcome", "Aborted"); break;
            case "wrong-namespace": root.Name = "TestRun"; break;
            case "wrong-root": root.Name = Ns + "Different"; break;
            case "empty-results": results.RemoveNodes(); break;
            case "failed-result": first.SetAttributeValue("outcome", "Failed"); break;
            case "skipped-result": first.SetAttributeValue("outcome", "Skipped"); break;
            case "unexecuted-result": first.SetAttributeValue("outcome", "NotExecuted"); break;
            case "total-disagrees": counters.SetAttributeValue("total", "1"); break;
            case "executed-disagrees": counters.SetAttributeValue("executed", "1"); break;
            case "passed-disagrees": counters.SetAttributeValue("passed", "1"); break;
            case "missing-required-counter": counters.Attribute("passed")!.Remove(); break;
            case "negative-counter": counters.SetAttributeValue("failed", "-1"); break;
            case "noninteger-counter": counters.SetAttributeValue("error", "false"); break;
            case "failed-counter": counters.SetAttributeValue("failed", "1"); break;
            case "error-counter": counters.SetAttributeValue("error", "1"); break;
            case "timeout-counter": counters.SetAttributeValue("timeout", "1"); break;
            case "aborted-counter": counters.SetAttributeValue("aborted", "1"); break;
            case "notexecuted-counter": counters.SetAttributeValue("notExecuted", "1"); break;
            case "unknown-nonzero-counter": counters.SetAttributeValue("unexpected", "1"); break;
            case "run-error": summary.Add(new XElement(Ns + "RunInfos", new XElement(Ns + "RunInfo", new XAttribute("outcome", "Error")))); break;
            case "error-details": first.Add(new XElement(Ns + "Output", new XElement(Ns + "ErrorInfo", new XElement(Ns + "Message", "Contradictory failure")))); break;
            case "missing-results": results.Remove(); break;
            case "duplicate-results-section": root.Add(new XElement(results)); break;
            case "missing-definitions": definitions.Remove(); break;
            case "missing-entries": entries.Remove(); break;
            case "foreign-result-child": results.Add(new XElement("ForeignResult")); break;
            case "duplicate-result-guid": second.SetAttributeValue("testId", FirstId); break;
            case "duplicate-result-execution": second.SetAttributeValue("executionId", FirstExecution); break;
            case "missing-result-execution": first.Attribute("executionId")!.Remove(); break;
            case "invalid-result-guid": first.SetAttributeValue("testId", "not-a-guid"); break;
            case "empty-result-guid": first.SetAttributeValue("testId", Guid.Empty.ToString()); break;
            case "result-definition-execution-mismatch": definition.Element(Ns + "Execution")!.SetAttributeValue("id", UnknownExecution); break;
            case "duplicate-definition-guid": secondDefinition.SetAttributeValue("id", FirstId); break;
            case "duplicate-definition-execution": secondDefinition.Element(Ns + "Execution")!.SetAttributeValue("id", FirstExecution); break;
            case "missing-definition-execution": definition.Element(Ns + "Execution")!.Remove(); break;
            case "unknown-definition-guid": definition.SetAttributeValue("id", UnknownExecution); break;
            case "entry-execution-mismatch": entries.Elements().First().SetAttributeValue("executionId", UnknownExecution); break;
            case "duplicate-entry-guid": entries.Elements().Last().SetAttributeValue("testId", FirstId); break;
            case "duplicate-entry-execution": entries.Elements().Last().SetAttributeValue("executionId", FirstExecution); break;
            case "missing-entry": entries.Elements().Last().Remove(); break;
            case "wrong-family": method.SetAttributeValue("codeBase", "/native/tests/CIS.Core.IntegrationTests.dll"); break;
            case "wrong-assembly-suffix": method.SetAttributeValue("codeBase", "/native/tests/OtherCIS.Core.UnitTests.dll"); break;
            case "missing-method-name": method.Attribute("name")!.Remove(); break;
            case "missing-class-name": method.Attribute("className")!.Remove(); break;
            default: throw new InvalidOperationException("Unknown native baseline control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Read("unit"));
    }

    [TestMethod]
    public void UnknownNativeFamiliesCannotBorrowAPassingUnitCohort()
    {
        using var fixture = new BaselineFixture("unit");
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Read("business"));
    }

    [TestMethod]
    [DataRow("<!DOCTYPE TestRun [<!ENTITY forged 'replacement'>]><TestRun />")]
    [DataRow("<TestRun>")]
    public void ExternalEntityAndMalformedXmlInputsCannotSupplyNativeResults(string xml)
    {
        using var temporary = new TemporaryDirectory(); string path = System.IO.Path.Combine(temporary.Path, "malformed.trx");
        File.WriteAllText(path, xml);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationBaselineValidation.ReadPassedCases(path, "unit"));
    }

    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private const string FirstId = "aaaaaaaa-1111-4111-8111-111111111111";
    private const string SecondId = "bbbbbbbb-2222-4222-8222-222222222222";
    private const string FirstExecution = "cccccccc-3333-4333-8333-333333333333";
    private const string SecondExecution = "dddddddd-4444-4444-8444-444444444444";
    private const string UnknownExecution = "eeeeeeee-5555-4555-8555-555555555555";
    private const string Class = "CIS.Tests.UnitTests.Healthcheck.NativeBaselineFixture";
    private const string Method = Class + ".SharedDataRowMethod";

    private sealed class BaselineFixture : IDisposable
    {
        private readonly TemporaryDirectory temporary = new();
        internal XDocument Document { get; }
        internal BaselineFixture(string family)
        {
            var results = new XElement(Ns + "Results"); var definitions = new XElement(Ns + "TestDefinitions"); var entries = new XElement(Ns + "TestEntries");
            foreach ((string id, string execution) in new[] { (FirstId, FirstExecution), (SecondId, SecondExecution) })
            {
                results.Add(new XElement(Ns + "UnitTestResult", new XAttribute("testId", id), new XAttribute("executionId", execution), new XAttribute("outcome", "Passed")));
                definitions.Add(new XElement(Ns + "UnitTest", new XAttribute("id", id), new XElement(Ns + "Execution", new XAttribute("id", execution)),
                    new XElement(Ns + "TestMethod", new XAttribute("className", Class), new XAttribute("name", "SharedDataRowMethod"),
                        new XAttribute("codeBase", "/synthetic/native/tests/CIS.Core." + (family == "unit" ? "UnitTests" : "IntegrationTests") + ".dll"))));
                entries.Add(new XElement(Ns + "TestEntry", new XAttribute("testId", id), new XAttribute("executionId", execution)));
            }
            Document = new XDocument(new XElement(Ns + "TestRun", results, definitions, entries,
                new XElement(Ns + "ResultSummary", new XAttribute("outcome", "Completed"), new XElement(Ns + "Counters", new XAttribute("total", "2"),
                    new XAttribute("executed", "2"), new XAttribute("passed", "2"), new XAttribute("failed", "0"), new XAttribute("error", "0")))));
        }
        internal string[] Read(string family)
        {
            string path = System.IO.Path.Combine(temporary.Path, "synthetic.trx"); Document.Save(path);
            return HealthcheckMutationBaselineValidation.ReadPassedCases(path, family);
        }
        public void Dispose() => temporary.Dispose();
    }
}
