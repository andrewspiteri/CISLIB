using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class VerificationEvidenceTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    public async Task AcceptsTheNativePopulatedFixture()
    {
        using var fixture = new EvidenceFixture();
        ProcessResult result = await fixture.Validate();
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        JsonObject validation = JsonNode.Parse(File.ReadAllText(fixture.ValidationPath))!.AsObject();
        Assert.AreEqual(0, validation["issues"]!.AsArray().Count);
    }

    [TestMethod]
    [DataRow("MissingFamily")]
    [DataRow("MissingReport")]
    [DataRow("MissingRequiredCase")]
    [DataRow("FailedCase")]
    [DataRow("UnexpectedSkip")]
    [DataRow("UnrecognizedReportSchema")]
    public async Task RejectsIncompleteOrInvalidEvidence(string fault)
    {
        using var fixture = new EvidenceFixture();
        fixture.Introduce(fault);
        ProcessResult result = await fixture.Validate();
        Assert.AreEqual(2, result.ExitCode, result.StandardOutput + result.StandardError);
        JsonArray issues = JsonNode.Parse(File.ReadAllText(fixture.ValidationPath))!["issues"]!.AsArray();
        Assert.AreEqual(1, issues.Count, result.StandardOutput);
        Assert.AreEqual(fault, issues[0]!["code"]!.GetValue<string>(), result.StandardOutput);
    }

    private sealed class EvidenceFixture : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        private readonly JsonObject manifest;
        private readonly JsonObject inventory;
        private readonly JsonObject summary;
        private readonly JsonObject execution;
        private readonly JsonObject unit;
        private readonly string nativePath;

        internal EvidenceFixture()
        {
            nativePath = Path.Combine(directory.Path, "families", "unit", "report.trx");
            Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
            // A retained, genuinely executed report supplies the positive baseline. This fixture
            // tests validator behavior, not current package or historical release compatibility.
            File.Copy(Path.Combine(RepositoryRoot, "planning", "logging", "evidence", "wp01-s03", "unit-tests.trx"), nativePath);
            XDocument native = XDocument.Load(nativePath);
            XNamespace ns = native.Root!.Name.Namespace;
            XElement counters = native.Descendants(ns + "Counters").Single();
            int total = (int)counters.Attribute("total")!;
            const string method = "CIS.Tests.UnitTests.SeverityTokenParserTests.NamedTokensAndAliasesHaveDefinedValues";
            int names = native.Descendants(ns + "TestMethod").Count(item => $"{item.Attribute("className")!.Value}.{item.Attribute("name")!.Value}" == method);

            manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "implementations", "csharp", "verification", "families.json")))!.AsObject();
            var families = new JsonArray();
            foreach (JsonNode? family in manifest["families"]!.AsArray())
            {
                string id = family!["id"]!.GetValue<string>();
                family["developerEnabled"] = id == "unit";
                families.Add(new JsonObject { ["id"] = id, ["status"] = "NotRun", ["reason"] = "Fixture selects only the retained populated unit report." });
            }

            inventory = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["cases"] = new JsonArray(new JsonObject
                {
                    ["id"] = "UT-SEVERITY-NAMES",
                    ["family"] = "unit",
                    ["implementation"] = "Implemented",
                    ["method"] = method,
                    ["minimumExecutions"] = names,
                    ["lanes"] = new JsonArray("windows-x64", "linux-ubuntu2404-x64")
                })
            };
            string discoveryPath = Path.Combine(directory.Path, "families", "unit", "discovery.json");
            Write(discoveryPath, new JsonObject
            {
                ["schema"] = "cis.discovery/v1",
                ["family"] = "unit",
                ["status"] = "Discovered",
                ["discovered"] = total,
                ["cases"] = new JsonArray(new JsonObject { ["id"] = "UT-SEVERITY-NAMES", ["count"] = names })
            });
            execution = new JsonObject
            {
                ["schema"] = "cis.execution/v1",
                ["family"] = "unit",
                ["status"] = "Executed",
                ["executed"] = total,
                ["passed"] = total,
                ["failed"] = 0,
                ["skipped"] = 0
            };
            Write(ExecutionPath, execution);
            unit = families.Single(item => item!["id"]!.GetValue<string>() == "unit")!.AsObject();
            unit["status"] = "Executed";
            unit["discovery"] = Reference(discoveryPath, "cis.discovery/v1");
            unit["execution"] = Reference(ExecutionPath, "cis.execution/v1");
            unit["report"] = Reference(nativePath, "vstest.trx/2010");
            summary = new JsonObject
            {
                ["schema"] = "cis.verification/v1",
                ["profile"] = "Developer",
                ["lane"] = "windows-x64",
                ["families"] = families,
                ["stages"] = new JsonArray()
            };
        }

        private string ExecutionPath => Path.Combine(directory.Path, "families", "unit", "execution.json");
        internal string ValidationPath => Path.Combine(directory.Path, "validation.json");

        internal void Introduce(string fault)
        {
            switch (fault)
            {
                case "MissingFamily":
                    summary["families"]!.AsArray().Remove(unit);
                    break;
                case "MissingReport":
                    unit["report"]!["path"] = "families/unit/absent.trx";
                    break;
                case "MissingRequiredCase":
                    inventory["cases"]![0]!["method"] = "CIS.Tests.UnitTests.MissingCase.DoesNotExist";
                    break;
                case "FailedCase":
                case "UnexpectedSkip":
                    XDocument native = XDocument.Load(nativePath);
                    XNamespace ns = native.Root!.Name.Namespace;
                    bool skipped = fault == "UnexpectedSkip";
                    native.Descendants(ns + "UnitTestResult").First().SetAttributeValue("outcome", skipped ? "NotExecuted" : "Failed");
                    XElement counters = native.Descendants(ns + "Counters").Single();
                    counters.SetAttributeValue("passed", (int)counters.Attribute("passed")! - 1);
                    counters.SetAttributeValue(skipped ? "notExecuted" : "failed", 1);
                    if (skipped)
                    {
                        counters.SetAttributeValue("executed", (int)counters.Attribute("executed")! - 1);
                        execution["executed"] = execution["executed"]!.GetValue<int>() - 1;
                    }

                    execution["passed"] = execution["passed"]!.GetValue<int>() - 1;
                    execution[skipped ? "skipped" : "failed"] = 1;
                    native.Save(nativePath);
                    Write(ExecutionPath, execution);
                    unit["execution"] = Reference(ExecutionPath, "cis.execution/v1");
                    unit["report"] = Reference(nativePath, "vstest.trx/2010");
                    break;
                case "UnrecognizedReportSchema":
                    XDocument invalid = XDocument.Load(nativePath);
                    invalid.Root!.Name = XName.Get("UnknownTestRun", invalid.Root.Name.NamespaceName);
                    invalid.Save(nativePath);
                    unit["report"] = Reference(nativePath, "vstest.trx/2010");
                    break;
                default:
                    throw new ArgumentException("Unknown evidence fault.", nameof(fault));
            }
        }

        internal Task<ProcessResult> Validate()
        {
            string manifestPath = Path.Combine(directory.Path, "manifest.json");
            string inventoryPath = Path.Combine(directory.Path, "inventory.json");
            Write(manifestPath, manifest);
            Write(inventoryPath, inventory);
            Write(Path.Combine(directory.Path, "summary.json"), summary);
            string script = Path.Combine(RepositoryRoot, "implementations", "csharp", "eng", "check-release.ps1");
            return ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", script, "-Profile", "Developer", "-EvidenceRoot", directory.Path, "-ManifestPath", manifestPath, "-InventoryPath", inventoryPath], RepositoryRoot, TimeSpan.FromSeconds(60));
        }

        private JsonObject Reference(string path, string schema) => new()
        {
            ["path"] = Path.GetRelativePath(directory.Path, path).Replace('\\', '/'),
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
            ["schema"] = schema
        };

        private static void Write(string path, JsonObject value) => File.WriteAllText(path, value.ToJsonString());
        public void Dispose() => directory.Dispose();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "implementations", "csharp", "CisLib.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Run the architecture tests from the CisLib checkout.");
    }
}
