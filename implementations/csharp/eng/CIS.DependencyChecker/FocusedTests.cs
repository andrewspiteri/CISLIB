using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;

namespace CIS.Verification;

// A selected native run, not a replacement for a Developer or Release checkpoint.
internal static class FocusedTests
{
    internal sealed record Selection(string Root, string Project, string Configuration, string? Filter, int TimeoutSeconds);
    internal sealed record NativeCase(string Id, string ExecutionId, string Name, string Method, string Source, string Outcome);
    internal sealed record Artifact(string Name, string Availability, long OriginalBytes, long RetainedBytes);
    internal const int MaximumReportBytes = 8 * 1024 * 1024;
    internal const int MaximumLogCharactersPerStream = 64 * 1024;

    internal static Selection Parse(string[] arguments)
    {
        if (arguments.Length < 3 || arguments.Length % 2 != 1)
        { throw new ArgumentException("Usage: focused-test <language-root> --project UnitTests [--class Name | --category Name | --filter VSTestExpression] [--configuration Debug|Release] [--timeout-seconds 300]"); }
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] known = ["--project", "--class", "--category", "--filter", "--configuration", "--timeout-seconds"];
        for (int index = 1; index < arguments.Length; index += 2)
        {
            if (!known.Contains(arguments[index], StringComparer.Ordinal) || string.IsNullOrWhiteSpace(arguments[index + 1]) || !options.TryAdd(arguments[index], arguments[index + 1]))
            { throw new ArgumentException("Unknown, empty or repeated focused-test option: " + arguments[index]); }
        }
        if (!options.TryGetValue("--project", out string? project)) { throw new ArgumentException("--project is required."); }
        string root = Path.GetFullPath(arguments[0]);
        JsonObject policy = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "eng/project-boundaries.json")))!.AsObject();
        string[] registered = policy["projects"]!.AsArray().Where(item => item!["role"]!.GetValue<string>() == "test").Select(item => item!["path"]!.GetValue<string>()).ToArray();
        string? selected = registered.SingleOrDefault(path => project == path || project == Path.GetFileNameWithoutExtension(path) || "CIS.Core." + project == Path.GetFileNameWithoutExtension(path));
        if (selected is null) { throw new ArgumentException("Select a registered native test project, for example UnitTests or IntegrationTests."); }
        string configuration = options.GetValueOrDefault("--configuration", "Debug");
        if (configuration is not ("Debug" or "Release")) { throw new ArgumentException("Configuration must be Debug or Release."); }
        if (!int.TryParse(options.GetValueOrDefault("--timeout-seconds", "300"), NumberStyles.None, CultureInfo.InvariantCulture, out int timeout) || timeout is < 1 or > 3600)
        { throw new ArgumentException("Timeout must be 1–3600 seconds per child process."); }
        if (new[] { "--class", "--category", "--filter" }.Count(options.ContainsKey) > 1)
        { throw new ArgumentException("Choose one selector; use --filter for combined VSTest expressions."); }
        string? filter = options.GetValueOrDefault("--filter");
        foreach (string key in new[] { "--class", "--category" })
        {
            if (!options.TryGetValue(key, out string? value)) { continue; }
            if (value.Any(character => !char.IsLetterOrDigit(character) && character is not ('_' or '.' or '+' or '-' or ' ')))
            { throw new ArgumentException("Use --filter for selectors containing VSTest operators or special characters."); }
            filter = key == "--class" ? "FullyQualifiedName~" + value + "." : "TestCategory=" + value;
        }
        return new(root, selected, configuration, filter, timeout);
    }

    internal static async Task<int> RunAsync(string[] arguments)
    {
        Selection selection;
        try { selection = Parse(arguments); }
        catch (Exception error) when (error is ArgumentException or IOException or JsonException)
        { Console.Error.WriteLine(error.Message); return 2; }

        string repository = Path.GetFullPath(Path.Combine(selection.Root, "../.."));
        string focusedRoot = Path.Combine(repository, ".artifacts/csharp/focused");
        string output = Path.Combine(focusedRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        Console.WriteLine("Focused native summary: " + Path.Combine(output, "summary.json"));
        DateTimeOffset started = DateTimeOffset.UtcNow;
        var elapsed = Stopwatch.StartNew();
        double buildSeconds = 0, nativeSeconds = 0;
        NativeCase[] cases = [];
        var commands = new List<object>();
        string? failure = null;
        try
        {
            await RunChild("build", ["build", selection.Project, "-c", selection.Configuration, "-p:RestoreLockedMode=true"]);
            var testArguments = new List<string> { "test", selection.Project, "-c", selection.Configuration, "--no-build", "--no-restore", "--logger", "trx;LogFileName=selected.trx", "--results-directory", output };
            if (selection.Filter is not null) { testArguments.AddRange(["--filter", selection.Filter]); }
            DateTimeOffset testStarted = DateTimeOffset.UtcNow;
            await RunChild("test", testArguments);
            cases = ReadResults(Path.Combine(output, "selected.trx"), testStarted);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or FormatException or TimeoutException or System.Xml.XmlException or System.ComponentModel.Win32Exception)
        { failure = error.Message; Console.Error.WriteLine(failure); }
        elapsed.Stop();
        Artifact[] artifacts;
        try { artifacts = FinalizeArtifacts(focusedRoot, output, failure is null); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            failure = (failure is null ? "" : failure + " ") + "Artifact cleanup failed: " + error.Message;
            artifacts = [];
            Console.Error.WriteLine(failure);
        }
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
        {
            scope = "focused-native-tests",
            passed = failure is null,
            failure,
            started,
            selection.Project,
            selection.Configuration,
            selection.Filter,
            selection.TimeoutSeconds,
            buildSeconds,
            nativeDiscoveryAndExecutionSeconds = nativeSeconds,
            totalSeconds = elapsed.Elapsed.TotalSeconds,
            selectedCaseCount = cases.Length,
            commands,
            cases,
            artifacts,
            retention = "Successful runs retain this summary only. Failed runs retain bounded native diagnostics until explicitly removed or superseded; no other run is cleaned automatically.",
            omitted = "Unselected tests and families, package/API/architecture checkpoint prerequisites, coverage, mutation, performance and release qualification are not credited."
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (failure is null) { Console.WriteLine($"Passed {cases.Length} selected native cases. Other verification is not credited."); }
        return failure is null ? 0 : 1;

        async Task RunChild(string phase, IEnumerable<string> command)
        {
            string[] commandArguments = command.ToArray();
            var phaseElapsed = Stopwatch.StartNew();
            ProcessResult? result = null;
            bool timedOut = false;
            bool? cleanupCompleted = null;
            try
            {
                result = await ProcessRunner.RunAsync("dotnet", commandArguments, selection.Root, TimeSpan.FromSeconds(selection.TimeoutSeconds), maximumCapturedBytesPerStream: 64 * 1024, allowTruncatedOutput: true);
            }
            catch (ProcessTimeoutException error)
            {
                result = error.Result;
                timedOut = true;
                cleanupCompleted = error.CleanupCompleted;
                throw;
            }
            finally
            {
                if (phase == "build") { buildSeconds = phaseElapsed.Elapsed.TotalSeconds; }
                else { nativeSeconds = phaseElapsed.Elapsed.TotalSeconds; }
                commands.Add(new
                {
                    phase,
                    executable = "dotnet",
                    arguments = commandArguments,
                    exitCode = result?.ExitCode,
                    timedOut,
                    cleanupCompleted,
                    standardOutputTotalBytes = result?.StandardOutputTotalBytes,
                    standardErrorTotalBytes = result?.StandardErrorTotalBytes,
                    standardOutputTruncated = result?.StandardOutputTruncated,
                    standardErrorTruncated = result?.StandardErrorTruncated,
                    outputDrained = result?.OutputDrained
                });
                if (result is not null)
                {
                    WriteDiagnosticLog(Path.Combine(output, phase + ".log"), result.StandardOutput, result.StandardError, result.CaptureTruncated);
                    Console.Write(result.StandardOutput);
                    Console.Error.Write(result.StandardError);
                }
            }
            if (!result.OutputDrained) { throw new InvalidOperationException($"Native {phase} output capture did not complete."); }
            if (result.ExitCode != 0) { throw new InvalidOperationException($"Native {phase} failed with exit code {result.ExitCode}."); }
        }
    }

    internal static NativeCase[] ReadResults(string path, DateTimeOffset started)
    {
        Require(new FileInfo(path).Length <= MaximumReportBytes, "Native TRX exceeds the focused diagnostic size limit.");
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        XElement root = XDocument.Load(path).Root ?? throw new InvalidDataException("Missing native TRX root.");
        Require(root.Name == ns + "TestRun", "Unexpected native TRX schema.");
        XElement summary = root.Element(ns + "ResultSummary") ?? throw new InvalidDataException("Missing native summary.");
        Require((string?)summary.Attribute("outcome") == "Completed", "Native test run did not complete successfully.");
        XElement counters = summary.Element(ns + "Counters") ?? throw new InvalidDataException("Missing native counters.");
        XElement[] results = root.Element(ns + "Results")?.Elements(ns + "UnitTestResult").ToArray() ?? [];
        Require(results.Length > 0, "The filter selected zero native cases.");
        foreach (string name in new[] { "total", "executed", "passed" })
        { Require((int?)counters.Attribute(name) == results.Length, "Native counters disagree with selected cases: " + name); }
        foreach (XAttribute counter in counters.Attributes().Where(item => item.Name.LocalName is not ("total" or "executed" or "passed")))
        { Require(int.TryParse(counter.Value, out int value) && value == 0, "Unexpected native outcome: " + counter.Name); }
        var definitions = root.Element(ns + "TestDefinitions")?.Elements(ns + "UnitTest").ToDictionary(item => Required(item, "id"), StringComparer.Ordinal)
            ?? throw new InvalidDataException("Missing native definitions.");
        var executions = new HashSet<string>(StringComparer.Ordinal);
        return results.Select(result =>
        {
            string id = Required(result, "testId"), execution = Required(result, "executionId");
            Require(executions.Add(execution), "Duplicate native execution ID.");
            Require(Required(result, "outcome") == "Passed", "A selected case failed or was skipped.");
            Require(definitions.ContainsKey(id), "Native case has no definition.");
            XElement definition = definitions[id];
            Require((string?)definition.Element(ns + "Execution")?.Attribute("id") == execution, "Native execution and definition disagree.");
            XElement method = definition.Element(ns + "TestMethod") ?? throw new InvalidDataException("Missing native method.");
            Require(DateTimeOffset.TryParse(Required(result, "startTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset caseStart) && caseStart >= started.AddSeconds(-1), "Stale native case result.");
            return new NativeCase(id, execution, Required(result, "testName"), Required(method, "className") + "." + Required(method, "name"), Required(method, "codeBase"), Required(result, "outcome"));
        }).ToArray();
    }

    internal static void WriteDiagnosticLog(string path, string standardOutput, string standardError, bool captureTruncated = false)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
        if (captureTruncated) { writer.WriteLine("[native output exceeded bounded process capture; omitted content is unavailable]"); }
        WriteStream("stdout", standardOutput);
        WriteStream("stderr", standardError);

        void WriteStream(string name, string value)
        {
            writer.WriteLine($"--- {name}: {value.Length} captured characters ---");
            if (value.Length <= MaximumLogCharactersPerStream) { writer.Write(value); }
            else
            {
                int half = MaximumLogCharactersPerStream / 2;
                writer.Write(value.AsSpan(0, half));
                writer.WriteLine("\n[focused diagnostic truncated; middle omitted]\n");
                writer.Write(value.AsSpan(value.Length - half));
            }
            writer.WriteLine();
        }
    }

    // Only the newly created GUID child belongs to this invocation. Never prune siblings or caches.
    internal static Artifact[] FinalizeArtifacts(string focusedRoot, string output, bool passed)
    {
        string root = Path.GetFullPath(focusedRoot), owned = Path.GetFullPath(output);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Require(string.Equals(Path.GetDirectoryName(owned), root, comparison) && Guid.TryParseExact(Path.GetFileName(owned), "N", out _), "Focused cleanup escaped its owned run directory.");
        for (DirectoryInfo? directory = new(owned); directory is not null; directory = directory.Parent)
        { Require((directory.Attributes & FileAttributes.ReparsePoint) == 0, "Linked focused output is not owned for cleanup."); }

        var artifacts = new List<Artifact>();
        string[] names = ["build.log", "test.log", "selected.trx"];
        foreach (string name in names)
        {
            string path = Path.Combine(owned, name);
            if (!File.Exists(path)) { artifacts.Add(new(name, "missing", 0, 0)); continue; }
            Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Linked native diagnostic is not a regular file.");
            long bytes = new FileInfo(path).Length;
            long maximum = name == "selected.trx" ? MaximumReportBytes : 512 * 1024;
            string availability = passed ? "cleaned-after-validation" : bytes > maximum ? "discarded-size-limit" : "retained";
            if (availability != "retained") { File.Delete(path); }
            artifacts.Add(new(name, availability, bytes, availability == "retained" ? bytes : 0));
        }
        foreach (string entry in Directory.EnumerateFileSystemEntries(owned))
        {
            if (!passed && artifacts.Any(item => item.Name == Path.GetFileName(entry) && item.Availability == "retained")) { continue; }
            DeleteOwnedEntry(entry);
        }
        return artifacts.ToArray();

        static void DeleteOwnedEntry(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0) { File.Delete(path); return; }
            // Delete a link itself without enumerating its target.
            if ((attributes & FileAttributes.ReparsePoint) == 0)
            { foreach (string child in Directory.EnumerateFileSystemEntries(path)) { DeleteOwnedEntry(child); } }
            Directory.Delete(path);
        }
    }

    private static string Required(XElement element, string name) => !string.IsNullOrWhiteSpace((string?)element.Attribute(name)) ? element.Attribute(name)!.Value : throw new InvalidDataException("Missing native " + name + ".");
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
