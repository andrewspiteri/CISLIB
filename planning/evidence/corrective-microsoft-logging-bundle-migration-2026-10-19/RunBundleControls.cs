#:property PublishAot=false
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

string repo = Path.GetFullPath(args[0]);
string basePath = "/home/user/cislib-verification/ubuntu-developer-396e87d6-archive";
string nativeRepo = basePath + "/source";
string workRelative = ".artifacts/corrective/microsoft-logging-bundle-migration-2026-10-19";
string work = Path.Combine(repo, workRelative);
string native = @"\\wsl.localhost\Ubuntu-24.04\home\user\cislib-verification\ubuntu-developer-396e87d6-archive\source";
string package = ".artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg";
string wrapper = "implementations/csharp/eng/write-microsoft-logging-bundle.ps1";
void Require(bool value, string reason) { if (!value) throw new InvalidDataException(reason); }
foreach (string owner in new[] { repo, native })
{
    JsonNode summary = JsonNode.Parse(File.ReadAllBytes(Path.Combine(owner, ".artifacts/csharp/verification/Developer/summary.json")))!;
    Require((int?)summary["exitCode"] == 0 && summary["finishedUtc"] is not null, "Wait for both complete passing checkpoints.");
}
Require(!File.Exists(Path.Combine(native, ".artifacts/csharp/tasks/wp06-s03-t03/mutation-qualified/mutation/reports/mutation-report.json")), "Expected missing historical native prerequisite changed.");
async Task Run(string lane, string executable, string[] arguments, int expected)
{
    string destination = Path.Combine(work, lane + "-cli");
    Require(!Directory.Exists(destination), "Preserve prior CLI controls.");
    Directory.CreateDirectory(destination);
    var start = new ProcessStartInfo(executable) { WorkingDirectory = repo, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    start.Environment["UseSharedCompilation"] = "false"; start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
    using var process = Process.Start(start)!;
    using var stdout = File.Create(Path.Combine(destination, "stdout.bin"));
    using var stderr = File.Create(Path.Combine(destination, "stderr.bin"));
    Task first = process.StandardOutput.BaseStream.CopyToAsync(stdout), second = process.StandardError.BaseStream.CopyToAsync(stderr);
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
    await Task.WhenAll(first, second);
    await stdout.FlushAsync(); await stderr.FlushAsync();
    string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    await stdout.DisposeAsync(); await stderr.DisposeAsync();
    var result = new JsonObject { ["lane"] = lane, ["executable"] = executable, ["arguments"] = JsonSerializer.SerializeToNode(arguments),
        ["pid"] = process.Id, ["exitCode"] = process.ExitCode, ["stdoutSha256"] = Hash(Path.Combine(destination, "stdout.bin")), ["stderrSha256"] = Hash(Path.Combine(destination, "stderr.bin")) };
    File.WriteAllText(Path.Combine(destination, "process.json"), result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Require(process.ExitCode == expected, lane + " CLI exit differs; inspect exact retained diagnostics.");
    Console.WriteLine(lane + " actual bundle CLI exit " + process.ExitCode);
}
await Run("windows", "pwsh", ["-NoProfile", "-File", wrapper, "-PackagePath", package, "-OutputRoot", workRelative + "/windows-draft"], 0);
await Run("ubuntu", "wsl.exe", ["-d", "Ubuntu-24.04", "-u", "user", "--cd", nativeRepo, "--exec", "/usr/bin/env",
    "DOTNET_ROOT=" + basePath + "/toolchain/sdk", "PATH=" + basePath + "/toolchain/powershell:" + basePath + "/toolchain/sdk:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
    "UseSharedCompilation=false", "MSBUILDDISABLENODEREUSE=1", "pwsh", "-NoProfile", "-File", wrapper, "-PackagePath", package, "-OutputRoot", workRelative + "/ubuntu-draft"], 1);
Require(!Directory.Exists(Path.Combine(native, workRelative, "ubuntu-draft")), "Missing native evidence must not materialize a draft.");
