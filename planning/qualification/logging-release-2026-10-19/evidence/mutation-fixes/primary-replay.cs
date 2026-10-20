using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CIS.TestSupport;

if (args.Length != 3) { throw new ArgumentException("Expected native report, input root and new output root."); }
string reportPath = Path.GetFullPath(args[0]), inputRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
if (Directory.Exists(output) || output.StartsWith(inputRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)) { throw new IOException("Use a new output outside the input root."); }
Directory.CreateDirectory(output);
string snapshot = Path.Combine(output, "source");
CopyTree(inputRoot, snapshot);
string language = Path.Combine(snapshot, "implementations/csharp");
const string project = "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj";
const string ringFilter = "FullyQualifiedName~IncomingRingBufferTests.RepeatedWrapPublishesEveryEventAndAccountsForActualRemovals|FullyQualifiedName~IncomingRingBufferTests.DefaultProfileFallbackCeilingAndStartupCaptureAreExplicit|FullyQualifiedName~IncomingRingBufferTests.FinalExchangeCountsActualReplacementAndStaleClaimLeavesNewOccupant|FullyQualifiedName~IncomingRingBufferTests.EnrolledPublisherMayFinishAfterCloseWhileOtherProducersKeepProgressing|FullyQualifiedName~IncomingRingBufferTests.ConditionalAbandonmentAccountsOnlyTheActualExpectedOwner|FullyQualifiedName~IncomingRingBufferTests.InvalidEventDoesNotEnrollOrInventAdmission|FullyQualifiedName~IncomingRingBufferTests.MaximumCapacityIsInclusiveAndKeepsRoundedBudgets|FullyQualifiedName~IncomingRingBufferTests.InvalidCapacityHasAnExplicitParameterFailure|FullyQualifiedName~IncomingRingBufferTests.MissingOptionsHasAnExplicitParameterFailure";
string fullFilter = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(language, "verification/mutation/frontend-interpolation-config.json")))
    .RootElement.GetProperty("stryker-config").GetProperty("test-case-filter").GetString()!;
var environment = new Dictionary<string, string?> { ["CisMutationTesting"] = "true", ["UseSharedCompilation"] = "false", ["MSBUILDDISABLENODEREUSE"] = "1" };
var results = new List<object>();
using var report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
ProcessResult sdk = (await Run("sdk", ["--version"], 60, required: true))!;
if (sdk.StandardOutput.Trim() != "10.0.401" || Environment.Version != new Version(10, 0, 12)) { throw new InvalidDataException("Replay toolchain differs from its pins."); }
File.WriteAllText(Path.Combine(output, "input-control.json"), JsonSerializer.Serialize(new
{
    sdk = sdk.StandardOutput.Trim(), runtime = Environment.Version.ToString(),
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    verifierSha256 = Hash(File.ReadAllBytes(typeof(Program).Assembly.Location)),
    nativeReportSha256 = Hash(File.ReadAllBytes(reportPath))
}, new JsonSerializerOptions { WriteIndented = true }));
await Run("baseline", ["test", project, "-c", "Release", "--filter", fullFilter, "--logger", "trx;LogFileName=tests.trx", "--results-directory", Path.Combine(output, "baseline")], 300, required: true);
foreach (var file in report.RootElement.GetProperty("files").EnumerateObject())
{
    var mutants = file.Value.GetProperty("mutants").EnumerateArray().Where(m => m.GetProperty("status").GetString() is "CompileError" or "Ignored").ToArray();
    if (mutants.Length == 0) { continue; }
    string relative = "implementations/" + file.Name.Replace('\\', '/').Split("/implementations/", 2)[1];
    string path = Path.Combine(snapshot, relative), source = File.ReadAllText(path);
    if (source != file.Value.GetProperty("source").GetString()) { throw new InvalidDataException("Source differs: " + relative); }
    string filter = relative.EndsWith("IncomingRingBuffer.cs", StringComparison.Ordinal) ? ringFilter :
        relative.EndsWith("IncomingBatchIndex.cs", StringComparison.Ordinal) ? "FullyQualifiedName~IncomingBatchIndexTests" : "FullyQualifiedName~InterpolationTests";
    foreach (var mutant in mutants)
    {
        string id = mutant.GetProperty("id").GetString()!, directory = Path.Combine(output, id);
        Directory.CreateDirectory(directory);
        int Position(JsonElement p)
        {
            int offset = 0;
            for (int line = 1; line < p.GetProperty("line").GetInt32(); line++) { offset = source.IndexOf('\n', offset) + 1; if (offset == 0) { throw new InvalidDataException("Invalid source position."); } }
            return offset + p.GetProperty("column").GetInt32() - 1;
        }
        var location = mutant.GetProperty("location");
        int start = Position(location.GetProperty("start")), end = Position(location.GetProperty("end"));
        string replacement = mutant.GetProperty("replacement").GetString()!;
        string changed = source[..start] + replacement + source[end..];
        File.WriteAllText(path, changed, new UTF8Encoding(false));
        string disposition;
        try
        {
            ProcessResult? result = await Run(id, ["test", project, "-c", "Release", "--no-restore", "--filter", filter,
                "--logger", "trx;LogFileName=tests.trx", "--results-directory", directory], 45);
            string trx = Path.Combine(directory, "tests.trx");
            if (result is null) { disposition = "Timeout"; }
            else if (File.Exists(trx))
            {
                var counters = XDocument.Load(trx).Descendants().Single(e => e.Name.LocalName == "Counters");
                int total = (int?)counters.Attribute("total") ?? 0, failed = (int?)counters.Attribute("failed") ?? 0;
                if (total == 0 || (int?)counters.Attribute("executed") != total) { throw new InvalidDataException("Empty/skipped replay tests: " + id); }
                disposition = failed > 0 && result.ExitCode != 0 ? "DetectedByExistingTests" : result.ExitCode == 0 ? "SurvivedSelectedTests" : throw new InvalidDataException("Ambiguous test exit: " + id);
            }
            else if (result.ExitCode != 0 && (result.StandardOutput + result.StandardError).Contains("error CS", StringComparison.Ordinal)) { disposition = "DoesNotCompileAlone"; }
            else { throw new InvalidDataException("Replay failed without a compiler diagnostic or native result: " + id); }
            results.Add(new { id, file = relative, originalStatus = mutant.GetProperty("status").GetString(), disposition,
                originalText = source[start..end], replacement, filter, sourceSha256 = Hash(Encoding.UTF8.GetBytes(source)), mutantSha256 = Hash(Encoding.UTF8.GetBytes(changed)) });
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
            { nativeReportSha256 = Hash(File.ReadAllBytes(reportPath)), metricAdjusted = false, complete = false, results }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(id + ": " + disposition);
        }
        finally { File.WriteAllText(path, source, new UTF8Encoding(false)); }
    }
}
await Run("restored", ["test", project, "-c", "Release", "--no-restore", "--filter", fullFilter, "--logger", "trx;LogFileName=tests.trx", "--results-directory", Path.Combine(output, "restored")], 300, required: true);
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
{ nativeReportSha256 = Hash(File.ReadAllBytes(reportPath)), metricAdjusted = false, complete = true, results }, new JsonSerializerOptions { WriteIndented = true }));

async Task<ProcessResult?> Run(string name, string[] command, int seconds, bool required = false)
{
    Directory.CreateDirectory(Path.Combine(output, name));
    ProcessResult result;
    try { result = await ProcessRunner.RunAsync("dotnet", command, language, TimeSpan.FromSeconds(seconds), environment, Encoding.UTF8); }
    catch (TimeoutException error) when (!required) { File.WriteAllText(Path.Combine(output, name, "timeout.txt"), error.Message); return null; }
    File.WriteAllBytes(Path.Combine(output, name, "stdout.bin"), result.StandardOutputBytes.ToArray());
    File.WriteAllBytes(Path.Combine(output, name, "stderr.bin"), result.StandardErrorBytes.ToArray());
    File.WriteAllText(Path.Combine(output, name, "process.json"), JsonSerializer.Serialize(new { result.ExitCode, result.ProcessId, command, seconds }));
    if (required && result.ExitCode != 0) { throw new InvalidDataException("Control failed: " + name); }
    return result;
}
static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
static void CopyTree(string source, string target)
{
    Directory.CreateDirectory(target);
    foreach (string file in Directory.EnumerateFiles(source)) { File.Copy(file, Path.Combine(target, Path.GetFileName(file))); }
    foreach (string child in Directory.EnumerateDirectories(source))
    {
        if (Path.GetFileName(child) is "bin" or "obj" or ".git" or ".artifacts" or "TestResults" || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) { continue; }
        CopyTree(child, Path.Combine(target, Path.GetFileName(child)));
    }
}
