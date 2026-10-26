using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Finite deliberately broken critical controls. This does not replace automatic mutation qualification.
internal static class HealthcheckCriticalMutationVerification
{
    private const string LanguagePrefix = "implementations/csharp/";
    private const string Scheduling = "CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
    private static readonly string[] Projects = ["UnitTests", "IntegrationTests"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(string[] args)
    {
        string[] adapted = AdaptArguments(args);
        var input = new TypeManagerVerificationInputs(adapted, ["repo-root", "matrix", "output-root"], []);
        string repo = input.PathOf("repo-root"), output = input.Output, matrixPath = input.PathOf("matrix");
        Require(!IsUnder(repo, output) || IsUnder(Path.Combine(repo, ".artifacts"), output),
            "Use an isolated output under repository .artifacts or outside the checkout.");
        Require(!IsUnder(output, repo) && !IsUnder(output, matrixPath), "Output must not contain the source checkout or input matrix.");
        input.EmptyOutput();
        var summary = new JsonObject
        {
            ["schema"] = "cis.healthcheck.critical-invariant-mutations/v1-development",
            ["scope"] = "Twenty explicit production controls; not all-Core Stryker or release qualification.",
            ["status"] = "Running",
            ["exitCode"] = 1,
            ["startedUtc"] = DateTimeOffset.UtcNow,
            ["matrix"] = new JsonObject { ["path"] = matrixPath, ["sha256"] = Hash(matrixPath) },
            ["controls"] = new JsonArray(),
            ["baseline"] = new JsonArray(),
            ["restored"] = new JsonArray()
        };
        SourceInventoryEntry[] inventory = [];
        string workspace = Path.Combine(output, "source"), language = Path.Combine(workspace, LanguagePrefix);
        Dictionary<string, byte[]> originals = new(StringComparer.Ordinal);
        bool copied = false, validated = false;
        Exception? failure = null;
        var environment = new Dictionary<string, string?>
        {
            ["CisMutationTesting"] = "true",
            ["UseSharedCompilation"] = "false",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1",
            ["RestoreLockedMode"] = "true",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        };
        try
        {
            JsonObject matrix = Read(matrixPath);
            Mutation[] mutations = ValidateMatrix(repo, matrix, originals);
            validated = true;
            inventory = (await SourceInventory.CaptureAsync(repo)).Where(pin => IsInput(pin.Path)).ToArray();
            Require(inventory.Length > 0 && inventory.Any(pin => pin.Path == LanguagePrefix + "src/CIS.Core/CIS.Core.csproj"),
                "Actual maintained Core and test inputs are required.");
            foreach (SourceInventoryEntry pin in inventory) { Copy(Path.Combine(repo, pin.Path), Path.Combine(workspace, pin.Path), pin.Sha256); }
            copied = true;
            Copy(matrixPath, Path.Combine(output, "inputs", "matrix.json"), summary["matrix"]!["sha256"]!.GetValue<string>());
            Write(Path.Combine(output, "inputs", "source-inputs.json"), JsonSerializer.Serialize(inventory, JsonOptions) + "\n");
            summary["sourceInventory"] = new JsonObject
            {
                ["path"] = Path.Combine(output, "inputs", "source-inputs.json"),
                ["sha256"] = Hash(Path.Combine(output, "inputs", "source-inputs.json")),
                ["files"] = inventory.Length
            };
            JsonObject toolchain = Read(Path.Combine(language, "eng/toolchain.json"));
            Require((string?)toolchain["sdk"] == "10.0.401" && (string?)toolchain["targetRuntime"] == "10.0.12",
                "Requalify the Healthcheck SDK/runtime pins.");
            ProcessResult sdk = await input.Run("sdk", ["--version"], language, environment: environment);
            Require(sdk.StandardOutput.Trim() == "10.0.401", "Healthcheck mutation SDK differs from its pin.");
            ProcessResult runtimes = await input.Run("runtimes", ["--list-runtimes"], language, environment: environment);
            foreach (string framework in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
            {
                Require(runtimes.StandardOutput.Split('\n').Any(line => line.StartsWith(framework + " 10.0.12 ", StringComparison.Ordinal)),
                    "Missing pinned runtime: " + framework + " 10.0.12.");
            }
            summary["sdk"] = "10.0.401";
            summary["targetRuntime"] = "10.0.12";
            summary["serialized"] = true;

            var baselines = new Dictionary<string, NativeRun>(StringComparer.Ordinal);
            foreach (string project in Projects)
            {
                await Restore(project, "baseline-" + project);
                Require((await Build(project, "baseline-" + project)).ExitCode == 0, "Unmutated baseline build failed.");
                JsonArray binaries = PinBinaries(project, "baseline-" + project);
                NativeRun baseline = await Test(project, FullFilter(project), "baseline-" + project);
                RequirePass(baseline);
                baselines.Add(project, baseline);
                summary["baseline"]!.AsArray().Add(RunJson(baseline, binaries));
                Save();
            }
            foreach (Mutation mutation in mutations)
            {
                string method = Method(mutation);
                NativeCase[] expected = baselines[mutation.Project].Cases.Where(item => item.Method == method || item.Method == Scheduling).ToArray();
                Require(expected.Count(item => item.Method == method) > 0 && expected.Count(item => item.Method == Scheduling) == 1,
                    "Matrix named method is absent from the passing native baseline: " + method);
                Require(mutation.Assertions.All(assertion => expected.Any(item => item.Method == method
                    && Regex.IsMatch(item.Name, assertion.CasePattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))),
                    "An intended assertion data row is absent from the passing native baseline: " + method);
            }

            foreach (Mutation mutation in mutations)
            {
                string name = mutation.Id, sourcePath = Path.Combine(language, "src/CIS.Core", mutation.Path);
                var control = new JsonObject
                {
                    ["id"] = mutation.Id,
                    ["project"] = mutation.Project,
                    ["method"] = Method(mutation),
                    ["source"] = mutation.Path,
                    ["originalSha256"] = Digest(originals[mutation.Path]),
                    ["status"] = "Running"
                };
                summary["controls"]!.AsArray().Add(control);
                Save();
                Console.WriteLine("Healthcheck critical mutation: " + name + " " + mutation.Test);
                try
                {
                    VerifyWorkspaceSources();
                    string changed = CompilerChecks.Mutate(Encoding.UTF8.GetString(originals[mutation.Path]), mutation.Find, mutation.Replace);
                    Write(sourcePath, changed);
                    control["mutatedSha256"] = Hash(sourcePath);
                    Copy(sourcePath, Path.Combine(output, "controls", name, "changed-source.cs"), control["mutatedSha256"]!.GetValue<string>());
                    WriteJson(Path.Combine(output, "controls", name, "patch.json"), new JsonObject
                    {
                        ["path"] = mutation.Path,
                        ["find"] = mutation.Find,
                        ["replace"] = mutation.Replace,
                        ["originalSha256"] = Digest(originals[mutation.Path]),
                        ["mutatedSha256"] = Hash(sourcePath)
                    });
                    ProcessResult build = await Build(mutation.Project, name);
                    control["buildExitCode"] = build.ExitCode;
                    if (build.ExitCode != 0) { control["status"] = "BuildError"; continue; }
                    JsonArray binaries = PinBinaries(mutation.Project, name);
                    NativeRun native = await Test(mutation.Project, SelectedFilter(mutation), name);
                    control["native"] = RunJson(native, binaries);
                    NativeCase[] expected = baselines[mutation.Project].Cases.Where(item => item.Method == Method(mutation) || item.Method == Scheduling).ToArray();
                    bool exactCases = expected.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(native.Cases.Select(Key).Order(StringComparer.Ordinal), StringComparer.Ordinal);
                    NativeCase[] selected = native.Cases.Where(item => item.Method == Method(mutation)).ToArray();
                    bool schedulingPassed = native.Cases.Count(item => item.Method == Scheduling && item.Outcome == "Passed") == 1 && native.Serialized;
                    bool recognized = native.Cases.All(item => item.Outcome is "Passed" or "Failed");
                    bool onlyIntendedAssertions = selected.Where(item => item.Outcome == "Failed").All(item => IsIntendedAssertion(mutation, item));
                    bool killed = native.ExitCode == 1 && exactCases && schedulingPassed && recognized && onlyIntendedAssertions
                        && selected.Any(item => item.Outcome == "Failed");
                    control["exactBaselineCases"] = exactCases;
                    control["onlyIntendedAssertions"] = onlyIntendedAssertions;
                    control["expectedAssertions"] = JsonSerializer.SerializeToNode(mutation.Assertions);
                    control["status"] = killed ? "Killed" : native.ExitCode == 0 && exactCases && schedulingPassed && recognized
                        ? "Survived" : "InfrastructureOrUnexpectedFailure";
                }
                catch (Exception error) when (IsExpectedFailure(error))
                {
                    control["status"] = error is TimeoutException ? "Timeout" : "InfrastructureOrUnexpectedFailure";
                    control["detail"] = error.Message;
                }
                finally
                {
                    File.WriteAllBytes(sourcePath, originals[mutation.Path]);
                    control["restoredSourceSha256"] = Hash(sourcePath);
                    Require(Hash(sourcePath) == Digest(originals[mutation.Path]), "Mutation source restoration failed: " + mutation.Path);
                    Save();
                }
            }
        }
        catch (Exception error) when (IsExpectedFailure(error)) { failure = error; }
        finally
        {
            if (copied && validated)
            {
                try
                {
                    foreach ((string path, byte[] bytes) in originals) { File.WriteAllBytes(Path.Combine(language, "src/CIS.Core", path), bytes); }
                    VerifyWorkspaceSources();
                    foreach (string project in Projects)
                    {
                        // Rebuild every referenced Core/fixture/probe output, including after an earlier failure.
                        await Restore(project, "restored-" + project);
                        Require((await Build(project, "restored-" + project)).ExitCode == 0, "Restored unmutated build failed.");
                        JsonArray binaries = PinBinaries(project, "restored-" + project);
                        NativeRun restored = await Test(project, FullFilter(project), "restored-" + project);
                        RequirePass(restored);
                        summary["restored"]!.AsArray().Add(RunJson(restored, binaries));
                        Save();
                    }
                    VerifyWorkspaceSources();
                    summary["restorationPassed"] = true;
                }
                catch (Exception error) when (IsExpectedFailure(error))
                {
                    summary["restorationPassed"] = false;
                    summary["restorationError"] = error.Message;
                    failure ??= error;
                }
            }
            try
            {
                foreach (SourceInventoryEntry pin in inventory) { Require(Hash(Path.Combine(repo, pin.Path)) == pin.Sha256, "Canonical source changed: " + pin.Path); }
                Require(Hash(matrixPath) == (string?)summary["matrix"]!["sha256"], "Canonical mutation matrix changed.");
                summary["canonicalInputsUnmodified"] = true;
            }
            catch (Exception error) when (IsExpectedFailure(error))
            { summary["canonicalInputsUnmodified"] = false; failure ??= error; }
            JsonArray controls = summary["controls"]!.AsArray();
            int killed = controls.Count(control => (string?)control!["status"] == "Killed");
            bool passed = failure is null && controls.Count == 20 && killed == 20 && (bool?)summary["restorationPassed"] == true;
            summary["killed"] = killed;
            summary["criticalControls"] = controls.Count;
            summary["status"] = passed ? "Passed" : "Failed";
            summary["exitCode"] = passed ? 0 : 1;
            summary["detail"] = failure?.Message;
            summary["finishedUtc"] = DateTimeOffset.UtcNow;
            Save();
        }
        Console.WriteLine("Healthcheck critical mutations: " + summary["status"] + "; " + summary["killed"] + "/20 killed. " + Path.Combine(output, "summary.json"));
        return summary["exitCode"]!.GetValue<int>();

        void Save() => WriteJson(Path.Combine(output, "summary.json"), summary);
        void VerifyWorkspaceSources()
        {
            foreach (SourceInventoryEntry pin in inventory)
            { Require(Hash(Path.Combine(workspace, pin.Path)) == pin.Sha256, "Isolated source changed outside the current mutation: " + pin.Path); }
        }
        async Task Restore(string project, string name)
        {
            await input.Run(name + "-restore", ["restore", Project(project), "--locked-mode", "--configfile", "NuGet.config"], language,
                environment: environment, timeout: TimeSpan.FromMinutes(10));
        }
        Task<ProcessResult> Build(string project, string name) => input.Run(name + "-build",
            ["build", Project(project), "-c", "Release", "--no-restore", "--no-incremental", "-maxcpucount:1"], language,
            environment: environment, timeout: TimeSpan.FromMinutes(10), requireSuccess: false);
        async Task<NativeRun> Test(string project, string filter, string name)
        {
            string results = Path.Combine(output, "native", name);
            ProcessResult result = await input.Run(name + "-test",
                ["test", Project(project), "-c", "Release", "--no-build", "--no-restore", "--filter", filter,
                    "--results-directory", results, "--logger", "trx;LogFileName=selected.trx", "--", "RunConfiguration.TestSessionTimeout=300000"], language,
                environment: environment, timeout: TimeSpan.FromMinutes(8), requireSuccess: false);
            return ReadNative(Path.Combine(results, "selected.trx"), result.ExitCode);
        }
        JsonArray PinBinaries(string project, string name)
        {
            string core = Path.Combine(language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll");
            string testOutput = Path.Combine(language, "tests", "CIS.Core." + project, "bin/Release/net10.0");
            string expected = Hash(core);
            string[] copies = Directory.GetFiles(testOutput, "CIS.Core.dll", SearchOption.AllDirectories);
            Require(copies.Length > 0 && copies.All(path => Hash(path) == expected), "Selected test/child copies do not contain the freshly built Core.");
            if (project == "IntegrationTests")
            {
                foreach (string directory in new[] { "healthcheck-http-host", "healthcheck-probe" })
                { Require(File.Exists(Path.Combine(testOutput, directory, "CIS.Core.dll")), "Missing actual Healthcheck child Core copy: " + directory); }
            }
            var binaries = new JsonArray();
            foreach (string path in copies.Prepend(core).Append(Path.Combine(testOutput, "CIS.Core." + project + ".dll")))
            {
                Retain(path);
            }
            string configPath = Path.Combine(testOutput, "CIS.Core." + project + ".runtimeconfig.json");
            JsonObject runtime = Read(configPath)["runtimeOptions"]!.AsObject();
            Require((string?)runtime["rollForward"] == "Disable", "Native tests must disable runtime roll-forward.");
            JsonNode[] frameworks = runtime["frameworks"] is JsonArray array ? array.OfType<JsonNode>().ToArray() : [runtime["framework"]!];
            Require(frameworks.Length > 0 && frameworks.All(framework => (string?)framework["version"] == "10.0.12"), "Native runtime differs from 10.0.12.");
            Copy(configPath, Path.Combine(output, "binaries", name, Path.GetRelativePath(workspace, configPath)));
            if (project == "IntegrationTests")
            {
                VerifyChild("healthcheck-probe", "CisHealthProbe", "tools/CisHealthProbe/bin/Release/net10.0");
                VerifyChild("healthcheck-http-host", "CIS.Healthcheck.HttpFixture", "tests/fixtures/HealthcheckHttpHost/bin/Release/net10.0");
            }
            return binaries;

            void Retain(string path)
            {
                string retained = Path.Combine(output, "binaries", name, Path.GetRelativePath(workspace, path));
                Copy(path, retained);
                binaries.Add(new JsonObject { ["observedPath"] = path, ["retainedPath"] = retained, ["sha256"] = Hash(path) });
            }
            void VerifyChild(string folder, string assembly, string buildFolder)
            {
                foreach (string suffix in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
                {
                    string copiedFile = Path.Combine(testOutput, folder, assembly + suffix);
                    string builtFile = Path.Combine(language, buildFolder, assembly + suffix);
                    Require(File.Exists(copiedFile) && File.Exists(builtFile) && Hash(copiedFile) == Hash(builtFile),
                        "Missing, stale or mismatched actual child payload: " + copiedFile);
                    Retain(copiedFile);
                }
                string childOutput = Path.Combine(testOutput, folder, assembly);
                JsonObject options = Read(childOutput + ".runtimeconfig.json")["runtimeOptions"]!.AsObject();
                JsonNode[] childFrameworks = options["frameworks"] is JsonArray many ? many.OfType<JsonNode>().ToArray() : [options["framework"]!];
                Require((string?)options["rollForward"] == "Disable" && childFrameworks.Length > 0
                    && childFrameworks.All(framework => (string?)framework["version"] == "10.0.12"), "Child runtime differs from its pinned disabled-roll-forward policy.");
                JsonObject deps = Read(childOutput + ".deps.json");
                Require(deps["targets"]!.AsObject().Any(target => target.Value!.AsObject().Any(library =>
                    library.Key.StartsWith("CIS.Core/", StringComparison.Ordinal) && library.Value?["runtime"]?["CIS.Core.dll"] is not null)),
                    "Child dependency manifest does not declare actual Core runtime loading.");
            }
        }
    }

    private static Mutation[] ValidateMatrix(string repo, JsonObject matrix, Dictionary<string, byte[]> originals)
    {
        Require((string?)matrix["schema"] == "cis.healthcheck.critical-invariant-mutation-plan/v1-development", "Unsupported critical-control matrix schema.");
        Require((string?)matrix["productionRoot"] == LanguagePrefix + "src/CIS.Core/", "Unexpected production root.");
        Require((string?)matrix["unitNamespace"] == "CIS.Tests.UnitTests.Healthcheck."
            && (string?)matrix["integrationNamespace"] == "CIS.Tests.IntegrationTests.Healthcheck.Reporting.", "Unexpected native matrix namespaces.");
        foreach (JsonNode pin in matrix["productionPins"]!.AsArray().OfType<JsonNode>())
        {
            string path = Required(pin, "path"), expected = Required(pin, "sha256");
            string source = Path.GetFullPath(Path.Combine(repo, LanguagePrefix, "src/CIS.Core", path));
            Require(path.StartsWith("Healthcheck/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)
                && IsUnder(Path.Combine(repo, LanguagePrefix, "src/CIS.Core/Healthcheck"), source), "Matrix source escaped Healthcheck.");
            byte[] bytes = File.ReadAllBytes(source);
            Require(Digest(bytes) == expected && originals.TryAdd(path, bytes), "Matrix source pin mismatch or duplicate: " + path);
        }
        JsonObject assertionSources = matrix["assertionSources"]!.AsObject();
        Mutation[] mutations = matrix["mutations"]!.AsArray().OfType<JsonNode>().Select(row =>
        {
            string id = Required(row, "id"), project = Required(row, "project"), test = Required(row, "test");
            Require(Regex.IsMatch(test, "^[A-Za-z_][A-Za-z0-9_]*\\.[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                "Invalid native method in expected assertion row.");
            Assertion[] assertions = matrix["expectedFailures"]![id]!.AsArray().OfType<JsonNode>().Select(assertion =>
            {
                string source = Required(assertionSources, Required(assertion, "source"));
                int line = assertion["line"]!.GetValue<int>();
                string path = Path.GetFullPath(Path.Combine(repo, LanguagePrefix, source));
                Require(IsUnder(Path.Combine(repo, LanguagePrefix, "tests", "CIS.Core." + project), path), "Expected assertion escaped its native project.");
                string[] lines = File.ReadAllLines(path);
                Require(line > 0 && line <= lines.Length && lines[line - 1].Contains(Required(assertion, "anchor"), StringComparison.Ordinal),
                    "Expected native assertion source/line changed: " + source);
                string kind = Required(assertion, "kind");
                Require(kind is "equal" or "false" or "true" or "array" or "fail", "Unknown expected assertion kind.");
                string pattern = (string?)assertion["casePattern"] ?? "^" + Regex.Escape(test.Split('.')[1]) + "(?: \\(.*\\))?$";
                _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                int[] nativeLines = assertion["nativeLines"] is JsonArray points ? points.Select(point => point!.GetValue<int>()).ToArray() : [line];
                Require(nativeLines.Length is > 0 and <= 4 && nativeLines.Distinct().Count() == nativeLines.Length
                    && nativeLines.All(point => point > 0 && point <= line), "Invalid explicit native assertion sequence points.");
                string expression = (string?)assertion["expression"] ?? Required(assertion, "anchor").TrimEnd(';');
                return new Assertion(source, line, nativeLines, expression, kind, pattern, (string?)assertion["expected"], (string?)assertion["actual"], (string?)assertion["message"]);
            }).ToArray();
            Require(assertions.Length > 0 && assertions.All(assertion => assertion.Kind != "equal" || assertion.Expected is not null && assertion.Actual is not null),
                "A control requires explicit intended assertion evidence: " + id);
            return new Mutation(id, project, Required(row, "path"), Required(row, "find"), Required(row, "replace"), test, assertions);
        }).ToArray();
        Require(mutations.Length == 20 && mutations.Select(row => row.Id).SequenceEqual(Enumerable.Range(1, 20).Select(index => "HC-M" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture)), StringComparer.Ordinal),
            "Expected the finite ordered twenty critical controls.");
        foreach (Mutation mutation in mutations)
        {
            Require(Projects.Contains(mutation.Project, StringComparer.Ordinal) && originals.ContainsKey(mutation.Path)
                && Regex.IsMatch(mutation.Test, "^[A-Za-z_][A-Za-z0-9_]*\\.[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                && mutation.Find != mutation.Replace, "Invalid matrix row: " + mutation.Id);
            _ = CompilerChecks.Mutate(Encoding.UTF8.GetString(originals[mutation.Path]), mutation.Find, mutation.Replace);
        }
        return mutations;
    }

    private static NativeRun ReadNative(string path, int exitCode)
    {
        XDocument trx = XDocument.Load(path);
        Require(trx.Root?.Name == XName.Get("TestRun", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"), "Expected native VSTest TRX.");
        var methods = trx.Descendants().Where(element => element.Name.LocalName == "UnitTest").ToDictionary(element => Attribute(element, "id"), element =>
        {
            XElement method = element.Elements().Single(child => child.Name.LocalName == "TestMethod");
            return Attribute(method, "className") + "." + Attribute(method, "name");
        }, StringComparer.OrdinalIgnoreCase);
        NativeCase[] cases = trx.Descendants().Where(element => element.Name.LocalName == "UnitTestResult").Select(element =>
            new NativeCase(Attribute(element, "testId"), Attribute(element, "testName"), methods[Attribute(element, "testId")], Attribute(element, "outcome"),
                element.Descendants().FirstOrDefault(child => child.Name.LocalName == "Message")?.Value ?? "",
                element.Descendants().FirstOrDefault(child => child.Name.LocalName == "StackTrace")?.Value ?? "")).ToArray();
        Require(cases.Length > 0 && cases.All(item => Guid.TryParse(item.Id, out Guid id) && id != Guid.Empty)
            && cases.Select(Key).Distinct(StringComparer.Ordinal).Count() == cases.Length, "Empty or invalid native case inventory.");
        XElement counters = trx.Descendants().Single(element => element.Name.LocalName == "Counters");
        Require((int?)counters.Attribute("total") == cases.Length && (int?)counters.Attribute("executed") == cases.Length, "Native report is incomplete or skipped.");
        bool serialized = trx.Descendants().Any(element => element.Name.LocalName == "StdOut" && element.Value.Contains("CIS mutation scheduling: serialized", StringComparison.Ordinal));
        return new NativeRun(path, Hash(path), exitCode, serialized, cases);
    }

    private static JsonObject RunJson(NativeRun run, JsonArray binaries) => new()
    {
        ["trx"] = run.Path,
        ["sha256"] = run.Hash,
        ["exitCode"] = run.ExitCode,
        ["serialized"] = run.Serialized,
        ["count"] = run.Cases.Length,
        ["passed"] = run.Cases.Count(item => item.Outcome == "Passed"),
        ["failed"] = run.Cases.Count(item => item.Outcome == "Failed"),
        ["cases"] = JsonSerializer.SerializeToNode(run.Cases),
        ["binaries"] = binaries
    };
    private static void RequirePass(NativeRun run) => Require(run.ExitCode == 0 && run.Serialized && run.Cases.All(item => item.Outcome == "Passed")
        && run.Cases.Count(item => item.Method == Scheduling) == 1, "Unmutated native Healthcheck baseline/restoration is empty, failed, skipped or not serialized.");
    private static bool IsAssertion(string message) => message.StartsWith("Assertion failed.", StringComparison.Ordinal)
        || message.StartsWith("Assert.", StringComparison.Ordinal)
        || message.StartsWith("CollectionAssert.", StringComparison.Ordinal) || message.StartsWith("StringAssert.", StringComparison.Ordinal)
        || message.Contains("Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException", StringComparison.Ordinal);
    private static bool IsIntendedAssertion(Mutation mutation, NativeCase native)
    {
        if (!IsAssertion(native.Message) || new[] { "TimeoutOrCanceled", "TransportOrProtocolFailure", "real fixture must be built and copied", "FileNotFoundException", "Win32Exception", "Test host process crashed" }
            .Any(text => native.Message.Contains(text, StringComparison.OrdinalIgnoreCase))) { return false; }
        return mutation.Assertions.Any(assertion =>
        {
            // Native release sequence points are explicitly reviewed per assertion;
            // there is no general location tolerance or generic Assert.* kill credit.
            bool source = native.StackTrace.Replace('\\', '/').Split('\n').Any(frame => frame.Contains(Method(mutation) + "(", StringComparison.Ordinal)
                && assertion.NativeLines.Any(line => frame.TrimEnd('\r').EndsWith("/" + assertion.Source + ":line " + line.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)));
            if (!source || !Regex.IsMatch(native.Name, assertion.CasePattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) { return false; }
            if ((assertion.Kind is "equal" or "false" or "true") && !native.Message.Contains(assertion.Expression, StringComparison.Ordinal)) { return false; }
            return assertion.Kind switch
            {
                "equal" => Value("expected", assertion.Expected!) && Value("actual", assertion.Actual!),
                "false" => native.Message.Contains("Expected condition to be false", StringComparison.OrdinalIgnoreCase),
                "true" => native.Message.Contains("Expected condition to be true", StringComparison.OrdinalIgnoreCase),
                "array" => native.Message.Contains("CollectionAssert.AreEqual(payload, actual)", StringComparison.Ordinal)
                    || native.Message.StartsWith("CollectionAssert.AreEqual failed", StringComparison.Ordinal),
                "fail" => assertion.Message is not null && native.Message.Contains(assertion.Message, StringComparison.Ordinal),
                _ => false
            };
        });

        bool Value(string label, string value) => Regex.IsMatch(native.Message, "\\b" + label + "\\s*:\\s*[<\"]?" + Regex.Escape(value) + "[>\"]?(?=\\s|[.]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
    private static string Key(NativeCase item) => item.Id.ToLowerInvariant() + "|" + item.Name + "|" + item.Method;
    private static string Method(Mutation mutation) => (mutation.Project == "UnitTests" ? "CIS.Tests.UnitTests.Healthcheck." : "CIS.Tests.IntegrationTests.Healthcheck.Reporting.") + mutation.Test;
    private static string SelectedFilter(Mutation mutation) => "FullyQualifiedName=" + Method(mutation) + "|FullyQualifiedName=" + Scheduling;
    private static string FullFilter(string project) => "FullyQualifiedName~CIS.Tests." + project + ".Healthcheck|FullyQualifiedName=" + Scheduling;
    private static string Project(string project) => "tests/CIS.Core." + project + "/CIS.Core." + project + ".csproj";
    private static string Attribute(XElement element, string name) => (string?)element.Attribute(name) ?? throw new InvalidDataException("Missing native " + name);
    private static string Required(JsonNode node, string key) => node[key]?.GetValue<string>() is { Length: > 0 } text ? text : throw new InvalidDataException("Missing matrix " + key);
    private static bool IsInput(string path) => path.StartsWith(LanguagePrefix, StringComparison.Ordinal) || path.StartsWith("conformance/", StringComparison.Ordinal)
        || path.StartsWith("contracts/", StringComparison.Ordinal) || path.StartsWith("compatibility/csharp/", StringComparison.Ordinal)
        || path is "README.md" or "AGENTS.md" or ".gitattributes" or ".editorconfig" or ".gitignore";
    private static bool IsUnder(string root, string path) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || Path.GetFullPath(root).Equals(Path.GetFullPath(path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool IsExpectedFailure(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
        or JsonException or System.Xml.XmlException or System.ComponentModel.Win32Exception or TimeoutException or KeyNotFoundException or FormatException;
    private static string[] AdaptArguments(string[] arguments)
    {
        string[] result = arguments.ToArray();
        for (int index = 0; index < result.Length; index += 2)
        {
            if (index + 1 >= result.Length || result[index] is not ("--repo-root" or "--matrix" or "--output" or "--dotnet"))
            { throw new ArgumentException("Use --repo-root <path> --matrix <path> --output <empty-isolated-path> [--dotnet <path>]."); }
            if (result[index] == "--output") { result[index] = "--output-root"; }
        }
        return result;
    }

    private sealed record Mutation(string Id, string Project, string Path, string Find, string Replace, string Test, Assertion[] Assertions);
    private sealed record Assertion(string Source, int Line, int[] NativeLines, string Expression, string Kind, string CasePattern, string? Expected, string? Actual, string? Message);
    private sealed record NativeCase(string Id, string Name, string Method, string Outcome, string Message, string StackTrace);
    private sealed record NativeRun(string Path, string Hash, int ExitCode, bool Serialized, NativeCase[] Cases);
}
