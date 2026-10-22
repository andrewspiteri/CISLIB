using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;
using ChildProtocol = CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildProtocol;

namespace CIS.Verification;

// Independent replacements from explicit pinned reports. No Stryker result, exclusion or release status is rewritten.
internal static class HealthcheckAspNetOmissionReplayVerification
{
    private const string Prefix = "implementations/csharp/";
    private const string Source = "src/CIS.Core/Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs";
    private const string Project = "tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj";
    private const string TestType = "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests";
    private const string Scheduling = "CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
    private const string Filter = "FullyQualifiedName~" + TestType + ".|FullyQualifiedName=" + Scheduling;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args, ["repo-root", "report", "output-root"], ["ids", "stryker-core", "profile"]);
        ReplayProfile profile = Profile(input.Optional("profile"));
        bool historicalProfile = profile.Name == "historical77";
        string reportBaselineKey = historicalProfile ? "historicalBaseline" : "reportSourceBaseline";
        string reportRestoredKey = historicalProfile ? "historicalRestored" : "reportSourceRestored";
        string reportRestorationKey = historicalProfile ? "historicalRestorationPassed" : "reportSourceRestorationPassed";
        string dotnet = Path.GetFullPath(input.Optional("dotnet") ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Supply the actual pinned --dotnet executable."));
        Require(File.Exists(dotnet) && Path.GetFileNameWithoutExtension(dotnet).Equals("dotnet", StringComparison.OrdinalIgnoreCase),
            "Run with the actual dotnet host or supply its full --dotnet path.");
        if (input.Optional("dotnet") is null)
        { input = new TypeManagerVerificationInputs([.. args, "--dotnet", dotnet], ["repo-root", "report", "output-root"], ["ids", "stryker-core", "profile"]); }
        string packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        string stryker = Path.GetFullPath(input.Optional("stryker-core") ?? Path.Combine(packages, "dotnet-stryker/4.16.0/tools/net8.0/any/Stryker.Core.dll"));
        Require(Hash(stryker) == "bf02f78d3725e1243edb7a32d88dc1a02c88f1f9f74b60b18730636073f234d7", "Supply the exact pinned Stryker.Core 4.16.0 --stryker-core provenance input.");
        string repo = input.PathOf("repo-root"), output = input.Output, reportPath = input.PathOf("report");
        Require(!Under(output, repo) && (!Under(repo, output) || Under(Path.Combine(repo, ".artifacts"), output)),
            "Use a new isolated output outside the checkout or under its .artifacts directory.");
        Require(!Under(output, reportPath), "Output must not contain the immutable original report.");
        input.EmptyOutput();
        string workspace = Path.Combine(output, "s"), language = Path.Combine(workspace, Prefix), target = Path.Combine(language, Source);
        string copiedReport = Path.Combine(output, "inputs", "report.json"), config = Path.Combine(output, "inputs", "child-config.json");
        var summary = new JsonObject
        {
            ["schema"] = "cis.healthcheck.aspnet-omission-replay/v1-development",
            ["scope"] = profile.OmissionCount.ToString(CultureInfo.InvariantCulture) + " individual " + profile.Name + " ASP.NET compiler/ignored replacements with a current frozen detector harness; no automatic, equivalence or release claim.",
            ["profile"] = profile.Name,
            ["status"] = "Running",
            ["exitCode"] = 1,
            ["startedUtc"] = DateTimeOffset.UtcNow,
            ["report"] = new JsonObject { ["path"] = reportPath, ["sha256"] = profile.ReportHash },
            ["reportSourceSha256"] = profile.SourceHash,
            ["nativeFilter"] = Filter,
            ["originalStatuses"] = new JsonObject
            {
                ["Killed"] = profile.Killed,
                ["Survived"] = profile.Survived,
                ["Timeout"] = 0,
                ["NoCoverage"] = 0,
                ["CompileError"] = 72,
                ["Ignored"] = profile.IgnoredIds.Length
            },
            ["selectedSubset"] = input.Optional("ids") is not null,
            ["requestedIds"] = input.Optional("ids"),
            ["controls"] = new JsonArray(),
            ["nativeTimeoutSeconds"] = 960,
            ["buildTimeoutSeconds"] = 240,
            ["automaticReportChanged"] = false,
            ["equivalenceGranted"] = false,
            ["releaseQualified"] = false
        };
        if (historicalProfile) { summary["historicalSourceSha256"] = profile.SourceHash; }
        var environment = new Dictionary<string, string?>
        {
            ["CisMutationTesting"] = "true",
            ["UseSharedCompilation"] = "false",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1",
            ["RestoreLockedMode"] = "true",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            [ChildProtocol.SessionVariable] = null,
            [ChildProtocol.LaunchVariable] = null,
            [ChildProtocol.SelectorVariable] = null,
            ["STRYKER_MUTANT_ID_CONTROL_VAR"] = null,
            ["STRYKER_MUTANT_FILE"] = null,
            ["STRYKER_COVERAGE_FILE"] = null
        };
        SourceInventoryEntry[] inventory = [];
        byte[] canonical = [];
        string historical = "";
        bool copied = false;
        int selected = profile.OmissionCount;
        NativeRun? canonicalBaseline = null, historicalBaseline = null;
        JsonArray? canonicalPins = null, historicalPins = null;
        Exception? failure = null;
        Save();
        try
        {
            JsonObject report = ReadPinned(reportPath, profile.ReportHash, "Supply the exact report pinned by profile " + profile.Name + ".");
            var parsed = Mutations(report, profile);
            historical = parsed.Source;
            Mutation[] mutations = parsed.Mutations;
            if (input.Optional("ids") is { } selection)
            {
                string[] ids = selection.Split(',');
                Require(ids.Length > 0 && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length
                    && ids.All(id => mutations.Any(mutation => mutation.Id == id)), "Requested IDs must be a distinct nonempty subset of the selected profile's exact omissions.");
                mutations = mutations.Where(mutation => ids.Contains(mutation.Id, StringComparer.Ordinal)).ToArray();
            }
            selected = mutations.Length;
            summary["selectedCount"] = selected;
            canonical = File.ReadAllBytes(Path.Combine(repo, Prefix, Source));
            summary["canonicalSourceSha256"] = Digest(canonical);
            summary["reportSourceEqualsCanonical"] = profile.SourceHash == Digest(canonical);
            inventory = (await SourceInventory.CaptureAsync(repo)).Where(pin => IsInput(pin.Path)).ToArray();
            Require(inventory.Any(pin => pin.Path == Prefix + Source && pin.Sha256 == Digest(canonical)), "Current canonical source was not inventoried.");
            foreach (SourceInventoryEntry pin in inventory)
            {
                string path = Path.Combine(repo, pin.Path);
                Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "A maintained source input is linked.");
                Copy(path, Path.Combine(workspace, pin.Path), pin.Sha256);
            }
            copied = true;
            Copy(reportPath, copiedReport, profile.ReportHash);
            SaveInventory("canonical.before", inventory);
            await Verify(workspace, Digest(canonical));
            WriteJson(config, new JsonObject
            {
                ["scope"] = "Manual source-replay child evidence metadata; no Stryker process is run.",
                ["stryker-config"] = new JsonObject
                {
                    ["coverage-analysis"] = "off",
                    ["test-runner"] = "vstest",
                    ["target-framework"] = "net10.0",
                    ["configuration"] = "Release",
                    ["test-projects"] = new JsonArray(Project)
                }
            });
            summary["childConfiguration"] = new JsonObject { ["path"] = config, ["sha256"] = Hash(config) };
            Require((await input.Run("sdk", ["--version"], language, environment: environment)).StandardOutput.Trim() == "10.0.401", "Unexpected SDK.");
            ProcessResult runtimes = await input.Run("runtimes", ["--list-runtimes"], language, environment: environment);
            foreach (string framework in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
            { Require(runtimes.StandardOutput.Split('\n').Any(line => line.StartsWith(framework + " 10.0.12 ", StringComparison.Ordinal)), "Missing pinned " + framework); }
            summary["sdk"] = "10.0.401"; summary["targetRuntime"] = "10.0.12";
            await input.Run("restore", ["restore", Project, "--locked-mode", "--configfile", "NuGet.config"], language, environment: environment);
            Require((await Build("c0")).ExitCode == 0, "Current canonical baseline build failed.");
            canonicalPins = PinBinaries("c0");
            canonicalBaseline = await Test("c0", "canonical-baseline", null);
            RequirePass(canonicalBaseline);
            summary["canonicalBaseline"] = NativeJson(canonicalBaseline, canonicalPins);
            Write(target, historical);
            await Verify(workspace, profile.SourceHash);
            Require((await Build("h0")).ExitCode == 0, "Pinned report-source baseline build failed.");
            historicalPins = PinBinaries("h0");
            historicalBaseline = await Test("h0", "historical-baseline", null);
            RequirePass(historicalBaseline);
            Require(SameCases(canonicalBaseline, historicalBaseline), "The report-source baseline changed the selected native case inventory.");
            summary[reportBaselineKey] = NativeJson(historicalBaseline, historicalPins);
            summary["reportSourceOverlayExplicit"] = true;
            if (historicalProfile) { summary["historicalOverlayExplicit"] = true; }
            Save();
            foreach (Mutation mutation in mutations)
            {
                string name = "m" + mutation.Id;
                var row = new JsonObject
                {
                    ["id"] = mutation.Id,
                    ["originalStatus"] = mutation.Status,
                    ["status"] = "Unresolved",
                    ["location"] = mutation.Location.DeepClone(),
                    ["mutator"] = mutation.Mutator,
                    ["offset"] = mutation.Offset,
                    ["originalText"] = mutation.Find,
                    ["replacement"] = mutation.Replacement
                };
                summary["controls"]!.AsArray().Add(row); Save();
                try
                {
                    await Verify(workspace, profile.SourceHash);
                    string changed = historical[..mutation.Offset] + mutation.Replacement + historical[(mutation.Offset + mutation.Find.Length)..];
                    Write(target, changed);
                    row["mutatedSourceSha256"] = Hash(target);
                    Copy(target, Path.Combine(output, "controls", mutation.Id, "source.cs"), Hash(target));
                    await Verify(workspace, Hash(target));
                    ProcessResult build = await Build(name);
                    row["buildExitCode"] = build.ExitCode;
                    string text = build.StandardOutput + build.StandardError;
                    if (build.ExitCode != 0)
                    {
                        string[] diagnostics = text.Split('\n').Where(line => line.Contains(": error ", StringComparison.Ordinal)).ToArray();
                        bool targetOnly = diagnostics.Length > 0 && diagnostics.All(line => line.Replace('\\', '/').Contains(Source + "(", StringComparison.Ordinal)
                            && Regex.IsMatch(line, @": error CS\d{4}:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) && !Infrastructure(text) && !Timeout(text);
                        row["compilerDiagnostics"] = JsonSerializer.SerializeToNode(diagnostics);
                        row["status"] = targetOnly ? "IndependentCompileError" : "BuildFailureUnresolved";
                        continue;
                    }
                    JsonArray binaries = PinBinaries(name);
                    NativeRun native = await Test(name, "mutant", mutation);
                    row["native"] = NativeJson(native, binaries);
                    bool exact = SameCases(historicalBaseline, native);
                    NativeCase[] failed = native.Cases.Where(item => item.Outcome == "Failed").ToArray();
                    bool assertions = failed.Length > 0 && failed.All(item => Assertion(item, workspace));
                    bool sound = exact && native.Serialized && !native.RunError && !native.TimedOut && native.ChildAuditPassed
                        && native.Cases.Count(item => item.Method == Scheduling && item.Outcome == "Passed") == 1
                        && native.Cases.All(item => item.Outcome is "Passed" or "Failed");
                    row["exactBaselineCases"] = exact; row["onlyRecognizedAssertions"] = assertions;
                    row["status"] = sound && native.ExitCode == 1 && assertions ? "ContractKilled"
                        : sound && native.ExitCode == 0 && failed.Length == 0 ? "Survived" : native.TimedOut ? "TimeoutUnresolved" : "NativeFailureUnresolved";
                }
                catch (Exception error) when (Expected(error))
                { row["status"] = error is TimeoutException ? "TimeoutUnresolved" : "Unresolved"; row["error"] = error.ToString(); }
                finally { Write(target, historical); await Verify(workspace, profile.SourceHash); Save(); }
            }
        }
        catch (Exception error) when (Expected(error)) { failure = error; }
        finally
        {
            if (copied)
            {
                try
                {
                    if (historicalBaseline is not null && historicalPins is not null)
                    {
                        Write(target, historical); await Verify(workspace, profile.SourceHash);
                        Require((await Build("h1")).ExitCode == 0, "Pinned report-source restoration build failed.");
                        JsonArray pins = PinBinaries("h1");
                        NativeRun restored = await Test("h1", "historical-baseline", null);
                        summary[reportRestoredKey] = NativeJson(restored, pins);
                        RequirePass(restored);
                        Require(SameCases(historicalBaseline, restored) && SameBinaries(historicalPins, pins), "Report-source restored cases/binaries differ from baseline.");
                        summary[reportRestorationKey] = true;
                    }
                }
                catch (Exception error) when (Expected(error)) { failure ??= error; }
                try
                {
                    File.WriteAllBytes(target, canonical); await Verify(workspace, Digest(canonical));
                    Require((await Build("c1")).ExitCode == 0, "Current canonical restoration build failed.");
                    JsonArray pins = PinBinaries("c1");
                    NativeRun restored = await Test("c1", "canonical-baseline", null);
                    summary["canonicalRestored"] = NativeJson(restored, pins);
                    RequirePass(restored);
                    Require(canonicalBaseline is not null && canonicalPins is not null && SameCases(canonicalBaseline, restored)
                        && SameBinaries(canonicalPins, pins), "Canonical restored cases/binaries differ from baseline.");
                    summary["canonicalRestorationPassed"] = true;
                }
                catch (Exception error) when (Expected(error)) { failure ??= error; }
            }
            try
            {
                await Verify(repo, Digest(canonical));
                Require(Hash(reportPath) == profile.ReportHash, "The immutable original report changed.");
                summary["canonicalInputsUnmodified"] = inventory.Length > 0;
            }
            catch (Exception error) when (Expected(error)) { summary["canonicalInputsUnmodified"] = false; failure ??= error; }
            JsonObject[] rows = summary["controls"]!.AsArray().OfType<JsonObject>().ToArray();
            int Count(string value) => rows.Count(row => (string?)row["status"] == value);
            int killed = Count("ContractKilled"), compiler = Count("IndependentCompileError"), survivors = Count("Survived");
            bool complete = rows.Length == selected && killed + compiler + survivors == selected;
            bool restoredBoth = (bool?)summary[reportRestorationKey] == true && (bool?)summary["canonicalRestorationPassed"] == true;
            bool sound = complete && restoredBoth && (bool?)summary["canonicalInputsUnmodified"] == true && failure is null;
            summary["outcomes"] = new JsonObject
            {
                ["requiredFullSelection"] = profile.OmissionCount,
                ["selected"] = selected,
                ["notSelected"] = profile.OmissionCount - selected,
                ["contractKilled"] = killed,
                ["independentCompileError"] = compiler,
                ["survived"] = survivors,
                ["unresolved"] = selected - killed - compiler - survivors,
                ["allRequiredObserved"] = sound && input.Optional("ids") is null && selected == profile.OmissionCount,
                ["currentAutomaticQualification"] = false,
                ["wholeHealthcheckQualification"] = false
            };
            if (historicalProfile) { summary["outcomes"]!["all77Observed"] = sound && input.Optional("ids") is null && selected == 77; }
            summary["status"] = sound ? input.Optional("ids") is null ? "ObservedOmissionReplays" : "ObservedSelectedSubset" : "Incomplete";
            summary["exitCode"] = sound ? 0 : 1; summary["error"] = failure?.ToString(); summary["finishedUtc"] = DateTimeOffset.UtcNow; Save();
        }
        return summary["exitCode"]!.GetValue<int>();

        void Save() => WriteJson(Path.Combine(output, "summary.json"), summary);
        void SaveInventory(string name, SourceInventoryEntry[] pins) => Write(Path.Combine(output, "inputs", name + ".json"), JsonSerializer.Serialize(pins, JsonOptions) + "\n");
        async Task Verify(string root, string targetHash)
        {
            SourceInventoryEntry[] actual = (await SourceInventory.CaptureAsync(root)).Where(pin => IsInput(pin.Path)).ToArray();
            string[] expected = inventory.Select(pin => pin.Path + "|" + (pin.Path == Prefix + Source ? targetHash : pin.Sha256)).Order(StringComparer.Ordinal).ToArray();
            Require(expected.Length > 0 && expected.SequenceEqual(actual.Select(pin => pin.Path + "|" + pin.Sha256).Order(StringComparer.Ordinal), StringComparer.Ordinal),
                "Maintained source/structural inputs differ outside the explicit source replacement: " + root);
            if (root == repo) { SaveInventory("canonical.after", actual); }
        }
        async Task<ProcessResult> Build(string name)
        {
            string[] arguments = ["build", Project, "-c", "Release", "--no-restore", "--no-incremental", "-maxcpucount:1"];
            Request(name + "-build", arguments, environment, 240);
            return await input.Run(name + "-build", arguments, language, environment: environment, timeout: TimeSpan.FromSeconds(240), requireSuccess: false);
        }
        void Request(string name, string[] arguments, IReadOnlyDictionary<string, string?> overrides, int seconds) => WriteJson(Path.Combine(output, "logs", name + ".request.json"), new JsonObject
        {
            ["executable"] = dotnet,
            ["executableSha256"] = Hash(dotnet),
            ["arguments"] = JsonSerializer.SerializeToNode(arguments),
            ["workingDirectory"] = language,
            ["timeoutSeconds"] = seconds,
            ["environmentOverrides"] = JsonSerializer.SerializeToNode(overrides)
        });
        JsonArray PinBinaries(string name)
        {
            string built = Path.Combine(language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll");
            string testRoot = Path.Combine(language, "tests/CIS.Core.IntegrationTests/bin/Release/net10.0");
            Require(Hash(Path.Combine(testRoot, "CIS.Core.dll")) == Hash(built), "The integration host did not receive freshly built Core.");
            string child = Path.Combine(testRoot, "healthcheck-http-host");
            Require(File.Exists(Path.Combine(child, "CIS.Healthcheck.HttpFixture.dll")), "The real HTTP fixture was not built/deployed.");
            var pins = new JsonArray();
            foreach (string path in Directory.EnumerateFiles(testRoot, "*", SearchOption.AllDirectories).Append(built).Order(StringComparer.Ordinal))
            { pins.Add(new JsonObject { ["path"] = Path.GetRelativePath(workspace, path).Replace('\\', '/'), ["sha256"] = Hash(path) }); }
            string retained = Path.Combine(output, "binaries", name, "CIS.Core.dll");
            Copy(built, retained, Hash(built));
            WriteJson(Path.Combine(output, "binaries", name, "pins.json"), new JsonObject { ["files"] = pins.DeepClone() });
            return pins;
        }
        async Task<NativeRun> Test(string name, string kind, Mutation? mutation)
        {
            string core = Path.Combine(output, "binaries", name, "CIS.Core.dll");
            var control = new ChildProtocol.SourceReplayControl(kind, copiedReport, profile.ReportHash, mutation?.Id ?? "", Source,
                profile.SourceHash, Digest(canonical), Hash(target), mutation?.Offset ?? 0, mutation?.Find ?? "", mutation?.Replacement ?? "",
                core, Hash(core), ChildProtocol.Mvid(core));
            string controlPath = Path.Combine(output, "controls", name, "child-control.json");
            Write(controlPath, JsonSerializer.Serialize(control, JsonOptions) + "\n");
            string childRoot = Path.Combine(output, "c", name), auditRoot = Path.Combine(output, "a", name);
            Require(await HealthcheckChildMutationVerification.RunAsync(["prepare", "--language-root", language, "--config", config,
                "--stryker-core", stryker, "--dotnet", dotnet, "--output", childRoot, "--phase", "source-replay", "--source-replay-control", controlPath]) == 0,
                "Could not prepare explicit manual source-replay child evidence.");
            string sessionPath = Path.Combine(childRoot, "session.json"), nativeRoot = Path.Combine(output, "native", name), trx = Path.Combine(nativeRoot, "selected.trx");
            var testEnvironment = new Dictionary<string, string?>(environment) { [ChildProtocol.SessionVariable] = sessionPath };
            string[] arguments = ["test", Project, "-c", "Release", "--no-build", "--no-restore", "--filter", Filter,
                "--results-directory", nativeRoot, "--logger", "trx;LogFileName=selected.trx", "--", "RunConfiguration.TestSessionTimeout=900000"];
            Request(name + "-test", arguments, testEnvironment, 960);
            ProcessResult result = await input.Run(name + "-test", arguments, language,
                environment: testEnvironment, timeout: TimeSpan.FromSeconds(960), requireSuccess: false);
            int audit = await HealthcheckChildMutationVerification.RunAsync(["audit", "--session", sessionPath, "--output", auditRoot,
                "--require-kind", "http-host", "--phase", "source-replay"]);
            if (name == "c0" && audit == 0)
            {
                int automatic = await HealthcheckChildMutationVerification.RunAsync(["audit", "--session", sessionPath,
                    "--output", Path.Combine(output, "a", "automatic-mode-rejection"), "--require-kind", "http-host"]);
                Require(automatic != 0, "An automatic audit must reject a manual source-replay session.");
                summary["automaticAuditRejectedManualSession"] = new JsonObject { ["actualExitCode"] = automatic, ["expectedRejection"] = true };
                Save();
            }
            Require(File.Exists(trx), "No actual native source-replay TRX was produced.");
            NativeRun native = ReadNative(trx, result.ExitCode, result.StandardOutput + result.StandardError);
            return native with
            {
                ChildAuditPassed = audit == 0,
                ChildAudit = Path.Combine(auditRoot, "results.json"),
                ChildAuditSha256 = Hash(Path.Combine(auditRoot, "results.json")),
                Session = sessionPath,
                SessionSha256 = Hash(sessionPath)
            };
        }
    }

    private static (string Source, Mutation[] Mutations) Mutations(JsonObject report, ReplayProfile profile)
    {
        JsonObject[] files = report["files"]!.AsObject().Where(pair => pair.Key.Replace('\\', '/').EndsWith("/" + Source, StringComparison.Ordinal))
            .Select(pair => pair.Value!.AsObject()).ToArray();
        Require(files.Length == 1, "The pinned report must contain exactly one selected ASP.NET source.");
        string original = Required(files[0], "source");
        Require((string?)report["schemaVersion"] == "2" && Digest(Encoding.UTF8.GetBytes(original)) == profile.SourceHash,
            "Pinned report schema/source bytes differ from the selected profile.");
        JsonObject[] all = files[0]["mutants"]!.AsArray().OfType<JsonObject>().ToArray();
        Require(all.Length == profile.Total && all.Select(row => Required(row, "id")).Distinct(StringComparer.Ordinal).Count() == profile.Total
            && all.Count(row => (string?)row["status"] == "Killed") == profile.Killed && all.Count(row => (string?)row["status"] == "Survived") == profile.Survived
            && all.Count(row => (string?)row["status"] == "CompileError") == 72 && all.Count(row => (string?)row["status"] == "Ignored") == profile.IgnoredIds.Length,
            "The pinned selected status inventory differs from profile " + profile.Name + ".");
        Mutation[] mutations = all.Where(row => (string?)row["status"] is "CompileError" or "Ignored").Select(row =>
        {
            JsonObject location = row["location"]!.AsObject();
            int start = Offset(original, location["start"]!), end = Offset(original, location["end"]!);
            Require(end > start, "Empty/reversed native source span.");
            string replacement = Required(row, "replacement");
            Require(original[start..end] != replacement, "A native replacement leaves its source slice unchanged.");
            return new Mutation(Required(row, "id"), Required(row, "status"), Required(row, "mutatorName"), start, original[start..end], replacement, location);
        }).OrderBy(row => int.Parse(row.Id, CultureInfo.InvariantCulture)).ToArray();
        string[] expectedCompiler = Enumerable.Range(profile.CompilerStart, 72).Select(id => id.ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal).ToArray();
        Require(mutations.Length == profile.OmissionCount
            && expectedCompiler.SequenceEqual(mutations.Where(row => row.Status == "CompileError").Select(row => row.Id).Order(StringComparer.Ordinal), StringComparer.Ordinal)
            && profile.IgnoredIds.Order(StringComparer.Ordinal).SequenceEqual(mutations.Where(row => row.Status == "Ignored").Select(row => row.Id).Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "The exact omission IDs/statuses differ from profile " + profile.Name + ".");
        return (original, mutations);
    }
    private static int Offset(string source, JsonNode point)
    {
        int line = point["line"]!.GetValue<int>(), column = point["column"]!.GetValue<int>(), offset = 0;
        Require(line > 0 && column > 0, "Native source locations are one-based.");
        for (int current = 1; current < line; current++) { int next = source.IndexOf('\n', offset); Require(next >= 0, "Source location exceeds its lines."); offset = next + 1; }
        int end = source.IndexOf('\n', offset); if (end < 0) { end = source.Length; }
        Require(column - 1 <= end - offset, "Native column exceeds its line.");
        return offset + column - 1;
    }
    private static NativeRun ReadNative(string path, int exit, string output)
    {
        XDocument document = XDocument.Load(path);
        Require(document.Root?.Name == XName.Get("TestRun", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"), "Expected actual VSTest TRX.");
        var methods = document.Descendants().Where(element => element.Name.LocalName == "UnitTest").ToDictionary(element => Attribute(element, "id"), element =>
        { XElement method = element.Elements().Single(item => item.Name.LocalName == "TestMethod"); return Attribute(method, "className") + "." + Attribute(method, "name"); }, StringComparer.OrdinalIgnoreCase);
        NativeCase[] cases = document.Descendants().Where(element => element.Name.LocalName == "UnitTestResult").Select(element => new NativeCase(Attribute(element, "testId"),
            Attribute(element, "testName"), methods[Attribute(element, "testId")], Attribute(element, "outcome"),
            element.Descendants().FirstOrDefault(item => item.Name.LocalName == "Message")?.Value ?? "",
            element.Descendants().FirstOrDefault(item => item.Name.LocalName == "StackTrace")?.Value ?? "")).ToArray();
        Require(cases.Length > 0 && cases.All(item => Guid.TryParse(item.Id, out Guid id) && id != Guid.Empty)
            && cases.Select(Key).Distinct(StringComparer.Ordinal).Count() == cases.Length, "Empty/duplicate native case inventory.");
        XElement counters = document.Descendants().Single(element => element.Name.LocalName == "Counters");
        string text = output + "\n" + string.Join("\n", document.Descendants().Where(item => item.Name.LocalName is "Message" or "RunInfo").Select(item => item.Value));
        bool complete = (int?)counters.Attribute("total") == cases.Length && (int?)counters.Attribute("executed") == cases.Length;
        bool serialized = document.Descendants().Any(item => item.Name.LocalName == "StdOut" && item.Value.Contains("CIS mutation scheduling: serialized", StringComparison.Ordinal));
        bool runError = !complete || document.Descendants().Any(item => item.Name.LocalName == "RunInfo" && (string?)item.Attribute("outcome") == "Error")
            || Infrastructure(text) || text.Contains("Test Run Aborted", StringComparison.OrdinalIgnoreCase);
        return new(path, Hash(path), exit, serialized, runError, Timeout(text), cases, false, "", "", "", "");
    }
    private static bool Assertion(NativeCase item, string workspace)
    {
        string root = Path.GetFullPath(Path.Combine(workspace, Prefix, "tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/HealthcheckHttpTransportTests.cs")).Replace('\\', '/');
        return item.Method.StartsWith(TestType + ".", StringComparison.Ordinal) && !Infrastructure(item.Message) && !Timeout(item.Message)
            && !item.StackTrace.Contains(".Host.DisposeAsync(", StringComparison.Ordinal)
            && (item.Message.StartsWith("Assertion failed.", StringComparison.Ordinal) || item.Message.StartsWith("Assert.", StringComparison.Ordinal)
                || item.Message.StartsWith("CollectionAssert.", StringComparison.Ordinal) || item.Message.StartsWith("StringAssert.", StringComparison.Ordinal))
            && item.StackTrace.Replace('\\', '/').Contains(root + ":line ", StringComparison.Ordinal)
            && item.StackTrace.Contains(item.Method + "(", StringComparison.Ordinal);
    }
    private static bool Infrastructure(string text) => new[] { "FileNotFoundException", "DirectoryNotFoundException", "DllNotFoundException", "FileLoadException", "BadImageFormatException",
        "TypeLoadException", "MissingMethodException", "Win32Exception", "UnauthorizedAccessException", "testhost process exited", "Failed to launch testhost", "No test matches",
        "real fixture must be built and copied", "observer-failure", "private child stage", "error NU", "error MSB" }.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool Timeout(string text) => new[] { "TimeoutException", "TaskCanceledException", "OperationCanceledException", "test session timeout", "test session timed out",
        "test run timeout", "exceeded execution timeout", "exceeded its" }.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static void RequirePass(NativeRun run) => Require(run.ExitCode == 0 && run.Serialized && !run.RunError && !run.TimedOut && run.ChildAuditPassed
        && run.Cases.Length >= 64 && run.Cases.All(item => item.Outcome == "Passed" && (item.Method == Scheduling || item.Method.StartsWith(TestType + ".", StringComparison.Ordinal)))
        && run.Cases.Count(item => item.Method == Scheduling) == 1
        && run.Cases.Count(item => item.Method == TestType + ".HeadMiddlewareFormatsItsSnapshotAndNeverWritesToTheActualResponseBody") == 1,
        "The full current HTTP baseline/restoration, required in-process HEAD case or actual manual child audit failed.");
    private static bool SameCases(NativeRun before, NativeRun after) => before.Cases.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(after.Cases.Select(Key).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    private static bool SameBinaries(JsonArray before, JsonArray after) => before.OfType<JsonObject>().Select(pin => (string?)pin["path"] + "|" + (string?)pin["sha256"]).Order(StringComparer.Ordinal)
        .SequenceEqual(after.OfType<JsonObject>().Select(pin => (string?)pin["path"] + "|" + (string?)pin["sha256"]).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    private static string Key(NativeCase item) => item.Id.ToLowerInvariant() + "|" + item.Name + "|" + item.Method;
    private static JsonObject NativeJson(NativeRun run, JsonArray pins) => new()
    {
        ["trx"] = run.Path,
        ["trxSha256"] = run.Sha256,
        ["exitCode"] = run.ExitCode,
        ["serialized"] = run.Serialized,
        ["runError"] = run.RunError,
        ["timedOut"] = run.TimedOut,
        ["caseCount"] = run.Cases.Length,
        ["passed"] = run.Cases.Count(item => item.Outcome == "Passed"),
        ["failed"] = run.Cases.Count(item => item.Outcome == "Failed"),
        ["cases"] = JsonSerializer.SerializeToNode(run.Cases),
        ["binaryPins"] = pins.DeepClone(),
        ["childAuditPassed"] = run.ChildAuditPassed,
        ["childAudit"] = run.ChildAudit,
        ["childAuditSha256"] = run.ChildAuditSha256,
        ["session"] = run.Session,
        ["sessionSha256"] = run.SessionSha256
    };
    private static string Attribute(XElement element, string name) => (string?)element.Attribute(name) ?? throw new InvalidDataException("Missing native " + name);
    private static string Required(JsonNode node, string name) => node[name]?.GetValue<string>() is { Length: > 0 } text ? text : throw new InvalidDataException("Missing native " + name);
    private static bool IsInput(string path) => path.StartsWith(Prefix, StringComparison.Ordinal) || path.StartsWith("contracts/", StringComparison.Ordinal)
        || path.StartsWith("conformance/", StringComparison.Ordinal) || path.StartsWith("compatibility/csharp/", StringComparison.Ordinal)
        || path.StartsWith("planning/healthcheck/", StringComparison.Ordinal) || path is "README.md" or "AGENTS.md" or ".gitattributes" or ".gitignore" or ".editorconfig";
    private static bool Under(string root, string path) => Path.GetFullPath(path).Equals(Path.GetFullPath(root), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
        || Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Expected(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
        or JsonException or System.Xml.XmlException or TimeoutException or System.ComponentModel.Win32Exception or KeyNotFoundException or FormatException;
    private static ReplayProfile Profile(string? name) => name switch
    {
        null or "historical77" => new("historical77", "afcc1871cc1d4e12b74a352809b9e75c054c2fa3b9d13e61f1c166a900eb800e",
            "d81256db1acc93e948d72bdc7c6df713cc6f9e436b02d3b9f3b22a8d7f66efa4", 101, 21, 3, 210, ["191", "198", "201", "282", "289"]),
        "gate-closure-a1" => new("gate-closure-a1", "5a109f16c2c355a571d0efb38fd59a297f2077e3f9a03d1eefb53fd074f7a750",
            "b4611cfccb7aafb377b7fbb663b81dbc6c58cb59af66a73b44fa85a1fd096d0e", 94, 16, 2, 205, ["191", "198", "201", "277"]),
        _ => throw new ArgumentException("Use --profile historical77 (default) or --profile gate-closure-a1.")
    };
    private sealed record ReplayProfile(string Name, string ReportHash, string SourceHash, int Total, int Killed, int Survived, int CompilerStart, string[] IgnoredIds)
    {
        internal int OmissionCount => 72 + IgnoredIds.Length;
    }
    private sealed record Mutation(string Id, string Status, string Mutator, int Offset, string Find, string Replacement, JsonObject Location);
    private sealed record NativeCase(string Id, string Name, string Method, string Outcome, string Message, string StackTrace);
    private sealed record NativeRun(string Path, string Sha256, int ExitCode, bool Serialized, bool RunError, bool TimedOut, NativeCase[] Cases,
        bool ChildAuditPassed, string ChildAudit, string ChildAuditSha256, string Session, string SessionSha256);
}
