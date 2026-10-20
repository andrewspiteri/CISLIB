using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

if (args.Length is 3 or 4 && args[0] == "--prepare-helper-coverage")
{
    string language = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
    if (Directory.Exists(output)) { throw new IOException("Use a new immutable helper coverage input directory."); }
    Directory.CreateDirectory(output);
    Directory.CreateDirectory(Path.Combine(output, "inputs"));
    string repo = Path.GetFullPath(Path.Combine(language, "../.."));
    using var summary = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo, ".artifacts/csharp/verification/Developer/summary.json")));
    if (summary.RootElement.GetProperty("exitCode").GetInt32() != 0) { throw new InvalidDataException("A successful Developer checkpoint is required."); }
    string inspection = Path.GetFullPath(summary.RootElement.GetProperty("architectureInspection").GetProperty("path").GetString()!, Path.Combine(repo, ".artifacts/csharp/verification/Developer"));
    using var identity = JsonDocument.Parse(File.ReadAllBytes(inspection));
    string package = Path.GetFullPath(identity.RootElement.GetProperty("package").GetProperty("path").GetString()!, repo);
    string packagePin = identity.RootElement.GetProperty("package").GetProperty("sha256").GetString()!;
    if (Hash(package) != packagePin) { throw new InvalidDataException("Package no longer matches its Developer checkpoint."); }
    string helper = Path.Combine(language, "eng/CIS.TypeManager.Build/bin/Release/net10.0/CIS.TypeManager.Build.dll");
    using (var zip = ZipFile.OpenRead(package))
    using (var stream = zip.GetEntry("tools/net10.0/CIS.TypeManager.Build.dll")!.Open())
    {
        if (Convert.ToHexStringLower(SHA256.HashData(stream)) != Hash(helper)) { throw new InvalidDataException("Helper differs from the package."); }
    }
    var files = new Dictionary<string, string>
    {
        [helper] = "inputs/CIS.TypeManager.Build.dll",
        [Path.ChangeExtension(helper, ".pdb")] = "inputs/CIS.TypeManager.Build.pdb",
        [package] = "inputs/candidate.nupkg",
        [Path.Combine(language, "eng/CIS.TypeManager.Build/obj/Release/net10.0/ref/CIS.TypeManager.Build.dll")] = "inputs/reference.dll",
        [args.Length == 4 ? Path.GetFullPath(args[3]) : Path.Combine(language, "verification/mutation/build-helper/WriteTypeInventoryTests.cs")] = "WriteTypeInventoryTests.cs",
        [Path.Combine(language, "tests/TestAssembly.cs")] = "TestAssembly.cs",
        [Path.Combine(language, "verification/coverage/shipping.runsettings")] = "inputs/shipping.runsettings",
        [Path.Combine(language, "Directory.Packages.props")] = "inputs/Directory.Packages.props",
        [Path.Combine(language, "global.json")] = "global.json",
        [Path.Combine(language, "NuGet.config")] = "NuGet.config"
    };
    var pins = files.Select(pair =>
    {
        string copied = Path.Combine(output, pair.Value);
        File.Copy(pair.Key, copied);
        return new { original = pair.Key, copied, sha256 = Hash(copied) };
    }).ToArray();
    var versions = XDocument.Load(Path.Combine(language, "Directory.Packages.props")).Descendants("PackageVersion")
        .ToDictionary(e => (string)e.Attribute("Include")!, e => (string)e.Attribute("Version")!);
    var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
        new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("RuntimeFrameworkVersion", "10.0.12"),
            new XElement("RollForward", "Disable"), new XElement("ImplicitUsings", "enable"), new XElement("Nullable", "enable"),
            new XElement("IsTestProject", "true"), new XElement("UseVSTest", "true"), new XElement("EnableMSTestRunner", "false"),
            new XElement("EnableDefaultCompileItems", "false"), new XElement("DefineConstants", "CIS_MUTATION_TESTS"), new XElement("NuGetAudit", "false")),
        new XElement("ItemGroup", new[] { "WriteTypeInventoryTests.cs", "TestAssembly.cs" }.Select(name => new XElement("Compile", new XAttribute("Include", name))),
            new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestFramework", "MSTest.TestAdapter" }.Select(name => new XElement("PackageReference", new XAttribute("Include", name), new XAttribute("Version", versions[name]))),
            new XElement("Reference", new XAttribute("Include", "CIS.TypeManager.Build"), new XAttribute("HintPath", "inputs/CIS.TypeManager.Build.dll"), new XAttribute("Private", "true")),
            new[] { "Microsoft.Build.Framework", "Microsoft.Build.Utilities.Core", "Microsoft.NET.StringTools" }.Select(name => new XElement("Reference", new XAttribute("Include", name), new XAttribute("HintPath", "$(MSBuildBinPath)/" + name + ".dll"), new XAttribute("Private", "true")))),
        new XElement("Target", new XAttribute("Name", "ReferenceFixture"), new XAttribute("AfterTargets", "Build"),
            new XElement("WriteLinesToFile", new XAttribute("File", "$(TargetDir)reference-path.txt"), new XAttribute("Lines", "$(MSBuildProjectDirectory)/inputs/reference.dll"), new XAttribute("Overwrite", "true"))));
    File.WriteAllText(Path.Combine(output, "HelperCoverage.csproj"), project.ToString());
    File.WriteAllText(Path.Combine(output, "inputs.json"), JsonSerializer.Serialize(new { packageSha256 = packagePin, helperSha256 = Hash(helper), pins }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(Path.Combine(output, "HelperCoverage.csproj"));
    return;
}

if (args is ["--verify-helper-coverage", string root])
{
    using var inputs = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "inputs.json")));
    foreach (var pin in inputs.RootElement.GetProperty("pins").EnumerateArray())
    {
        string hash = pin.GetProperty("sha256").GetString()!;
        if (Hash(pin.GetProperty("original").GetString()!) != hash || Hash(pin.GetProperty("copied").GetString()!) != hash)
        { throw new InvalidDataException("Helper coverage input changed."); }
    }
    string binary = Path.Combine(root, "bin/Release/net10.0/CIS.TypeManager.Build.dll");
    if (Hash(binary) != inputs.RootElement.GetProperty("helperSha256").GetString()) { throw new InvalidDataException("Test host did not use the packaged helper."); }
    Console.WriteLine("Exact candidate/helper and original/copied inputs verified.");
    return;
}

if (args is ["--compare-shipping", string inventoryPath, string repository])
{
    using var inventory = JsonDocument.Parse(File.ReadAllBytes(inventoryPath));
    string[] roots = ["implementations/csharp/src/CIS.Core/", "implementations/csharp/eng/CIS.TypeManager.Build/"];
    bool Selected(string path) => roots.Any(root => path.StartsWith(root, StringComparison.Ordinal)) &&
        (path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".csproj", StringComparison.Ordinal));
    var pins = inventory.RootElement.EnumerateArray().Where(p => Selected(p.GetProperty("path").GetString()!))
        .ToDictionary(p => p.GetProperty("path").GetString()!, p => p.GetProperty("sha256").GetString()!, StringComparer.Ordinal);
    var actual = roots.SelectMany(root => Directory.EnumerateFiles(Path.Combine(repository, root), "*", SearchOption.AllDirectories))
        .Where(file => !Path.GetRelativePath(repository, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
        .Select(file => Path.GetRelativePath(repository, file).Replace('\\', '/')).Where(Selected).ToHashSet(StringComparer.Ordinal);
    if (!actual.SetEquals(pins.Keys)) { throw new InvalidDataException("Shipping source/project membership changed."); }
    foreach (var pin in pins)
    { if (Hash(Path.Combine(repository, pin.Key)) != pin.Value) { throw new InvalidDataException("Shipping source/project bytes changed: " + pin.Key); } }
    Console.WriteLine(JsonSerializer.Serialize(new { matched = true, inventorySha256 = Hash(inventoryPath), sourceAndProjectFiles = pins.Count,
        scope = "Both shipping assemblies: all handwritten C# source and project files match the preserved mutation input inventory byte for byte." }));
    return;
}

if (args.Length != 1) { throw new ArgumentException("Expected a native log or final native mutation report."); }
string path = Path.GetFullPath(args[0]);
object result;
if (Path.GetExtension(path) == ".json")
{
    using var report = JsonDocument.Parse(File.ReadAllBytes(path));
    var files = report.RootElement.GetProperty("files").EnumerateObject().Select(file =>
    {
        var mutants = file.Value.GetProperty("mutants").EnumerateArray().ToArray();
        return new { file = file.Name, total = mutants.Length, counts = mutants.GroupBy(m => m.GetProperty("status").GetString()!).ToDictionary(g => g.Key, g => g.Count()) };
    }).ToArray();
    var counts = files.SelectMany(f => f.counts).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
    long denominator = new[] { "Killed", "Survived", "NoCoverage", "Timeout" }.Sum(key => (long)counts.GetValueOrDefault(key));
    result = new { path, sourceFiles = files.Length, total = files.Sum(f => f.total), counts,
        strictScore = denominator == 0 ? (decimal?)null : 100m * counts.GetValueOrDefault("Killed") / denominator,
        files = files.OrderByDescending(f => f.counts.GetValueOrDefault("Survived") + f.counts.GetValueOrDefault("NoCoverage") + f.counts.GetValueOrDefault("Timeout")) };
}
else
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(stream);
    string[] lines = reader.ReadToEnd().Split('\n');
    var groups = lines.Select(line => Regex.Match(line, "Test run for \\\"(?<mutants>.+)\\\" is \\\"(?<outcome>[^\\\"]+)\\\""))
        .Where(match => match.Success).ToArray();
    var tested = groups.SelectMany(match => Regex.Matches(match.Groups["mutants"].Value, @"(?:^|,\s*)(\d+):")
        .Select(id => int.Parse(id.Groups[1].Value))).Distinct().Order().ToArray();
    result = new { path, lastWriteUtc = File.GetLastWriteTimeUtc(path), completedGroups = groups.Length,
        testedDistinctMutantIds = tested.Length, outcomes = groups.GroupBy(m => m.Groups["outcome"].Value).ToDictionary(g => g.Key, g => g.Count()),
        latest = lines.LastOrDefault(line => line.Contains("Test run for", StringComparison.Ordinal)),
        note = "Progress observations only. Compound groups, retries and tool exclusions prevent treating this count as a final score." };
}
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
