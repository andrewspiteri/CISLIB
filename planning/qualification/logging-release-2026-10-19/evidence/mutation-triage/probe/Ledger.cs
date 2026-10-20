using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Ledger
{
    internal static void Write(string originalPath, string serialPath, string outputPath)
    {
        if (File.Exists(outputPath)) { throw new IOException("Ledger output exists."); }
        using var original = JsonDocument.Parse(File.ReadAllBytes(originalPath));
        using var serial = JsonDocument.Parse(File.ReadAllBytes(serialPath));
        var second = Mutants(serial).ToDictionary(item => item.Mutant.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var rows = new List<object>();
        var counts = new Dictionary<string, int>();
        var secondCounts = new Dictionary<string, int>();
        int changed = 0, reviewed = 0;
        foreach (var (file, source, mutant) in Mutants(original))
        {
            string id = mutant.GetProperty("id").GetString()!;
            if (!second.Remove(id, out var other) || other.File != file || other.Source != source ||
                other.Mutant.GetProperty("replacement").GetString() != mutant.GetProperty("replacement").GetString() ||
                other.Mutant.GetProperty("location").ToString() != mutant.GetProperty("location").ToString())
            { throw new InvalidDataException("Report input mismatch: " + id); }
            string before = mutant.GetProperty("status").GetString()!, after = other.Mutant.GetProperty("status").GetString()!;
            counts[before] = counts.GetValueOrDefault(before) + 1;
            secondCounts[after] = secondCounts.GetValueOrDefault(after) + 1;
            if (before != after) { changed++; }
            if (before == "Killed" && after == "Killed") { continue; }
            if (before != "Killed") { reviewed++; }
            int line = mutant.GetProperty("location").GetProperty("start").GetProperty("line").GetInt32();
            string topic = Topic(Path.GetFileName(file), line);
            string action = after == "Killed" ? "ExistingTestsDetectAfterSerialization" :
                after == "CompileError" ? "ReviewNativeCompilerRollback" :
                after == "Ignored" ? "ReviewBlockFilterWithoutKillCredit" :
                after == "Timeout" ? "RetainTimeoutWithoutKillCredit" : "ReviewBehaviorAndRelevantTestClosure";
            rows.Add(new { id, file, sourceSha256 = Hash(Encoding.UTF8.GetBytes(source)),
                location = mutant.GetProperty("location"), replacement = mutant.GetProperty("replacement").GetString(),
                originalStatus = before, serializedStatus = after, topic, action,
                originalReason = Reason(mutant), serializedReason = Reason(other.Mutant),
                originalCoveringTests = Tests(original, mutant), serializedCoveringTests = Tests(serial, other.Mutant),
                manualReplay = id is "317" or "1270" or "1904" or "1950" or "4094" or "4402" or "1952" or "4092" or "4464"
                    ? "ExactStandaloneVariantFailsExistingTest" : null });
        }
        if (second.Count != 0 || reviewed != 187 || counts.Values.Sum() != 346) { throw new InvalidDataException("Unexpected selected inventory."); }
        File.WriteAllText(outputPath, JsonSerializer.Serialize(new
        {
            sourceCommit = "8254022298b8f42c2d7f2f01f1df5e418b44c4aa",
            originalReportSha256 = Hash(File.ReadAllBytes(originalPath)), serializedReportSha256 = Hash(File.ReadAllBytes(serialPath)),
            originalCounts = counts, serializedCounts = secondCounts, changedStatuses = changed,
            originalNonKilledReviewed = reviewed, note = "Native reports are unchanged. These rows include every original non-killed mutant plus any newly non-killed mutant. Actions are triage, not accepted score exclusions.", rows
        }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine($"Reviewed {reviewed} original non-killed mutants; {changed} native status changes; {rows.Count} ledger rows.");
    }

    private static IEnumerable<(string File, string Source, JsonElement Mutant)> Mutants(JsonDocument report)
    {
        foreach (var file in report.RootElement.GetProperty("files").EnumerateObject())
        {
            foreach (var mutant in file.Value.GetProperty("mutants").EnumerateArray())
            { yield return ("implementations/" + file.Name.Split("/implementations/", 2)[1], file.Value.GetProperty("source").GetString()!, mutant); }
        }
    }
    private static string Topic(string file, int line) => file switch
    {
        "CoreLogger.Interpolation.cs" => "API guards and conflict diagnostics",
        "CapturedInterpolation.cs" when line <= 28 => "Exact template and capture truncation",
        "CapturedInterpolation.cs" when line <= 58 => "Rendered content, empty tails and formatting capacity",
        "CapturedInterpolation.cs" => "Alignment, padding and render truncation",
        "InterpolationCapture.cs" when line <= 22 => "Literal capture and part limit",
        "InterpolationCapture.cs" when line <= 38 => "Owned span validation and scalar budget",
        "InterpolationCapture.cs" when line <= 47 => "Format limit and omitted values",
        "InterpolationCapture.cs" when line <= 85 => "User formatting, null fallback and safe errors",
        "InterpolationCapture.cs" when line <= 91 => "Part-count boundary",
        "InterpolationCapture.cs" when line <= 100 => "Owned text budget and Unicode",
        "InterpolationCapture.cs" => "Numeric precision guard",
        "IncomingBatchIndex.cs" => "Creation order, equal-time slot ties and stale references",
        "IncomingRingBuffer.cs" when line <= 28 => "Capacity, configuration and rotation origin",
        "IncomingRingBuffer.cs" when line <= 65 => "Closed admission and enrollment races",
        "IncomingRingBuffer.cs" when line <= 80 => "Bounded P01 scan and equal-time victims",
        "IncomingRingBuffer.cs" => "Actual ownership and abandonment accounting",
        _ => throw new InvalidDataException("Unreviewed file " + file)
    };
    private static string? Reason(JsonElement mutant) => mutant.TryGetProperty("statusReason", out var reason) ? reason.GetString() : null;
    private static string[] Tests(JsonDocument report, JsonElement mutant)
    {
        if (!mutant.TryGetProperty("coveredBy", out var covered)) { return []; }
        var ids = covered.EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.Ordinal);
        return report.RootElement.GetProperty("testFiles").EnumerateObject().SelectMany(file => file.Value.GetProperty("tests").EnumerateArray())
            .Where(test => ids.Contains(test.GetProperty("id").GetString())).Select(test => test.GetProperty("name").GetString()!).ToArray();
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
