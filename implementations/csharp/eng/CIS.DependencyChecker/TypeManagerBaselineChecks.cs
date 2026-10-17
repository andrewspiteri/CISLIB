using System.Reflection;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static class TypeManagerBaselineChecks
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args, ["repo-root", "candidate-core", "output-root"], []);
        input.EmptyOutput();
        string root = input.Output, core = input.PathOf("candidate-core");
        string baseline = Path.Combine(input.Language, "verification/contracts/type-manager/v1-development");
        string pin = Hash(core), manifestPin = Hash(Path.Combine(baseline, "baseline-hashes.json"));
        var report = new JsonObject { ["scope"] = "Unpublished initial development baseline; no released Type Manager history", ["complete"] = false, ["coreSha256"] = pin, ["baselineManifestSha256"] = manifestPin, ["results"] = new JsonArray() };
        void Passed(string name, JsonObject? row = null) { row ??= new(); row["name"] = name; row["passed"] = true; report["results"]!.AsArray().Add(row); }
        try
        {
            ValidateBaseline(baseline);
            JsonObject policy = Read(Path.Combine(input.Language, "eng/toolchain.json"));
            string runtime = (string)policy["targetRuntime"]!;
            string tool = Environment.GetEnvironmentVariable("CIS_COMPAT_API_TOOL") ?? Path.Combine(Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget/packages"), "microsoft.dotnet.apicompat.tool/10.0.401/tools/net8.0/any/Microsoft.DotNet.ApiCompat.Tool.dll");
            Require(File.Exists(tool), "Restore the pinned ApiCompat tool or set CIS_COMPAT_API_TOOL.");
            string apiDotnet = Environment.GetEnvironmentVariable("CIS_COMPAT_API_DOTNET") ?? input.Dotnet;
            string apiRuntime = (string)policy["toolHostRuntime"]!;
            string toolPin = Hash(tool);
            ProcessResult version = await ProcessRunner.RunAsync(apiDotnet, ["exec", "--fx-version", apiRuntime, "--roll-forward", "Disable", tool, "--version"], input.Language, TimeSpan.FromMinutes(1));
            Write(Path.Combine(root, "logs/api-version.log"), version.StandardOutput + version.StandardError);
            Require(version.ExitCode == 0 && version.StandardOutput.Trim().StartsWith("10.0.401+", StringComparison.Ordinal), "Wrong API compatibility tool.");
            report["apiToolVersion"] = version.StandardOutput.Trim();
            ProcessResult api = await ProcessRunner.RunAsync(apiDotnet, ["exec", "--fx-version", apiRuntime, "--roll-forward", "Disable", tool, "-l", Path.Combine(baseline, "reference/CIS.Core.dll"), "-r", core, "--enable-rule-cannot-change-parameter-name", "--enable-rule-attributes-must-match"], input.Language, TimeSpan.FromMinutes(2));
            Write(Path.Combine(root, "logs/api.log"), api.StandardOutput + api.StandardError);
            Require(api.ExitCode == 0, "API compatibility failed; inspect logs/api.log.");
            Require(Hash(tool) == toolPin, "API tool changed during comparison.");
            report["apiToolSha256"] = toolPin;
            Passed("api");
            string frozen = Path.Combine(root, "frozen");
            CopyTree(Path.Combine(baseline, "compiled"), frozen);
            string consumer = Path.Combine(frozen, "CIS.TypeManager.ContractConsumer.dll"), frozenPin = Hash(consumer);
            await Replay("frozen-binary", frozen);
            Require(Hash(consumer) == frozenPin, "Frozen consumer was modified.");
            string source = Path.Combine(root, "source");
            Copy(Path.Combine(baseline, "source/Program.cs"), Path.Combine(source, "Program.cs"));
            Write(Path.Combine(source, "Directory.Build.props"), "<Project />");
            Write(Path.Combine(source, "Directory.Build.targets"), "<Project />");
            string template = File.ReadAllText(Path.Combine(baseline, "source/Consumer.csproj.template"));
            Write(Path.Combine(source, "Consumer.csproj"), template.Replace("../reference/CIS.Core.dll", System.Security.SecurityElement.Escape(core), StringComparison.Ordinal));
            await input.Run("source-build", ["build", Path.Combine(source, "Consumer.csproj"), "-c", "Release", "-m:1", "--nologo"], input.Language);
            await Replay("source-consumer", Path.Combine(source, "bin/Release/net10.0"));
            ValidateBaseline(baseline);
            Require(Hash(Path.Combine(baseline, "baseline-hashes.json")) == manifestPin && Hash(core) == pin, "Baseline or candidate changed during compatibility checks.");
            report["complete"] = true;
            return 0;

            async Task Replay(string name, string directory)
            {
                var references = new JsonArray();
                var runtimeInputs = new JsonArray();
                foreach (string file in new[] { "CIS.Core.dll", "Microsoft.Extensions.Logging.Abstractions.dll", "Microsoft.Extensions.DependencyInjection.Abstractions.dll" })
                {
                    string path = file == "CIS.Core.dll" ? core : Path.Combine(Path.GetDirectoryName(core)!, file);
                    Copy(path, Path.Combine(directory, file));
                    runtimeInputs.Add(new JsonObject { ["name"] = file, ["sha256"] = Hash(path) });
                }
                string binary = Path.Combine(directory, "CIS.TypeManager.ContractConsumer.dll"), manifest = Path.Combine(directory, "consumer.types.json");
                string hostIdentity = AssemblyName.GetAssemblyName(binary).FullName;
                foreach (string path in new[] { binary, core }) { references.Add(new JsonObject { ["assemblyName"] = AssemblyName.GetAssemblyName(path).FullName, ["relativePath"] = path == core ? "CIS.Core.dll" : "CIS.TypeManager.ContractConsumer.dll" }); }
                WriteJson(manifest, new JsonObject { ["schemaVersion"] = 1, ["hostAssembly"] = hostIdentity, ["assemblies"] = references });
                // The first consumer predates Core's approved dependencies. Supply an external
                // host manifest without changing the frozen consumer or its deps file.
                var assets = new JsonObject(); var libraries = new JsonObject();
                foreach (JsonNode? file in runtimeInputs.Skip(1))
                {
                    string fileName = (string)file!["name"]!;
                    AssemblyName identity = AssemblyName.GetAssemblyName(Path.Combine(directory, fileName));
                    string key = identity.Name + "/" + identity.Version;
                    assets[key] = new JsonObject { ["runtime"] = new JsonObject { [fileName] = new JsonObject { ["assemblyVersion"] = identity.Version!.ToString() } } };
                    libraries[key] = new JsonObject { ["type"] = "project", ["serviceable"] = false, ["sha512"] = "" };
                }
                const string target = ".NETCoreApp,Version=v10.0";
                string supplemental = Path.Combine(directory, "approved-dependencies.deps.json");
                WriteJson(supplemental, new JsonObject { ["runtimeTarget"] = new JsonObject { ["name"] = target }, ["targets"] = new JsonObject { [target] = assets }, ["libraries"] = libraries });
                ProcessResult child = await input.Run(name, ["exec", "--fx-version", runtime, "--roll-forward", "Disable", "--additional-deps", supplemental, binary, manifest], input.Language);
                JsonObject observed = JsonNode.Parse(child.StandardOutput)!.AsObject();
                Require((string?)observed["contractConsumer"] == "passed" && (int?)observed["selectedAssemblyCount"] == 2, "Invalid result from " + name);
                Passed(name, new JsonObject { ["consumerSha256"] = Hash(binary), ["runtimeInputs"] = runtimeInputs, ["result"] = observed });
            }
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { WriteJson(Path.Combine(root, "results.json"), report); }
    }

    internal static void ValidateBaseline(string baseline)
    {
        foreach (JsonNode? file in Read(Path.Combine(baseline, "baseline-hashes.json"))["files"]!.AsArray())
        {
            string path = Path.GetFullPath(Path.Combine(baseline, (string)file!["path"]!));
            Require(path.StartsWith(Path.GetFullPath(baseline) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "Frozen baseline path escapes its root.");
            Require(Hash(path).Equals((string?)file["sha256"], StringComparison.OrdinalIgnoreCase), "Frozen baseline changed: " + (string?)file["path"]);
        }
    }
}
