#:property PublishAot=false
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

string repo = Path.GetFullPath(args[0]);
Assembly fixture = Assembly.LoadFrom(Path.Combine(repo, "implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll"));
MethodInfo execute = fixture.GetType("CIS.Tests.DiscoveryFixture.ExceptionQualificationFixture", true)!.GetMethod("Execute", BindingFlags.Static | BindingFlags.NonPublic)!;
MethodInfo validate = fixture.GetType("CIS.Tests.DiscoveryFixture.ExceptionQualificationExpectations", true)!.GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)!;
JsonObject catalog = JsonNode.Parse(File.ReadAllBytes(Path.Combine(repo, "conformance/logging/exception-qualification-cases.json")))!.AsObject();
JsonObject bindings = catalog["adapterObservations"]!["csharp"]!.AsObject();
JsonObject row = catalog["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "EXCEPTION-SINGLE")!.AsObject();
JsonObject actual = (JsonObject)execute.Invoke(null, [row, bindings])!;
bool Accept(JsonObject selectedRow, JsonObject report, JsonObject selectedBindings, out string? error)
{
    try { validate.Invoke(null, [selectedRow, report, selectedBindings]); error = null; return true; }
    catch (TargetInvocationException exception) { error = exception.InnerException!.GetType().Name + ": " + exception.InnerException.Message; return false; }
}
bool positive = Accept(row, actual, bindings, out string? positiveError);
JsonObject floating = actual.DeepClone().AsObject();
string label = floating["reads"]!.AsObject().First(item => item.Value!["message"]!.GetValue<int>() == 1).Key;
floating["reads"]![label]!["message"] = JsonNode.Parse("1.0");
bool acceptsFloat = Accept(row, floating, bindings, out string? floatError);
JsonObject invalidRow = catalog["cases"]!.AsArray().First(item => item!["expected"]!["error"] is not null)!.AsObject();
JsonObject invalidActual = (JsonObject)execute.Invoke(null, [invalidRow, bindings])!;
JsonObject missingBindings = bindings.DeepClone().AsObject();
string tag = invalidRow["expected"]!["error"]!.GetValue<string>();
missingBindings.Remove(tag);
bool missingBindingAloneAccepted = Accept(invalidRow, invalidActual, missingBindings, out string? missingAloneError);
invalidActual["nativeError"] = null;
invalidActual["parameter"] = null;
bool missingBindingAndNullsAccepted = Accept(invalidRow, invalidActual, missingBindings, out string? missingNullsError);
var result = new { recordedUtc = DateTimeOffset.UtcNow, positive, positiveError, observerCount = new { label, injected = "1.0", accepted = acceptsFloat, error = floatError },
    missingBinding = new { tag, missingBindingAloneAccepted, missingAloneError, missingBindingAndNullsAccepted, missingNullsError },
    scope = "Isolated C# reflection probe against the built fixture and current Core; only cloned reports/bindings are corrupted. Maintained files and catalogs unchanged." };
string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n";
File.WriteAllText(Path.Combine(repo, ".artifacts/claude-review-2026-10-17-approved/exception-reader-probes.json"), json);
Console.WriteLine(json);
