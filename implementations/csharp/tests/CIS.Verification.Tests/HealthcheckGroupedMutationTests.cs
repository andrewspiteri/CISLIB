using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ChildProtocol = CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol;
using Grouped = CIS.Verification.HealthcheckGroupedMutation;

namespace CIS.Tests.Verification;

[TestClass]
public sealed class HealthcheckGroupedMutationTests
{
    [TestMethod]
    public void CompleteScopeSumsDefinitionsRatherThanAveragingGroupPercentages()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs", "Healthcheck/B.cs"]);
        Grouped.Definition[] definitions = Enumerable.Range(0, 9).Select(index => Definition("Healthcheck/A.cs", index.ToString(), "Killed"))
            .Append(Definition("Healthcheck/B.cs", "9", "Survived")).ToArray();
        var review = new Grouped.Review(candidate.Identity, definitions[^1].Identity, "Survived", "HC-DIAGNOSTIC-TEXT", "ReviewedNoncritical", "local-source-review", "Reviewed the diagnostic spelling; the contracted failure and bounded content remain protected.", "2026-10-29T12:00:00Z");
        Grouped.Assessment result = Grouped.Assess(candidate, candidate.Sources, definitions, [review], new("User-authorized local source review", ["local-source-review"]));
        Assert.AreEqual(90m, result.ConservativeScore);
        Assert.IsTrue(result.Qualified);
    }

    [TestMethod]
    public void PartialScopeHasNoGatePercentageAndNoQualification()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs", "Healthcheck/Empty.cs"]);
        Grouped.Assessment result = Grouped.Assess(candidate, ["Healthcheck/A.cs"], [Definition("Healthcheck/A.cs", "0", "Killed")], [], new("none", []));
        Assert.IsNull(result.ConservativeScore);
        Assert.IsFalse(result.CompleteScope);
        Assert.IsFalse(result.Qualified);
    }

    [TestMethod]
    public void CandidateInventoryCannotOmitAFileWhileKeepingTheFrozenInputs()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs"]) with
        {
            Inputs = [new("implementations/csharp/src/CIS.Core/Healthcheck/A.cs", "a"), new("implementations/csharp/src/CIS.Core/Healthcheck/Empty.cs", "b")]
        };
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateInventory(candidate));
        Grouped.ValidateInventory(candidate with { Sources = ["Healthcheck/A.cs", "Healthcheck/Empty.cs"] });
    }

    [TestMethod]
    public void ReviewedTimeoutStillReceivesNoKillCreditAndStaysInTheDenominator()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs"]);
        Grouped.Definition[] definitions = [Definition("Healthcheck/A.cs", "0", "Killed"), Definition("Healthcheck/A.cs", "1", "Timeout")];
        var review = new Grouped.Review(candidate.Identity, definitions[1].Identity, "Timeout", "HC-LIFETIME", "NonTermination", "reviewer", "The changed loop cannot terminate under the controlled lifetime; the original native Timeout is retained.", "2026-10-29T12:00:00Z");
        Grouped.Assessment result = Grouped.Assess(candidate, candidate.Sources, definitions, [review], new("User-authorized local review", ["reviewer"]));
        Assert.AreEqual(50m, result.ConservativeScore);
        Assert.AreEqual(1, result.Killed);
        Assert.AreEqual(1, result.Timeout);
        Assert.IsFalse(result.Qualified);
    }

    [TestMethod]
    public void GenuineReviewedEquivalentRemovesOnlyItsEligibleDenominatorAndAddsNoKill()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs"]);
        Grouped.Definition killed = Definition("Healthcheck/A.cs", "0", "Killed"), equivalent = Definition("Healthcheck/A.cs", "1", "Survived");
        var review = new Grouped.Review(candidate.Identity, equivalent.Identity, "Survived", "HC-EQUIVALENCE", "Equivalent", "reviewer",
            "The exact source-bound replacement has the same observable behavior for every supported caller input.", "2026-10-29T12:00:00Z");
        Grouped.Assessment result = Grouped.Assess(candidate, candidate.Sources, [killed, equivalent], [review], new("Maintained review authority", ["reviewer"]));
        Assert.AreEqual(1, result.Killed);
        Assert.AreEqual(2, result.RawEligibleDenominator);
        Assert.AreEqual(1, result.EffectiveDenominator);
        Assert.AreEqual(1, result.ReviewedEquivalent);
        Assert.AreEqual(50m, result.RawConservativeScore);
        Assert.AreEqual(100m, result.ConservativeScore);
        Assert.IsTrue(result.Qualified);
    }

    [TestMethod]
    public void NativeNonviableAndRedundantBlockDefinitionsStayRawWithoutExpandingTheEligibleDenominator()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs"]);
        var killed = Definition("Healthcheck/A.cs", "0", "Killed") with { Location = Location(2, 2, 2, 10) };
        var compile = Definition("Healthcheck/A.cs", "1", "CompileError") with { StatusReason = "Mutant caused compile errors" };
        var block = Definition("Healthcheck/A.cs", "2", "Ignored") with
        {
            Operator = "Block removal mutation",
            Location = Location(1, 1, 3, 2),
            StatusReason = "Removed by block already covered filter"
        };
        Grouped.Assessment result = Grouped.Assess(candidate, candidate.Sources, [killed, compile, block], [], new("none", []));
        Assert.AreEqual(1, result.RawEligibleDenominator);
        Assert.AreEqual(1, result.CompileError);
        Assert.AreEqual(1, result.Ignored);
        Assert.AreEqual(3, result.SourcePopulations.Single().Definitions);
        Assert.AreEqual(100m, result.ConservativeScore);
        Assert.IsTrue(result.Qualified);
    }

    [TestMethod]
    public void ACompilerOnlyPopulationIsReportedAsNonviableRatherThanEmptyOrKilled()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs", "Healthcheck/B.cs"]);
        var compile = Definition("Healthcheck/B.cs", "1", "CompileError") with { StatusReason = "Mutant caused compile errors" };
        Grouped.Assessment result = Grouped.Assess(candidate, candidate.Sources, [Definition("Healthcheck/A.cs", "0", "Killed"), compile], [], new("none", []));
        Grouped.SourcePopulation population = result.SourcePopulations.Single(source => source.Source == "Healthcheck/B.cs");
        Assert.AreEqual(1, population.Definitions);
        Assert.AreEqual(0, population.Eligible);
        Assert.AreEqual("native nonviable or ignored definitions only", population.Population);
        Assert.AreEqual(1, result.Killed);
        Assert.IsTrue(result.Qualified);
        Grouped.Assessment noEligible = Grouped.Assess(Candidate(["Healthcheck/B.cs"]), ["Healthcheck/B.cs"], [compile], [], new("none", []));
        Assert.IsNull(noEligible.ConservativeScore);
        Assert.IsFalse(noEligible.Qualified);
    }

    [TestMethod]
    [DataRow("Ignored", "Removed by file pattern filter")]
    [DataRow("CompileError", "Native initialization failed")]
    [DataRow("RuntimeError", "The test host crashed")]
    [DataRow("Pending", null)]
    public void UnknownExclusionOrExecutionFailureCannotQualifyWithOtherKilledDefinitions(string status, string? reason)
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs"]);
        var unresolved = Definition("Healthcheck/A.cs", "1", status) with { StatusReason = reason };
        Grouped.Assessment result = Grouped.Assess(candidate, candidate.Sources, [Definition("Healthcheck/A.cs", "0", "Killed"), unresolved], [], new("none", []));
        Assert.IsFalse(result.Qualified);
        Assert.IsTrue(result.Holds.Length > 0);
    }

    [TestMethod]
    public void ARedundantBlockLabelNeedsAnActualContainedEligibleDefinition()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs"]);
        var killed = Definition("Healthcheck/A.cs", "0", "Killed") with { Location = Location(8, 1, 8, 5) };
        var block = Definition("Healthcheck/A.cs", "1", "Ignored") with
        { Operator = "Block removal mutation", Location = Location(1, 1, 3, 2), StatusReason = "Removed by block already covered filter" };
        Grouped.Assessment result = Grouped.Assess(candidate, candidate.Sources, [killed, block], [], new("none", []));
        Assert.IsFalse(result.Qualified);
    }

    [TestMethod]
    public void PilotCompilationContextIsExplicitAndCannotLeakIntoAnotherGroup()
    {
        var pilot = new Grouped.Group("pilot", [], [], "", "perTest");
        var runtime = new Grouped.Group("runtime", [], [], "", "perTest");
        Assert.AreEqual("true", pilot.BuildProperties["CisHealthcheckPilotCohort"]);
        Assert.AreEqual("false", runtime.BuildProperties["CisHealthcheckPilotCohort"]);
        var run = new JsonObject { ["buildProperties"] = JsonSerializer.SerializeToNode(pilot.BuildProperties), ["environment"] = JsonSerializer.SerializeToNode(pilot.BuildProperties) };
        Grouped.ValidateContext(run, pilot);
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateContext(run, runtime));
        run["environment"]!["CisHealthcheckPilotCohort"] = "false";
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateContext(run, pilot));
    }

    [TestMethod]
    public void PrivilegedIdentityCompilationIsExplicitAndLimitedToTheLinuxPipesLane()
    {
        foreach (string name in new[] { "pilot", "runtime", "http", "pipes" })
        {
            var group = new Grouped.Group(name, [], [], "", "perTest");
            string expected = name == "pipes" && OperatingSystem.IsLinux() ? "true" : "false";
            Assert.AreEqual(expected, group.BuildProperties["CisLinuxIdentityQualification"]);
            var run = new JsonObject
            {
                ["buildProperties"] = JsonSerializer.SerializeToNode(group.BuildProperties),
                ["environment"] = JsonSerializer.SerializeToNode(group.BuildProperties)
            };
            Grouped.ValidateContext(run, group);
            run["environment"]!.AsObject().Remove("CisLinuxIdentityQualification");
            Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateContext(run, group));
        }
    }

    [TestMethod]
    public void StableNativeProfilesUseEightWorkersCompleteScopePerTestAndDisabledBail()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/Contracts/HealthDiagnostic.cs", "Healthcheck/Contracts/HealthMeasurement.cs",
            "Healthcheck/Contracts/Collection/IHealthCollector.cs", "Healthcheck/Execution/HealthExecutionBinder.cs",
            "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", "Healthcheck/Pipes/HealthProbeClient.cs", "Healthcheck/Pipes/HealthPipeServer.cs"]);
        foreach (Grouped.Group group in Grouped.Groups(candidate))
        {
            JsonNode settings = Grouped.Configuration(group)["stryker-config"]!;
            Assert.AreEqual(8, (int)settings["concurrency"]!);
            Assert.AreEqual("Complete", (string?)settings["mutation-level"]);
            Assert.AreEqual("perTest", (string?)settings["coverage-analysis"]);
            Assert.IsTrue((bool)settings["disable-bail"]!);
            CollectionAssert.AreEqual(group.Sources, settings["mutate"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray());
        }
    }

    [TestMethod]
    [DataRow("http", 1)]
    [DataRow("pipes", 2)]
    public void EachTransportGroupNeedsItsActualAsyncStatementKillAndNativeTestWitness(string group, int controls)
    {
        var fixture = TransportAsyncFixture(group);
        Assert.HasCount(controls, Grouped.ValidateTransportAsyncControls(group, fixture.Definitions, fixture.Report, path => fixture.Sources[path]));
    }

    [TestMethod]
    [DataRow("status")]
    [DataRow("span")]
    [DataRow("operator")]
    [DataRow("replacement")]
    [DataRow("source")]
    public void WrongNativeAsyncStatusSpanOperatorReplacementOrSourceCannotAdmitATransport(string change)
    {
        var fixture = TransportAsyncFixture("pipes");
        Grouped.Definition first = fixture.Definitions[0];
        fixture.Definitions[0] = change switch
        {
            "status" => first with { Status = "Timeout" },
            "span" => first with { Location = Location(2, 4, 2, 5) },
            "operator" => first with { Operator = "Block removal mutation" },
            "replacement" => first with { Replacement = "false" },
            _ => first with { Source = "Healthcheck/Pipes/HealthPipeBudget.cs" }
        };
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateTransportAsyncControls("pipes", fixture.Definitions, fixture.Report, path => fixture.Sources[path]));
    }

    [TestMethod]
    [DataRow("missing-killer")]
    [DataRow("wrong-test")]
    [DataRow("changed-test-source")]
    public void AsyncKillNeedsTheMappedMaintainedNativeTestRatherThanAnOpaqueId(string change)
    {
        var fixture = TransportAsyncFixture("http");
        JsonNode testFile = fixture.Report["testFiles"]!.AsObject().Single().Value!;
        if (change == "missing-killer") { fixture.Report["files"]!.AsObject().Single().Value!["mutants"]![0]!["killedBy"] = new JsonArray("unknown"); }
        else if (change == "wrong-test") { testFile["tests"]![0]!["name"] = "Unrelated.Native.Test"; }
        else { testFile["source"] = "changed test implementation"; }
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateTransportAsyncControls("http", fixture.Definitions, fixture.Report, path => fixture.Sources[path]));
    }

    private static (Grouped.Definition[] Definitions, JsonObject Report, Dictionary<string, string> Sources) TransportAsyncFixture(string group)
    {
        var rules = group == "http" ? new[]
        {
            ("Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", "await WriteAsync(context, report, status).ConfigureAwait(false);",
                "tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/HealthcheckHttpTransportTests.cs",
                "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.ActualBodyWriteCompletionFaultAndCancellationGovernTheHealthRequest")
        } : new[]
        {
            ("Healthcheck/Pipes/HealthProbeClient.cs", "await WriteErrorAsync(standardError, \"TimeoutOrCanceled\").ConfigureAwait(false);",
                "tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeActiveCancellationBoundaryTests.cs",
                "CIS.Tests.UnitTests.Healthcheck.HealthPipeActiveCancellationBoundaryTests.CallerCancellationAfterARealRequestDoesNotWaitForThePeerToSendAResponse"),
            ("Healthcheck/Pipes/HealthPipeServer.cs", "await stopCompletion.Task.ConfigureAwait(false);",
                "tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeActiveCancellationBoundaryTests.cs",
                "CIS.Tests.UnitTests.Healthcheck.HealthPipeActiveCancellationBoundaryTests.ClosingTheTransportPrecedesSettlementOfItsActuallyRunningSource")
        };
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var definitions = new List<Grouped.Definition>();
        var files = new JsonObject(); var tests = new JsonObject();
        foreach ((string source, string statement, string testSource, string name) in rules)
        {
            string id = definitions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), testId = "native-test-" + id;
            string text = "// actual shipping source\r\n    " + statement + "\r\n";
            sources["implementations/csharp/src/CIS.Core/" + source] = text;
            string location = Location(2, 5, 2, 5 + statement.Length);
            Grouped.Definition definition = Definition(source, id, "Killed") with { Operator = "Statement mutation", Location = location, Replacement = ";" };
            definitions.Add(definition);
            files["/candidate/src/CIS.Core/" + source] = new JsonObject
            {
                ["source"] = text,
                ["mutants"] = new JsonArray(new JsonObject
                {
                    ["id"] = id,
                    ["mutatorName"] = definition.Operator,
                    ["location"] = JsonNode.Parse(location),
                    ["replacement"] = ";",
                    ["status"] = "Killed",
                    ["killedBy"] = new JsonArray(testId)
                })
            };
            string key = "/candidate/implementations/csharp/" + testSource;
            if (tests[key] is null) { tests[key] = new JsonObject { ["tests"] = new JsonArray() }; }
            sources["implementations/csharp/" + testSource] = sources.GetValueOrDefault("implementations/csharp/" + testSource, "") + "// " + name + "\r\n";
            tests[key]!["source"] = sources["implementations/csharp/" + testSource];
            tests[key]!["tests"]!.AsArray().Add(new JsonObject { ["id"] = testId, ["name"] = name });
        }
        return (definitions.ToArray(), new JsonObject { ["files"] = files, ["testFiles"] = tests }, sources);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(7)]
    public void AnUndersizedHostCannotStartTheDeclaredNativeProfile(int available)
        => Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateProcessorCapacity(available));

    [TestMethod]
    [DataRow(8)]
    [DataRow(32)]
    public void AnAdequateHostUsesTheSameDeclaredEightWorkerProfile(int available)
        => Grouped.ValidateProcessorCapacity(available);

    [TestMethod]
    public void OwnedTaskScopeIncludesCandidateEarlierEvidenceAndAnInternalTempExactlyOnce()
    {
        using var directory = new TemporaryDirectory();
        string candidate = Path.Combine(directory.Path, "candidate"), temporary = Path.Combine(directory.Path, "temp");
        Directory.CreateDirectory(candidate); Directory.CreateDirectory(temporary);
        File.WriteAllBytes(Path.Combine(directory.Path, "earlier-evidence.bin"), new byte[3]);
        File.WriteAllBytes(Path.Combine(candidate, "candidate.bin"), new byte[5]);
        File.WriteAllBytes(Path.Combine(temporary, "temporary.bin"), new byte[7]);
        Assert.AreEqual(Path.GetFullPath(directory.Path), Grouped.ValidateOwnedScope(candidate, directory.Path));
        Assert.AreEqual(15L, Grouped.CountOwnedScopeBytes(directory.Path, temporary));
    }

    [TestMethod]
    public void OwnedScopeCannotUseASiblingPrefixOrExcludeItsCandidate()
    {
        using var directory = new TemporaryDirectory();
        string scope = Path.Combine(directory.Path, "task"), candidate = Path.Combine(directory.Path, "task-backup", "candidate");
        Directory.CreateDirectory(scope); Directory.CreateDirectory(candidate);
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateOwnedScope(candidate, scope));
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateOwnedScope(directory.Path, candidate));
    }

    [TestMethod]
    public void ASeparateOwnedShortTempIsIncludedAlongsideTheWholeTaskRoot()
    {
        using var task = new TemporaryDirectory(); using var temporary = new TemporaryDirectory();
        File.WriteAllBytes(Path.Combine(task.Path, "stage.bin"), new byte[11]);
        File.WriteAllBytes(Path.Combine(temporary.Path, "temporary.bin"), new byte[13]);
        Assert.AreEqual(24L, Grouped.CountOwnedScopeBytes(task.Path, temporary.Path));
    }

    [TestMethod]
    public void DisappearingOwnedDirectoriesDoNotDiscardIndependentTemporaryBytes()
    {
        using var task = new TemporaryDirectory(); using var temporary = new TemporaryDirectory();
        File.WriteAllBytes(Path.Combine(temporary.Path, "retained.bin"), new byte[13]);
        string descendant = Path.Combine(task.Path, "removed-scratch");
        Directory.CreateDirectory(descendant);
        Directory.Delete(descendant);
        Assert.AreEqual(0L, Grouped.CountOwnedScopeBytes(descendant, descendant));
        Assert.AreEqual(13L, Grouped.CountOwnedScopeBytes(descendant, temporary.Path));
        Directory.Delete(task.Path);
        Assert.AreEqual(0L, Grouped.CountOwnedScopeBytes(task.Path, task.Path));
        Assert.AreEqual(13L, Grouped.CountOwnedScopeBytes(task.Path, temporary.Path));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void ChildControlsRequireTheExistingCompletedNativeBindingWithExactSourceListSemantics(int sources)
    {
        using var fixture = new ChildBindingFixture(sources);
        fixture.Validate();
    }

    [TestMethod]
    public void SpanChildBindingRequiresExplicitVersionThreeSelectionMetadata()
    {
        using var fixture = new ChildBindingFixture(2, spans: true);
        fixture.Validate();
        fixture.Binding["selection"]![0]!["end"] = 8;
        fixture.SaveBinding();
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Validate());
    }

    [TestMethod]
    public void ActualChildDefinitionMustBeCompletedAndWhollyInsideTheBoundUtf16Span()
    {
        const string source = "// \U0001f4a1\r\nclass A { }\r\n";
        int start = source.IndexOf("A", StringComparison.Ordinal);
        var selected = new[] { new CIS.Verification.HealthcheckMutationNativeRun.Selection("Healthcheck/A.cs", start, start + 1) };
        var definition = Definition("Healthcheck/A.cs", "0", "Killed") with { Location = Location(2, 7, 2, 8) };
        Grouped.ValidateChildDefinitionSelection(definition, selected, source);
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateChildDefinitionSelection(definition with { Status = "Ignored" }, selected, source));
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateChildDefinitionSelection(definition with { Location = Location(2, 7, 2, 9) }, selected, source));
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateChildDefinitionSelection(definition with { Location = Location(2, 6, 2, 8) }, selected, source));
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateChildDefinitionSelection(definition with { Location = Location(2, 7, 2, 7) }, selected, source));
    }

    [TestMethod]
    public void AChildSessionAndReportFromSeparateSameSourceInvocationsCannotBePaired()
    {
        using var fixture = new ChildBindingFixture(1);
        string otherSession = Path.Combine(fixture.Root, "other-session.json");
        File.Copy(fixture.Control.Session, otherSession);
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Validate(fixture.Control with { Session = otherSession }));
        string otherReport = Path.Combine(fixture.Root, "other-report.json");
        File.Copy(fixture.Control.NativeReport, otherReport);
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Validate(fixture.Control with { NativeReport = otherReport }));
    }

    [TestMethod]
    public void NativeChildBindingCannotTurnAFailedOrDifferentCommandIntoACompletedControl()
    {
        using var fixture = new ChildBindingFixture(1);
        fixture.Binding["native"]!["exitCode"] = 1;
        fixture.SaveBinding();
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Validate());
        fixture.Binding["native"]!["exitCode"] = 0;
        fixture.Binding["native"]!["command"]![1] = fixture.Session.StrykerCorePath;
        fixture.SaveBinding();
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Validate());
    }

    [TestMethod]
    public void NativeChildBindingRequiresCurrentCandidateToolBytes()
    {
        using var fixture = new ChildBindingFixture(1);
        File.AppendAllText(fixture.Session.StrykerCorePath, "changed deployment");
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Validate());
    }

    [TestMethod]
    [DataRow(".editorconfig")]
    [DataRow("implementations/csharp/Directory.Build.props")]
    [DataRow("implementations/csharp/tests/FixtureTests.cs")]
    [DataRow("implementations/csharp/src/CIS.Core/Healthcheck/A.cs")]
    public void NativeChildControlRequiresTheFrozenSourceTestAndBuildConfiguration(string relative)
    {
        using var fixture = new ChildBindingFixture(1);
        File.AppendAllText(Path.Combine(fixture.Root, relative), "changed maintained input");
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Validate());
    }

    private sealed class ChildBindingFixture : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        internal string Root => directory.Path;
        internal Grouped.ChildControl Control { get; private set; }
        internal ChildProtocol.Session Session { get; }
        internal Grouped.Candidate Candidate { get; }
        internal Grouped.Toolchain Tools { get; }
        internal JsonObject Binding { get; }
        internal ChildBindingFixture(int count, bool spans = false)
        {
            string Write(string relative, string text)
            {
                string path = Path.Combine(Root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text);
                return path;
            }
            Write(".editorconfig", "root = true");
            Write("implementations/csharp/Directory.Build.props", "<Project />");
            Write("implementations/csharp/tests/FixtureTests.cs", "class FixtureTests { }");
            string[] selected = Enumerable.Range(0, count).Select(index => "Healthcheck/" + (char)('A' + index) + ".cs").ToArray();
            int version = spans ? 3 : count;
            foreach (string source in selected) { Write("implementations/csharp/src/CIS.Core/" + source, "class " + Path.GetFileNameWithoutExtension(source) + " { }"); }
            Grouped.Input[] inputs = Grouped.Capture(Root);
            Candidate = HealthcheckGroupedMutationTests.Candidate(selected) with { Inputs = inputs };
            var producer = new JsonObject();
            var toolFiles = new List<Grouped.Input>();
            foreach ((string name, string file) in new[] { ("dotnet", "dotnet"), ("strykerCli", "Stryker.CLI.dll"), ("strykerCore", "Stryker.Core.dll"),
                ("wrapper", "CIS.DependencyChecker.dll"), ("helper", "CIS.Core.TestSupport.dll"), ("wrapperDeps", "CIS.DependencyChecker.deps.json"),
                ("wrapperRuntimeConfig", "CIS.DependencyChecker.runtimeconfig.json") })
            {
                string path = Write("tools/" + file, file); toolFiles.Add(new(path, Hash(path))); producer[name] = Pin(path);
            }
            string runtime = Write("tools/System.Private.CoreLib.dll", "runtime"); toolFiles.Add(new(runtime, Hash(runtime)));
            string dotnet = (string)producer["dotnet"]!["path"]!, core = (string)producer["strykerCore"]!["path"]!;
            Tools = new("cis.healthcheck.grouped-toolchain/v1", Candidate.Platform, Candidate.Host, dotnet, dotnet, "10.0.401", "10.0.12", "8.0.31", toolFiles.ToArray(), []);
            string language = Path.Combine(Root, "implementations/csharp"), attempt = Path.Combine(Root, ".cis/child-attempt");
            string[] patterns = selected.Select(source => source + (spans ? "{6..7}" : "")).ToArray();
            string config = Write(".cis/child-attempt/native-config.json", new JsonObject { ["stryker-config"] = new JsonObject { ["mutate"] = JsonSerializer.SerializeToNode(patterns) } }.ToJsonString());
            Session = new(ChildProtocol.Schema, language, Path.Combine(attempt, "session"), config, Hash(config), core, Hash(core), "selector",
                dotnet, Hash(dotnet), runtime, Hash(runtime), inputs.Where(pin => pin.Path.StartsWith("implementations/csharp/", StringComparison.Ordinal))
                    .Select(pin => new ChildProtocol.FilePin(pin.Path["implementations/csharp/".Length..], pin.Sha256)).ToArray(), DateTimeOffset.UtcNow, true, true);
            string session = Write(".cis/child-attempt/session/session.json", JsonSerializer.Serialize(Session));
            string report = Write(".cis/native-output/reports/mutation-report.json", "{\"files\":{}}");
            string output = Path.GetDirectoryName(Path.GetDirectoryName(report))!;
            var command = JsonSerializer.SerializeToNode(new[] { dotnet, (string)producer["strykerCli"]!["path"]!, "--config-file", config, "--output", output });
            var sources = new JsonArray(selected.Select(source => (JsonNode)Pin(Path.Combine(language, "src/CIS.Core", source))).ToArray());
            var prepared = new JsonObject
            {
                ["schema"] = "cis.healthcheck.shared-child-attempt-prepared/v" + version,
                ["profileId"] = "child-control",
                ["attemptId"] = "0123456789ab",
                ["phase"] = "mutation",
                ["languageRoot"] = language,
                ["outputRoot"] = output,
                ["workingDirectory"] = Path.Combine(language, "src/CIS.Core"),
                ["config"] = Pin(config),
                ["session"] = Pin(session),
                ["source"] = count == 1 ? sources[0]!.DeepClone() : null,
                ["sources"] = sources,
                ["tools"] = producer,
                ["command"] = command
            };
            if (spans)
            { prepared["selection"] = JsonSerializer.SerializeToNode(selected.Select(source => new CIS.Verification.HealthcheckMutationNativeRun.Selection(source, 6, 7)), new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            string preparedPath = Write(".cis/child-attempt/prepared.json", prepared.ToJsonString());
            string now = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            var started = new JsonObject
            {
                ["schema"] = "cis.healthcheck.shared-child-attempt-started/v1",
                ["prepared"] = Pin(preparedPath),
                ["processId"] = 42,
                ["startedUtc"] = now,
                ["command"] = command!.DeepClone()
            };
            string startedPath = Write(".cis/child-attempt/started.json", started.ToJsonString());
            Binding = prepared.DeepClone().AsObject(); Binding["schema"] = "cis.healthcheck.shared-child-attempt/v" + version;
            Binding.Remove("command");
            Binding["phases"] = new JsonObject { ["prepared"] = Pin(preparedPath), ["started"] = Pin(startedPath) };
            Binding["native"] = new JsonObject
            {
                ["exitCode"] = 0,
                ["processId"] = 42,
                ["startedUtc"] = now,
                ["command"] = command!.DeepClone(),
                ["reportSha256"] = Hash(report),
                ["reportBytes"] = new FileInfo(report).Length
            };
            string binding = Write(".cis/child-attempt/binding.json", Binding.ToJsonString());
            Control = new(session, "probe", 0, report, Hash(report), "definition", binding, Hash(binding));
        }
        internal void SaveBinding() { File.WriteAllText(Control.NativeBinding, Binding.ToJsonString()); Control = Control with { NativeBindingSha256 = Hash(Control.NativeBinding) }; }
        internal void Validate(Grouped.ChildControl? control = null) => Grouped.ValidateChildBinding(control ?? Control, Session, Candidate, Tools);
        public void Dispose() => directory.Dispose();
        private static string Hash(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        private static JsonObject Pin(string path) => new() { ["path"] = path, ["sha256"] = Hash(path), ["bytes"] = new FileInfo(path).Length };
    }

    [TestMethod]
    public void MissReviewRequiresCandidateStatusAndDelegatedReviewerIdentity()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs"]);
        Grouped.Definition definition = Definition("Healthcheck/A.cs", "0", "Survived");
        var review = new Grouped.Review(candidate.Identity, definition.Identity, "Survived", "HC-REQUIREMENT", "Equivalent", "unknown", "A source-bound explanation longer than an approval flag.", "2026-10-29T12:00:00Z");
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.Assess(candidate, candidate.Sources, [definition], [review], new("Maintained review policy", ["authorized"])));
    }

    [TestMethod]
    public void AsyncPilotControlMustRemoveTheActualAwaitedBarrierAndBeNativelyKilled()
    {
        const string source = "private static async Task CompleteSettlementAsync(Task actual, TaskCompletionSource completion)\n{\n await actual.ConfigureAwait(false);\n completion.TrySetResult();\n}\n";
        var definition = new Grouped.Definition("Healthcheck/Execution/HealthExecutionBinder.cs", "hash", "42", "definition", "Statement mutation",
            "{\"start\":{\"line\":3,\"column\":2},\"end\":{\"line\":3,\"column\":36}}", ";", "Killed");
        Grouped.ValidateAsyncPilotDefinition(definition, source);
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateAsyncPilotDefinition(definition with { Status = "Timeout" }, source));
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateAsyncPilotDefinition(definition with { Source = "Healthcheck/Contracts/HealthMeasurement.cs" }, source));
    }

    [TestMethod]
    [DataRow(1, true)]
    [DataRow(0, false)]
    public void InfrastructureOrCleanupFailureRejectsAnAllKilledReport(int nativeExit, bool restored)
    {
        var run = HealthyRun();
        run["nativeExitCode"] = nativeExit;
        run["restored"] = restored;
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateExecution(run));
    }

    [TestMethod]
    public void NativeZeroDefinitionSourceMustActuallyBePresentInTheReport()
    {
        const string source = "namespace Fixture; public interface IEmpty { }\n";
        string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        var pins = new[] { new Grouped.Input("implementations/csharp/src/CIS.Core/Healthcheck/Empty.cs", hash) };
        var report = new JsonObject { ["files"] = new JsonObject { ["/candidate/src/CIS.Core/Healthcheck/Empty.cs"] = new JsonObject { ["source"] = source, ["mutants"] = new JsonArray() } } };
        Assert.HasCount(0, Grouped.Definitions(report, ["Healthcheck/Empty.cs"], pins));
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.Definitions(new JsonObject { ["files"] = new JsonObject() }, ["Healthcheck/Empty.cs"], pins));
    }

    [TestMethod]
    public void PinnedNativeOutOfGroupMarkersDoNotOmitSelectedZeroDefinitionScope()
    {
        const string source = "namespace Fixture; public interface IEmpty { }\n";
        string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        Grouped.Input[] pins = [new("implementations/csharp/src/CIS.Core/Healthcheck/Empty.cs", hash), new("implementations/csharp/src/CIS.Core/Logging/Other.cs", "other-hash")];
        var report = new JsonObject
        {
            ["files"] = new JsonObject
            {
                ["/candidate/src/CIS.Core/Healthcheck/Empty.cs"] = new JsonObject { ["language"] = "cs", ["source"] = source, ["mutants"] = new JsonArray() },
                ["/candidate/src/CIS.Core/Logging/Other.cs"] = NativeOutOfGroupMarker()
            }
        };
        Assert.HasCount(0, Grouped.Definitions(report, ["Healthcheck/Empty.cs"], pins));
        report["files"]!["/candidate/src/CIS.Core/Healthcheck/Empty.cs"] = NativeOutOfGroupMarker();
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.Definitions(report, ["Healthcheck/Empty.cs"], pins));
    }

    [TestMethod]
    public void AChangedOrUnboundOutOfGroupMarkerCannotEnterNativeReceiving()
    {
        var report = new JsonObject { ["files"] = new JsonObject { ["Logging/Other.cs"] = NativeOutOfGroupMarker() } };
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.Definitions(report, [], []));
        Grouped.Input[] pins = [new("implementations/csharp/src/CIS.Core/Logging/Other.cs", "other-hash")];
        report["files"]!["Logging/Other.cs"]!["language"] = "cs";
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.Definitions(report, [], pins));
    }

    private static JsonObject NativeOutOfGroupMarker() => new() { ["language"] = "none", ["source"] = "File ignored by mutate filter", ["mutants"] = new JsonArray() };

    [TestMethod]
    public void UnknownOrNotRunNativeOutcomeCannotBeReclassifiedAsKilled()
    {
        const string source = "namespace Fixture; public class Test { }\n";
        string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        var report = new JsonObject
        {
            ["files"] = new JsonObject
            {
                ["Healthcheck/A.cs"] = new JsonObject
                {
                    ["source"] = source,
                    ["mutants"] = new JsonArray(new JsonObject
                    { ["id"] = "0", ["mutatorName"] = "Boolean", ["location"] = new JsonObject { ["start"] = new JsonObject { ["line"] = 1, ["column"] = 1 } }, ["replacement"] = "false", ["status"] = "NotRun" })
                }
            }
        };
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.Definitions(report, ["Healthcheck/A.cs"], [new("implementations/csharp/src/CIS.Core/Healthcheck/A.cs", hash)]));
    }

    [TestMethod]
    public async Task OwnerCancellationUsesExistingProcessTreeCleanup()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        Task<ProcessResult> child = StartWaitingChild(directory.Path, cancellation.Token);
        try
        {
            await Ready(child, directory.Path);
            await cancellation.CancelAsync();
            ProcessTimeoutException error = await Assert.ThrowsExactlyAsync<ProcessTimeoutException>(() => child);
            Assert.IsTrue(error.CleanupCompleted);
            Assert.IsTrue(error.Result.OutputDrained);
            StringAssert.Contains(error.Message, "canceled by its owner");
        }
        finally { await Stop(child, cancellation); }
    }

    [TestMethod]
    public async Task ObservedCapacityBreachCancelsTheNativeChildAndCompletesCleanup()
    {
        using var capacity = new CancellationTokenSource();
        using var directory = new TemporaryDirectory();
        Task<ProcessResult> child = StartWaitingChild(directory.Path, capacity.Token);
        try
        {
            await Ready(child, directory.Path);
            Exception? budget = await Grouped.MonitorCapacityAsync(() => 2, () => long.MaxValue, _ => { }, capacity,
                CancellationToken.None, maximumOwned: 1, minimumFree: 0);
            Assert.IsInstanceOfType<InvalidDataException>(budget);
            ProcessTimeoutException error = await Assert.ThrowsExactlyAsync<ProcessTimeoutException>(() => child);
            Assert.IsTrue(error.CleanupCompleted);
            Assert.IsTrue(error.Result.OutputDrained);
        }
        finally { await Stop(child, capacity); }
    }

    [TestMethod]
    public void StorageMountSelectionUsesTheMostSpecificDirectoryBoundary()
    {
        string root = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory))!;
        string data = Path.Combine(root, "data"), backup = Path.Combine(root, "data-backup");
        string nested = Path.Combine(data, "owned");
        Assert.AreEqual(data, Grouped.MostSpecificMount(Path.Combine(data, "pilot"), [root, backup, data]));
        Assert.AreEqual(backup, Grouped.MostSpecificMount(Path.Combine(backup, "pilot"), [root, data, backup]));
        Assert.AreEqual(nested, Grouped.MostSpecificMount(Path.Combine(nested, "pilot"), [root, data, nested]));
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.MostSpecificMount(Path.Combine(backup, "pilot"), [data]));
    }

    [TestMethod]
    public void StorageMountSelectionUsesTheHostPathCaseRules()
    {
        string root = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory))!;
        string data = Path.Combine(root, "data"), requested = Path.Combine(root, "DATA", "pilot");
        Assert.AreEqual(OperatingSystem.IsWindows() ? data : root, Grouped.MostSpecificMount(requested, [root, data]));
    }

    [TestMethod]
    public void StorageVolumeQueryUsesTheNearestActualDirectoryBeforeOutputExists()
    {
        using var directory = new TemporaryDirectory();
        var selected = Grouped.ResolveStorageVolume(Path.Combine(directory.Path, "future", "pilot"));
        Assert.AreEqual(directory.Path, selected.QueryDirectory);
        Assert.AreEqual(Grouped.MostSpecificMount(directory.Path, DriveInfo.GetDrives().Select(volume => volume.Name)), selected.MountPoint);
        Assert.IsTrue(selected.AvailableBytes() > 0);
        string file = Path.Combine(directory.Path, "file");
        File.WriteAllText(file, "A file must not be mistaken for a volume query directory.");
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ResolveStorageVolume(file));
    }

    [TestMethod]
    public void CompositePilotCanContributeLaterWithoutAnInventedPartialScore()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs", "Healthcheck/B.cs", "Healthcheck/Empty.cs"]);
        Grouped.Definition first = Definition("Healthcheck/A.cs", "0", "Killed"), second = Definition("Healthcheck/B.cs", "1", "Killed");
        Grouped.Assessment pilot = Grouped.Assess(candidate, [first.Source], [first], [], new("none", []));
        Grouped.Assessment partial = Grouped.SumCompositeAssessments(candidate, [first.Source], [first], [pilot], []);
        Assert.IsFalse(partial.SourceInventoryComplete);
        Assert.IsFalse(partial.CompleteScope);
        Assert.IsFalse(partial.Qualified);
        Assert.IsNull(partial.ConservativeScore);
        Grouped.Assessment remaining = Grouped.Assess(candidate, [second.Source, "Healthcheck/Empty.cs"], [second], [], new("none", []));
        Grouped.Assessment complete = Grouped.SumCompositeAssessments(candidate, candidate.Sources, [first, second], [pilot, remaining], []);
        Assert.IsTrue(complete.CompleteScope);
        Assert.IsTrue(complete.Qualified);
        Assert.AreEqual(2, complete.Killed);
        Assert.AreEqual(100m, complete.ConservativeScore);
        Assert.AreEqual("zero definitions", complete.SourcePopulations.Single(row => row.Source == "Healthcheck/Empty.cs").Population);
    }

    [TestMethod]
    public void CompositeSummationRetainsRawTimeoutsAndMissingPhysicalPrerequisites()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs"]);
        Grouped.Definition killed = Definition("Healthcheck/A.cs", "0", "Killed"), timeout = Definition("Healthcheck/A.cs", "1", "Timeout");
        var review = new Grouped.Review(candidate.Identity, timeout.Identity, "Timeout", "actual requirement", "ReviewedTimeout", "reviewer",
            "The actual bounded native timeout remains in the full denominator without kill credit.", "2026-11-01T00:00:00Z");
        Grouped.Assessment native = Grouped.Assess(candidate, candidate.Sources, [killed, timeout], [review], new("Maintained authority", ["reviewer"]));
        Grouped.Assessment total = Grouped.SumCompositeAssessments(candidate, candidate.Sources, [killed, timeout], [native], ["Current Linux permission proof remains missing."]);
        Assert.IsTrue(total.CompleteScope);
        Assert.IsFalse(total.Qualified);
        Assert.AreEqual(1, total.Killed);
        Assert.AreEqual(1, total.Timeout);
        Assert.AreEqual(2, total.RawEligibleDenominator);
        Assert.AreEqual(2, total.EffectiveDenominator);
        Assert.AreEqual(50m, total.RawConservativeScore);
        Assert.AreEqual(50m, total.ConservativeScore);
        Assert.Contains("Current Linux permission proof remains missing.", total.Holds);
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("wrong-platform")]
    [DataRow("unknown-group")]
    public void CompositeOriginsCannotChooseTheBetterPlatformPerOutcome(string change)
    {
        JsonArray origins = new(new JsonObject { ["group"] = "pilot", ["platform"] = "linux" });
        Grouped.ValidateCompositeSelection(origins);
        if (change == "duplicate") { origins.Add(new JsonObject { ["group"] = "pilot", ["platform"] = "linux" }); }
        else if (change == "wrong-platform") { origins[0]!["platform"] = "windows"; }
        else { origins[0]!["group"] = "preferred-results"; }
        Assert.ThrowsExactly<InvalidDataException>(() => Grouped.ValidateCompositeSelection(origins));
    }

    [TestMethod]
    public void CompositeSummationCannotHideAnInfrastructureFailureBehindAnotherKilledOrigin()
    {
        Grouped.Candidate candidate = Candidate(["Healthcheck/A.cs", "Healthcheck/B.cs"]);
        Grouped.Definition killed = Definition("Healthcheck/A.cs", "0", "Killed"), failed = Definition("Healthcheck/B.cs", "1", "RuntimeError");
        Grouped.Assessment first = Grouped.Assess(candidate, [killed.Source], [killed], [], new("none", []));
        Grouped.Assessment second = Grouped.Assess(candidate, [failed.Source], [failed], [], new("none", []));
        Grouped.Assessment total = Grouped.SumCompositeAssessments(candidate, candidate.Sources, [killed, failed], [first, second], []);
        Assert.IsTrue(total.SourceInventoryComplete, "Complete source coverage remains distinct from unhealthy execution.");
        Assert.IsFalse(total.CompleteScope);
        Assert.IsFalse(total.Qualified);
        Assert.IsNull(total.ConservativeScore);
        Assert.AreEqual(1, total.RuntimeError);
        Assert.IsTrue(total.Holds.Any(hold => hold.Contains("infrastructure", StringComparison.Ordinal)));
    }

    private static Task<ProcessResult> StartWaitingChild(string directory, CancellationToken cancellation)
    {
        const string script = "[IO.File]::WriteAllText($env:CIS_GROUPED_CHILD_READY, 'ready'); [Console]::Out.Write('ready'); [Console]::Out.Flush(); [Threading.ManualResetEvent]::new($false).WaitOne()";
        return ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
            directory, TimeSpan.FromSeconds(20), new Dictionary<string, string?> { ["CIS_GROUPED_CHILD_READY"] = Path.Combine(directory, "child.ready") }, cancellationToken: cancellation);
    }

    private static async Task Ready(Task<ProcessResult> child, string directory)
    {
        var clock = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(directory, "child.ready")) && !child.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(10)) { await Task.Delay(10); }
        if (child.IsCompleted)
        {
            ProcessResult result = await child;
            Assert.Fail("The native waiting child exited before the handshake: " + result.StandardOutput + result.StandardError);
        }
        Assert.IsTrue(File.Exists(Path.Combine(directory, "child.ready")), "The native child did not reach its waiting handshake.");
    }

    private static async Task Stop(Task<ProcessResult> child, CancellationTokenSource cancellation)
    {
        await cancellation.CancelAsync();
        try { await child; }
        catch (ProcessTimeoutException) { }
    }

    private static Grouped.Candidate Candidate(string[] sources) => new("cis.healthcheck.grouped-candidate/v1", "candidate", "test-platform", "test-host", [], sources, "archive");
    private static Grouped.Definition Definition(string source, string id, string status) => new(source, "source-hash", id, source + ":" + id, "Boolean", "line/column", "false", status);
    private static string Location(int startLine, int startColumn, int endLine, int endColumn) => new JsonObject
    {
        ["start"] = new JsonObject { ["line"] = startLine, ["column"] = startColumn },
        ["end"] = new JsonObject { ["line"] = endLine, ["column"] = endColumn }
    }.ToJsonString();
    private static JsonObject HealthyRun() => new()
    {
        ["processorCount"] = 8,
        ["nativeConcurrency"] = 8,
        ["nativeExitCode"] = 0,
        ["restored"] = true,
        ["failure"] = null,
        ["processCleanupHealthy"] = true,
        ["temporaryCleanup"] = "owned short temp removed",
        ["cleanup"] = "native scratch removed; candidate workspace retained for next group",
        ["peakOwnedBytes"] = 1000,
        ["commandLog"] = new JsonArray(new JsonObject { ["exitCode"] = 0, ["outputDrained"] = true })
    };
}
