using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;

if (args.Length != 4) { Console.Error.WriteLine("root probe-binaries sdk-msbuild new-output"); return 2; }
string root = Path.GetFullPath(args[0]), probe = Path.GetFullPath(args[1]), sdk = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
if (Directory.Exists(output)) { throw new InvalidOperationException("Use a new output directory."); }
Directory.CreateDirectory(output);
string Hash(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
void Write(string name, string text) => File.WriteAllText(Path.Combine(output, name), text, new System.Text.UTF8Encoding(false));
var observations = new JsonArray();
var report = new JsonObject { ["complete"] = false, ["fullShippingMatrixQualified"] = false,
    ["scope"] = "Known activation controls in real Core and MSBuild children; not a full child mutation matrix",
    ["verifierSha256"] = Hash(Assembly.GetExecutingAssembly().Location), ["msbuildSha256"] = Hash(sdk), ["runs"] = observations };
try
{
    var pins = new JsonObject();
    foreach (string name in new[] { "CIS.Core.dll", "CIS.TypeManager.Build.dll" })
    {
        string path = Path.Combine(root, "instrumented", name);
        using var pe = new PEReader(File.OpenRead(path)); MetadataReader metadata = pe.GetMetadataReader();
        if (!metadata.TypeDefinitions.Any(handle => metadata.StringComparer.Equals(metadata.GetTypeDefinition(handle).Name, "MutantControl")))
        { throw new InvalidDataException("Missing real Stryker instrumentation: " + name); }
        pins[name] = Hash(path);
    }
    report["instrumentedInputs"] = pins;
    string core = Path.Combine(output, "core"); Directory.CreateDirectory(core);
    foreach (string file in Directory.GetFiles(probe)) { File.Copy(file, Path.Combine(core, Path.GetFileName(file))); }
    File.Copy(Path.Combine(root, "instrumented/CIS.Core.dll"), Path.Combine(core, "CIS.Core.dll"), overwrite: true);
    foreach ((string label, int id) in new[] { ("baseline", -1), ("mutation", 1270), ("restored", -1) })
    {
        ProcessResult child = await Run("core-" + label, id, ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Path.Combine(core, "Probe.dll")]);
        if (id < 0)
        {
            JsonObject observed = JsonNode.Parse(child.StandardOutput)!.AsObject();
            Require(child.ExitCode == 0 && (int?)observed["processId"] == child.ProcessId && (string?)observed["coreSha256"] == (string?)pins["CIS.Core.dll"] &&
                (string?)observed["message"] == "value    42", "Core baseline/restoration mismatch.");
        }
        else { Require(child.ExitCode == 1 && child.StandardError.Contains("CORE-CHILD-ASSERT:", StringComparison.Ordinal), "Core child did not detect the alignment mutation by assertion."); }
    }

    string helper = Path.Combine(output, "helper"); Directory.CreateDirectory(helper);
    string task = Path.Combine(helper, "CIS.TypeManager.Build.dll"); File.Copy(Path.Combine(root, "instrumented/CIS.TypeManager.Build.dll"), task);
    foreach ((string label, int id) in new[] { ("baseline", -1), ("mutation", 7), ("restored", -1) })
    {
        string destination = Path.Combine(helper, label); Directory.CreateDirectory(destination);
        File.Copy(Path.Combine(probe, "CIS.Core.dll"), Path.Combine(destination, "Host.dll"));
        string project = Path.Combine(destination, "Inventory.proj");
        new XDocument(new XElement("Project", new XElement("UsingTask", new XAttribute("TaskName", "CIS.TypeManagement.Build.WriteTypeInventory"), new XAttribute("AssemblyFile", task)),
            new XElement("ItemGroup", new XElement("Asset", new XAttribute("Include", "Host.dll"), new XElement("RelativePath", "Host.dll"))),
            new XElement("Target", new XAttribute("Name", "Inventory"), new XElement("CIS.TypeManagement.Build.WriteTypeInventory",
                new XAttribute("OutputDirectory", destination), new XAttribute("HostFileName", "Host.dll"), new XAttribute("Assets", "@(Asset)"))))).Save(project);
        ProcessResult child = await Run("helper-" + label, id, ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", sdk, project, "-t:Inventory", "-nologo", "-nr:false"]);
        if (id < 0)
        {
            string manifest = Path.Combine(destination, "CIS.Core.types.json");
            Require(child.ExitCode == 0 && File.Exists(manifest), "MSBuild baseline/restoration failed.");
            JsonObject json = JsonNode.Parse(File.ReadAllBytes(manifest))!.AsObject();
            Require((int?)json["schemaVersion"] == 1 && json["assemblies"]!.AsArray().Count == 1 &&
                (string?)json["assemblies"]![0]!["relativePath"] == "Host.dll", "MSBuild inventory mismatch.");
        }
        else { Require(child.ExitCode == 1 && (child.StandardOutput + child.StandardError).Contains("MSB4181", StringComparison.Ordinal), "MSBuild child did not reject the mutated false task result."); }
    }
    foreach ((string name, JsonNode? pin) in pins) { Require(Hash(Path.Combine(root, "instrumented", name)) == (string?)pin, "Instrumented input changed."); }
    report["complete"] = true;
    return 0;
}
finally { Write("results.json", report.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); }

void Require(bool fact, string message) { if (!fact) { throw new InvalidDataException(message); } }
async Task<ProcessResult> Run(string name, int id, string[] arguments)
{
    var environment = new Dictionary<string, string?> { ["STRYKER_MUTANT_ID_CONTROL_VAR"] = "CIS_RELEASE_MUTANT", ["CIS_RELEASE_MUTANT"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["STRYKER_MUTANT_FILE"] = null, ["STRYKER_COVERAGE_FILE"] = null, ["MSBUILDDISABLENODEREUSE"] = "1" };
    ProcessResult child = await ProcessRunner.RunAsync("dotnet", arguments, output, TimeSpan.FromSeconds(60), environment);
    File.WriteAllBytes(Path.Combine(output, name + ".stdout.bin"), child.StandardOutputBytes.ToArray());
    File.WriteAllBytes(Path.Combine(output, name + ".stderr.bin"), child.StandardErrorBytes.ToArray());
    observations.Add(new JsonObject { ["name"] = name, ["activeMutant"] = id, ["processId"] = child.ProcessId, ["exitCode"] = child.ExitCode,
        ["argv"] = JsonSerializer.SerializeToNode(arguments), ["stdoutSha256"] = Hash(Path.Combine(output, name + ".stdout.bin")), ["stderrSha256"] = Hash(Path.Combine(output, name + ".stderr.bin")) });
    return child;
}
