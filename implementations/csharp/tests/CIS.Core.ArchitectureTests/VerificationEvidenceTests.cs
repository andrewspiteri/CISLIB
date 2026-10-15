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

    [TestMethod]
    public async Task AcceptsCompositeGroupWithExactNativeBindings()
    {
        using var fixture = new EvidenceFixture();
        fixture.Introduce("CompositeGroup");
        ProcessResult result = await fixture.Validate();
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
    }

    [TestMethod]
    [DataRow("UnicodeLegacy")]
    [DataRow("UnicodeModern")]
    [DataRow("XmlNonCharacterLegacy")]
    [DataRow("IsoDateName")]
    public async Task AcceptsOnlyTheKnownNativeDisplayNameRepresentations(string representation)
    {
        using var fixture = new EvidenceFixture();
        fixture.Introduce(representation);
        ProcessResult result = await fixture.Validate();
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
    }

    [TestMethod]
    [DataRow("AbortedDiscovery", "InvalidDiscovery")]
    [DataRow("MissingAbortedFlag", "InvalidDiscovery")]
    [DataRow("WrapperDiscoveredMismatch", "DiscoveryExecutionMismatch")]
    [DataRow("GroupMinimumExceeded", "MissingRequiredCase")]
    [DataRow("DuplicateDiscoveryId", "InvalidDiscovery")]
    [DataRow("ExtraDiscoveryRow", "DiscoveryExecutionMismatch")]
    [DataRow("SubstitutedDiscoveryId", "DiscoveryExecutionMismatch")]
    [DataRow("ZeroDiscoveryId", "InvalidDiscovery")]
    [DataRow("EmptyDiscoveryId", "InvalidDiscovery")]
    [DataRow("RelativeDiscoverySource", "InvalidDiscovery")]
    [DataRow("NullDiscoverySource", "InvalidDiscovery")]
    [DataRow("DiscoveryErrors", "InvalidDiscovery")]
    [DataRow("DiscoveryTotalMismatch", "InvalidDiscovery")]
    [DataRow("DiscoveryIgnorableName", "DiscoveryExecutionMismatch")]
    [DataRow("DiscoveryIgnorableMethod", "DiscoveryExecutionMismatch")]
    [DataRow("DiscoveryWrongClass", "DiscoveryExecutionMismatch")]
    [DataRow("DiscoveryWrongName", "DiscoveryExecutionMismatch")]
    [DataRow("DiscoveryLiteralEscapeMismatch", "DiscoveryExecutionMismatch")]
    [DataRow("DiscoverySwappedSurrogates", "DiscoveryExecutionMismatch")]
    [DataRow("DiscoveryWrongSource", "DiscoveryExecutionMismatch")]
    [DataRow("DiscoveryMissingRow", "DiscoveryExecutionMismatch")]
    [DataRow("DiscoveryCaseCount", "DiscoveryCaseMismatch")]
    [DataRow("DuplicateCaseId", "InvalidCaseInventory")]
    [DataRow("UnknownCaseLane", "InvalidCaseInventory")]
    [DataRow("EmptyGroup", "InvalidCaseInventory")]
    [DataRow("DuplicateGroupMethod", "InvalidCaseInventory")]
    [DataRow("MissingGroupMethod", "MissingRequiredCase")]
    public async Task RejectsNativeDiscoveryAndGroupDrift(string fault, string expectedCode)
    {
        using var fixture = new EvidenceFixture();
        fixture.Introduce(fault);
        ProcessResult result = await fixture.Validate();
        Assert.AreEqual(2, result.ExitCode, result.StandardOutput + result.StandardError);
        JsonArray issues = JsonNode.Parse(File.ReadAllText(fixture.ValidationPath))!["issues"]!.AsArray();
        Assert.IsTrue(issues.Any(issue => issue!["code"]!.GetValue<string>() == expectedCode), result.StandardOutput + result.StandardError);
    }

    [TestMethod]
    public async Task ReconciliationPreservesRequiredInapplicableUnboundAndUnexecutedFacts()
    {
        using var fixture = new EvidenceFixture();
        fixture.Introduce("ReportInventory");
        ProcessResult result = await fixture.Reconcile();
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        JsonObject report = JsonNode.Parse(File.ReadAllText(fixture.ReconciliationPath))!.AsObject();
        Assert.AreEqual("checker-fixture-run", report["runId"]!.GetValue<string>());
        Assert.AreEqual("checker-fixture-commit", report["source"]!["commit"]!.GetValue<string>());
        JsonObject unit = report["families"]!.AsArray().Single(family => family!["id"]!.GetValue<string>() == "unit")!.AsObject();
        JsonArray cases = unit["cases"]!.AsArray();
        Assert.IsFalse(cases.Single(item => item!["id"]!.GetValue<string>() == "UT-INAPPLICABLE")!["applicable"]!.GetValue<bool>());
        Assert.IsNotNull(cases.Single(item => item!["id"]!.GetValue<string>() == "UT-PLANNED")!["pending"]);
        JsonObject binding = cases[0]!["bindings"]![0]!.AsObject();
        Assert.AreEqual(binding["discovered"]!.GetValue<int>(), binding["executed"]!.GetValue<int>() + binding["skipped"]!.GetValue<int>());
        Assert.AreEqual(binding["executed"]!.GetValue<int>(), binding["passed"]!.GetValue<int>());
        Assert.AreEqual(unit["reportedRows"]!.GetValue<int>() - 1, unit["executedRows"]!.GetValue<int>());
        Assert.AreEqual(0, binding["failed"]!.GetValue<int>());
        JsonArray supporting = unit["supportingMethods"]!.AsArray();
        Assert.IsTrue(supporting.Any(item => item!["classification"]!.GetValue<string>() == "BoundOnlyToInapplicableCases"));
        Assert.IsTrue(supporting.Any(item => item!["classification"]!.GetValue<string>() == "UnboundTechnical" && item["executed"]!.GetValue<int>() > 0));
        JsonNode only = supporting.Single(item => item!["method"]!.GetValue<string>() == "Discovered.Only.TechnicalCase")!;
        Assert.AreEqual(1, only["discovered"]!.GetValue<int>());
        Assert.AreEqual(0, only["executed"]!.GetValue<int>());
    }

    [TestMethod]
    [DataRow("Hash", "ReportHashMismatch")]
    [DataRow("Run", "InvalidReconciliation")]
    [DataRow("Source", "InvalidReconciliation")]
    [DataRow("Status", "InvalidReconciliation")]
    [DataRow("Missing", "InvalidReconciliation")]
    public async Task RejectsAlteredOrStaleDeclaredReconciliation(string fault, string expectedCode)
    {
        using var fixture = new EvidenceFixture();
        ProcessResult prepared = await fixture.Reconcile();
        Assert.AreEqual(0, prepared.ExitCode, prepared.StandardOutput + prepared.StandardError);
        fixture.AlterReconciliation(fault);
        ProcessResult result = await fixture.Validate();
        Assert.AreEqual(2, result.ExitCode, result.StandardOutput + result.StandardError);
        JsonArray issues = JsonNode.Parse(File.ReadAllText(fixture.ValidationPath))!["issues"]!.AsArray();
        Assert.IsTrue(issues.Any(issue => issue!["code"]!.GetValue<string>() == expectedCode), result.StandardOutput);
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
        private readonly string discoveryPath;
        private readonly string nativeDiscoveryPath;
        private readonly JsonObject discovery;
        private readonly JsonObject nativeDiscovery;

        internal EvidenceFixture()
        {
            nativePath = Path.Combine(directory.Path, "families", "unit", "report.trx");
            Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
            // A retained, genuinely executed report supplies the positive baseline. This fixture
            // tests validator behavior, not current package or historical release compatibility.
            File.Copy(Path.Combine(RepositoryRoot, "planning", "logging", "evidence", "wp01-s03", "unit-tests.trx"), nativePath);
            XDocument native = XDocument.Load(nativePath);
            XNamespace ns = native.Root!.Name.Namespace;
            // Relocate source locators in this checker-only historical fixture for the current OS.
            foreach (XElement methodElement in native.Descendants(ns + "TestMethod"))
            {
                methodElement.SetAttributeValue("codeBase", Path.Combine(directory.Path, "Retained.Cases.dll"));
            }
            native.Save(nativePath);
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
                families.Add(new JsonObject { ["id"] = id, ["status"] = "NotRun", ["reason"] = "Fixture selects only the retained populated unit report.", ["report"] = null });
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
            discoveryPath = Path.Combine(directory.Path, "families", "unit", "discovery.json");
            nativeDiscoveryPath = Path.Combine(directory.Path, "families", "unit", "discovery-native.json");
            var definitions = native.Descendants(ns + "UnitTest").ToDictionary(definition => definition.Attribute("id")!.Value,
                definition => definition.Element(ns + "TestMethod")!);
            nativeDiscovery = new JsonObject
            {
                ["schema"] = "cis.vstest-discovery/v1",
                ["aborted"] = false,
                ["errors"] = 0,
                ["total"] = total,
                ["tests"] = new JsonArray(native.Descendants(ns + "UnitTestResult").Select(result =>
                {
                    string id = result.Attribute("testId")!.Value;
                    XElement definition = definitions[id];
                    return (JsonNode)new JsonObject
                    {
                        ["Id"] = id,
                        ["Method"] = $"{definition.Attribute("className")!.Value}.{definition.Attribute("name")!.Value}",
                        ["Name"] = result.Attribute("testName")!.Value,
                        ["Source"] = definition.Attribute("codeBase")!.Value
                    };
                }).ToArray())
            };
            Write(nativeDiscoveryPath, nativeDiscovery);
            discovery = new JsonObject
            {
                ["schema"] = "cis.discovery/v1",
                ["family"] = "unit",
                ["status"] = "Discovered",
                ["discovered"] = total,
                ["cases"] = new JsonArray(new JsonObject { ["id"] = "UT-SEVERITY-NAMES", ["count"] = names }),
                ["nativeReport"] = Reference(nativeDiscoveryPath, "cis.vstest-discovery/v1")
            };
            Write(discoveryPath, discovery);
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
        internal string ReconciliationPath => Path.Combine(directory.Path, "discovery-reconciliation.json");
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
                case "CompositeGroup":
                case "EmptyGroup":
                case "DuplicateGroupMethod":
                case "MissingGroupMethod":
                case "GroupMinimumExceeded":
                    JsonObject requiredCase = inventory["cases"]![0]!.AsObject();
                    var binding = new JsonObject { ["method"] = requiredCase["method"]!.DeepClone(), ["minimumExecutions"] = requiredCase["minimumExecutions"]!.DeepClone() };
                    requiredCase["method"] = null;
                    const string secondMethod = "CIS.Tests.UnitTests.SeverityTokenParserTests.BlankValuesAreAbsentAndClearTheOutput";
                    int secondCount = nativeDiscovery["tests"]!.AsArray().Count(test => test!["Method"]!.GetValue<string>() == secondMethod);
                    var second = new JsonObject { ["method"] = secondMethod, ["minimumExecutions"] = secondCount };
                    requiredCase["bindings"] = fault == "EmptyGroup" ? new JsonArray() : new JsonArray(binding, second);
                    discovery["cases"]![0]!["count"] = binding["minimumExecutions"]!.GetValue<int>() + secondCount;
                    JsonObject sharedLeaf = requiredCase.DeepClone().AsObject();
                    sharedLeaf.Remove("bindings");
                    sharedLeaf["id"] = "UT-SHARED-METHOD";
                    sharedLeaf["method"] = binding["method"]!.DeepClone();
                    sharedLeaf["minimumExecutions"] = binding["minimumExecutions"]!.DeepClone();
                    inventory["cases"]!.AsArray().Add(sharedLeaf);
                    discovery["cases"]!.AsArray().Add(new JsonObject { ["id"] = "UT-SHARED-METHOD", ["count"] = binding["minimumExecutions"]!.DeepClone() });
                    if (fault == "DuplicateGroupMethod") { requiredCase["bindings"]!.AsArray().Add(binding.DeepClone()); }
                    if (fault == "MissingGroupMethod") { second["method"] = "CIS.Tests.UnitTests.MissingCase.DoesNotExist"; }
                    if (fault == "GroupMinimumExceeded") { second["minimumExecutions"] = secondCount + 1; }
                    break;
                case "DuplicateCaseId":
                    inventory["cases"]!.AsArray().Add(inventory["cases"]![0]!.DeepClone());
                    break;
                case "UnknownCaseLane":
                    inventory["cases"]![0]!["lanes"] = new JsonArray("unknown-os");
                    break;
                case "MissingAbortedFlag":
                    nativeDiscovery.Remove("aborted");
                    break;
                case "WrapperDiscoveredMismatch":
                    discovery["discovered"] = nativeDiscovery["total"]!.GetValue<int>() + 1;
                    break;
                case "ReportInventory":
                    Introduce("UnexpectedSkip");
                    inventory["cases"]!.AsArray().Add(new JsonObject
                    {
                        ["id"] = "UT-INAPPLICABLE",
                        ["family"] = "unit",
                        ["implementation"] = "Implemented",
                        ["method"] = "CIS.Tests.UnitTests.SeverityTokenParserTests.BlankValuesAreAbsentAndClearTheOutput",
                        ["minimumExecutions"] = 1,
                        ["lanes"] = new JsonArray("linux-ubuntu2404-x64")
                    });
                    inventory["cases"]!.AsArray().Add(new JsonObject
                    {
                        ["id"] = "UT-PLANNED",
                        ["family"] = "unit",
                        ["implementation"] = "Planned",
                        ["method"] = null,
                        ["minimumExecutions"] = 1,
                        ["lanes"] = new JsonArray("windows-x64", "linux-ubuntu2404-x64")
                    });
                    JsonNode unexecuted = nativeDiscovery["tests"]![0]!.DeepClone();
                    unexecuted["Id"] = Guid.NewGuid().ToString();
                    unexecuted["Method"] = "Discovered.Only.TechnicalCase";
                    nativeDiscovery["tests"]!.AsArray().Add(unexecuted);
                    nativeDiscovery["total"] = nativeDiscovery["total"]!.GetValue<int>() + 1;
                    break;
                case "AbortedDiscovery":
                    nativeDiscovery["aborted"] = true;
                    break;
                case "DuplicateDiscoveryId":
                    nativeDiscovery["tests"]![1]!["Id"] = nativeDiscovery["tests"]![0]!["Id"]!.DeepClone();
                    break;
                case "ExtraDiscoveryRow":
                    JsonNode extra = nativeDiscovery["tests"]![0]!.DeepClone();
                    extra["Id"] = Guid.NewGuid().ToString();
                    nativeDiscovery["tests"]!.AsArray().Add(extra);
                    nativeDiscovery["total"] = nativeDiscovery["total"]!.GetValue<int>() + 1;
                    discovery["discovered"] = nativeDiscovery["total"]!.DeepClone();
                    break;
                case "SubstitutedDiscoveryId":
                    nativeDiscovery["tests"]![0]!["Id"] = Guid.NewGuid().ToString();
                    break;
                case "ZeroDiscoveryId":
                    nativeDiscovery["tests"]![0]!["Id"] = Guid.Empty.ToString();
                    break;
                case "EmptyDiscoveryId":
                    nativeDiscovery["tests"]![0]!["Id"] = "";
                    break;
                case "NullDiscoverySource":
                    nativeDiscovery["tests"]![0]!["Source"] = null;
                    break;
                case "RelativeDiscoverySource":
                    nativeDiscovery["tests"]![0]!["Source"] = "relative/Tests.dll";
                    break;
                case "DiscoveryErrors":
                    nativeDiscovery["errors"] = 1;
                    break;
                case "DiscoveryTotalMismatch":
                    nativeDiscovery["total"] = nativeDiscovery["total"]!.GetValue<int>() + 1;
                    break;
                case "DiscoveryIgnorableName":
                    nativeDiscovery["tests"]![0]!["Name"] = nativeDiscovery["tests"]![0]!["Name"]!.GetValue<string>() + "\u00ad";
                    break;
                case "DiscoveryIgnorableMethod":
                    nativeDiscovery["tests"]![0]!["Method"] = nativeDiscovery["tests"]![0]!["Method"]!.GetValue<string>() + "\u00ad";
                    break;
                case "DiscoveryWrongClass":
                    nativeDiscovery["tests"]![0]!["Method"] = "Other.Class." + nativeDiscovery["tests"]![0]!["Method"]!.GetValue<string>().Split('.').Last();
                    break;
                case "IsoDateName":
                case "UnicodeLegacy":
                case "UnicodeModern":
                case "XmlNonCharacterLegacy":
                case "DiscoveryLiteralEscapeMismatch":
                case "DiscoverySwappedSurrogates":
                    string unicode = fault == "IsoDateName" ? "2026-10-15T12:34:56Z" : "Unicode \u03a9-\U0001f642";
                    nativeDiscovery["tests"]![0]!["Name"] = fault == "DiscoveryLiteralEscapeMismatch" ? "Unicode \u03a9-" + @"\ud83d\ude42" :
                        fault == "XmlNonCharacterLegacy" ? "Non-character \uffff" : unicode;
                    XDocument named = XDocument.Load(nativePath);
                    XElement namedResult = named.Descendants(named.Root!.Name.Namespace + "UnitTestResult").First();
                    namedResult.SetAttributeValue("testName", fault == "UnicodeLegacy" ? "Unicode \u03a9-" + @"\ud83d\ude42" :
                        fault == "DiscoverySwappedSurrogates" ? "Unicode \u03a9-" + @"\ude42\ud83d" :
                        fault == "XmlNonCharacterLegacy" ? @"Non-character \uffff" : unicode);
                    named.Save(nativePath);
                    unit["report"] = Reference(nativePath, "vstest.trx/2010");
                    break;
                case "DiscoveryWrongName":
                    nativeDiscovery["tests"]![0]!["Name"] = "different row";
                    break;
                case "DiscoveryWrongSource":
                    nativeDiscovery["tests"]![0]!["Source"] = Path.Combine(directory.Path, "Other.Tests.dll");
                    break;
                case "DiscoveryMissingRow":
                    nativeDiscovery["tests"]!.AsArray().RemoveAt(0);
                    nativeDiscovery["total"] = nativeDiscovery["total"]!.GetValue<int>() - 1;
                    discovery["discovered"] = nativeDiscovery["total"]!.DeepClone();
                    break;
                case "DiscoveryCaseCount":
                    discovery["cases"]![0]!["count"] = 9999;
                    break;
                default:
                    throw new ArgumentException("Unknown evidence fault.", nameof(fault));
            }
        }

        internal async Task<ProcessResult> Reconcile()
        {
            summary["runId"] = "checker-fixture-run";
            summary["source"] = new JsonObject
            {
                ["commit"] = "checker-fixture-commit",
                ["inputInventory"] = new JsonObject { ["path"] = "fixture-inputs.json", ["sha256"] = "checker-fixture-input-hash" }
            };
            WriteInputs();
            string script = Path.Combine(directory.Path, "reconcile.ps1");
            File.WriteAllText(script, """
                param($Root, $Module)
                $ErrorActionPreference = 'Stop'
                Import-Module $Module -Force
                $summary = Get-Content -Raw -LiteralPath (Join-Path $Root 'summary.json') | ConvertFrom-Json -AsHashtable -DateKind String
                $manifest = Get-Content -Raw -LiteralPath (Join-Path $Root 'manifest.json') | ConvertFrom-Json -AsHashtable -DateKind String
                $inventory = Get-Content -Raw -LiteralPath (Join-Path $Root 'inventory.json') | ConvertFrom-Json -AsHashtable -DateKind String
                Write-DiscoveryReconciliation $Root $summary $manifest $inventory 'windows-x64'
                $summary.reconciliation = @{ status = 'Produced'; report = New-EvidenceReference $Root (Join-Path $Root 'discovery-reconciliation.json') 'cis.discovery-reconciliation/v1'; reason = $null }
                Write-VerificationJson (Join-Path $Root 'summary.json') $summary
                """);
            string module = Path.Combine(RepositoryRoot, "implementations", "csharp", "eng", "Evidence.psm1");
            ProcessResult result = await ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", script, "-Root", directory.Path, "-Module", module], RepositoryRoot, TimeSpan.FromSeconds(60));
            if (result.ExitCode == 0)
            {
                summary["reconciliation"] = JsonNode.Parse(File.ReadAllText(Path.Combine(directory.Path, "summary.json")))!["reconciliation"]!.DeepClone();
            }
            return result;
        }

        internal void AlterReconciliation(string fault)
        {
            JsonObject report = JsonNode.Parse(File.ReadAllText(ReconciliationPath))!.AsObject();
            if (fault == "Missing") { summary.Remove("reconciliation"); return; }
            if (fault == "Status") { summary["reconciliation"]!["status"] = "NotProduced"; return; }
            if (fault == "Source") { report["source"]!["commit"] = "another-commit"; }
            else { report["runId"] = "another-run"; }
            Write(ReconciliationPath, report);
            if (fault != "Hash") { summary["reconciliation"]!["report"] = Reference(ReconciliationPath, "cis.discovery-reconciliation/v1"); }
        }

        private void WriteInputs()
        {
            Write(nativeDiscoveryPath, nativeDiscovery);
            discovery["nativeReport"] = Reference(nativeDiscoveryPath, "cis.vstest-discovery/v1");
            Write(discoveryPath, discovery);
            unit["discovery"] = Reference(discoveryPath, "cis.discovery/v1");
            string manifestPath = Path.Combine(directory.Path, "manifest.json");
            string inventoryPath = Path.Combine(directory.Path, "inventory.json");
            Write(manifestPath, manifest);
            Write(inventoryPath, inventory);
            Write(Path.Combine(directory.Path, "summary.json"), summary);
        }

        internal Task<ProcessResult> Validate()
        {
            WriteInputs();
            string script = Path.Combine(RepositoryRoot, "implementations", "csharp", "eng", "check-release.ps1");
            return ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", script, "-Profile", "Developer", "-EvidenceRoot", directory.Path, "-ManifestPath", Path.Combine(directory.Path, "manifest.json"), "-InventoryPath", Path.Combine(directory.Path, "inventory.json")], RepositoryRoot, TimeSpan.FromSeconds(60));
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
