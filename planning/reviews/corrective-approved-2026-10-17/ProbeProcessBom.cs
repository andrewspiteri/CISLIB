#:property PublishAot=false
using System.Reflection;
using System.Text;
using System.Text.Json;

byte[] emitted = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{\"case\":\"bom-probe\"}\n")];
if (args is ["--emit"]) { using Stream stream = Console.OpenStandardError(); stream.Write(emitted); return; }
string repo = Path.GetFullPath(args[0]);
Assembly support = Assembly.LoadFrom(Path.Combine(repo, "implementations/csharp/tests/CIS.Core.TestSupport/bin/Release/net10.0/CIS.Core.TestSupport.dll"));
MethodInfo run = support.GetType("CIS.TestSupport.ProcessRunner", true)!.GetMethod("RunAsync")!;
string self = Assembly.GetExecutingAssembly().Location;
Task task = (Task)run.Invoke(null, ["dotnet", new[] { "exec", self, "--emit" }, repo, TimeSpan.FromSeconds(30), null, new UTF8Encoding(false, true)])!;
await task;
object result = task.GetType().GetProperty("Result")!.GetValue(task)!;
string error = (string)result.GetType().GetProperty("StandardError")!.GetValue(result)!;
var observation = new { recordedUtc = DateTimeOffset.UtcNow, emittedHex = Convert.ToHexStringLower(emitted), emittedHasUtf8Bom = true, childExit = (int)result.GetType().GetProperty("ExitCode")!.GetValue(result)!, capturedError = error, capturedHasBom = error.StartsWith('\uFEFF'), reencodedHex = Convert.ToHexStringLower(Encoding.UTF8.GetBytes(error)), originalBytesPreserved = Encoding.UTF8.GetBytes(error).SequenceEqual(emitted), scope = "Isolated C# child writes a known BOM to raw stderr. Current built ProcessRunner reads it through its StreamReader. No maintained code or files are changed; this probes physical-byte evidence preservation, not production logging output." };
string json = JsonSerializer.Serialize(observation, new JsonSerializerOptions { WriteIndented = true }) + "\n";
File.WriteAllText(Path.Combine(repo, ".artifacts/claude-review-2026-10-17-approved/process-bom-probe.json"), json);
Console.WriteLine(json);
