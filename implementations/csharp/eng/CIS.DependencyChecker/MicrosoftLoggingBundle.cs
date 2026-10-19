using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// An index of current products and existing evidence, always an incomplete release draft.
internal static class MicrosoftLoggingBundle
{
    private static readonly string[] Areas = ["Product", "Source", "Tests", "Contracts", "History", "Examples", "Tooling", "Evidence", "Integrity"];
    internal static async Task<int> RunAsync(string[] args)
    {
        var inputs = new TypeManagerVerificationInputs(args, ["repo-root", "package-path", "output-root", "registration-path"], []);
        JsonObject manifest = await WriteAsync(inputs.PathOf("repo-root"), inputs.PathOf("package-path"), inputs.Output, inputs.PathOf("registration-path"));
        Console.WriteLine("Provider bundle draft indexed " + manifest["files"]!.AsArray().Count + " files. Release qualification remains pending. Manifest: " + Path.Combine(inputs.Output, "manifest.json"));
        return 0;
    }

    internal static async Task<JsonObject> WriteAsync(string repo, string package, string output, string registration)
    {
        repo = Path.GetFullPath(repo); output = Path.GetFullPath(output);
        Require(!File.Exists(output) && !Directory.Exists(output), "Use a new manifest output directory.");
        string Own(string path) => SourceReplay.InputPath(repo, Path.GetRelativePath(repo, Path.GetFullPath(path, repo)));
        registration = Own(registration);
        JsonObject policy = Read(registration, out string registrationHash);
        Require((string?)policy["schema"] == "cis.csharp.verification-bundle-registration/v1" && (string?)policy["module"]?["id"] == "logging-microsoft", "Unsupported bundle registration.");
        string[] required = Strings(policy["requiredReleaseAreas"]);
        Require(required.Order(StringComparer.Ordinal).SequenceEqual(Areas.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Bundle must preserve all nine release content areas.");
        string[] pending = Strings(policy["pendingReleaseRequirements"]);
        Require(pending.Length > 0, "Preserve pending release requirements.");
        JsonObject module = policy["module"]!.AsObject();
        string[] apiMembers = Strings(module["apiMembers"]);
        Require(apiMembers.Length > 0 && apiMembers.Distinct(StringComparer.Ordinal).Count() == apiMembers.Length, "Expected distinct registered provider API members.");
        var files = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        void Add(string path, string role, string? expectedHash = null)
        {
            string absolute = Own(path), relative = Path.GetRelativePath(repo, absolute).Replace('\\', '/');
            Require(File.Exists(absolute), "Missing required inventory input: " + relative);
            byte[] bytes = File.ReadAllBytes(absolute);
            string hash = Digest(bytes);
            Require(expectedHash is null || hash == expectedHash, "Inventory reference differs from its recorded hash: " + relative);
            if (files.TryGetValue(relative, out JsonObject? previous))
            { Require((string?)previous["sha256"] == hash, "Indexed input changed: " + relative); return; }
            files.Add(relative, new JsonObject { ["path"] = relative, ["bytes"] = bytes.LongLength, ["sha256"] = hash, ["role"] = role });
        }
        Add(registration, "BundleRegistration", registrationHash);
        foreach (string producer in new[]
        {
            "implementations/csharp/eng/write-microsoft-logging-bundle.ps1",
            "implementations/csharp/eng/CIS.DependencyChecker/MicrosoftLoggingBundle.cs",
            "implementations/csharp/eng/CIS.DependencyChecker/SourceReplay.cs",
            "implementations/csharp/eng/CIS.DependencyChecker/TypeManagerVerificationInputs.cs",
            "implementations/csharp/eng/CIS.DependencyChecker/ToolchainInputs.cs",
            "implementations/csharp/eng/CIS.DependencyChecker/Program.cs"
        }) { Add(producer, "ManifestProducer"); }
        foreach (string relative in Strings(module["documentation"]).Concat(Strings(module["requiredFiles"])))
        { Add(relative, "ModuleDocumentationSourceOrInventory"); }
        foreach (JsonNode? source in module["sourceRoots"]!.AsArray())
        {
            string relative = Text(source!, "path"), root = Own(relative);
            Require(Directory.Exists(root), "Missing module source root: " + relative);
            ProcessResult git = await Git(repo, "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", relative);
            string[] members = git.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Where(path => File.Exists(Own(path))).ToArray();
            Require(members.Length > 0, "Empty module source root: " + relative);
            foreach (string member in members) { Add(member, Text(source!, "role")); }
        }
        var groups = new JsonArray();
        foreach (string relative in Strings(module["priorEvidenceRoots"]))
        {
            string root = Own(relative);
            Require(Directory.Exists(root), "Missing required provider evidence root: " + relative);
            string[] members = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            Require(members.Length > 0, "Empty provider evidence root: " + relative);
            foreach (string member in members) { Add(member, "PriorDevelopmentEvidenceOnly"); }
            groups.Add(new JsonObject { ["root"] = relative, ["files"] = members.Length, ["candidateQualification"] = "Not credited to selected candidate; preserve original reports and native gate status." });
        }
        JsonObject native = module["nativeMutation"]!.AsObject();
        string auditPath = Own(Text(native, "audit"));
        JsonObject mutationAudit = Read(auditPath, out string auditHash);
        Add(auditPath, "PriorDevelopmentEvidenceOnly", auditHash);
        string expectedMutation = Text(mutationAudit[Text(native, "run")]!, "nativeReportSha256");
        Add(Text(native, "json"), "LocalNativeMutationReportNotReleaseRetained", expectedMutation);
        Add(Text(native, "html"), "LocalNativeMutationReportNotReleaseRetained");
        string developer = Own(".artifacts/csharp/verification/Developer");
        JsonObject summary = Read(Path.Combine(developer, "summary.json"), out string summaryHash);
        Require((int?)summary["exitCode"] == 0 && (string?)summary["profile"] == "Developer" && summary["finishedUtc"] is not null &&
            summary["issues"]!.AsArray().Count == 0, "Require a terminal passing Developer observation for this inventory checkpoint.");
        Add(Path.Combine(developer, "summary.json"), "CurrentDeveloperProfileOnly", summaryHash);
        foreach (string name in new[] { "source-inputs.json", "validation.json" }) { Add(Path.Combine(developer, name), "CurrentDeveloperProfileOnly"); }
        if (summary["source"]?["inputInventory"] is { } sourceReference)
        { Add(Path.Combine(developer, Text(sourceReference, "path")), "CurrentDeveloperProfileOnly", Text(sourceReference, "sha256")); }
        foreach (JsonNode? family in summary["families"]!.AsArray())
        {
            foreach (string slot in new[] { "discovery", "execution", "report" })
            {
                if (family![slot] is { } reference)
                { Add(Path.Combine(developer, Text(reference, "path")), "CurrentDeveloperProfileOnly", Text(reference, "sha256")); }
            }
        }

        byte[] packageBytes = File.ReadAllBytes(package);
        string packageHash = Digest(packageBytes);
        byte[] core, xmlBytes;
        string version;
        string? packageCommit;
        using (var stream = new MemoryStream(packageBytes))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            Require(archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == archive.Entries.Count, "Ambiguous duplicate package entries.");
            byte[] Entry(string name)
            {
                ZipArchiveEntry? entry = archive.GetEntry(name);
                Require(entry is not null, "Required package entry missing: " + name);
                using Stream input = entry!.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray();
            }
            XDocument nuspec = ToolchainInputs.Xml(Entry("CIS.Core.nuspec"));
            XElement metadata = nuspec.Descendants().Single(element => element.Name.LocalName == "metadata");
            string id = metadata.Elements().Single(element => element.Name.LocalName == "id").Value;
            Require(id == "CIS.Core", "Selected package identity must be CIS.Core.");
            version = metadata.Elements().Single(element => element.Name.LocalName == "version").Value;
            Require(!string.IsNullOrWhiteSpace(version), "Selected package version is missing.");
            packageCommit = (string?)metadata.Elements().SingleOrDefault(element => element.Name.LocalName == "repository")?.Attribute("commit");
            core = Entry("lib/net10.0/CIS.Core.dll"); xmlBytes = Entry("lib/net10.0/CIS.Core.xml");
            string[] actualMembers = ToolchainInputs.Xml(xmlBytes).Descendants("member").Select(element => (string?)element.Attribute("name") ?? "").ToArray();
            Require(apiMembers.All(member => actualMembers.Contains(member, StringComparer.Ordinal)), "Provider API documentation missing.");
        }
        string built = Own("implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll"), coreHash = Digest(core);
        Require(coreHash == Hash(built), "Selected package Core differs from current Release build output.");
        ProcessResult head = await Git(repo, "rev-parse", "HEAD"), tree = await Git(repo, "rev-parse", "HEAD^{tree}");
        foreach (JsonObject file in files.Values)
        {
            string path = Own(Text(file, "path"));
            Require(Hash(path) == (string?)file["sha256"] && new FileInfo(path).Length == (long?)file["bytes"], "Indexed input changed before manifest creation.");
        }
        Directory.CreateDirectory(Path.Combine(output, "products"));
        string retained = "products/" + Path.GetFileName(package);
        File.WriteAllBytes(Path.Combine(output, retained), packageBytes);
        File.WriteAllBytes(Path.Combine(output, "products/CIS.Core.dll"), core);
        File.WriteAllBytes(Path.Combine(output, "products/CIS.Core.xml"), xmlBytes);
        Require(Hash(Path.Combine(output, retained)) == packageHash && Hash(Path.Combine(output, "products/CIS.Core.dll")) == coreHash &&
            Hash(Path.Combine(output, "products/CIS.Core.xml")) == Digest(xmlBytes), "Retained product bytes differ.");
        var product = new JsonObject
        {
            ["comparedBuildOutput"] = new JsonObject { ["path"] = Path.GetRelativePath(repo, built).Replace('\\', '/'), ["sha256"] = coreHash, ["qualification"] = "Current file comparison only; not cryptographically tied to Developer run." },
            ["packageSourceCommit"] = packageCommit,
            ["packageId"] = "CIS.Core",
            ["packageVersion"] = version,
            ["sourcePackage"] = Path.GetFullPath(package),
            ["retainedPackage"] = retained,
            ["packageSha256"] = packageHash,
            ["assembly"] = "products/CIS.Core.dll",
            ["assemblySha256"] = coreHash,
            ["apiDocumentation"] = "products/CIS.Core.xml",
            ["apiDocumentationSha256"] = Digest(xmlBytes),
            ["providerApiMembers"] = JsonSerializer.SerializeToNode(apiMembers)
        };
        var manifest = new JsonObject
        {
            ["schema"] = "cis.csharp.verification-bundle-draft/v1",
            ["state"] = "IncompleteReleaseDraft",
            ["releaseQualified"] = false,
            ["module"] = "logging-microsoft",
            ["product"] = product,
            ["source"] = new JsonObject { ["headCommit"] = head.StandardOutput.Trim(), ["headTree"] = tree.StandardOutput.Trim(), ["qualification"] = "Head identity plus individually hashed working inputs; not an exact complete release source snapshot." },
            ["developer"] = new JsonObject { ["runId"] = summary["runId"]!.DeepClone(), ["sourceCommit"] = summary["source"]?["commit"]?.DeepClone(), ["profile"] = summary["profile"]!.DeepClone(), ["lane"] = summary["lane"]!.DeepClone(), ["scope"] = "Developer profile only; full candidate-package suites remain required." },
            ["reportGroups"] = groups,
            ["files"] = new JsonArray(files.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => (JsonNode)item.Value).ToArray()),
            ["pendingReleaseRequirements"] = JsonSerializer.SerializeToNode(pending),
            ["releaseAreas"] = new JsonArray(Areas.Select(area => (JsonNode)new JsonObject { ["area"] = area, ["fullReleaseState"] = "Pending" }).ToArray()),
            ["retention"] = "File entries reference existing inputs; this command does not materialize a complete bundle. Local-only native reports, all final candidate evidence and durable prerequisites must be retained before release."
        };
        string manifestPath = Path.Combine(output, "manifest.json");
        WriteJson(manifestPath, manifest);
        Write(Path.Combine(output, "manifest.sha256"), Hash(manifestPath) + "\n");
        return manifest;
    }
    private static string Text(JsonNode value, string name) => (string?)value[name] is { Length: > 0 } text ? text : throw new InvalidDataException("Missing " + name + ".");
    private static string[] Strings(JsonNode? value)
    {
        Require(value is JsonArray, "Expected registered string inventory.");
        string[] strings = value!.AsArray().Select(item => item?.GetValue<string>() ?? "").ToArray();
        Require(strings.All(item => !string.IsNullOrWhiteSpace(item)), "Empty registration member.");
        return strings;
    }
    private static async Task<ProcessResult> Git(string repo, params string[] args)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", ["-C", repo, .. args], repo, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
        Require(result.ExitCode == 0, "Cannot enumerate or record repository source identity: " + result.StandardError);
        return result;
    }
}

