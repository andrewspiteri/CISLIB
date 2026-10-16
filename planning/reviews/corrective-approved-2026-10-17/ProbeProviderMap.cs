#:property PublishAot=false
using System.Reflection;
using System.Text.Json;

string repo = Path.GetFullPath(args[0]);
Assembly core = Assembly.LoadFrom(Path.Combine(repo, "implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll"));
MethodInfo capture = core.GetType("CIS.Logging.MicrosoftLogger.ProviderStateCapture", true)!.GetMethod("CaptureScope", BindingFlags.NonPublic | BindingFlags.Static)!;
object Observe(int invalidIndex)
{
    var nested = Enumerable.Range(0, 1024).Select(index => new KeyValuePair<string, object?>(index == invalidIndex ? new string((char)0xD800, 1) : "key" + index, index)).ToList();
    var state = new List<KeyValuePair<string, object?>> { new("nested", nested) };
    try
    {
        object context = capture.Invoke(null, [state])!;
        object attributes = context.GetType().GetProperty("Attributes", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(context)!;
        return new { invalidIndex, accepted = true, truncated = (bool)attributes.GetType().GetProperty("IsTruncated")!.GetValue(attributes)!, error = (string?)null };
    }
    catch (TargetInvocationException error)
    {
        return new { invalidIndex, accepted = false, truncated = false, error = error.InnerException!.GetType().Name + ": " + error.InnerException.Message };
    }
}
var result = new { recordedUtc = DateTimeOffset.UtcNow, atFirstOmittedKey = Observe(1022), beyondFirstOmittedKey = Observe(1023), scope = "Isolated C# probe against current built Core internal capture; malformed surrogate key changes position across the exhausted node budget. No maintained source, catalog or runtime options are modified. This establishes capture inconsistency, not a full public runtime execution." };
string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n";
File.WriteAllText(Path.Combine(repo, ".artifacts/claude-review-2026-10-17-approved/provider-map-probes.json"), json);
Console.WriteLine(json);
