using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Records the exact restored package/tool inputs; this is local smoke evidence, not an offline kit.
internal static class ToolchainInputs
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var inputs = new TypeManagerVerificationInputs(args, ["run-root", "language-root", "cache", "powershell-version"], []);
        string language = inputs.PathOf("language-root");
        ProcessResult git = await ProcessRunner.RunAsync("git", ["--version"], language, TimeSpan.FromSeconds(30));
        Require(git.ExitCode == 0, "Cannot record Git version.");
        Record(inputs.PathOf("run-root"), language, inputs.PathOf("cache"), inputs.Get("powershell-version"), git.StandardOutput.Trim());
        return 0;
    }

    internal static void Record(string output, string language, string cache, string powershell, string git)
    {
        string target = Path.Combine(output, "prerequisites.json");
        Require(!File.Exists(target), "Retained prerequisites already exist.");
        string[] policyPaths = ["eng/toolchain.json", "eng/runtime-dependencies.json", "eng/development-dependencies.json"];
        string[] pins = policyPaths.Select(path => Hash(Path.Combine(language, path))).ToArray();
        JsonObject policy = ReadPinned(Path.Combine(language, policyPaths[0]), pins[0], "Toolchain policy changed.");
        JsonObject runtime = ReadPinned(Path.Combine(language, policyPaths[1]), pins[1], "Runtime policy changed.");
        JsonObject development = ReadPinned(Path.Combine(language, policyPaths[2]), pins[2], "Development policy changed.");
        JsonObject[] required = runtime["packages"]!.AsArray().Concat(development["packages"]!.AsArray()).Concat(policy["tools"]!.AsArray()).Select(item => item!.AsObject()).ToArray();
        JsonObject[] downloads = policy["frameworkDownloads"]?["packages"]?.AsArray().Select(item => item!.AsObject()).ToArray() ?? [];
        JsonObject[] approved = [.. required, .. downloads];
        Require(approved.Select(Identity).Distinct(StringComparer.Ordinal).Count() == approved.Length, "Duplicate approved package identity.");
        var restored = Directory.GetDirectories(cache).SelectMany(directory => Directory.GetDirectories(directory)
            .Select(version => Path.GetFileName(directory) + "/" + Path.GetFileName(version))).ToHashSet(StringComparer.Ordinal);
        Require(required.All(package => restored.Contains(Identity(package))) && restored.All(identity => approved.Any(package => Identity(package) == identity)),
            "Fresh cache differs from the approved package/tool/download inventory.");

        var packages = new JsonArray();
        foreach (JsonObject package in approved.Where(package => restored.Contains(Identity(package))))
        {
            string id = Text(package, "id"), version = Text(package, "version"), lower = id.ToLowerInvariant();
            string directory = Path.Combine(cache, lower, version), archive = Path.Combine(directory, lower + "." + version + ".nupkg");
            byte[] bytes = File.ReadAllBytes(archive);
            string hash = Digest(bytes), retained = "retained/sha256/" + hash;
            using var stream = new MemoryStream(bytes);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            byte[] nuspec = Entry(zip, lower + ".nuspec");
            string[] extractedNuspecs = Directory.GetFiles(directory).Where(path => Path.GetFileName(path).Equals(lower + ".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(extractedNuspecs.Length == 1 && File.ReadAllBytes(extractedNuspecs[0]).AsSpan().SequenceEqual(nuspec), "Extracted nuspec differs from its archive.");
            XElement metadata = Xml(nuspec).Root!.Elements().Single(item => item.Name.LocalName == "metadata");
            Require(Value(metadata, "id").Equals(id, StringComparison.OrdinalIgnoreCase) && Value(metadata, "version") == version, "NuGet metadata identity differs from its approved identity.");
            XElement license = metadata.Elements().Single(item => item.Name.LocalName == "license");
            string licenseType = (string?)license.Attribute("type") ?? "";
            Require(licenseType is "file" or "expression" && !string.IsNullOrWhiteSpace(license.Value), "Missing or unsupported NuGet licence.");
            Copy(archive, Path.Combine(output, retained, Path.GetFileName(archive)), hash);
            var entry = new JsonObject
            {
                ["id"] = id,
                ["version"] = version,
                ["authors"] = Value(metadata, "authors"),
                ["sha256"] = hash,
                ["retainedArchive"] = retained + "/" + Path.GetFileName(archive),
                ["source"] = Text(package, "provenance"),
                ["licenseType"] = licenseType,
                ["license"] = license.Value,
                ["role"] = downloads.Contains(package) ? "SDK implicit framework download; not a Core PackageReference" :
                    policy["tools"]!.AsArray().Any(tool => Identity(tool!.AsObject()) == Identity(package)) ? "development-only CLI" : "approved project package"
            };
            if (licenseType == "file")
            {
                string relative = Relative(license.Value);
                RetainEntry(zip, directory, output, retained, relative);
                entry["licenseSha256"] = Hash(Path.Combine(directory, relative));
                entry["retainedLicense"] = retained + "/" + relative;
            }
            JsonObject? tool = policy["tools"]!.AsArray().Select(item => item!.AsObject()).SingleOrDefault(item => Identity(item) == Identity(package));
            if (tool is not null)
            {
                string relative = "tools/net8.0/any/" + Path.GetFileNameWithoutExtension(Text(tool, "entryPoint")) + ".deps.json";
                RetainEntry(zip, directory, output, retained, relative);
                JsonObject deps = Read(Path.Combine(directory, relative), out string depsHash);
                entry["scope"] = "development-only CLI; embedded dependencies are never Core runtime dependencies";
                entry["embeddedDependencyManifestSha256"] = depsHash;
                entry["retainedDependencyManifest"] = retained + "/" + relative;
                entry["embeddedDependencies"] = new JsonArray(deps["libraries"]!.AsObject().OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => (JsonNode)new JsonObject { ["identity"] = item.Key, ["type"] = Text(item.Value!.AsObject(), "type") }).ToArray());
                entry["licenseReview"] = "Top-level NuGet licence recorded; complete embedded third-party notices review remains release retention work in WP07-S06.";
            }
            Require(Hash(archive) == hash, "NuGet archive changed while recording inputs.");
            packages.Add(entry);
        }
        for (int index = 0; index < pins.Length; index++) { Require(Hash(Path.Combine(language, policyPaths[index])) == pins[index], "Approved input policy changed."); }
        WriteJson(target, new JsonObject
        {
            ["sdk"] = Text(policy, "sdk"),
            ["targetRuntime"] = Text(policy, "targetRuntime"),
            ["toolHostRuntime"] = Text(policy, "toolHostRuntime"),
            ["powershellVersion"] = powershell,
            ["gitVersion"] = git,
            ["architecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["operatingSystem"] = RuntimeInformation.OSDescription,
            ["network"] = "HTTPS access to api.nuget.org for fresh replay; exact retained NuGet archives are included locally.",
            ["sdkRuntimeRetention"] = "Installed SDK/runtime versions are qualified; installer/distribution bytes are not retained by this smoke command. Immutable offline materialization remains WP07-S06.",
            ["retention"] = "Retain the entire run directory, including source snapshot, native reports/logs, commands.json, fresh caches and content-addressed archives. This is local evidence, not a published verification kit.",
            ["policyInputs"] = JsonSerializer.SerializeToNode(policyPaths.Select((path, index) => new SourceInventoryEntry(path, pins[index]))),
            ["packages"] = packages
        });
    }

    private static void RetainEntry(ZipArchive zip, string directory, string output, string retained, string relative)
    {
        relative = Relative(relative);
        byte[] bytes = Entry(zip, relative);
        string path = Path.Combine(directory, relative);
        Require(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "Extracted package input differs from its archive: " + relative);
        Copy(path, Path.Combine(output, retained, relative), Digest(bytes));
    }
    private static byte[] Entry(ZipArchive zip, string relative)
    {
        ZipArchiveEntry[] matches = zip.Entries.Where(item => item.FullName.Replace('\\', '/').Equals(relative, StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(matches.Length == 1, "Expected exactly one archive entry: " + relative);
        using Stream source = matches[0].Open(); using var result = new MemoryStream(); source.CopyTo(result); return result.ToArray();
    }
    internal static string Relative(string path)
    {
        string value = path.Replace('\\', '/');
        Require(!string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && !value.Contains(':') &&
            value.Split('/').All(part => part.Length > 0 && part is not "." and not ".."), "Package input path escapes its package.");
        return value;
    }
    internal static XDocument Xml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
    private static string Value(XElement element, string name) => element.Elements().Single(item => item.Name.LocalName == name).Value;
    internal static string Text(JsonObject value, string key) => (string?)value[key] is { Length: > 0 } text ? text : throw new InvalidDataException("Missing " + key + ".");
    private static string Identity(JsonObject package) => Relative(Text(package, "id").ToLowerInvariant()) + "/" + Relative(Text(package, "version"));
}
