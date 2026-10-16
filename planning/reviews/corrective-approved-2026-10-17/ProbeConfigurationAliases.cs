#:property PublishAot=false
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

string repo = Path.GetFullPath(args[0]);
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
string original = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
var buffer = new StringBuilder(32768);
uint length = Native.GetShortPathName(original, buffer, (uint)buffer.Capacity);
if (length == 0 || length >= buffer.Capacity || !buffer.ToString().Contains('~')) throw new InvalidDataException("No existing alias for the read-only observation.");
string alias = buffer.ToString();
// Apply the exact ownerRoot expression used by TemporaryDirectory, substituting a known alias for GetTempPath.
// This only normalizes strings; nothing is created in Program Files.
string ownerRoot = Path.GetFullPath(Path.Combine(alias, "cislib-tests"));
string fixtureBase = Path.Combine(ownerRoot, "owned-placeholder", "before");
string relative = "logs/example.jsonl";
MethodInfo normalize = Assembly.LoadFrom(Path.Combine(repo, "implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll"))
    .GetType("CIS.Logging.Configuration.LoggingFilePath", true)!.GetMethod("Normalize", BindingFlags.NonPublic | BindingFlags.Static)!;
string Actual(string basePath) => (string)normalize.Invoke(null, [relative, basePath])!;
var result = new { recordedUtc = DateTimeOffset.UtcNow, original, alias, ownerRoot,
    directAliasBase = new { oracle = Path.GetFullPath(relative, alias), production = Actual(alias), differs = Path.GetFullPath(relative, alias) != Actual(alias) },
    helperNormalizedBase = new { fixtureBase, oracle = Path.GetFullPath(relative, fixtureBase), production = Actual(fixtureBase), matches = Path.GetFullPath(relative, fixtureBase) == Actual(fixtureBase) },
    conclusion = "The alias-expansion mechanism exists, but the omitted TemporaryDirectory helper normalizes its owner root first. The reported default-temp-root counterexample does not apply to that helper; explicit alias cases use literal expected paths.",
    scope = "Read-only Windows C# observation; no directory or file was created in Program Files. Maintained source is unchanged." };
string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n";
File.WriteAllText(Path.Combine(repo, ".artifacts/claude-review-2026-10-17-approved/configuration-alias-probe.json"), json);
Console.WriteLine(json);

internal static class Native
{
    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetShortPathName(string path, StringBuilder buffer, uint length);
}
