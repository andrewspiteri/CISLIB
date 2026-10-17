using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
[DoNotParallelize] // Launcher controls share the checker build output.
public sealed class VerificationEvidenceTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    [DataRow("Developer")]
    [DataRow("Release")]
    public async Task CheckpointOverrideGuardPreservesExistingReports(string profile)
    {
        string summary = Path.Combine(RepositoryRoot, ".artifacts/csharp/verification", profile, "summary.json");
        byte[]? before = File.Exists(summary) ? File.ReadAllBytes(summary) : null;
        ProcessResult result = await ProcessRunner.RunAsync("pwsh",
            ["-NoProfile", "-File", Path.Combine(RepositoryRoot, "eng/verify.ps1"), "-Profile", profile],
            RepositoryRoot, TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["CIS_ARCHITECTURE_INSPECTION"] = "focused-fixture.json" });
        Assert.AreNotEqual(0, result.ExitCode, profile + ": " + result.StandardOutput);
        StringAssert.Contains(result.StandardError, "CIS_ARCHITECTURE_INSPECTION is only for focused tests.");
        if (before is null) { Assert.IsFalse(File.Exists(summary), profile); }
        else { CollectionAssert.AreEqual(before, File.ReadAllBytes(summary), profile); }
    }
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
    [DataRow("MissingSource", "InvalidReconciliation")]
    [DataRow("MissingRunId", "InvalidReconciliation")]
    [DataRow("MissingInputHash", "InvalidReconciliation")]
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

    [TestMethod]
    [DataRow("ReportHashMismatch", "ReportHashMismatch")]
    [DataRow("EvidencePathOutsideRoot", "EvidencePathOutsideRoot")]
    [DataRow("LaneMismatch", "LaneMismatch")]
    [DataRow("ProfileMismatch", "ProfileMismatch")]
    [DataRow("FailedStage", "FailedStage")]
    [DataRow("InvalidFamilyManifest", "InvalidFamilyManifest")]
    [DataRow("CounterMismatch", "CounterMismatch")]
    [DataRow("DuplicateNativeId", "UnrecognizedReportSchema")]
    [DataRow("NativeCounterMismatch", "UnrecognizedReportSchema")]
    [DataRow("NativeUnknownOutcome", "UnrecognizedReportSchema")]
    [DataRow("MissingNativeDefinition", "UnrecognizedReportSchema")]
    [DataRow("FractionalDiscoveryCount", "UnrecognizedReportSchema")]
    public async Task NativeMigrationKeepsCorruptEvidenceFailClosed(string fault, string expected)
    {
        using var fixture = new EvidenceFixture();
        fixture.Introduce(fault);
        ProcessResult result = await fixture.Validate();
        Assert.AreEqual(2, result.ExitCode, result.StandardOutput);
        JsonArray issues = JsonNode.Parse(File.ReadAllText(fixture.ValidationPath))!["issues"]!.AsArray();
        Assert.IsTrue(issues.Any(issue => issue!["code"]!.GetValue<string>() == expected), result.StandardOutput);
    }

    [TestMethod]
    public async Task AcceptsHistoricalIntegralDecimalCounts()
    {
        using var fixture = new EvidenceFixture();
        fixture.Introduce("IntegralDiscoveryCount");
        ProcessResult result = await fixture.Validate();
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput);
    }

    [TestMethod]
    [DataRow("Developer", 0)]
    [DataRow("Release", 2)]
    public async Task StableLauncherUsesNativeValidationAndPreservesReleaseRefusal(string profile, int expected)
    {
        using var fixture = new EvidenceFixture();
        if (profile == "Release") { fixture.Introduce("ReportInventory"); }
        ProcessResult result = await fixture.ValidateLauncher(profile);
        Assert.AreEqual(expected, result.ExitCode, result.StandardOutput + result.StandardError);
        JsonObject validation = JsonNode.Parse(File.ReadAllText(fixture.ValidationPath))!.AsObject();
        Assert.AreEqual(expected, validation["exitCode"]!.GetValue<int>());
        if (profile == "Release")
        {
            JsonArray issues = validation["issues"]!.AsArray();
            Assert.AreEqual(8, issues.Count(issue => issue!["code"]!.GetValue<string>() == "MissingReleaseGate"));
            Assert.IsTrue(issues.Any(issue => issue!["code"]!.GetValue<string>() == "MissingFamilyExecution"));
            Assert.IsTrue(issues.Any(issue => issue!["code"]!.GetValue<string>() == "MissingRequiredCase" && issue["detail"]!.GetValue<string>().Contains("UT-PLANNED", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    public async Task CopiedEvidenceReaderRunsWithoutRepositorySources()
    {
        using var directory = new TemporaryDirectory();
        string module = Path.Combine(RepositoryRoot, "implementations/csharp/eng/Evidence.psm1");
        string script = Path.Combine(directory.Path, "copy-reader.ps1");
        File.WriteAllText(script, """
            param($Module, $Root)
            $ErrorActionPreference = 'Stop'
            Import-Module $Module -Force
            @(Copy-EvidenceTool $Root) | ConvertTo-Json -Depth 8
            """);
        ProcessResult copied = await ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", script, module, directory.Path], directory.Path, TimeSpan.FromSeconds(90));
        Assert.AreEqual(0, copied.ExitCode, copied.StandardOutput + copied.StandardError);
        JsonArray inputs = JsonNode.Parse(copied.StandardOutput)!.AsArray();
        CollectionAssert.AreEquivalent(new[] { "CIS.DependencyChecker.dll", "CIS.DependencyChecker.deps.json", "CIS.DependencyChecker.runtimeconfig.json", "CIS.Core.TestSupport.dll" },
            inputs.Select(input => Path.GetFileName(input!["path"]!.GetValue<string>())).ToArray());
        File.Copy(module, Path.Combine(directory.Path, "Evidence.psm1"));
        string trx = Path.Combine(RepositoryRoot, "planning/logging/evidence/wp01-s03/unit-tests.trx");
        File.WriteAllText(script, """
            param($Module, $Trx)
            $ErrorActionPreference = 'Stop'
            Import-Module $Module -Force
            [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
            Read-NativeTrx $Trx | ConvertTo-Json -Depth 8
            """);
        ProcessResult read = await ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", script, Path.Combine(directory.Path, "Evidence.psm1"), trx], directory.Path, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
        Assert.AreEqual(0, read.ExitCode, read.StandardOutput + read.StandardError);
        JsonObject expected = VerificationEvidence.ReadTrx(trx), actual = JsonNode.Parse(read.StandardOutput)!.AsObject();
        foreach (string counter in new[] { "total", "passed", "failed", "skipped" })
        { Assert.AreEqual(expected[counter]!.GetValue<int>(), actual[counter]!.GetValue<int>(), counter); }
        JsonArray expectedRows = expected["results"]!.AsArray(), actualRows = actual["results"]!.AsArray();
        Assert.AreEqual(expectedRows.Count, actualRows.Count);
        for (int row = 0; row < expectedRows.Count; row++)
        {
            foreach (string field in new[] { "testId", "method", "source", "name", "outcome" })
            { Assert.AreEqual(expectedRows[row]![field]!.GetValue<string>(), actualRows[row]![field]!.GetValue<string>(), $"row {row}, {field}"); }
        }
        Assert.IsFalse(Directory.Exists(Path.Combine(directory.Path, "CIS.DependencyChecker")));
        foreach (JsonNode? input in inputs)
        {
            string path = Path.Combine(directory.Path, input!["path"]!.GetValue<string>());
            Assert.AreEqual(input["sha256"]!.GetValue<string>(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        }
    }
    [TestMethod]
    public void MalformedInputsProduceStructuredRefusal()
    {
        using var directory = new TemporaryDirectory();
        string invalid = Path.Combine(directory.Path, "invalid.json");
        File.WriteAllText(invalid, "{not-json");
        JsonObject result = VerificationEvidence.Validate(directory.Path, invalid, invalid, "Release", "windows-x64");
        Assert.AreEqual(2, result["exitCode"]!.GetValue<int>());
        Assert.AreEqual("UnrecognizedReportSchema", result["issues"]![0]!["code"]!.GetValue<string>());
    }

    [TestMethod]
    public void FamilyDiscoveryRetainsExactBindingsWithoutCreditingPlannedOrForeignLaneCases()
    {
        using var fixture = new EvidenceFixture();
        fixture.Introduce("ReportInventory");
        JsonObject report = fixture.Discover();
        JsonArray cases = report["cases"]!.AsArray();
        Assert.AreEqual(1, cases.Count);
        Assert.AreEqual("UT-SEVERITY-NAMES", cases[0]!["id"]!.GetValue<string>());
        Assert.AreEqual(cases[0]!["count"]!.GetValue<int>(), cases[0]!["bindings"]![0]!["count"]!.GetValue<int>());
        Assert.IsTrue(report["discovered"]!.GetValue<int>() > cases[0]!["count"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task MutationSliceNeverSatisfiesFullShippingQualification()
    {
        using var fixture = new EvidenceFixture();
        fixture.Introduce("MutationSlice");
        ProcessResult result = await fixture.Validate();
        Assert.AreEqual(2, result.ExitCode, result.StandardOutput);
        JsonArray issues = JsonNode.Parse(File.ReadAllText(fixture.ValidationPath))!["issues"]!.AsArray();
        CollectionAssert.AreEquivalent(new[] { "IncompleteMutationScope", "MissingReleaseGate" }, issues.Select(issue => issue!["code"]!.GetValue<string>()).ToArray());
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
                case "MutationSlice":
                    JsonNode mutationFamily = manifest["families"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "mutation")!;
                    mutationFamily["developerEnabled"] = true;
                    JsonNode mutationEntry = summary["families"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "mutation")!;
                    mutationEntry["status"] = "Executed";
                    mutationEntry["scope"] = "partial-slice";
                    string mutationPath = Path.Combine(directory.Path, "mutation.json"), wrapperPath = Path.Combine(directory.Path, "mutation-discovery.json"), runPath = Path.Combine(directory.Path, "mutation-execution.json");
                    File.WriteAllText(mutationPath, """{"schemaVersion":"3","files":{"Core.cs":{"mutants":[{"id":"0","status":"Killed"}]}}}""");
                    File.WriteAllText(wrapperPath, """{"schema":"cis.discovery/v1","family":"mutation"}""");
                    File.WriteAllText(runPath, """{"schema":"cis.execution/v1","family":"mutation"}""");
                    mutationEntry["discovery"] = Reference(wrapperPath, "cis.discovery/v1");
                    mutationEntry["execution"] = Reference(runPath, "cis.execution/v1");
                    mutationEntry["report"] = Reference(mutationPath, "stryker.mutation/v3");
                    break;
                case "ReportHashMismatch":
                    unit["report"]!["sha256"] = new string('0', 64);
                    break;
                case "EvidencePathOutsideRoot":
                    unit["report"]!["path"] = "../outside.trx";
                    break;
                case "LaneMismatch":
                    summary["lane"] = "linux-ubuntu2404-x64";
                    break;
                case "ProfileMismatch":
                    summary["profile"] = "Release";
                    break;
                case "FailedStage":
                    summary["stages"]!.AsArray().Add(new JsonObject { ["id"] = "broken-build", ["exitCode"] = 1 });
                    break;
                case "InvalidFamilyManifest":
                    manifest["families"]!.AsArray().RemoveAt(5);
                    break;
                case "CounterMismatch":
                    execution["passed"] = execution["passed"]!.GetValue<int>() + 1;
                    Write(ExecutionPath, execution);
                    unit["execution"] = Reference(ExecutionPath, "cis.execution/v1");
                    break;
                case "IntegralDiscoveryCount":
                    discovery["cases"]![0]!["count"] = JsonNode.Parse(discovery["cases"]![0]!["count"]!.ToJsonString() + ".0");
                    break;
                case "FractionalDiscoveryCount":
                    discovery["cases"]![0]!["count"] = 1.5;
                    break;
                case "DuplicateNativeId":
                case "NativeCounterMismatch":
                case "NativeUnknownOutcome":
                case "MissingNativeDefinition":
                    XDocument corrupt = XDocument.Load(nativePath);
                    XNamespace corruptNs = corrupt.Root!.Name.Namespace;
                    XElement[] rows = corrupt.Descendants(corruptNs + "UnitTestResult").ToArray();
                    if (fault == "DuplicateNativeId") { rows[1].SetAttributeValue("testId", rows[0].Attribute("testId")!.Value); }
                    if (fault == "NativeUnknownOutcome") { rows[0].SetAttributeValue("outcome", "Unknown"); }
                    if (fault == "MissingNativeDefinition") { rows[0].SetAttributeValue("testId", Guid.NewGuid().ToString()); }
                    if (fault == "NativeCounterMismatch")
                    {
                        XElement counts = corrupt.Descendants(corruptNs + "Counters").Single();
                        counts.SetAttributeValue("passed", (int)counts.Attribute("passed")! - 1);
                    }
                    corrupt.Save(nativePath);
                    unit["report"] = Reference(nativePath, "vstest.trx/2010");
                    break;
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

        internal Task<ProcessResult> Reconcile()
        {
            summary["runId"] = "checker-fixture-run";
            summary["source"] = new JsonObject
            {
                ["commit"] = "checker-fixture-commit",
                ["inputInventory"] = new JsonObject { ["path"] = "fixture-inputs.json", ["sha256"] = "checker-fixture-input-hash" }
            };
            WriteInputs();
            JsonObject report = VerificationEvidence.Reconcile(directory.Path, summary, manifest, inventory, "windows-x64");
            VerificationEvidence.Write(ReconciliationPath, report);
            summary["reconciliation"] = new JsonObject { ["status"] = "Produced", ["report"] = Reference(ReconciliationPath, "cis.discovery-reconciliation/v1"), ["reason"] = null };
            Write(Path.Combine(directory.Path, "summary.json"), summary);
            return Task.FromResult(new ProcessResult(0, report.ToJsonString(), ""));
        }
        internal void AlterReconciliation(string fault)
        {
            JsonObject report = JsonNode.Parse(File.ReadAllText(ReconciliationPath))!.AsObject();
            if (fault == "Missing") { summary.Remove("reconciliation"); return; }
            if (fault == "Status") { summary["reconciliation"]!["status"] = "NotProduced"; return; }
            if (fault == "Source") { report["source"]!["commit"] = "another-commit"; }
            else if (fault == "MissingSource") { report.Remove("source"); summary.Remove("source"); }
            else if (fault == "MissingRunId") { report.Remove("runId"); summary.Remove("runId"); }
            else if (fault == "MissingInputHash")
            {
                report["source"]!["inputInventory"]!.AsObject().Remove("sha256");
                summary["source"]!["inputInventory"]!.AsObject().Remove("sha256");
            }
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
            JsonObject result = VerificationEvidence.Validate(directory.Path, Path.Combine(directory.Path, "manifest.json"), Path.Combine(directory.Path, "inventory.json"), "Developer", "windows-x64");
            VerificationEvidence.Write(ValidationPath, result);
            return Task.FromResult(new ProcessResult(result["exitCode"]!.GetValue<int>(), result.ToJsonString(), ""));
        }
        internal JsonObject Discover()
        {
            WriteInputs();
            return VerificationEvidence.FamilyDiscovery(directory.Path, inventory, "unit", "windows-x64", nativeDiscoveryPath);
        }

        internal Task<ProcessResult> ValidateLauncher(string profile)
        {
            summary["profile"] = profile;
            WriteInputs();
            string script = Path.Combine(RepositoryRoot, "implementations/csharp/eng/check-release.ps1");
            return ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", script, "-Profile", profile, "-EvidenceRoot", directory.Path,
                "-ManifestPath", Path.Combine(directory.Path, "manifest.json"), "-InventoryPath", Path.Combine(directory.Path, "inventory.json"),
                "-OutputPath", ValidationPath], RepositoryRoot, TimeSpan.FromSeconds(90));
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

    [TestMethod]
    [DataRow(false, false, 0)]
    [DataRow(false, true, 0)]
    [DataRow(true, false, 0)]
    [DataRow(true, true, 0)]
    [DataRow(false, false, 7)]
    [DataRow(false, true, 7)]
    [DataRow(true, false, 7)]
    [DataRow(true, true, 7)]
    public async Task CommandLogContainsOnlyTheCurrentInvocation(bool previousLog, bool emitsOutput, int childExitCode)
    {
        using var directory = new TemporaryDirectory();
        string log = Path.Combine(directory.Path, "command log.txt");
        if (previousLog) { File.WriteAllText(log, "stale previous invocation\n"); }
        string script = Path.Combine(directory.Path, "invoke.ps1");
        File.WriteAllText(script, """
            param($Module, $Log, $Program)
            $ErrorActionPreference = 'Stop'
            Import-Module $Module -Force
            Invoke-VerificationCommand -LogPath $Log -Executable pwsh -Arguments @('-NoProfile', '-Command', $Program)
            """);
        string program = (emitsOutput ? "[Console]::WriteLine('current invocation'); " : "") + "exit " + childExitCode;
        ProcessResult result = await ProcessRunner.RunAsync("pwsh",
            ["-NoProfile", "-File", script, Path.Combine(RepositoryRoot, "implementations/csharp/eng/Verification.psm1"), log, program],
            directory.Path, TimeSpan.FromSeconds(30));
        Assert.AreEqual(childExitCode == 0 ? 0 : 1, result.ExitCode, result.StandardOutput + result.StandardError);
        Assert.IsTrue(File.Exists(log), "Every invocation must create its own command log.");
        Assert.AreEqual(emitsOutput ? "current invocation" + Environment.NewLine : "", File.ReadAllText(log),
            "A silent invocation must replace old contents with an empty file.");
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
