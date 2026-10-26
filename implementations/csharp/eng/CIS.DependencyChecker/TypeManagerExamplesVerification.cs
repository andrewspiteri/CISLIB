using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal sealed class TypeManagerExamplesVerification
{
    private readonly TypeManagerVerificationInputs input;
    private readonly string root, language, repository, feed;
    private readonly IReadOnlyDictionary<string, string?> environment;
    private readonly JsonObject report;
    private string version = "", coreHash = "", helperHash = "";
    private static readonly string[] ExpectedTypes = ["CIS.TypeManagement.Examples.Unused.DirectExample", "CIS.TypeManagement.Examples.Unused.InheritedExample"];
    private TypeManagerExamplesVerification(TypeManagerVerificationInputs input)
    {
        this.input = input; root = input.Output; language = input.Language; repository = input.PathOf("repo-root"); feed = Path.Combine(root, "feed"); environment = HermeticEnvironment(root);
        report = new JsonObject
        {
            ["complete"] = false,
            ["scope"] = "Native source and exact-package example integration; not a release gate",
            ["platform"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["architecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ["verifierSha256"] = Hash(typeof(TypeManagerExamplesVerification).Assembly.Location),
            ["packageSha256"] = Hash(input.PathOf("package")),
            ["results"] = new JsonArray()
        };
    }
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["repo-root", "package", "dependency-cache", "output-root"], []);
        Require(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64 && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()), "Supported example lanes are Windows x64 and Linux x64.");
        input.EmptyOutput(); return await new TypeManagerExamplesVerification(input).Verify();
    }
    private void Save() => WriteJson(Path.Combine(root, "results.json"), report);
    private void Passed(string name, JsonObject? details = null) { JsonObject row = details ?? new(); row["case"] = name; row["passed"] = true; report["results"]!.AsArray().Add(row); Save(); }
    private async Task<string> Run(string name, IEnumerable<string> arguments, string? cwd = null, string? failure = null, string? failedFile = null)
    {
        var child = await input.Run(name, arguments, cwd, environment: environment, requireSuccess: failure is null); string combined = child.StandardOutput + child.StandardError;
        if (failure is not null)
        {
            Require(child.ExitCode == 1 && child.StandardError.Contains(failure, StringComparison.Ordinal) && failedFile is not null && child.StandardError.Contains(failedFile, StringComparison.Ordinal)
                && !combined.Contains("Unhandled exception", StringComparison.Ordinal) && !new[] { "PREPARATION_COMPLETE", "BUILDER_CREATING", "CONTAINER_BUILDING", "CONTAINER_BUILT", "APPLICATION_READY" }.Any(marker => combined.Contains(marker, StringComparison.Ordinal)),
                name + " did not fail before container/application startup for " + failure); return child.StandardError;
        }
        return child.StandardOutput;
    }
    private void Setup()
    {
        using (ZipArchive archive = ZipFile.OpenRead(input.PathOf("package")))
        {
            using Stream spec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
            version = XDocument.Load(spec).Descendants().Single(e => e.Name.LocalName == "metadata").Elements().Single(e => e.Name.LocalName == "version").Value;
            coreHash = EntryHash(archive, "lib/net10.0/CIS.Core.dll"); helperHash = EntryHash(archive, "tools/net10.0/CIS.TypeManager.Build.dll");
        }
        report["packageVersion"] = version; report["packageCoreSha256"] = coreHash; report["packageHelperSha256"] = helperHash;
        Copy(Path.Combine(language, "global.json"), Path.Combine(root, "global.json")); Copy(Path.Combine(repository, ".editorconfig"), Path.Combine(root, ".editorconfig")); Write(Path.Combine(root, "Directory.Build.targets"), "<Project />\n");
        Directory.CreateDirectory(feed); Copy(input.PathOf("package"), Path.Combine(feed, "CIS.Core." + version + ".nupkg"));
        string policy = Path.Combine(language, "eng", "runtime-dependencies.json"); JsonObject dependencies = Read(policy, out string dependencyPolicyHash); report["runtimeDependencyPolicySha256"] = dependencyPolicyHash; report["dependencyPackages"] = new JsonObject();
        foreach (JsonNode? item in dependencies["packages"]!.AsArray())
        {
            string name = ((string)item!["id"]!).ToLowerInvariant(), dependencyVersion = (string)item["version"]!;
            string path = Path.Combine(input.PathOf("dependency-cache"), name, dependencyVersion, name + "." + dependencyVersion + ".nupkg");
            Copy(path, Path.Combine(feed, Path.GetFileName(path))); report["dependencyPackages"]![Path.GetFileName(path)] = Hash(path);
        }
        // The example projects restore both supported RIDs; keep their actual
        // configuration and seed the private feed with the pinned SDK packs.
        report["sdkPackages"] = new JsonObject();
        string toolchainPath = Path.Combine(language, "eng", "toolchain.json");
        JsonObject toolchain = Read(toolchainPath, out string toolchainHash);
        report["toolchainPolicySha256"] = toolchainHash;
        foreach (JsonNode? pack in toolchain["frameworkDownloads"]!["packages"]!.AsArray())
        {
            string name = ((string)pack!["id"]!).ToLowerInvariant(), runtimeVersion = (string)pack["version"]!;
            Require(runtimeVersion == (string?)toolchain["targetRuntime"], "Framework download version must match targetRuntime");
            string path = Path.Combine(input.PathOf("dependency-cache"), name, runtimeVersion, name + "." + runtimeVersion + ".nupkg");
            Copy(path, Path.Combine(feed, Path.GetFileName(path))); report["sdkPackages"]![Path.GetFileName(path)] = Hash(path);
        }

        report["sourceProvenanceScope"] = "Recorded C# and example inputs plus copied build inputs; not a transitive build attestation";
        report["copiedBuildInputs"] = new JsonObject(); report["exampleInputs"] = new JsonObject();
        foreach (string folder in new[] { "examples/type-manager", "tests/fixtures/TypeManager/ExampleLate" })
        { foreach ((string relative, JsonNode? hash) in TreeHashes(Path.Combine(language, folder), sourceOnly: true)) { report["exampleInputs"]![folder + "/" + relative] = hash!.DeepClone(); } }
        report["sourceCoreInputs"] = input.CoreSources();
    }
    private static string EntryHash(ZipArchive archive, string name)
    { using Stream data = archive.GetEntry(name)!.Open(); return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data)); }
    private string PrepareLane(string lane)
    {
        string tree = Path.Combine(root, lane, "implementations", "csharp"); Directory.CreateDirectory(tree);
        foreach (string file in new[] { "global.json", "Directory.Build.props", "Directory.Packages.props" }) { Copy(Path.Combine(language, file), Path.Combine(tree, file)); }
        if (File.Exists(Path.Combine(language, "Directory.Build.targets"))) { Copy(Path.Combine(language, "Directory.Build.targets"), Path.Combine(tree, "Directory.Build.targets")); }
        Write(Path.Combine(tree, "NuGet.config"), new XElement("configuration", new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "local"), new XAttribute("value", feed))),
            new XElement("fallbackPackageFolders", new XElement("clear")), new XElement("auditSources", new XElement("clear"))).ToString());
        CopyTree(Path.Combine(language, "examples", "type-manager"), Path.Combine(tree, "examples", "type-manager"), sourceOnly: true);
        CopyTree(Path.Combine(language, "tests", "fixtures", "TypeManager", "ExampleLate"), Path.Combine(tree, "tests", "fixtures", "TypeManager", "ExampleLate"), sourceOnly: true);
        if (lane == "source")
        {
            foreach (string folder in new[] { "src/CIS.Core", "eng/CIS.TypeManager.Build", "build/type-manager" }) { CopyTree(Path.Combine(language, folder), Path.Combine(tree, folder), sourceOnly: true); }
            Copy(Path.Combine(repository, "README.md"), Path.Combine(root, lane, "README.md"));
        }
        else
        {
            foreach (string host in new[] { "Console", "Web" })
            {
                string path = Path.Combine(tree, "examples", "type-manager", host, host + ".csproj"); XElement project = XElement.Load(path);
                project.AddFirst(new XElement("Sdk", new XAttribute("Name", "CIS.Core"), new XAttribute("Version", version)));
                XElement[] references = project.Elements().SelectMany(group => group.Elements()).Where(e => e.Name.LocalName == "ProjectReference" && (string?)e.Attribute("Include") == "../../../src/CIS.Core/CIS.Core.csproj").ToArray();
                XElement[] imports = project.Elements("Import").Where(e => (string?)e.Attribute("Project") == "../../../build/type-manager/CIS.TypeManager.targets").ToArray();
                Require(references.Length == 1 && imports.Length == 1, "Expected exactly one Core reference and source inventory import");
                references[0].Remove(); imports[0].Remove(); Write(path, project.ToString());
            }
        }
        report["copiedBuildInputs"]![lane] = TreeHashes(tree); return tree;
    }

    private async Task Http(string name, string directory, string hostName)
    {
        var start = new ProcessStartInfo(input.Dotnet) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true) };
        foreach (string argument in new[] { Path.Combine(directory, hostName + ".dll"), "--urls", "http://127.0.0.1:0" }) { start.ArgumentList.Add(argument); }
        foreach ((string key, string? value) in environment) { if (value is null) { start.Environment.Remove(key); } else { start.Environment[key] = value; } }
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start owned example server.");
        var gate = new object(); var text = new StringBuilder(); Task stdout = Drain(process.StandardOutput), stderr = Drain(process.StandardError);
        try
        {
            string? address = null; var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(45))
            {
                string snapshot; lock (gate) { snapshot = text.ToString(); }
                Match match = Regex.Match(snapshot, @"Now listening on: (http://127\.0\.0\.1:\d+)");
                if (match.Success) { address = match.Groups[1].Value; break; }
                Require(!process.HasExited, name + " exited before listening"); await Task.Delay(100);
            }
            Require(address is not null, name + " did not start listening");
            using var handler = new HttpClientHandler { UseProxy = false }; using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            var responses = new JsonArray();
            for (int index = 0; index < 2; index++)
            {
                using HttpResponseMessage response = await client.GetAsync(address + "/catalog"); response.EnsureSuccessStatusCode();
                JsonObject observed = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
                Require((bool?)observed["samePreparedManager"] == true && (bool?)observed["sameCachedQuery"] == true && (bool?)observed["sameConsumerWithinRequest"] == true && !string.IsNullOrEmpty((string?)observed["consumerId"])
                    && observed["candidates"]!.AsArray().Select(c => (string)c!["type"]!).ToHashSet(StringComparer.Ordinal).SetEquals(ExpectedTypes), name + " returned an incorrect request-scoped catalog");
                responses.Add(observed);
            }
            Require((string?)responses[0]!["consumerId"] != (string?)responses[1]!["consumerId"], name + " reused a scoped consumer across HTTP requests");
            Passed(name, new JsonObject { ["responses"] = responses });
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10));
            Write(Path.Combine(root, "logs", name + ".log"), text.ToString()); WriteJson(Path.Combine(root, "logs", name + ".process.json"), new JsonObject { ["pid"] = process.Id, ["exitCode"] = process.ExitCode, ["ownedServerExited"] = process.HasExited });
        }
        async Task Drain(StreamReader reader) { while (await reader.ReadLineAsync() is string line) { lock (gate) { text.AppendLine(line); } } }
    }
    private async Task<JsonObject> Validate(string name, string lane, string host, string hostName, string directory, string project, string late)
    {
        JsonObject manifest = Read(Path.Combine(directory, hostName + ".types.json")), assets = Read(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json"));
        string[] folders = assets["packageFolders"]!.AsObject().Select(p => Path.GetFullPath(p.Key)).ToArray(), sources = assets["project"]!["restore"]!["sources"]!.AsObject().Select(p => Path.GetFullPath(p.Key)).ToArray();
        Require(folders.Length == 1 && SamePath(folders[0], Path.Combine(root, "packages")) && sources.Length == 1 && SamePath(sources[0], feed), name + " used an unexpected NuGet package folder or source");
        var expected = new HashSet<string>([hostName, "CIS.Core", "CIS.TypeManager.Examples.Contracts", "CIS.TypeManager.Examples.Unused"], StringComparer.Ordinal);
        JsonArray entries = manifest["assemblies"]!.AsArray();
        Require(entries.Count == expected.Count && entries.Select(n => ((string)n!["assemblyName"]!).Split(',')[0]).ToHashSet(StringComparer.Ordinal).SetEquals(expected), name + " selected an incomplete or unexpected inventory");
        string actualCore = Hash(Path.Combine(directory, "CIS.Core.dll"));
        if (lane == "package")
        { Require(actualCore == coreHash && Hash(Path.Combine(root, "packages", "cis.core", version.ToLowerInvariant(), "tools", "net10.0", "CIS.TypeManager.Build.dll")) == helperHash, name + " did not consume the exact Core/helper bytes"); }
        string output = await Run(name + "-run", [Path.Combine(directory, hostName + ".dll"), "--verify", late]);
        string final = output.Trim().Split('\n')[^1].TrimEnd('\r'); JsonObject observed = JsonNode.Parse(final)!.AsObject();
        Require((bool?)observed["unusedInitiallyLoaded"] == false && (bool?)observed["fixedMembershipChecked"] == true && observed["candidates"]!.AsArray().Select(c => (string)c!["type"]!).ToHashSet(StringComparer.Ordinal).SetEquals(ExpectedTypes), name + " did not discover the complete unused selection or check later membership");
        Require(observed["candidates"]!.AsArray().Select(c => (string)c!["assembly"]!).ToHashSet(StringComparer.Ordinal).SetEquals(entries.Select(n => (string)n!["assemblyName"]!).Where(n => n.StartsWith("CIS.TypeManager.Examples.Unused,", StringComparison.Ordinal))), name + " lost defining assembly identity");
        JsonNode facts = observed["report"]!;
        Require(observed["selected"]!.AsArray().Select(n => (string)n!).ToHashSet(StringComparer.Ordinal).SetEquals(entries.Select(n => (string)n!["assemblyName"]!)) && facts["SelectedAssemblyCount"]!.GetValue<int>() == entries.Count && facts["PreparedContractCount"]!.GetValue<int>() == 1 && facts["TypeCount"]!.GetValue<int>() >= 2, name + " returned an inconsistent preparation report");
        Require(facts["LoadedAssemblyIdentities"]!.AsArray().Any(n => ((string)n!).StartsWith("CIS.TypeManager.Examples.Unused,", StringComparison.Ordinal)), name + " did not record the initially unloaded library");
        string boundary = host == "Web" ? "BUILDER_CREATING" : "APPLICATION_READY"; int prepared = output.IndexOf("PREPARATION_COMPLETE", StringComparison.Ordinal), started = output.IndexOf(boundary, StringComparison.Ordinal);
        Require(prepared >= 0 && started > prepared, name + " crossed the startup boundary before preparation");
        if (host == "Web") { Require((bool?)observed["rootAndScopesShareManager"] == true && (bool?)observed["queryIdentityStable"] == true, name + " did not retain the exact manager/query across scopes"); }
        JsonObject deps = Read(Path.Combine(directory, hostName + ".deps.json"));
        Require(!deps["libraries"]!.AsObject().Any(p => p.Key.Contains("Microsoft.Build", StringComparison.Ordinal) || p.Key.Contains("CIS.TypeManager.Build", StringComparison.Ordinal)) && !File.Exists(Path.Combine(directory, "CIS.TypeManager.Build.dll")), name + " deployed build tooling");
        JsonNode runtime = Read(Path.Combine(directory, hostName + ".runtimeconfig.json"))["runtimeOptions"]!; JsonArray frameworks = runtime["frameworks"]?.AsArray() ?? new JsonArray(runtime["framework"]!.DeepClone());
        Require(frameworks.Select(f => (string)f!["name"]!).ToHashSet(StringComparer.Ordinal).SetEquals(["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"]), name + " has an unexpected shared framework requirement");
        return new JsonObject { ["coreSha256"] = actualCore, ["manifest"] = manifest, ["observed"] = observed };
    }
    private static bool SamePath(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private async Task<int> Verify()
    {
        Save();
        try
        {
            Setup(); report["sdk"] = (await Run("sdk", ["--version"])).Trim(); await Run("runtime-info", ["--info"]);
            Require((string?)report["sdk"] == (string?)Read(Path.Combine(root, "global.json"))["sdk"]!["version"], "The native SDK does not match the repository pin");
            foreach (string lane in new[] { "source", "package" })
            {
                string tree = PrepareLane(lane), lateProject = Path.Combine(tree, "tests", "fixtures", "TypeManager", "ExampleLate", "ExampleLate.csproj");
                await Run(lane + "-late-build", ["build", lateProject, "-c", "Release", "-m:1", "--nologo"], tree);
                string late = Path.Combine(Path.GetDirectoryName(lateProject)!, "bin", "Release", "net10.0", "CIS.TypeManager.Examples.Late.dll");
                foreach (string host in new[] { "Console", "Web" })
                {
                    string project = Path.Combine(tree, "examples", "type-manager", host, host + ".csproj"), hostName = "CIS.TypeManager.Examples." + host;
                    foreach (string layout in new[] { "build-debug", "build-release", "publish", "publish-no-build" })
                    {
                        string name = lane + "-" + host.ToLowerInvariant() + "-" + layout, directory;
                        if (layout.StartsWith("build", StringComparison.Ordinal))
                        {
                            string configuration = layout == "build-debug" ? "Debug" : "Release";
                            await Run(name + "-build", ["build", project, "-c", configuration, "-m:1", "--nologo"], tree);
                            string target = (await Run(name + "-target-path", ["msbuild", project, "-getProperty:TargetPath", "-p:Configuration=" + configuration, "-nologo"], tree)).Trim(); directory = Path.GetDirectoryName(target)!;
                        }
                        else
                        {
                            directory = Path.Combine(root, "deployments", name);
                            await Run(name + "-publish", ["publish", project, "-c", "Release", "-m:1", "--nologo", "-o", directory, .. layout == "publish-no-build" ? new[] { "--no-build" } : Array.Empty<string>()], tree);
                        }
                        Passed(name, await Validate(name, lane, host, hostName, directory, project, late));
                        foreach (string fault in new[] { "missing-manifest", "malformed-manifest", "missing-library" })
                        {
                            string broken = Path.Combine(root, "broken", name + "-" + fault); CopyTree(directory, broken);
                            string inventory = Path.Combine(broken, hostName + ".types.json");
                            if (fault == "missing-manifest") { File.Delete(inventory); } else if (fault == "malformed-manifest") { Write(inventory, "{"); } else { File.Delete(Path.Combine(broken, "CIS.TypeManager.Examples.Unused.dll")); }
                            string failedFile = fault == "missing-library" ? Path.Combine(broken, "CIS.TypeManager.Examples.Unused.dll") : inventory;
                            string rejected = await Run(name + "-" + fault, [Path.Combine(broken, hostName + ".dll"), "--verify", late], failure: "PREPARATION_FAILED: InvalidInventory", failedFile: failedFile);
                            Require(rejected.Contains(fault == "malformed-manifest" ? "BytePositionInLine" : "FileNotFoundException", StringComparison.Ordinal), name + "-" + fault + " lost the expected original failure detail");
                            Passed(name + "-" + fault, new JsonObject { ["expectedFailure"] = "InvalidInventory", ["startupBoundaryReached"] = false });
                        }
                        if (host == "Web" && layout == "build-release") { await Http(name + "-http", directory, hostName); }
                    }
                }
            }
            Require(report["results"]!.AsArray().Count == 66, "Unexpected example check count"); Require(Hash(input.PathOf("package")) == (string?)report["packageSha256"], "Input package changed during verification");
            report["complete"] = true; Save(); return 0;
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { Save(); }
    }
}
