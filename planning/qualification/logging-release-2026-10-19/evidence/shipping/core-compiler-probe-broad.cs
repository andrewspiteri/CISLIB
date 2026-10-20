using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CIS.TestSupport;

if (args.Length != 3) { throw new ArgumentException("Expected live native trace, preserved source input root and new probe output root."); }
string log = Path.GetFullPath(args[0]), input = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
if (Directory.Exists(output) || output.StartsWith(input + Path.DirectorySeparatorChar, StringComparison.Ordinal)) { throw new IOException("Use a new output outside the input root."); }
Directory.CreateDirectory(output);
string trace;
using (var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
using (var reader = new StreamReader(stream)) { trace = reader.ReadToEnd(); }
string creation = trace.Split('\n').Single(line => line.Contains("Mutant 2984 created \"!writable\" -> \"writable\"", StringComparison.Ordinal));
if (!trace.Contains("Safe Mode! Stryker will remove all mutations in \"Initialize\" and mark them as 'compile error'", StringComparison.Ordinal))
{ throw new InvalidDataException("Native trace does not establish the initialization rollback."); }
string frozen = Path.Combine(output, "native-trace-prefix.txt");
File.WriteAllText(frozen, trace, new UTF8Encoding(false));
string snapshot = Path.Combine(output, "source"); CopyTree(input, snapshot);
string language = Path.Combine(snapshot, "implementations/csharp");
const string relative = "src/CIS.Core/Logging/Runtime/ConsoleOutputWriter.cs";
string sourcePath = Path.Combine(language, relative), original = File.ReadAllText(sourcePath);
byte[] originalBytes = File.ReadAllBytes(sourcePath);
int index = original.IndexOf("!writable", StringComparison.Ordinal);
if (index < 0 || original.IndexOf("!writable", index + 1, StringComparison.Ordinal) >= 0) { throw new InvalidDataException("Probe source is not unique."); }
string changed = original[..index] + "writable" + original[(index + "!writable".Length)..];
const string project = "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj";
const string filter = "FullyQualifiedName!~IncomingMeasurementsTests&FullyQualifiedName!~InterpolationMeasurementsTests&FullyQualifiedName!~LoggingReviewMeasurements";
var environment = new Dictionary<string, string?> { ["CisMutationTesting"] = "true", ["UseSharedCompilation"] = "false", ["MSBUILDDISABLENODEREUSE"] = "1", ["RestoreLockedMode"] = "true" };
ProcessResult sdk = await Run("sdk", ["--version"], required: true);
if (sdk.StandardOutput.Trim() != "10.0.401" || Environment.Version != new Version(10, 0, 12)) { throw new InvalidDataException("Probe toolchain differs from its pins."); }
await Run("baseline-build", ["build", project, "-c", "Release"], required: true);
string[] Test(string name, bool build) => ["test", project, "-c", "Release", "--no-restore", .. build ? Array.Empty<string>() : new[] { "--no-build" },
    "--filter", filter, "--logger", "trx;LogFileName=tests.trx", "--results-directory", Path.Combine(output, name)];
await Run("baseline", Test("baseline", false), required: true, control: true);
string disposition;
try
{
    File.WriteAllText(sourcePath, changed, new UTF8Encoding(originalBytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })));
    ProcessResult mutant = await Run("variant-2984", Test("variant-2984", true));
    string trxPath = Path.Combine(output, "variant-2984/tests.trx");
    if (!File.Exists(trxPath))
    {
        if (mutant.ExitCode == 0 || !(mutant.StandardOutput + mutant.StandardError).Contains("error CS", StringComparison.Ordinal))
        { throw new InvalidDataException("Probe has neither a native result nor a compiler-negative observation."); }
        disposition = "DoesNotCompileAlone";
    }
    else
    {
        var counters = XDocument.Load(trxPath).Descendants().Single(e => e.Name.LocalName == "Counters");
        int total = (int?)counters.Attribute("total") ?? 0, failed = (int?)counters.Attribute("failed") ?? 0;
        if (total == 0 || (int?)counters.Attribute("executed") != total) { throw new InvalidDataException("Empty/skipped probe execution."); }
        disposition = mutant.ExitCode != 0 && failed > 0 ? "CompilesAndIsDetectedByExistingTests" : mutant.ExitCode == 0 && failed == 0 ? "CompilesAndSurvivesSelectedTests" : throw new InvalidDataException("Ambiguous probe exit.");
    }
}
finally { File.WriteAllBytes(sourcePath, originalBytes); }
await Run("restored", Test("restored", true), required: true, control: true);
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
{
    scope = "One exact logged source variant; final native JSON confirmation remains separate. No native status or score is changed.",
    complete = true, metricAdjusted = false, loggedMutantId = "2984", creation, disposition,
    originalText = "!writable", replacement = "writable", source = relative,
    sourceSha256 = Hash(originalBytes), variantSha256 = Hash(Encoding.UTF8.GetBytes(changed)),
    frozenTraceSha256 = Hash(File.ReadAllBytes(frozen)), verifierSha256 = Hash(File.ReadAllBytes(typeof(Program).Assembly.Location)),
    sdk = sdk.StandardOutput.Trim(), runtime = Environment.Version.ToString(), filter
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(disposition);

async Task<ProcessResult> Run(string name, string[] command, bool required = false, bool control = false)
{
    Directory.CreateDirectory(Path.Combine(output, name));
    ProcessResult result = await ProcessRunner.RunAsync("dotnet", command, language, TimeSpan.FromMinutes(5), environment, Encoding.UTF8);
    File.WriteAllBytes(Path.Combine(output, name, "stdout.bin"), result.StandardOutputBytes.ToArray());
    File.WriteAllBytes(Path.Combine(output, name, "stderr.bin"), result.StandardErrorBytes.ToArray());
    File.WriteAllText(Path.Combine(output, name, "process.json"), JsonSerializer.Serialize(new { result.ExitCode, result.ProcessId, command }));
    if (required && result.ExitCode != 0) { throw new InvalidDataException("Control failed: " + name); }
    if (control)
    {
        var trx = XDocument.Load(Path.Combine(output, name, "tests.trx"));
        var counters = trx.Descendants().Single(e => e.Name.LocalName == "Counters");
        int total = (int?)counters.Attribute("total") ?? 0;
        if (total == 0 || (int?)counters.Attribute("passed") != total || (int?)counters.Attribute("executed") != total ||
            !trx.Descendants().Any(e => e.Name.LocalName == "StdOut" && e.Value.Contains("CIS mutation scheduling: serialized", StringComparison.Ordinal)))
        { throw new InvalidDataException("Baseline/restored control is empty, failed, skipped or not serialized."); }
    }
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
