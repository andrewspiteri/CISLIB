using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Preserves the parser smoke command; complete release qualification remains separate.
internal static class ToolchainQualification
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var inputs = new TypeManagerVerificationInputs(args, ["repo-root", "output-root", "powershell-version"], ["powershell"]);
        string repo = inputs.PathOf("repo-root"), output = inputs.Output;
        MicrosoftLoggingQualification.RequireNewOutput(output);
        Console.WriteLine("Toolchain: inventory maintained source");
        SourceInventoryEntry[] inventory = await SourceInventory.CaptureAsync(repo);
        Require(inventory.Length > 0, "Unable to enumerate repository source.");
        inputs.EmptyOutput();
        string source = Path.Combine(output, "source"), language = Path.Combine(source, "implementations/csharp");
        Console.WriteLine("Toolchain: snapshot " + inventory.Length + " source inputs");
        foreach (SourceInventoryEntry item in inventory) { Copy(Path.Combine(repo, item.Path), Path.Combine(source, item.Path), item.Sha256); }
        Write(Path.Combine(output, "source-inventory.json"), JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        JsonObject policy = Read(Path.Combine(language, "eng/toolchain.json"));
        var environment = new Dictionary<string, string?>
        {
            ["DOTNET_CLI_HOME"] = Path.Combine(output, "dotnet-home"),
            ["NUGET_PACKAGES"] = Path.Combine(output, "packages"),
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(output, "http-cache"),
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
            ["TESTINGPLATFORM_TELEMETRY_OPTOUT"] = "1",
            ["VSTEST_TELEMETRY_OPTEDOUT"] = "1",
            ["UseSharedCompilation"] = "false",
            ["MSBUILDDISABLENODEREUSE"] = "1"
        };
        string cache = environment["NUGET_PACKAGES"]!, powershell = inputs.Optional("powershell") ?? "pwsh";
        var commands = new JsonArray();
        async Task<ProcessResult> Run(string name, string[] argv, string? cwd = null, int expected = 0, string? executable = null)
        {
            Console.WriteLine("Toolchain: " + name);
            string directory = cwd ?? language, exe = executable ?? inputs.Dotnet;
            // Console diagnostics may contain mixed Windows encodings. Hash and retain the original streams.
            var commandEnvironment = new Dictionary<string, string?>(environment);
            if (name == "mutation") { commandEnvironment["CisMutationTesting"] = "true"; }
            ProcessResult result = await ProcessRunner.RunAsync(exe, argv, directory, TimeSpan.FromMinutes(45), commandEnvironment, Encoding.UTF8);
            RecordProcess(output, name, result);
            commands.Add(new JsonObject
            {
                ["name"] = name,
                ["executable"] = exe,
                ["arguments"] = JsonSerializer.SerializeToNode(argv),
                ["workingDirectory"] = directory,
                ["expectedExit"] = expected,
                ["exitCode"] = result.ExitCode,
                ["log"] = "logs/" + name + ".log"
            });
            Write(Path.Combine(output, "commands.json"), commands.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Require(result.ExitCode == expected, name + " returned " + result.ExitCode + "; expected " + expected + ". Native output retained.");
            return result;
        }
        string assembly = Path.Combine(language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll");
        string? assemblyHash = null;
        bool mutationStarted = false;
        var errors = new List<string>();
        var summary = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["task"] = "WP01-S03",
            ["runRoot"] = output,
            ["recordedUtc"] = DateTimeOffset.UtcNow,
            ["sdk"] = policy["sdk"]!.DeepClone(),
            ["targetRuntime"] = policy["targetRuntime"]!.DeepClone(),
            ["toolHostRuntime"] = policy["toolHostRuntime"]!.DeepClone(),
            ["runner"] = policy["runner"]!.DeepClone(),
            ["sourceFileCount"] = inventory.Length,
            ["apiCompatibility"] = "Core self-comparison plus compatible and deliberately breaking public fixtures; no historical release claim"
        };
        try
        {
            ProcessResult sdk = await Run("sdk", ["--version"]);
            Require(sdk.StandardOutput.Trim() == (string?)policy["sdk"], "Required SDK differs from the selected SDK.");
            await Run("dotnet-info", ["--info"]);
            await Run("runtimes", ["--list-runtimes"]);
            await Run("tool-restore", ["tool", "restore", "--tool-manifest", ".config/dotnet-tools.json", "--configfile", "eng/Tools.NuGet.config", "--no-http-cache"]);
            string[] stryker = ToolArguments(policy, cache, "dotnet-stryker"), api = ToolArguments(policy, cache, "Microsoft.DotNet.ApiCompat.Tool");
            await Run("stryker-help", [.. stryker, "--help"]);
            await Run("apicompat-version", [.. api, "--version"]);
            await Run("locked-restore", ["restore", "CisLib.slnx", "--locked-mode", "--configfile", "NuGet.config", "--no-http-cache"]);
            ProcessResult git = await Run("git-version", ["--version"], executable: "git");
            ToolchainInputs.Record(output, language, cache, inputs.Get("powershell-version"), git.StandardOutput.Trim());
            await Run("developer-verification", ["-NoProfile", "-File", "eng/verify.ps1", "-MaxBuildNodes", "4"], executable: powershell);
            await Run("exact-runtime-host", ["exec", "--fx-version", ToolchainInputs.Text(policy, "targetRuntime"), "--roll-forward", "Disable", "examples/logging/ConsoleHuman/bin/Release/net10.0/CisLib.Examples.ConsoleHuman.dll"]);
            summary["unitCases"] = ToolchainReports.PassedTests(Path.Combine(source, ".artifacts/csharp/tests/CIS.Core.UnitTests.trx"));
            summary["architectureCases"] = ToolchainReports.PassedTests(Path.Combine(source, ".artifacts/csharp/tests/CIS.Core.ArchitectureTests.trx"));
            string coverage = Path.Combine(output, "coverage");
            await Run("parser-coverage", ["test", "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj", "-c", "Release", "--no-build", "--no-restore",
                "--filter", "FullyQualifiedName~SeverityTokenParserTests", "--settings", "verification/coverage/core.runsettings",
                "--collect", "Code Coverage;Format=cobertura", "--results-directory", coverage, "--logger", "trx;LogFileName=parser.trx", "--diag", Path.Combine(coverage, "vstest.log")]);
            JsonObject observation = ToolchainReports.Coverage(coverage, policy["coverage"]!.AsObject());
            WriteJson(Path.Combine(output, "coverage-observation.json"), observation);
            summary["coverage"] = observation.DeepClone();
            Require(observation["passed"]!.GetValue<bool>(), "Coverage denominator or threshold failed; native observation retained.");
            await Run("apicompat-core", [.. api, "--left", assembly, "--right", assembly, "--strict-mode"]);
            string controls = Path.Combine(output, "api-controls");
            foreach (string variant in new[] { "baseline", "compatible", "breaking" })
            {
                string directory = Path.Combine(controls, variant);
                Write(Path.Combine(directory, "ApiControl.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ApiControl</AssemblyName><Nullable>enable</Nullable><Deterministic>true</Deterministic></PropertyGroup></Project>");
                string method = variant == "breaking" ? "" : "public static int Parse(string token) => token.Length;";
                Write(Path.Combine(directory, "Contract.cs"), "namespace ToolchainControl; public static class Contract { " + method + " }");
                await Run("api-build-" + variant, ["build", Path.Combine(directory, "ApiControl.csproj"), "-c", "Release", "-p:RestoreConfigFile=" + Path.Combine(language, "NuGet.config")]);
            }
            string Control(string variant) => Path.Combine(controls, variant, "bin/Release/net10.0/ApiControl.dll");
            await Run("apicompat-positive", [.. api, "--left", Control("baseline"), "--right", Control("compatible"), "--strict-mode"]);
            ProcessResult negative = await Run("apicompat-negative", [.. api, "--left", Control("baseline"), "--right", Control("breaking"), "--strict-mode"], expected: 1);
            Require(Regex.IsMatch(negative.StandardOutput + negative.StandardError, @"CP0002.*Parse", RegexOptions.CultureInvariant), "API control failed for an unexpected reason.");
            assemblyHash = Hash(assembly);
            mutationStarted = true;
            await Run("mutation", [.. stryker, "--config-file", "../../verification/mutation/stryker-config.json", "--output", Path.Combine(output, "mutation"), "--skip-version-check", "--log-to-file"],
                Path.Combine(language, "tests/CIS.Core.UnitTests"));
            string[] reports = Directory.GetFiles(Path.Combine(output, "mutation"), "mutation-report.json", SearchOption.AllDirectories);
            Require(reports.Length == 1, "Expected exactly one native mutation report.");
            JsonObject mutation = ToolchainReports.Mutation(reports[0], Path.Combine(language, "verification/mutation/smoke-dispositions.json"),
                language, policy["tools"]!.AsArray().Single(tool => (string?)tool!["id"] == "dotnet-stryker")!["version"]!.GetValue<string>());
            WriteJson(Path.Combine(output, "mutation-dispositions.json"), mutation);
            summary["mutation"] = mutation.DeepClone();
        }
        catch (Exception error) { errors.Add(error.Message); }
        finally
        {
            try { MicrosoftLoggingQualification.RequireUnchanged(repo, inventory); MicrosoftLoggingQualification.RequireUnchanged(source, inventory); summary["sourceIntegrity"] = "Passed"; }
            catch (Exception error) { errors.Add(error.Message); summary["sourceIntegrity"] = "Failed"; }
            if (mutationStarted)
            {
                try
                {
                    await Run("fresh-unmutated-build", ["build", "src/CIS.Core/CIS.Core.csproj", "-c", "Release", "--no-restore", "--no-incremental"]);
                    Require(Hash(assembly) == assemblyHash, "Fresh unmutated assembly differs from the qualified assembly.");
                    summary["unmutatedRebuild"] = "Passed";
                }
                catch (Exception error) { errors.Add(error.Message); summary["unmutatedRebuild"] = "Failed"; }
            }
        }
        if (errors.Count == 0)
        {
            try
            {
                await Run("fresh-unmutated-pack", ["pack", "src/CIS.Core/CIS.Core.csproj", "-c", "Release", "--no-build", "--no-restore", "-o", Path.Combine(output, "unmutated-package")]);
                string[] packages = Directory.GetFiles(Path.Combine(output, "unmutated-package"), "*.nupkg");
                Require(packages.Length == 1, "Expected one fresh unmutated package.");
                await Run("unmutated-package-boundaries", ["-NoProfile", "-File", "eng/check-dependencies.ps1", "-ProjectPath", "src/CIS.Core/CIS.Core.csproj", "-AssemblyPath", assembly, "-PackagePath", packages[0]], executable: powershell);
                summary["coreAssemblySha256"] = Hash(assembly); summary["packageSha256"] = Hash(packages[0]);
            }
            catch (Exception error) { errors.Add(error.Message); }
        }
        summary["outcome"] = errors.Count == 0 ? "Passed" : "Failed";
        summary["issues"] = JsonSerializer.SerializeToNode(errors);
        WriteJson(Path.Combine(output, "summary.json"), summary);
        string[] evidence = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Where(path =>
        {
            string relative = Path.GetRelativePath(output, path).Replace('\\', '/');
            return !relative.Contains('/') || new[] { "logs/", "coverage/", "mutation/logs/", "mutation/reports/", "unmutated-package/" }.Any(prefix => relative.StartsWith(prefix, StringComparison.Ordinal)) ||
                relative.StartsWith("source/.artifacts/csharp/tests/", StringComparison.Ordinal) && path.EndsWith(".trx", StringComparison.Ordinal);
        }).Order(StringComparer.Ordinal).ToArray();
        Write(Path.Combine(output, "evidence-inventory.json"), JsonSerializer.Serialize(evidence.Select(path => new { path = Path.GetRelativePath(output, path).Replace('\\', '/'), sha256 = Hash(path), bytes = new FileInfo(path).Length }), new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("Toolchain smoke " + (errors.Count == 0 ? "passed" : "failed") + ". Retain: " + output);
        foreach (string error in errors) { Console.Error.WriteLine(error); }
        return errors.Count == 0 ? 0 : 1;
    }

    internal static string[] ToolArguments(JsonObject policy, string cache, string id)
    {
        JsonObject tool = policy["tools"]!.AsArray().Select(item => item!.AsObject()).Single(item => (string?)item["id"] == id);
        string entry = Path.Combine(cache, ToolchainInputs.Relative(id.ToLowerInvariant()), ToolchainInputs.Relative(ToolchainInputs.Text(tool, "version")),
            "tools/net8.0/any", ToolchainInputs.Relative(ToolchainInputs.Text(tool, "entryPoint")));
        entry = Path.GetFullPath(entry);
        Require(File.Exists(entry), "Fresh restore did not materialize pinned tool: " + id);
        return ["exec", "--fx-version", ToolchainInputs.Text(policy, "toolHostRuntime"), "--roll-forward", "Disable", entry];
    }

    internal static void RecordProcess(string output, string name, ProcessResult result)
    {
        string prefix = Path.Combine(output, "logs", name);
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
        File.WriteAllBytes(prefix + ".stdout.bin", result.StandardOutputBytes.ToArray());
        File.WriteAllBytes(prefix + ".stderr.bin", result.StandardErrorBytes.ToArray());
        Write(prefix + ".log", result.StandardOutput + result.StandardError);
        WriteJson(prefix + ".process.json", new JsonObject
        {
            ["pid"] = result.ProcessId,
            ["exitCode"] = result.ExitCode,
            ["stdoutSha256"] = Digest(result.StandardOutputBytes.Span),
            ["stderrSha256"] = Digest(result.StandardErrorBytes.Span),
            ["stdout"] = "logs/" + name + ".stdout.bin",
            ["stderr"] = "logs/" + name + ".stderr.bin",
            ["diagnosticDecoding"] = "UTF-8 with replacement for display only; original streams retained and hashed"
        });
    }
}
