using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static class TypeManagerInventoryVerification
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["repo-root", "output-root", "native-windows", "native-linux"], ["build-targets", "host-sdk", "configuration"]);
        Require(RuntimeInformation.ProcessArchitecture == Architecture.X64 && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()), "Supported inventory lanes are Windows x64 and Linux x64.");
        string hostSdk = input.Optional("host-sdk") ?? "console", configuration = input.Optional("configuration") ?? "Release";
        Require(hostSdk is "console" or "web" && configuration is "Debug" or "Release", "Use console/web and Debug/Release.");
        string windows = input.PathOf("native-windows"), linux = input.PathOf("native-linux"); _ = Hash(windows); _ = Hash(linux);
        input.EmptyOutput(); string root = input.Output, workspace = Path.Combine(root, "workspace"), language = input.Language;
        string native = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", cross = native == "win-x64" ? "linux-x64" : "win-x64";
        var report = new JsonObject
        {
            ["complete"] = false,
            ["hostSdk"] = hostSdk,
            ["configuration"] = configuration,
            ["nativeRid"] = native,
            ["verifierSha256"] = Hash(typeof(TypeManagerInventoryVerification).Assembly.Location),
            ["nativeWindowsSha256"] = Hash(windows),
            ["nativeLinuxSha256"] = Hash(linux),
            ["publishScope"] = "dotnet CLI with explicit -o; PublishUrl/Visual Studio profile copy is not qualified",
            ["results"] = new JsonArray(),
            ["inputs"] = new JsonObject()
        };
        void Save() => WriteJson(Path.Combine(root, "results.json"), report); Save();
        void Put(string relative, string text) => Write(Path.Combine(workspace, relative), text);
        void CopyInput(string source, string relative)
        { Copy(source, Path.Combine(workspace, relative)); report["inputs"]![relative] = Hash(source); }
        try
        {
            foreach (string file in new[] { "global.json", "Directory.Build.props", "build/type-manager/CIS.TypeManager.targets", "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj", "eng/CIS.TypeManager.Build/WriteTypeInventory.cs" })
            { CopyInput(Path.Combine(language, file), file); }
            if (input.Optional("build-targets") is not null) { CopyInput(input.PathOf("build-targets"), "build/type-manager/CIS.TypeManager.targets"); }
            foreach (string fixture in new[] { "Contracts", "Transitive", "Unused" })
            {
                foreach (string file in Directory.GetFiles(Path.Combine(language, "tests", "fixtures", "TypeManager", fixture)).Where(p => Path.GetExtension(p) is ".cs" or ".csproj" or ".resx"))
                { CopyInput(file, "fixtures/" + fixture + "/" + Path.GetFileName(file)); }
            }
            Put("asset/Asset.csproj", """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><AssemblyName>CIS.TypeManager.RidAsset</AssemblyName><DefineConstants>$(DefineConstants);$(FixtureVariant)</DefineConstants></PropertyGroup></Project>
                """);
            Put("asset/Variant.cs", """
                namespace InventoryFixture;
                public static class Variant
                {
                #if WINDOWS_ASSET
                    public static string Value => "win-x64";
                #elif LINUX_ASSET
                    public static string Value => "linux-x64";
                #else
                    public static string Value => "neutral";
                #endif
                }
                """);
            foreach ((string variant, string define) in new[] { ("neutral", "NEUTRAL_ASSET"), ("win-x64", "WINDOWS_ASSET"), ("linux-x64", "LINUX_ASSET") })
            { await input.Run("compile-" + variant, ["build", "asset/Asset.csproj", "-c", "Release", "-m:1", "--nologo", "-p:FixtureVariant=" + define, "-o", Path.Combine(root, "variants", variant)], workspace); }
            string feed = Path.Combine(root, "feed"); Directory.CreateDirectory(feed);
            string package = Path.Combine(feed, "CIS.TypeManager.RidFixture.1.0.0.nupkg");
            using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("CIS.TypeManager.RidFixture.nuspec").Open()))
                { writer.Write("""<package><metadata><id>CIS.TypeManager.RidFixture</id><version>1.0.0</version><authors>CIS inventory tests</authors><description>Local verification-only fixture.</description><dependencies><group targetFramework="net10.0" /></dependencies></metadata></package>"""); }
                archive.CreateEntryFromFile(Path.Combine(workspace, "asset", "obj", "Release", "net10.0", "ref", "CIS.TypeManager.RidAsset.dll"), "ref/net10.0/CIS.TypeManager.RidAsset.dll");
                foreach (string variant in new[] { "neutral", "win-x64", "linux-x64" })
                { archive.CreateEntryFromFile(Path.Combine(root, "variants", variant, "CIS.TypeManager.RidAsset.dll"), (variant == "neutral" ? "lib/net10.0" : "runtimes/" + variant + "/lib/net10.0") + "/CIS.TypeManager.RidAsset.dll"); }
                archive.CreateEntryFromFile(windows, "runtimes/win-x64/native/cistm-native.dll");
                archive.CreateEntryFromFile(linux, "runtimes/linux-x64/native/libcistm-native.so");
                archive.CreateEntryFromFile(linux, "runtimes/linux-x64/native/cistm-native.dll");
            }
            string packageHash = Hash(package); report["packageSha256"] = packageHash;
            Put("NuGet.Config", new XElement("configuration", new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "fixture"), new XAttribute("value", feed))),
                new XElement("config", new XElement("add", new XAttribute("key", "globalPackagesFolder"), new XAttribute("value", Path.Combine(root, "packages", packageHash))))).ToString());
            Put("host/Host.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><OutputType>Exe</OutputType><AssemblyName>InventoryMatrixHost</AssemblyName><UseAppHost>false</UseAppHost><CisTypeManagerEnabled>true</CisTypeManagerEnabled></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../fixtures/Unused/Unused.csproj" />
                    <PackageReference Include="CIS.TypeManager.RidFixture" Version="1.0.0" />
                    <Content Include="decoy.txt" Link="content-decoy.dll" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
                  </ItemGroup>
                  <Import Project="../build/type-manager/CIS.TypeManager.targets" />
                  <Target Name="CaptureInventorySelections" AfterTargets="CisWriteBuildTypeInventory">
                    <WriteLinesToFile File="$(TargetDir)sdk-runtime-items.txt" Overwrite="true" Lines="@(ReferenceCopyLocalPaths->'%(Identity)|%(AssetType)|%(DestinationSubDirectory)|%(NuGetPackageId)|%(RuntimeIdentifier)')" />
                  </Target>
                </Project>
                """);
            if (hostSdk == "web")
            {
                string path = Path.Combine(workspace, "host", "Host.csproj"); Write(path, File.ReadAllText(path).Replace("Sdk=\"Microsoft.NET.Sdk\"", "Sdk=\"Microsoft.NET.Sdk.Web\"", StringComparison.Ordinal));
                Put("host/Properties/PublishProfiles/Folder.pubxml", "<Project><PropertyGroup><WebPublishMethod>FileSystem</WebPublishMethod><DeleteExistingFiles>false</DeleteExistingFiles></PropertyGroup></Project>");
            }
            Put("host/decoy.txt", "Ordinary content with a DLL destination is not a runtime asset.\n");
            Put("host/Program.cs", """
                using System.Reflection;
                using System.Text.Json;
                var initiallyLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "CIS.TypeManager.RidAsset");
                var value = Assembly.Load("CIS.TypeManager.RidAsset").GetType("InventoryFixture.Variant")!.GetProperty("Value")!.GetValue(null);
                Console.WriteLine(JsonSerializer.Serialize(new { initiallyLoaded, variant = value }));
                return initiallyLoaded ? 1 : 0;
                """);
            foreach (string rid in new[] { "default", native, cross })
            {
                string selectedRid = rid == "default" ? native : rid;
                string[] properties = rid == "default" ? [] : ["-p:RuntimeIdentifier=" + rid, "-p:SelfContained=false"];
                var evaluated = await input.Run("output-path-" + rid, ["msbuild", "host/Host.csproj", "-p:Configuration=" + configuration, "-getProperty:TargetDir", .. properties], workspace);
                string output = evaluated.StandardOutput.Trim(); Require(Path.IsPathFullyQualified(output), "MSBuild returned an invalid output directory.");
                DeletePrevious(Path.Combine(output, "InventoryMatrixHost.types.json"));
                var built = await input.Run("build-" + rid, ["build", "host/Host.csproj", "-c", configuration, "-m:1", "--nologo", .. properties], workspace, requireSuccess: false);
                if (built.ExitCode != 0) { report["results"]!.AsArray().Add(new JsonObject { ["case"] = rid, ["passed"] = false, ["phase"] = "build", ["exitCode"] = built.ExitCode }); Save(); continue; }
                try
                {
                    foreach (string mode in new[] { "build", "publish-no-build", "publish-build" })
                    {
                        string directory = mode == "build" ? output : Path.Combine(root, "published", rid, mode);
                        if (mode != "build")
                        {
                            DeletePrevious(Path.Combine(directory, "InventoryMatrixHost.types.json"));
                            var publish = new List<string> { "publish", "host/Host.csproj", "-c", configuration, "-m:1", "--nologo", "-o", directory };
                            publish.AddRange(properties); if (hostSdk == "web") { publish.Add("-p:PublishProfile=Folder"); }
                            if (mode == "publish-no-build") { publish.Add("--no-build"); }
                            await input.Run(mode + "-" + rid, publish, workspace);
                        }
                        string manifestPath = Path.Combine(directory, "InventoryMatrixHost.types.json"); Require(File.Exists(manifestPath), "The current command did not generate its inventory");
                        JsonObject inventory = Read(manifestPath); JsonArray members = inventory["assemblies"]!.AsArray();
                        var selected = members.ToDictionary(m => ((string)m!["assemblyName"]!).Split(',')[0], m => m!, StringComparer.Ordinal);
                        foreach (string file in new[] { selectedRid == "win-x64" ? "cistm-native.dll" : "libcistm-native.so", "cistm-native.dll", "fr/CIS.TypeManager.Fixture.Unused.resources.dll", "content-decoy.dll" })
                        { Require(File.Exists(Path.Combine(directory, file)), "The exclusion fixture was not deployed: " + file); }
                        string[] expected = ["InventoryMatrixHost", "CIS.TypeManager.RidAsset", "CIS.TypeManager.Fixture.Contracts", "CIS.TypeManager.Fixture.Transitive", "CIS.TypeManager.Fixture.Unused"];
                        Require(selected.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected) && members.Count == expected.Length, "Exact inventory membership/duplicates");
                        Require(inventory["schemaVersion"]!.GetValue<int>() == 1 && (string?)inventory["hostAssembly"] == (string?)selected["InventoryMatrixHost"]["assemblyName"], "Inventory schema/host identity");
                        string[] identities = members.Select(m => (string)m!["assemblyName"]!).ToArray(); Require(identities.SequenceEqual(identities.Order(StringComparer.Ordinal)), "Manifest order is not ordinal");
                        string chosen = Path.Combine(directory, (string)selected["CIS.TypeManager.RidAsset"]["relativePath"]!);
                        Require(Hash(chosen) == Hash(Path.Combine(root, "variants", selectedRid, "CIS.TypeManager.RidAsset.dll")), "Selected managed RID bytes");
                        JsonNode? observed = null;
                        if (selectedRid == native)
                        {
                            var child = await input.Run(mode + "-run-" + rid, [Path.Combine(directory, "InventoryMatrixHost.dll")], workspace);
                            Require(child.StandardError.Length == 0, "Inventory consumer wrote stderr"); observed = JsonNode.Parse(child.StandardOutput);
                            Require(JsonNode.DeepEquals(new JsonObject { ["initiallyLoaded"] = false, ["variant"] = native }, observed), "Actual native RID selection");
                        }
                        Copy(manifestPath, Path.Combine(root, mode + "-" + rid + ".types.json"));
                        report["results"]!.AsArray().Add(new JsonObject { ["case"] = rid, ["mode"] = mode, ["passed"] = true, ["assemblyCount"] = members.Count, ["runtime"] = observed }); Save();
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
                { report["results"]!.AsArray().Add(new JsonObject { ["case"] = rid, ["passed"] = false, ["error"] = error.Message }); Save(); }
            }
            report["sdk"] = (await input.Run("sdk-version", ["--version"], workspace)).StandardOutput.Trim();
            Require((string?)report["sdk"] == (string?)Read(Path.Combine(language, "global.json"))["sdk"]!["version"], "Inventory SDK differs from the pin");
            Require(Hash(windows) == (string?)report["nativeWindowsSha256"] && Hash(linux) == (string?)report["nativeLinuxSha256"], "Native fixture inputs changed");
            bool complete = report["results"]!.AsArray().Count == 9 && report["results"]!.AsArray().All(r => (bool?)r!["passed"] == true);
            report["complete"] = complete; Save(); return complete ? 0 : 1;
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { Save(); }
    }
    private static void DeletePrevious(string path)
    { try { File.Delete(path); } catch (DirectoryNotFoundException) { } }

}
