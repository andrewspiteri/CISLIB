using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

string work = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
string original = Path.Combine(work, "implementations/csharp"), fixture = Path.Combine(output, "fixture"), language = Path.Combine(fixture, "implementations/csharp");
if (Directory.Exists(fixture)) { throw new IOException("Use a fresh control root."); }
Directory.CreateDirectory(language);
void Copy(string source, string destination) { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination); }
foreach (string path in Directory.EnumerateFiles(original, "*", SearchOption.AllDirectories))
{
    string relative = Path.GetRelativePath(original, path);
    if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin" or ".vs")) { continue; }
    Copy(path, Path.Combine(language, relative));
}
JsonNode policy = JsonNode.Parse(File.ReadAllText(Path.Combine(language, "verification/coverage/shipping.json")))!;
foreach (JsonNode assembly in policy["assemblies"]!.AsArray().OfType<JsonNode>())
{
    string project = assembly["project"]!.GetValue<string>(), name = assembly["name"]!.GetValue<string>();
    foreach (string extension in new[] { ".dll", ".pdb" })
    {
        string relative = Path.Combine(Path.GetDirectoryName(project)!, "bin/Release/net10.0", name + extension);
        Copy(Path.Combine(original, relative), Path.Combine(language, relative));
    }
}
foreach ((string _, JsonNode? value) in policy["families"]!.AsObject())
{
    string name = "CIS.Core." + value!.GetValue<string>(), relative = $"tests/{name}/bin/Release/net10.0";
    foreach (string file in new[] { name + ".dll", "CIS.Core.dll", "CIS.TypeManager.Build.dll" })
    {
        string source = Path.Combine(original, relative, file);
        if (File.Exists(source)) { Copy(source, Path.Combine(language, relative, file)); }
    }
}
string packageName = Directory.GetFiles(Path.Combine(work, ".artifacts/csharp/packages"), "CIS.Core.0.1.0-dev.1.nupkg").Single();
string package = Path.Combine(fixture, ".artifacts/csharp/packages", Path.GetFileName(packageName));
Copy(packageName, package);
// An older unrelated version must not displace the evaluated candidate selection.
File.WriteAllText(Path.Combine(Path.GetDirectoryName(package)!, "CIS.Core.0.0.0-previous.nupkg"), "deliberately invalid older package");
var results = new List<object>();
async Task Control(string name, string expected)
{
    var start = new ProcessStartInfo("pwsh") { WorkingDirectory = language, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (string argument in new[] { "-NoProfile", "-File", Path.Combine(language, "eng/verify-shipping-coverage.ps1"), "-OutputRoot", Path.Combine(output, name) }) { start.ArgumentList.Add(argument); }
    using Process process = Process.Start(start)!;
    Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
    try { await process.WaitForExitAsync(deadline.Token); }
    catch { process.Kill(true); throw; }
    string text = await stdout + await stderr;
    File.WriteAllText(Path.Combine(output, name + ".log"), text);
    if (process.ExitCode == 0 || !text.Contains(expected, StringComparison.Ordinal)) { throw new InvalidDataException(name + " failed: " + text); }
    results.Add(new { name, exit = process.ExitCode, expected, passed = true });
}
string copy = Path.Combine(language, "tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.dll");
byte[] bytes = File.ReadAllBytes(copy);
try { File.WriteAllText(copy, "stale copy-local binary"); await Control("stale-copy-with-other-package-version", "Copy-local shipping assembly differs from the package"); }
finally { File.WriteAllBytes(copy, bytes); }
byte[] archiveBytes = File.ReadAllBytes(package);
try
{
    using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Update))
    using (var writer = new StreamWriter(archive.CreateEntry("runtimes/win-x64/native/unregistered.DLL").Open())) { writer.Write("hidden DLL control"); }
    await Control("dll-outside-lib-tools", "Shipped DLL inventory differs from the coverage policy");
}
finally { File.WriteAllBytes(package, archiveBytes); }
File.WriteAllText(Path.Combine(output, "controls.json"), JsonSerializer.Serialize(new { schema = "cis.coverage-preflight-controls/v1", sourceScriptSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(language, "eng/verify-shipping-coverage.ps1")))), candidateSha256 = Convert.ToHexStringLower(SHA256.HashData(archiveBytes)), controls = results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Coverage preflight rejection controls passed: " + results.Count);
