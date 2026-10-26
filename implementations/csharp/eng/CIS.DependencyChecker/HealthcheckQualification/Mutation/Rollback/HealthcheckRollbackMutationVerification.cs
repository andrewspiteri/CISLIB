using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Replays native automatic03 rollback/filter omissions individually. Original Stryker evidence is immutable.
internal static class HealthcheckRollbackMutationVerification
{
    private const string Prefix = "implementations/csharp/";
    private const string StatePath = Prefix + "src/CIS.Core/Healthcheck/Internal/State/HealthStateCoordinator.cs";
    private const string StateHash = "7e1462309c085cf952cf0bff034f37eda57108165a1ac7f348fd84142e870c1a";
    private const string ReportHash = "cf22c436cae175745414562918318b2e3cd23cb5e307e62c4c4cba9a448aae02";
    private const string ProbePath = Prefix + "src/CIS.Core/Healthcheck/Pipes/HealthProbeClient.cs";
    private const string ProbeHash = "e022538f8c4694b3b772625d58886ba9897b6fb8f075a365faf08887878f2f54";
    private const string Project = "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj";
    private const string Scheduling = "CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
    private const string Filter = "FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck|FullyQualifiedName=" + Scheduling;
    private static readonly string[] ResourceTests =
    [
        "CIS.Tests.UnitTests.Healthcheck.HealthcheckCancellationResourceQualificationTests.CompletingAnEnteredCollectionDisposesItsOwnedCancellationWaitHandle",
        "CIS.Tests.UnitTests.Healthcheck.HealthcheckCancellationResourceQualificationTests.RetiringAnIdleAttachmentDisposesItsOwnedCancellationWaitHandle"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(Adapt(args), ["repo-root", "report", "output-root"], ["ids", "review-survivor", "review-assertions"]);
        bool reviewSurvivor = input.Optional("review-survivor") is not null;
        string[] assertionIds = input.Optional("review-assertions")?.Split(',') ?? [];
        bool reviewAssertions = input.Optional("review-assertions") is not null;
        if (reviewAssertions && (assertionIds.Length == 0 || assertionIds.Any(id => id is not ("2107" or "2210" or "2216"))
            || assertionIds.Distinct(StringComparer.Ordinal).Count() != assertionIds.Length || input.Optional("ids") is not null || reviewSurvivor))
        { throw new ArgumentException("Use a nonempty distinct subset of --review-assertions 2107,2210,2216, separately from --ids and --review-survivor."); }
        if (reviewSurvivor && (input.Optional("review-survivor") != "2346" || input.Optional("ids") is not null))
        { throw new ArgumentException("Use --review-survivor 2346 only, separately from --ids."); }
        bool explicitReview = reviewSurvivor || reviewAssertions;
        string repo = input.PathOf("repo-root"), reportPath = input.PathOf("report"), output = input.Output;
        Require(!Under(repo, output) || Under(Path.Combine(repo, ".artifacts"), output), "Use an isolated output under .artifacts or outside the checkout.");
        Require(!Under(output, repo) && !Under(output, reportPath), "Output must not contain the checkout or original report.");
        input.EmptyOutput();
        string workspace = Path.Combine(output, "source"), language = Path.Combine(workspace, Prefix), sourcePath = Path.Combine(workspace, StatePath);
        var summary = new JsonObject
        {
            ["schema"] = "cis.healthcheck.independent-rollback-replay/v1-development",
            ["scope"] = reviewSurvivor ? "One explicit automatic03 survivor control (2346) with new resource tests; finite supplement, no automatic/replay retroactive credit or whole-Healthcheck qualification."
                : reviewAssertions ? "Explicit named-assertion controls for automatic03 2107/2210/2216; finite supplement, no raw/frozen replay status rewriting or whole qualification."
                : "103 automatic03 StateCoordinator omissions; supplemental observations, not whole-Healthcheck qualification.",
            ["status"] = "Running",
            ["startedUtc"] = DateTimeOffset.UtcNow,
            ["exitCode"] = 1,
            ["originalReport"] = new JsonObject { ["path"] = reportPath, ["sha256"] = ReportHash, ["modified"] = false },
            ["originalStatuses"] = new JsonObject { ["Killed"] = 196, ["Survived"] = 9, ["Timeout"] = 9, ["NoCoverage"] = 3, ["CompileError"] = 35, ["Ignored"] = 68 },
            ["originalStrictScore"] = new JsonObject { ["numerator"] = 196, ["denominator"] = 217, ["percent"] = 196d / 217 * 100 },
            ["requiredMutationPercent"] = 90,
            ["controls"] = new JsonArray(),
            ["nativeTimeoutSeconds"] = 60,
            ["buildTimeoutSeconds"] = 180,
            ["selectedSubset"] = explicitReview || input.Optional("ids") is not null,
            ["requestedIds"] = input.Optional("ids"),
            ["reviewSurvivor"] = input.Optional("review-survivor"),
            ["reviewAssertions"] = JsonSerializer.SerializeToNode(assertionIds)
        };
        var environment = new Dictionary<string, string?>
        {
            ["CisMutationTesting"] = "true",
            ["UseSharedCompilation"] = "false",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1",
            ["RestoreLockedMode"] = "true",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        };
        SourceInventoryEntry[] inventory = [];
        byte[] originalBytes = [];
        NativeRun? baseline = null;
        JsonArray? baselinePins = null;
        int selectedCount = reviewSurvivor ? 1 : reviewAssertions ? assertionIds.Length : 103;
        bool subset = explicitReview || input.Optional("ids") is not null;
        bool copied = false;
        Exception? failure = null;
        Save();
        try
        {
            JsonObject report = ReadPinned(reportPath, ReportHash, "Supply the immutable raw automatic-state-windows-03 report.");
            originalBytes = File.ReadAllBytes(Path.Combine(repo, StatePath));
            Require(Digest(originalBytes) == StateHash && Hash(Path.Combine(repo, ProbePath)) == ProbeHash, "Current production candidate differs from its pinned State/Probe sources.");
            string original = new UTF8Encoding(false, true).GetString(originalBytes);
            Mutation[] mutations = ReadMutations(report, original, reviewSurvivor);
            if (reviewAssertions)
            {
                mutations = mutations.Where(mutation => assertionIds.Contains(mutation.Id, StringComparer.Ordinal)).ToArray();
                Require(mutations.Length == assertionIds.Length && mutations.All(AssertionMutation), "Assertion-review source spans/replacements differ from the pinned native controls.");
                foreach (Mutation mutation in mutations)
                {
                    AssertionProof proof = Proof(mutation.Id);
                    string[] lines = File.ReadAllLines(Path.Combine(repo, Prefix, proof.Source));
                    Require(lines.Length >= proof.Line && lines[proof.Line - 1].Contains(proof.Expression, StringComparison.Ordinal), "Named direct assertion source changed: " + proof.Source);
                }
            }
            if (input.Optional("ids") is { } requested)
            {
                string[] ids = requested.Split(',');
                Require(ids.Length > 0 && ids.All(id => id.Length > 0 && id.All(char.IsAsciiDigit)) && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length,
                    "Use a nonempty comma-separated distinct list of exact native IDs.");
                Require(ids.All(id => mutations.Any(mutation => mutation.Id == id)), "Requested ID is outside the pinned 103 rollback/filter omissions.");
                mutations = mutations.Where(mutation => ids.Contains(mutation.Id, StringComparer.Ordinal)).ToArray();
            }
            selectedCount = mutations.Length;
            summary["selectedCount"] = selectedCount;
            inventory = (await SourceInventory.CaptureAsync(repo)).Where(pin => IsInput(pin.Path)).ToArray();
            Require(inventory.Any(pin => pin.Path == StatePath && pin.Sha256 == StateHash), "Source inventory omitted the pinned production source.");
            Write(Path.Combine(output, "inputs", "source-inputs.before.json"), JsonSerializer.Serialize(inventory, JsonOptions) + "\n");
            summary["sourceInventory"] = new JsonObject { ["path"] = Path.Combine(output, "inputs", "source-inputs.before.json"), ["files"] = inventory.Length, ["sha256"] = Hash(Path.Combine(output, "inputs", "source-inputs.before.json")) };
            Copy(reportPath, Path.Combine(output, "inputs", "automatic03-report.json"), ReportHash);
            foreach (SourceInventoryEntry pin in inventory) { Copy(Path.Combine(repo, pin.Path), Path.Combine(workspace, pin.Path), pin.Sha256); }
            copied = true;
            await VerifySources(workspace);
            JsonObject toolchain = Read(Path.Combine(language, "eng/toolchain.json"));
            Require((string?)toolchain["sdk"] == "10.0.401" && (string?)toolchain["targetRuntime"] == "10.0.12", "Requalify SDK/runtime before changing these pins.");
            Require((await input.Run("sdk", ["--version"], language, environment: environment)).StandardOutput.Trim() == "10.0.401", "SDK differs from 10.0.401.");
            ProcessResult runtimes = await input.Run("runtimes", ["--list-runtimes"], language, environment: environment);
            foreach (string framework in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
            { Require(runtimes.StandardOutput.Split('\n').Any(line => line.StartsWith(framework + " 10.0.12 ", StringComparison.Ordinal)), "Missing pinned runtime " + framework); }
            summary["sdk"] = "10.0.401"; summary["runtime"] = "10.0.12"; summary["filter"] = Filter;
            await Restore("baseline");
            Require((await Build("baseline")).ExitCode == 0, "Baseline native build failed.");
            baselinePins = PinBinaries("baseline");
            baseline = await Test("baseline");
            summary["baseline"] = NativeJson(baseline, (JsonArray)baselinePins.DeepClone());
            RequirePass(baseline);
            if (explicitReview)
            { Require(ResourceTests.All(method => baseline.Cases.Count(item => item.Method == method) == 1), "Explicit review requires both actual new resource-release cases in its full passing native baseline."); }
            if (reviewAssertions)
            { Require(mutations.All(mutation => baseline.Cases.Count(item => item.Method == Proof(mutation.Id).Method && item.Name == Proof(mutation.Id).CaseName) == 1), "Named assertion case is absent from the full passing native baseline."); }
            foreach (Mutation mutation in mutations)
            {
                summary["controls"]!.AsArray().Add(new JsonObject
                {
                    ["id"] = mutation.Id,
                    ["originalStatus"] = mutation.OriginalStatus,
                    ["status"] = "NotAttempted",
                    ["mutatorName"] = mutation.Mutator,
                    ["location"] = mutation.Location.DeepClone(),
                    ["originalText"] = mutation.Find,
                    ["replacement"] = mutation.Replacement,
                    ["sourceSha256"] = StateHash,
                    ["offset"] = mutation.Offset,
                    ["length"] = mutation.Find.Length
                });
            }
            Save();
            foreach (Mutation mutation in mutations)
            {
                JsonObject control = summary["controls"]!.AsArray().OfType<JsonObject>().Single(row => (string?)row["id"] == mutation.Id);
                string name = "mutant-" + mutation.Id;
                control["status"] = "Running"; control["stage"] = "SourceValidation"; Save();
                Console.WriteLine("Healthcheck independent rollback replay: " + mutation.Id + " (original " + mutation.OriginalStatus + ")");
                try
                {
                    await VerifySources(workspace);
                    Require(Hash(sourcePath) == StateHash, "Previous mutant was not restored.");
                    string changed = original[..mutation.Offset] + mutation.Replacement + original[(mutation.Offset + mutation.Find.Length)..];
                    Write(sourcePath, changed);
                    control["mutatedSourceSha256"] = Hash(sourcePath);
                    Copy(sourcePath, Path.Combine(output, "controls", mutation.Id, "changed-source.cs"));
                    WriteJson(Path.Combine(output, "controls", mutation.Id, "patch.json"), (JsonObject)control.DeepClone());
                    control["patchSha256"] = Hash(Path.Combine(output, "controls", mutation.Id, "patch.json"));
                    control["stage"] = "Build"; Save();
                    ProcessResult build = await Build(name);
                    control["buildExitCode"] = build.ExitCode;
                    string buildText = build.StandardOutput + build.StandardError;
                    string[] diagnostics = buildText.Split('\n').Where(line => Regex.IsMatch(line, @"\berror [A-Z]+\d+\s*:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).Distinct(StringComparer.Ordinal).ToArray();
                    control["buildDiagnostics"] = JsonSerializer.SerializeToNode(diagnostics);
                    if (build.ExitCode != 0)
                    {
                        // Only observed C# errors located in the one changed production file establish independent noncompilation.
                        bool compiler = diagnostics.Length > 0 && diagnostics.All(line => line.Contains("HealthStateCoordinator.cs(", StringComparison.Ordinal)
                            && Regex.IsMatch(line, @"\berror CS\d{4}\s*:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
                        control["status"] = compiler ? "IndependentCompileError" : "BuildFailureUnresolved";
                        continue;
                    }
                    JsonArray binaries = PinBinaries(name);
                    control["stage"] = "Test"; control["binaries"] = binaries;
                    control["nativeTrxPath"] = Path.Combine(output, "native", name, "selected.trx"); Save();
                    NativeRun native = await Test(name);
                    control["native"] = NativeJson(native, (JsonArray)binaries.DeepClone());
                    bool exactCases = SameCases(baseline, native);
                    bool scheduling = native.Serialized && native.Cases.Count(item => item.Method == Scheduling && item.Outcome == "Passed") == 1;
                    bool complete = exactCases && scheduling && native.Cases.All(item => item.Outcome is "Passed" or "Failed") && !native.RunError;
                    NativeCase[] failed = native.Cases.Where(item => item.Outcome == "Failed").ToArray();
                    bool boundedFailure = native.TimedOut || failed.Any(item => TimeoutFailure(item.Message));
                    bool recognized = failed.All(item => RecognizedFailure(item, workspace));
                    bool resourceFailures = !reviewSurvivor || failed.Length == ResourceTests.Length && ResourceTests.All(method => failed.Any(item => item.Method == method
                        && item.Message.Contains("Assert.IsTrue(resource.IsClosed)", StringComparison.Ordinal) && item.Message.Contains("actual: false", StringComparison.OrdinalIgnoreCase)));
                    bool assertionFailures = false;
                    if (reviewAssertions)
                    {
                        AssertionProof proof = Proof(mutation.Id);
                        NativeCase[] matched = failed.Where(item => MatchesProof(item, proof, workspace)).ToArray();
                        control["directAssertionProof"] = JsonSerializer.SerializeToNode(proof);
                        control["matchedDirectAssertionCases"] = JsonSerializer.SerializeToNode(matched);
                        control["otherFailedCases"] = JsonSerializer.SerializeToNode(failed.Where(item => !matched.Contains(item)).Select(item => new
                        {
                            item.Id,
                            item.Name,
                            item.Method,
                            DefaultRecognized = RecognizedFailure(item, workspace),
                            KnownTestSideConsequence = KnownConsequence(mutation.Id, item, workspace),
                            KillCredit = false
                        }));
                        assertionFailures = matched.Length == 1 && failed.All(item => RecognizedFailure(item, workspace) || KnownConsequence(mutation.Id, item, workspace));
                        control["namedDirectAssertionDetected"] = assertionFailures;
                    }
                    control["exactBaselineCases"] = exactCases; control["recognizedFailures"] = recognized;
                    if (reviewSurvivor) { control["bothIntendedResourceFailures"] = resourceFailures; }
                    control["status"] = boundedFailure ? "TestTimeout" : complete && native.ExitCode == 0 && failed.Length == 0 ? "Survived"
                        : complete && native.ExitCode == 1 && failed.Length > 0 && (reviewAssertions ? assertionFailures : recognized && resourceFailures) ? "Killed" : "NativeFailureUnresolved";
                }
                catch (TimeoutException error)
                { control["status"] = (string?)control["stage"] == "Test" ? "TestTimeout" : "BuildTimeoutUnresolved"; control["detail"] = error.Message; control["partialStreamsAvailable"] = false; }
                catch (Exception error) when (Expected(error))
                { control["status"] = "InfrastructureFailureUnresolved"; control["detail"] = error.Message; }
                finally
                {
                    File.WriteAllBytes(sourcePath, originalBytes);
                    control["restoredSourceSha256"] = Hash(sourcePath);
                    Require(Hash(sourcePath) == StateHash, "Exact source restoration failed.");
                    Save();
                }
            }
        }
        catch (Exception error) when (Expected(error)) { failure = error; }
        finally
        {
            if (copied)
            {
                try
                {
                    File.WriteAllBytes(sourcePath, originalBytes);
                    await VerifySources(workspace);
                    // Freshly replace all referenced mutated outputs, including after a failed/aborted row.
                    await Restore("restored");
                    Require((await Build("restored")).ExitCode == 0, "Final unmutated rebuild failed.");
                    JsonArray binaries = PinBinaries("restored");
                    NativeRun restored = await Test("restored");
                    summary["restored"] = NativeJson(restored, binaries);
                    RequirePass(restored);
                    Require(baselinePins is not null && SameBinaryPins(baselinePins, binaries), "Final unmutated Core/test binaries differ from their exact baseline identities.");
                    Require(baseline is not null && SameCases(baseline, restored), "Restored native test identities differ from the baseline.");
                    await VerifySources(workspace);
                    summary["restorationPassed"] = true;
                    summary["exactBaselineBinariesRestored"] = true;
                }
                catch (Exception error) when (Expected(error)) { summary["restorationPassed"] = false; summary["restorationError"] = error.Message; failure ??= error; }
            }
            try
            {
                if (inventory.Length > 0) { await VerifySources(repo); }
                Require(Hash(reportPath) == ReportHash, "Original native report changed.");
                summary["canonicalInputsUnmodified"] = inventory.Length > 0;
            }
            catch (Exception error) when (Expected(error)) { summary["canonicalInputsUnmodified"] = false; failure ??= error; }
            JsonObject[] controls = summary["controls"]!.AsArray().OfType<JsonObject>().ToArray();
            int Count(string status) => controls.Count(row => (string?)row["status"] == status);
            int killed = Count("Killed"), compiler = Count("IndependentCompileError"), survived = Count("Survived"), timedOut = Count("TestTimeout");
            int unresolved = selectedCount - killed - compiler - survived - timedOut;
            int denominator = 320 - compiler, numerator = 196 + killed;
            bool executionComplete = controls.Length == selectedCount && unresolved == 0;
            bool accounted = !subset && controls.Length == 103 && executionComplete;
            bool sound = failure is null && executionComplete && (bool?)summary["restorationPassed"] == true && (bool?)summary["canonicalInputsUnmodified"] == true;
            double lowerBound = (double)numerator / denominator * 100;
            summary["supplemental"] = new JsonObject
            {
                ["requiredFullSelection"] = explicitReview ? selectedCount : 103,
                ["selected"] = selectedCount,
                ["notSelected"] = explicitReview ? 0 : 103 - selectedCount,
                ["killed"] = killed,
                ["survived"] = survived,
                ["testTimeout"] = timedOut,
                ["independentCompileError"] = compiler,
                ["unresolved"] = unresolved,
                ["observedTestable"] = killed + survived + timedOut,
                ["allSelectedAccounted"] = accounted,
                ["combinedKilled"] = numerator,
                ["combinedConservativeDenominator"] = denominator,
                ["combinedLowerBoundPercent"] = lowerBound,
                ["denominatorRule"] = "All 320 original selected mutants remain except individually observed source compiler errors; all timeouts, survivors, uncovered and unresolved rows receive zero kill credit.",
                ["originalReportOrScoreChanged"] = false,
                ["equivalenceOrArbitraryExclusionsGranted"] = false,
                ["stateSubsetMeets90Percent"] = sound && accounted && lowerBound >= 90,
                ["wholeHealthcheckMutationQualification"] = "Pending separate remaining-source qualification"
            };
            if (reviewSurvivor)
            {
                summary["supplemental"]!["finiteSurvivorControl"] = true;
                summary["supplemental"]!["originalSurvivorStatus"] = "Survived";
                summary["supplemental"]!["remainingRollbackOmissions"] = 103;
                summary["supplemental"]!["automaticOrFrozenReplayRetroactiveCredit"] = false;
                summary["supplemental"]!["allRequestedControlsObserved"] = executionComplete;
                summary["supplemental"]!["wholeQualification"] = false;
            }
            if (reviewAssertions)
            {
                summary["supplemental"]!["finiteAssertionControls"] = true;
                summary["supplemental"]!["automaticOrFrozenReplayRetroactiveCredit"] = false;
                summary["supplemental"]!["allRequestedControlsObserved"] = executionComplete;
                summary["supplemental"]!["wholeQualification"] = false;
            }
            summary["status"] = sound ? subset ? "SelectedSubset" : lowerBound >= 90 ? "PassedStateSupplement" : "BelowStateThreshold" : "Incomplete";
            summary["exitCode"] = sound && (subset || lowerBound >= 90) ? 0 : 1;
            summary["detail"] = failure?.Message; summary["finishedUtc"] = DateTimeOffset.UtcNow; Save();
        }
        Console.WriteLine("Healthcheck rollback replay: " + summary["status"] + "; " + Path.Combine(output, "summary.json"));
        return summary["exitCode"]!.GetValue<int>();

        void Save() => WriteJson(Path.Combine(output, "summary.json"), summary);
        async Task VerifySources(string root)
        {
            SourceInventoryEntry[] actual = (await SourceInventory.CaptureAsync(root)).Where(pin => IsInput(pin.Path)).ToArray();
            string[] expectedKeys = inventory.Select(pin => pin.Path + "|" + pin.Sha256).Order(StringComparer.Ordinal).ToArray();
            string[] actualKeys = actual.Select(pin => pin.Path + "|" + pin.Sha256).Order(StringComparer.Ordinal).ToArray();
            if (root == repo)
            { Write(Path.Combine(output, "inputs", "source-inputs.after.json"), JsonSerializer.Serialize(actual, JsonOptions) + "\n"); }
            Require(expectedKeys.SequenceEqual(actualKeys, StringComparer.Ordinal), "Maintained source inventory changed: " + root);
        }
        async Task Restore(string name) => _ = await input.Run(name + "-restore", ["restore", Project, "--locked-mode", "--configfile", "NuGet.config"], language,
            environment: environment, timeout: TimeSpan.FromSeconds(180));
        async Task<ProcessResult> Build(string name)
        {
            string[] arguments = ["build", Project, "-c", "Release", "--no-restore", "--no-incremental", "-maxcpucount:1"];
            Request(name + "-build", arguments, 180);
            return await input.Run(name + "-build", arguments, language, environment: environment, timeout: TimeSpan.FromSeconds(180), requireSuccess: false);
        }
        async Task<NativeRun> Test(string name)
        {
            string results = Path.Combine(output, "native", name), trx = Path.Combine(results, "selected.trx");
            string[] arguments = ["test", Project, "-c", "Release", "--no-build", "--no-restore", "--filter", Filter,
                "--results-directory", results, "--logger", "trx;LogFileName=selected.trx", "--", "RunConfiguration.TestSessionTimeout=55000"];
            Request(name + "-test", arguments, 60);
            ProcessResult result = await input.Run(name + "-test", arguments, language,
                environment: environment, timeout: TimeSpan.FromSeconds(60), requireSuccess: false);
            if (!File.Exists(trx) && TimeoutFailure(result.StandardOutput + result.StandardError))
            { throw new TimeoutException("Native test session timed out/canceled without a complete TRX; inspect the retained completed-process log."); }
            return ReadNative(trx, result.ExitCode, result.StandardOutput + result.StandardError);
        }
        void Request(string name, string[] arguments, int seconds) => WriteJson(Path.Combine(output, "logs", name + ".request.json"), new JsonObject
        {
            ["executable"] = input.Dotnet,
            ["arguments"] = JsonSerializer.SerializeToNode(arguments),
            ["workingDirectory"] = language,
            ["timeoutSeconds"] = seconds,
            ["environmentOverrides"] = JsonSerializer.SerializeToNode(environment)
        });
        JsonArray PinBinaries(string name)
        {
            string built = Path.Combine(language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll"), testRoot = Path.Combine(language, "tests/CIS.Core.UnitTests/bin/Release/net10.0");
            string coreHash = Hash(built), coreCopy = Path.Combine(testRoot, "CIS.Core.dll");
            Require(Hash(coreCopy) == coreHash, "Actual test output did not receive freshly built Core.");
            var retained = new JsonArray();
            foreach (string path in new[] { built, Path.ChangeExtension(built, ".pdb"), coreCopy, Path.Combine(testRoot, "CIS.Core.pdb"),
                Path.Combine(testRoot, "CIS.Core.UnitTests.dll"), Path.Combine(testRoot, "CIS.Core.UnitTests.pdb"),
                Path.Combine(testRoot, "CIS.Core.UnitTests.deps.json"), Path.Combine(testRoot, "CIS.Core.UnitTests.runtimeconfig.json") })
            {
                string copy = Path.Combine(output, "binaries", name, Path.GetRelativePath(workspace, path));
                Copy(path, copy);
                var pin = new JsonObject { ["observedPath"] = path, ["retainedPath"] = copy, ["sha256"] = Hash(path) };
                if (path.EndsWith(".dll", StringComparison.Ordinal))
                {
                    using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
                    MetadataReader metadata = pe.GetMetadataReader();
                    pin["assemblyName"] = metadata.GetString(metadata.GetAssemblyDefinition().Name);
                    pin["moduleVersionId"] = metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("D");
                }
                retained.Add(pin);
            }
            JsonObject runtime = Read(Path.Combine(testRoot, "CIS.Core.UnitTests.runtimeconfig.json"))["runtimeOptions"]!.AsObject();
            JsonNode[] frameworks = runtime["frameworks"] is JsonArray many ? many.OfType<JsonNode>().ToArray() : [runtime["framework"]!];
            Require((string?)runtime["rollForward"] == "Disable" && frameworks.Length > 0 && frameworks.All(item => (string?)item["version"] == "10.0.12"), "Native runtime policy differs from its pin.");
            JsonObject deps = Read(Path.Combine(testRoot, "CIS.Core.UnitTests.deps.json"));
            Require(deps["targets"]!.AsObject().Any(target => target.Value!.AsObject().Any(library => library.Key.StartsWith("CIS.Core/", StringComparison.Ordinal)
                && library.Value?["runtime"]?["CIS.Core.dll"] is not null)), "Actual unit dependency manifest does not select Core.");
            return retained;
        }
    }

    private static Mutation[] ReadMutations(JsonObject report, string original, bool reviewSurvivor)
    {
        JsonObject[] files = report["files"]!.AsObject().Where(pair => pair.Key.Replace('\\', '/').EndsWith("/" + StatePath, StringComparison.Ordinal)).Select(pair => pair.Value!.AsObject()).ToArray();
        Require(files.Length == 1 && (string?)files[0]["source"] == original, "Raw native source differs from the exact pinned production source.");
        JsonObject[] all = files[0]["mutants"]!.AsArray().OfType<JsonObject>().ToArray();
        var expected = new Dictionary<string, int>(StringComparer.Ordinal) { ["Killed"] = 196, ["Survived"] = 9, ["Timeout"] = 9, ["NoCoverage"] = 3, ["CompileError"] = 35, ["Ignored"] = 68 };
        Require(all.Length == 320 && all.Select(row => Required(row, "id")).Distinct(StringComparer.Ordinal).Count() == 320
            && expected.All(pair => all.Count(row => (string?)row["status"] == pair.Key) == pair.Value), "Native selected inventory/statuses differ from automatic03.");
        int Offset(JsonNode point)
        {
            int line = point["line"]!.GetValue<int>(), column = point["column"]!.GetValue<int>(), start = 0;
            Require(line > 0 && column > 0, "Native locations are one-based.");
            for (int current = 1; current < line; current++) { int next = original.IndexOf('\n', start); Require(next >= 0, "Native location exceeds source lines."); start = next + 1; }
            int end = original.IndexOf('\n', start); if (end < 0) { end = original.Length; }
            Require(column - 1 <= end - start, "Native column exceeds its source line.");
            return start + column - 1;
        }
        Mutation[] mutations = all.Where(row => reviewSurvivor ? (string?)row["id"] == "2346" && (string?)row["status"] == "Survived"
            : (string?)row["status"] is "CompileError" or "Ignored").Select(row =>
        {
            JsonObject location = row["location"]!.AsObject();
            int start = Offset(location["start"]!), end = Offset(location["end"]!);
            Require(end > start, "Empty or reversed native source span.");
            string replacement = Required(row, "replacement"), find = original[start..end];
            Require(find != replacement && int.TryParse(Required(row, "id"), out _), "Invalid raw mutant replacement/ID.");
            return new Mutation(Required(row, "id"), Required(row, "status"), Required(row, "mutatorName"), start, find, replacement, (JsonObject)location.DeepClone());
        }).OrderBy(row => int.Parse(row.Id, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (reviewSurvivor)
        {
            Require(mutations.Length == 1 && mutations[0].Id == "2346" && mutations[0].OriginalStatus == "Survived"
                && mutations[0].Find == "source.Dispose();" && mutations[0].Replacement == ";"
                && (int?)mutations[0].Location["start"]?["line"] == 427 && (int?)mutations[0].Location["end"]?["line"] == 427,
                "Survivor review differs from the pinned raw 2346 disposal-statement replacement.");
        }
        return mutations;
    }

    private static NativeRun ReadNative(string path, int exitCode, string output)
    {
        XDocument trx = XDocument.Load(path);
        Require(trx.Root?.Name == XName.Get("TestRun", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"), "Expected actual native VSTest TRX.");
        var methods = trx.Descendants().Where(element => element.Name.LocalName == "UnitTest").ToDictionary(element => Attribute(element, "id"), element =>
        {
            XElement method = element.Elements().Single(child => child.Name.LocalName == "TestMethod");
            return Attribute(method, "className") + "." + Attribute(method, "name");
        }, StringComparer.OrdinalIgnoreCase);
        NativeCase[] cases = trx.Descendants().Where(element => element.Name.LocalName == "UnitTestResult").Select(element => new NativeCase(
            Attribute(element, "testId"), Attribute(element, "testName"), methods[Attribute(element, "testId")], Attribute(element, "outcome"),
            element.Descendants().FirstOrDefault(child => child.Name.LocalName == "Message")?.Value ?? "",
            element.Descendants().FirstOrDefault(child => child.Name.LocalName == "StackTrace")?.Value ?? "")).ToArray();
        Require(cases.Length > 0 && cases.All(item => Guid.TryParse(item.Id, out Guid id) && id != Guid.Empty)
            && cases.Select(Key).Distinct(StringComparer.Ordinal).Count() == cases.Length, "Empty/invalid native case inventory.");
        XElement counters = trx.Descendants().Single(element => element.Name.LocalName == "Counters");
        bool complete = (int?)counters.Attribute("total") == cases.Length && (int?)counters.Attribute("executed") == cases.Length;
        string runText = string.Join("\n", trx.Descendants().Where(element => element.Name.LocalName is "RunInfo" or "Message").Select(element => element.Value));
        bool serialized = trx.Descendants().Any(element => element.Name.LocalName == "StdOut" && element.Value.Contains("CIS mutation scheduling: serialized", StringComparison.Ordinal));
        bool runError = !complete || trx.Descendants().Any(element => element.Name.LocalName == "RunInfo" && (string?)element.Attribute("outcome") == "Error")
            || InfrastructureFailure(runText + "\n" + output) || output.Contains("Test Run Aborted", StringComparison.OrdinalIgnoreCase);
        return new NativeRun(path, Hash(path), exitCode, serialized, runError, TimeoutFailure(runText + "\n" + output), cases);
    }

    private static bool AssertionMutation(Mutation mutation) => mutation.Id switch
    {
        "2107" => mutation.OriginalStatus == "CompileError" && mutation.Find == "true" && mutation.Replacement == "false"
            && (int?)mutation.Location["start"]?["line"] == 119 && (int?)mutation.Location["end"]?["line"] == 119,
        "2210" => mutation.OriginalStatus == "Ignored" && mutation.Replacement == "{}" && mutation.Find.StartsWith("{\n        ArgumentNullException.ThrowIfNull(measurement);", StringComparison.Ordinal)
            && (int?)mutation.Location["start"]?["line"] == 232 && (int?)mutation.Location["end"]?["line"] == 242,
        "2216" => mutation.OriginalStatus == "Ignored" && mutation.Replacement == "{}" && mutation.Find.StartsWith("{\n            EnsureActive(entry);", StringComparison.Ordinal)
            && (int?)mutation.Location["start"]?["line"] == 236 && (int?)mutation.Location["end"]?["line"] == 241,
        _ => false
    };

    private static AssertionProof Proof(string id)
    {
        string method = id == "2107" ? "ConditionRegistrationRecordsItsActualInitialObservation" : "MeasurementKeyLimitRejectsOnlyNewKeysAndAllowsExistingKeyReplacement";
        string type = id == "2107" ? "HealthcheckRevisionQualificationTests" : "HealthcheckServicePolicyTests";
        string expression = id == "2107" ? "Assert.AreEqual(flagged ? \"InitialFlag\" : null, observed.Diagnostic?.Code)" : "Assert.AreEqual(32, Find(full, monitor).Measurements.Count)";
        string message = "Assertion failed. Expected values to be equal.\n\nexpected: " + (id == "2107" ? "\"InitialFlag\"" : "32")
            + "\nactual:   " + (id == "2107" ? "null" : "0") + "\n\n" + expression;
        return new("CIS.Tests.UnitTests.Healthcheck." + type + "." + method, method + (id == "2107" ? " (True)" : ""),
            "tests/CIS.Core.UnitTests/Healthcheck/Core/" + type + ".cs", id == "2107" ? 36 : 446, expression, message);
    }

    private static bool MatchesProof(NativeCase item, AssertionProof proof, string workspace)
    {
        string source = Path.GetFullPath(Path.Combine(workspace, Prefix, proof.Source)).Replace('\\', '/');
        return item.Method == proof.Method && item.Name == proof.CaseName && item.Message.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() == proof.Message
            && item.StackTrace.Replace('\\', '/').Split('\n').Any(frame => frame.Contains(item.Method + "(", StringComparison.Ordinal)
                && frame.TrimEnd('\r').EndsWith(source + ":line " + proof.Line.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    private static bool KnownConsequence(string id, NativeCase item, string workspace)
    {
        string? exception = id == "2107" && item.Method == "CIS.Tests.UnitTests.Healthcheck.HealthcheckServicePolicyTests.StartupCompletionIsIdempotentAndDoesNotClearAStartupFailure"
            ? "System.NullReferenceException: Object reference not set to an instance of an object."
            : id is "2210" or "2216" ? item.Method switch
            {
                "CIS.Tests.UnitTests.Healthcheck.HealthcheckStateSeamTests.OptionalErrorPreservesPolicyAndPreviousMeasurementWithoutRefreshingEither" => "System.InvalidOperationException: Sequence contains no matching element",
                "CIS.Tests.UnitTests.Healthcheck.HealthCollectorRunnerLifecycleTests.OptionalEvaluationErrorsPreserveHealthAndTheLastMeasurementValueAndTime" => "System.InvalidOperationException: Sequence contains no elements",
                "CIS.Tests.UnitTests.Healthcheck.HealthReportFormatterTests.NumericPolicyAndAllDiagnosticScalarKindsRetainTheirWireTypes"
                    or "CIS.Tests.UnitTests.Healthcheck.HealthReportFormatterTests.EscapedControlTextAndSupplementaryUnicodeRoundTripAsStrictUtf8" => "System.IndexOutOfRangeException: Index was outside the bounds of the array.",
                _ => null
            } : null;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(workspace, Prefix))).Replace('\\', '/') + "/tests/CIS.Core.UnitTests/Healthcheck/";
        string stack = item.StackTrace.Replace('\\', '/');
        return exception is not null && !InfrastructureFailure(item.Message) && !TimeoutFailure(item.Message)
            && item.Message.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() == "Test method " + item.Method + " threw exception:\n" + exception
            && stack.Contains(root, StringComparison.Ordinal) && stack.Contains(item.Method + "(", StringComparison.Ordinal);
    }

    private static bool RecognizedFailure(NativeCase item, string workspace)
    {
        if (item.Method == Scheduling || !item.Method.StartsWith("CIS.Tests.UnitTests.Healthcheck.", StringComparison.Ordinal)
            || InfrastructureFailure(item.Message) || TimeoutFailure(item.Message)) { return false; }
        string stack = item.StackTrace.Replace('\\', '/');
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(workspace, Prefix))).Replace('\\', '/') + "/";
        bool testFrame = stack.Contains(root + "tests/CIS.Core.UnitTests/Healthcheck/", StringComparison.Ordinal)
            && stack.Contains(item.Method + "(", StringComparison.Ordinal);
        bool assertion = item.Message.StartsWith("Assertion failed.", StringComparison.Ordinal) || item.Message.StartsWith("Assert.", StringComparison.Ordinal)
            || item.Message.StartsWith("CollectionAssert.", StringComparison.Ordinal) || item.Message.StartsWith("StringAssert.", StringComparison.Ordinal);
        bool productionException = item.Message.StartsWith("Test method " + item.Method + " threw exception:", StringComparison.Ordinal)
            && stack.Contains(root + "src/CIS.Core/Healthcheck/", StringComparison.Ordinal);
        return testFrame && (assertion || productionException);
    }
    private static bool InfrastructureFailure(string text) => new[] { "FileNotFoundException", "DirectoryNotFoundException", "DllNotFoundException", "FileLoadException", "BadImageFormatException",
        "TypeLoadException", "MissingMethodException", "Win32Exception", "UnauthorizedAccessException", "Test host process crashed", "testhost process exited", "Failed to launch testhost",
        "Could not find testhost", "No test matches", "real fixture must be built and copied", "TransportOrProtocolFailure" }.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool TimeoutFailure(string text) => new[] { "TimeoutException", "TaskCanceledException", "OperationCanceledException", "test session timeout", "test session timed out", "test run timeout",
        "exceeded execution timeout", "exceeded its", "TimeoutOrCanceled" }.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static void RequirePass(NativeRun run) => Require(run.ExitCode == 0 && run.Serialized && !run.RunError && !run.TimedOut && run.Cases.All(item => item.Outcome == "Passed")
        && run.Cases.Count(item => item.Method == Scheduling) == 1, "Native baseline/restoration was empty, failed, skipped or not serialized.");
    private static bool SameCases(NativeRun before, NativeRun after) => before.Cases.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(after.Cases.Select(Key).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    private static bool SameBinaryPins(JsonArray before, JsonArray after) => before.OfType<JsonObject>().Select(pin => (string?)pin["observedPath"] + "|" + (string?)pin["sha256"]).Order(StringComparer.Ordinal)
        .SequenceEqual(after.OfType<JsonObject>().Select(pin => (string?)pin["observedPath"] + "|" + (string?)pin["sha256"]).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    private static string Key(NativeCase item) => item.Id.ToLowerInvariant() + "|" + item.Name + "|" + item.Method;
    private static JsonObject NativeJson(NativeRun run, JsonArray binaries) => new()
    {
        ["trx"] = run.Path,
        ["sha256"] = run.Hash,
        ["exitCode"] = run.ExitCode,
        ["serialized"] = run.Serialized,
        ["runError"] = run.RunError,
        ["timedOut"] = run.TimedOut,
        ["count"] = run.Cases.Length,
        ["passed"] = run.Cases.Count(item => item.Outcome == "Passed"),
        ["failed"] = run.Cases.Count(item => item.Outcome == "Failed"),
        ["cases"] = JsonSerializer.SerializeToNode(run.Cases),
        ["binaries"] = binaries
    };
    private static string Attribute(XElement element, string name) => (string?)element.Attribute(name) ?? throw new InvalidDataException("Missing native " + name);
    private static string Required(JsonNode node, string key) => node[key]?.GetValue<string>() is { Length: > 0 } text ? text : throw new InvalidDataException("Missing native " + key);
    private static bool IsInput(string path) => path.StartsWith(Prefix, StringComparison.Ordinal) || path.StartsWith("conformance/", StringComparison.Ordinal)
        || path.StartsWith("contracts/", StringComparison.Ordinal) || path.StartsWith("compatibility/csharp/", StringComparison.Ordinal)
        || path is "README.md" or "AGENTS.md" or ".gitattributes" or ".editorconfig" or ".gitignore";
    private static bool Under(string root, string path) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || Path.GetFullPath(root).Equals(Path.GetFullPath(path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Expected(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
        or JsonException or System.Xml.XmlException or System.ComponentModel.Win32Exception or TimeoutException or KeyNotFoundException or FormatException;
    private static string[] Adapt(string[] args)
    {
        string[] result = args.ToArray();
        for (int index = 0; index < result.Length; index += 2)
        {
            if (index + 1 >= result.Length || result[index] is not ("--repo-root" or "--report" or "--output" or "--dotnet" or "--ids" or "--review-survivor" or "--review-assertions"))
            { throw new ArgumentException("Use --repo-root <path> --report <raw-automatic03-json> --output <empty-isolated-path> [--dotnet <path>] [--ids <native-id,native-id> | --review-survivor 2346 | --review-assertions <2107,2210,2216-subset>]."); }
            if (result[index] == "--output") { result[index] = "--output-root"; }
        }
        return result;
    }
    private sealed record Mutation(string Id, string OriginalStatus, string Mutator, int Offset, string Find, string Replacement, JsonObject Location);
    private sealed record AssertionProof(string Method, string CaseName, string Source, int Line, string Expression, string Message);
    private sealed record NativeCase(string Id, string Name, string Method, string Outcome, string Message, string StackTrace);
    private sealed record NativeRun(string Path, string Hash, int ExitCode, bool Serialized, bool RunError, bool TimedOut, NativeCase[] Cases);
}
