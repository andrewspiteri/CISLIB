using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class LogValueQualificationArchitectureTests
{
    [TestMethod]
    public void ActualCoreConsumerControlsMatchDiagnosticsAndExecuteTheTypedExample()
    {
        JsonObject report = Read(".artifacts/csharp/verification/Developer/contracts/log-value-consumers/summary.json");
        Assert.AreEqual(0, report["exitCode"]!.GetValue<int>());
        Assert.AreEqual("Passed", report["status"]!.GetValue<string>());
        Assert.AreEqual(12, report["controls"]!.AsArray().Count);
        Assert.IsTrue(report["controls"]!.AsArray().All(control => control!["matched"]!.GetValue<bool>()));
        Assert.AreEqual(10, report["controls"]!.AsArray().Count(control => control!["expectedDiagnostic"] is not null));
        JsonNode executable = report["controls"]!.AsArray().Single(control => control!["executed"]!.GetValue<bool>())!;
        Assert.AreEqual(0, executable["executionExitCode"]!.GetValue<int>());
        Assert.AreEqual(Hash(typeof(LogValue).Assembly.Location), report["coreSha256"]!.GetValue<string>());
        foreach (JsonNode? source in report["source"]!.AsArray())
        {
            Assert.AreEqual(source!["sha256"]!.GetValue<string>(), Hash(Find(source["path"]!.GetValue<string>())));
        }
    }

    [TestMethod]
    public void QualificationCatalogDeclaresNativeUnicodeCapabilitiesAndRealErrorTargets()
    {
        JsonObject catalog = Read("conformance/logging/value-qualification-cases.json");
        JsonArray cases = catalog["cases"]!.AsArray();
        Assert.AreEqual(71, cases.Count);
        Assert.AreEqual(71, catalog["caseCount"]!.GetValue<int>());
        Assert.AreEqual(71, cases.Select(row => row!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(8, cases.Count(row => row!["requires"]?.GetValue<string>() == "utf16-code-unit-input"));
        Assert.AreEqual(3, cases.Count(row => row!["input"]!["mode"]!.GetValue<string>() == "mutation"));
        foreach (JsonNode? row in cases.Where(row => !row!["expected"]!["valid"]!.GetValue<bool>()))
        {
            Assert.IsTrue(new[] { "argument-null", "argument-invalid", "argument-out-of-range" }.Contains(row!["expected"]!["error"]!.GetValue<string>(), StringComparer.Ordinal));
            Assert.AreEqual(1, row["expected"]!["factoryCalls"]!.GetValue<int>());
        }
    }

    [TestMethod]
    public void OriginalValueAndCollectionCatalogsRemainBoundToActualCompiledTests()
    {
        JsonArray groups = Read("conformance/logging/case-inventory.json")["cases"]!.AsArray();
        foreach (string id in new[] { "UT-LOG-VALUE-T01-01", "UT-LOG-VALUE-T02-01", "UT-LOG-VALUE-T03-01", "UT-LOG-VALUE-T04-01", "IT-LOG-VALUE-T04-03" })
        {
            JsonNode group = groups.Single(row => row!["id"]!.GetValue<string>() == id)!;
            Assert.AreEqual("Implemented", group["implementation"]!.GetValue<string>());
            Assert.IsTrue(group["minimumExecutions"]!.GetValue<int>() > 0);
        }
        Assert.AreEqual(47, Read("conformance/logging/event-value-cases.json")["caseCount"]!.GetValue<int>());
        Assert.AreEqual(12, Read("conformance/logging/value-normalization-cases.json")["caseCount"]!.GetValue<int>());
        Assert.AreEqual(22, Read("conformance/logging/value-collection-cases.json")["caseCount"]!.GetValue<int>());
    }

    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(Find(path)))!.AsObject();

    private static string Find(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, path);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Qualification input unavailable.", path);
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
