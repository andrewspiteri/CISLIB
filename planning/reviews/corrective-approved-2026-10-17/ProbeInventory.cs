#:property PublishAot=false
using System.Diagnostics;
using System.Text.Json;

string source = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
string owned = Path.Combine(output, "owned-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(owned);
string repository = Directory.CreateDirectory(Path.Combine(owned, "repo")).FullName;
async Task<(int Exit, string Output, string Error)> Run(string executable, string[] arguments, string directory)
{
    var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
    try { await process.WaitForExitAsync(deadline.Token); }
    catch { process.Kill(true); throw; }
    return (process.ExitCode, await stdout, await stderr);
}
var init = await Run("git", ["init", "--quiet"], repository);
if (init.Exit != 0) throw new InvalidDataException(init.Error);
File.WriteAllText(Path.Combine(repository, ".gitignore"), "ignored.txt\n");
File.WriteAllText(Path.Combine(repository, "kept.txt"), "kept");
File.WriteAllText(Path.Combine(repository, "ignored.txt"), "must be excluded in Git mode");
string alias = Path.Combine(owned, "alias");
Directory.CreateSymbolicLink(alias, repository);
string checker = Path.Combine(source, "implementations/csharp/eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll");
var canonical = await Run("dotnet", [checker, "source-inventory", repository], owned);
var linked = await Run("dotnet", [checker, "source-inventory", alias], owned);
string[] Paths(string text) => JsonDocument.Parse(text).RootElement.EnumerateArray().Select(item => item.GetProperty("path").GetString()!).ToArray();
if (canonical.Exit != 0 || linked.Exit != 0) throw new InvalidDataException(canonical.Error + linked.Error);
string script = Path.Combine(owned, "bridge-relative.ps1");
string Quote(string value) => "'" + value.Replace("'", "''") + "'";
File.WriteAllText(script, "$ErrorActionPreference = 'Stop'\nImport-Module " + Quote(Path.Combine(source, "implementations/csharp/eng/Verification.psm1")) + " -Force\nSet-Location -LiteralPath " + Quote(owned) + "\n@(Get-SourceInventory 'repo') | ConvertTo-Json -AsArray -Compress\n");
var relative = await Run("pwsh", ["-NoProfile", "-File", script], source);
var record = new { recordedUtc = DateTimeOffset.UtcNow, source, owned, checker,
    aliasRoot = new { canonicalExit = canonical.Exit, aliasExit = linked.Exit, canonicalPaths = Paths(canonical.Output), aliasPaths = Paths(linked.Output), ignoredFileIncorrectlyIncludedViaAlias = !Paths(canonical.Output).Contains("ignored.txt") && Paths(linked.Output).Contains("ignored.txt") },
    relativeBridge = new { relative.Exit, relative.Output, relative.Error, matchesCanonical = relative.Exit == 0 && Paths(relative.Output).SequenceEqual(Paths(canonical.Output)) },
    scope = "Isolated native C# reproduction only; production and maintained tests unchanged. Shell script is invocation glue to the existing bridge." };
string json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }) + "\n";
File.WriteAllText(Path.Combine(output, "source-inventory-probes.json"), json);
Console.WriteLine(json);
