using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Exercise the real SDK build/publish/clean lifecycle; never replace it with report fixtures.
internal static class TypeManagerSourceInventoryChecks
{
    private const string HostProject = "tests/fixtures/TypeManager/Host/Host.csproj";
    private const string HostName = "CIS.TypeManager.Fixture.Host";
    private const string ManifestName = HostName + ".types.json";

    internal static async Task<int> RunAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args, ["repo-root", "output-root"], []);
        Directory.CreateDirectory(Path.Combine(input.Output, "logs"));
        string reportPath = Path.Combine(input.Output, "results.json");
        File.Delete(reportPath); // A failed rerun cannot retain an earlier success.
        var results = new JsonArray();
        void Passed(string name, int? count = null)
        {
            var row = new JsonObject { ["name"] = name, ["passed"] = true };
            if (count is not null) { row["selectedAssemblyCount"] = count; }
            results.Add(row);
        }
        void Clear(string directory, string manifest = ManifestName) { string path = Path.Combine(directory, manifest); if (File.Exists(path)) { File.Delete(path); } }
        void Check(string name, string directory, int count = 7)
        {
            string path = Path.Combine(directory, ManifestName);
            ValidateInventory(Read(path), count, asset => AssemblyName.GetAssemblyName(Path.Combine(directory, asset)).FullName);
            Copy(path, Path.Combine(input.Output, name + ".types.json"));
            Passed(name, count);
        }
        async Task<string> Run(string name, string[] command, string? error = null)
        {
            var child = await input.Run(name, command, input.Language, expectedError: error);
            return child.StandardOutput;
        }
        async Task Reject(string name, string[] command, string error)
        {
            await Run(name, command, error); Passed(name);
        }
        string hostDirectory = (await Run("target-directory", ["msbuild", HostProject, "-p:Configuration=Release", "-getProperty:TargetDir", "-nologo"])).Trim();
        Require(Path.IsPathFullyQualified(hostDirectory), "MSBuild returned an invalid TargetDir.");
        Clear(hostDirectory);
        await Run("build", ["build", HostProject, "-c", "Release", "-m:1", "--nologo", "-v:minimal"]);
        Check("build", hostDirectory);
        Require(File.Exists(Path.Combine(hostDirectory, "fr/CIS.TypeManager.Fixture.Unused.resources.dll")), "Satellite fixture was not deployed.");
        Require(File.Exists(Path.Combine(hostDirectory, "content-only.dll")), "Content exclusion fixture was not deployed.");
        JsonObject observed = JsonNode.Parse(await Run("fresh-process", ["run", "--project", HostProject, "-c", "Release", "--no-build", "--no-restore"]))!.AsObject();
        Require((bool?)observed["unusedInitiallyLoaded"] == false && observed["identities"]!.AsArray().Count == 7, "Initially-unloaded fixture failed.");
        Require((bool?)observed["containerConstructed"] == true && (int?)observed["typeCount"] > 0, "Preparation did not finish before the container boundary.");
        Passed("initially-unloaded");
        observed = JsonNode.Parse(await Run("cancel-during-load", [Path.Combine(hostDirectory, HostName + ".dll"), "cancel-during-load"]))!.AsObject();
        Require((bool?)observed["canceledDuringLoad"] == true && (bool?)observed["containerConstructed"] == false && (bool?)observed["unusedInitiallyLoaded"] == false, "Cancellation did not prevent publication during real loading.");
        Passed("cancel-during-load");

        string manifest = Path.Combine(hostDirectory, ManifestName), beforeHash = Hash(manifest);
        long beforeTicks = File.GetLastWriteTimeUtc(manifest).Ticks;
        string incremental = await Run("incremental", ["build", HostProject, "-c", "Release", "--no-restore", "-m:1", "--nologo", "-v:normal"]);
        Require(incremental.Contains("Type Manager inventory:", StringComparison.Ordinal), "Incremental build did not execute the inventory generator.");
        Require(Hash(manifest) == beforeHash && File.GetLastWriteTimeUtc(manifest).Ticks == beforeTicks, "Unchanged manifest was rewritten.");
        Passed("unchanged-output");

        string publish = Path.Combine(input.Output, "publish");
        Clear(publish);
        await Run("publish-no-build", ["publish", HostProject, "-c", "Release", "--no-build", "--no-restore", "-m:1", "-o", publish, "--nologo"]);
        Check("publish-no-build", publish);
        await Run("published-process", [Path.Combine(publish, HostName + ".dll")]);
        string defaultPublish = Path.GetFullPath((await Run("default-publish-directory", ["msbuild", HostProject, "-p:Configuration=Release", "-getProperty:PublishDir", "-nologo"])).Trim().Replace('\\', Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.Combine(input.Language, HostProject))!);
        Clear(defaultPublish);
        await Run("default-publish-no-build", ["publish", HostProject, "-c", "Release", "--no-build", "--no-restore", "-m:1", "--nologo"]);
        Check("default-publish-no-build", defaultPublish);
        await Run("default-published-process", [Path.Combine(defaultPublish, HostName + ".dll")]);

        await Reject("package-copy-disabled", ["build", HostProject, "-c", "Release", "--no-restore", "-m:1", "-p:CopyLocalLockFileAssemblies=false", "--nologo"], "CISTM003: Type Manager requires");
        foreach (string property in new[] { "PublishSingleFile", "PublishTrimmed", "PublishAot", "SelfContained", "PublishSelfContained" })
        { await Reject("unsupported-" + property, ["msbuild", HostProject, "-t:CisValidateTypeManagerProfile", "-p:" + property + "=true", "-nologo"], "CISTM003"); }
        foreach (string rid in new[] { "win-arm64", "linux-arm64", "osx-x64" })
        { await Reject("unsupported-" + rid, ["msbuild", HostProject, "-t:CisValidateTypeManagerProfile", "-p:RuntimeIdentifier=" + rid, "-nologo"], "CISTM004"); }
        await Reject("disabled-current-rid", ["msbuild", HostProject, "-t:CisValidateTypeManagerProfile", "-p:UseCurrentRuntimeIdentifier=false", "-nologo"], "CISTM004");
        await Reject("library-host", ["msbuild", HostProject, "-t:CisValidateTypeManagerProfile", "-p:OutputType=Library", "-nologo"], "CISTM002");
        string unsupportedPublish = Path.Combine(input.Output, "unsupported-publish");
        Clear(unsupportedPublish);
        await Run("unsupported-no-build-publish", ["publish", HostProject, "-c", "Release", "--no-build", "--no-restore", "-p:PublishSingleFile=true", "-o", unsupportedPublish, "--nologo"], "CISTM003");
        Require(!File.Exists(Path.Combine(unsupportedPublish, ManifestName)), "Unsupported publish generated an inventory.");
        Passed("unsupported-no-build-publish");
        Clear(hostDirectory);
        await Run("design-time", ["msbuild", HostProject, "-t:CisWriteBuildTypeInventory", "-p:Configuration=Release", "-p:DesignTimeBuild=true", "-nologo"]);
        Require(!File.Exists(manifest), "Design-time build generated an inventory.");
        Passed("design-time");
        await Run("after-design-time", ["build", HostProject, "-c", "Release", "--no-restore", "-m:1", "--nologo"]);
        Check("after-design-time", hostDirectory);

        string custom = Path.Combine(input.Output, "custom-host-output");
        Clear(custom);
        await Run("custom-host-output", ["build", HostProject, "-c", "Release", "--no-restore", "-m:1", "-o", custom, "--nologo"]);
        Check("custom-host-output", custom);
        string dependencies = File.ReadAllText(Path.Combine(custom, HostName + ".deps.json"));
        Require(!File.Exists(Path.Combine(custom, "CIS.TypeManager.Build.dll")) && !dependencies.Contains("CIS.TypeManager.Build", StringComparison.Ordinal) && !dependencies.Contains("Microsoft.Build", StringComparison.Ordinal), "Build tooling leaked into runtime dependencies.");
        Passed("build-tool-runtime-isolation");
        const string renamed = "CIS.TypeManager.RenamedHost";
        Clear(hostDirectory, renamed + ".types.json");
        await Run("renamed-host", ["build", HostProject, "-c", "Release", "--no-restore", "-m:1", "-p:TypeManagerFixtureHostName=" + renamed, "--nologo"]);
        Require((string?)Read(Path.Combine(hostDirectory, renamed + ".types.json"))["hostAssembly"] == AssemblyName.GetAssemblyName(Path.Combine(hostDirectory, renamed + ".dll")).FullName, "Renamed host inventory identity is stale.");
        Passed("renamed-host");
        try
        {
            Clear(hostDirectory);
            await Run("remove-reference", ["build", HostProject, "-c", "Release", "-m:1", "-p:ExcludeUnusedFixture=true", "--nologo"]);
            Check("remove-reference", hostDirectory, 4);
        }
        finally
        {
            Clear(hostDirectory);
            await Run("restore-reference", ["build", HostProject, "-c", "Release", "-m:1", "--nologo"]);
        }
        Check("restore-reference", hostDirectory);
        string asset = Path.Combine(input.Language, "tests/fixtures/TypeManager/Contracts/bin/Release/net10.0/CIS.TypeManager.Fixture.Contracts.dll");
        Clear(hostDirectory);
        await Run("explicit-duplicate", ["build", HostProject, "-c", "Release", "--no-restore", "-m:1", "-p:ExtraDiscoveryAsset=" + asset, "--nologo"]);
        Check("explicit-duplicate", hostDirectory);
        string conflict = Path.Combine(input.Output, "ConflictingIdentity.dll");
        Copy(asset, conflict);
        await Reject("identity-conflict", ["build", HostProject, "-c", "Release", "--no-restore", "-m:1", "-p:ExtraDiscoveryAsset=" + conflict, "--nologo"], "CISTM001: Conflicting assembly selection");
        string invalid = Path.Combine(input.Output, "InvalidManagedAsset.dll");
        Write(invalid, "This explicitly selected assembly is invalid.");
        await Reject("invalid-managed-asset", ["build", HostProject, "-c", "Release", "--no-restore", "-m:1", "-p:ExtraDiscoveryAsset=" + invalid, "--nologo"], "CISTM001: Selected managed asset has invalid executable metadata: InvalidManagedAsset.dll");
        string reference = Path.Combine(input.Output, "ReferenceOnly.dll");
        Copy(Path.Combine(input.Language, "tests/fixtures/TypeManager/Contracts/obj/Release/net10.0/ref/CIS.TypeManager.Fixture.Contracts.dll"), reference);
        await Reject("reference-assembly", ["build", HostProject, "-c", "Release", "--no-restore", "-m:1", "-p:ExtraDiscoveryAsset=" + reference, "--nologo"], "CISTM001: Selected runtime asset is a reference assembly:");
        Clear(hostDirectory);
        await Run("final-build", ["build", HostProject, "-c", "Release", "--no-restore", "-m:1", "--nologo"]);
        Check("final-build", hostDirectory);
        await Run("clean", ["clean", HostProject, "-c", "Release", "-m:1", "--nologo"]);
        Require(!File.Exists(manifest), "Clean left the generated build manifest behind.");
        Passed("clean");
        await Run("rebuild", ["build", HostProject, "-c", "Release", "--no-restore", "-m:1", "--nologo"]);
        Check("rebuild", hostDirectory);
        Require(results.Count == 32, "Unexpected source inventory check count.");
        Write(reportPath, results.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("Type Manager inventory: 32 checks passed. Evidence: " + input.Output);
        return 0;
    }

    internal static void ValidateInventory(JsonObject inventory, int count, Func<string, string> deployedIdentity)
    {
        string[] names = ["CIS.Core", HostName, "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Logging.Abstractions"];
        if (count == 7) { names = [.. names, "CIS.TypeManager.Fixture.Contracts", "CIS.TypeManager.Fixture.Transitive", "CIS.TypeManager.Fixture.Unused"]; }
        JsonArray entries = inventory["assemblies"]!.AsArray();
        string[] identities = entries.Select(row => (string)row!["assemblyName"]!).ToArray();
        Require((int?)inventory["schemaVersion"] == 1 && entries.Count == count && identities.Select(id => new AssemblyName(id).Name!).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal)), "Wrong selected inventory set.");
        Require((string?)inventory["hostAssembly"] == deployedIdentity(HostName + ".dll"), "Wrong host identity.");
        Require(identities.SequenceEqual(identities.Order(StringComparer.Ordinal)), "Nondeterministic inventory order.");
        foreach (JsonNode? entry in entries)
        { Require((string?)entry!["assemblyName"] == deployedIdentity((string)entry["relativePath"]!), "Wrong deployed identity."); }
    }
}
