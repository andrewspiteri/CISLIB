using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

// Filesystem setup stages precise interruption states; actual public creation performs recovery.
internal static class LoggingReviewFixture
{
    internal static int Run(string mode, string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            JsonObject report = mode == "cleanup-denied" ? CleanupDenied(root) : RecoverAsync(mode, root).GetAwaiter().GetResult();
            report["mode"] = mode; report["pid"] = Environment.ProcessId;
            report["runtime"] = RuntimeInformation.FrameworkDescription;
            report["os"] = OperatingSystem.IsWindows() ? "windows" : "linux";
            report["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location);
            report["fixtureSha256"] = Hash(typeof(LoggingReviewFixture).Assembly.Location);
            Console.WriteLine(report.ToJsonString());
            return 0;
        }
        catch (Exception error)
        {
            // A process-boundary handler also guarantees normal exception unwinding:
            // an otherwise unhandled synchronous exception may skip finally on exit.
            Console.Error.WriteLine(error);
            return 1;
        }
    }
    private static async Task<JsonObject> RecoverAsync(string mode, string root)
    {
        bool recovery = !mode.StartsWith("rotation-", StringComparison.Ordinal);
        string stage = mode[(mode.IndexOf('-', StringComparison.Ordinal) + 1)..];
        int retained = int.TryParse(stage, out int selected) ? selected : 3;
        int limit = int.TryParse(stage, out _) ? retained : 1;
        string path = Path.Combine(root, "active.jsonl");
        Guid group = Seed(path, retained, recovery);
        string neighbor = Path.Combine(root, "foreign.closed"); File.WriteAllText(neighbor, "foreign evidence");
        byte[] original = File.ReadAllBytes(path);
        if (!int.TryParse(stage, out _))
        {
            using var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines);
            FileGroupInventory next = recovery ? owner.Inventory!.AfterPartialRecovery(path, limit) : owner.Inventory!.AfterRotation(path, limit);
            FileGroupTransition intent = FileGroupTransition.Begin(owner, next, limit, recovery);
            if (stage == "denied")
            {
                IDisposable? held = null;
                UnixFileMode savedMode = default;
                string victim = Path.Combine(root, owner.Inventory!.Segments[0].FileName);
                if (OperatingSystem.IsWindows()) { held = new FileStream(victim, FileMode.Open, FileAccess.Read, FileShare.Read); }
                else { savedMode = File.GetUnixFileMode(root); File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute); }
                try
                {
                    try { intent.Retire(path); throw new InvalidOperationException("Deletion unexpectedly succeeded."); }
                    catch (FileOutputWriteException error) when (error.FaultCode == "RetentionDeleteFailed") { }
                    Require(File.Exists(FileGroupTransition.PendingPath(path)) && File.Exists(victim));
                    Require(Directory.EnumerateFiles(root, "*.damaged").Count() == 0);
                    Require(File.ReadAllBytes(victim).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes("{\"old\":1}\n")));
                }
                finally
                {
                    held?.Dispose();
                    if (!OperatingSystem.IsWindows()) { File.SetUnixFileMode(root, savedMode); }
                }
            }
            else
            {
                int deletions = stage == "intent" ? 0 : stage == "retire-one" ? 1 : stage == "retire-two" ? 2 : 3;
                foreach (FileInventorySegment member in owner.Inventory!.Segments.Take(deletions)) { File.Delete(Path.Combine(root, member.FileName)); }
                if (stage is "renamed" or "published" or "fresh")
                {
                    owner.ActiveFile.Dispose();
                    File.Move(path, Path.Combine(root, next.Segments[^2].FileName));
                    if (stage is "published" or "fresh") { next.PublishReplacement(path); }
                    if (stage == "fresh") { File.WriteAllBytes(path, []); }
                }
            }
        }
        LoggingOptions options = EnvironmentOptions.Logging;
        options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = CoreLogLevel.Information;
        options.FilePath = path; options.FileFormat = LogOutputFormat.JsonLines; options.FileRetainedFiles = limit;
        options.FileMaxBytes = 1048576; options.MaxEntryBytes = 65536;
        await using CoreLogRuntime runtime = CoreLogRuntime.Create();
        Require(runtime.GetDiagnostics().Outputs.Single().RecoveredPartialSegment == recovery);
        Require(runtime.CreateLogger("PublicReview").Write(CoreLogLevel.Information, new LogEventData { EventName = "review.append", Message = "fresh" }).Outcome == LogWriteOutcome.Accepted);
        Require((await runtime.StopAsync(CancellationToken.None)).Succeeded);
        string archived = Path.Combine(root, FileGroupInventory.ClosedName(group, retained + 1, recovery ? FileSegmentState.Damaged : FileSegmentState.Closed));
        Require(original.AsSpan().SequenceEqual(File.ReadAllBytes(archived)) && File.ReadAllText(neighbor) == "foreign evidence");
        Require(!File.Exists(FileGroupTransition.PendingPath(path)));
        return new JsonObject
        {
            ["path"] = path,
            ["archived"] = archived,
            ["originalSha256"] = Convert.ToHexStringLower(SHA256.HashData(original)),
            ["limit"] = limit,
            ["inventory"] = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json")),
            ["publicCreate"] = true
        };
    }

    private static JsonObject CleanupDenied(string root)
    {
        string path = Path.Combine(root, "metadata.json");
        string candidate = FileMetadataPublication.CandidatePath(path);
        string foreign = path + ".foreign.tmp"; File.WriteAllText(foreign, "preserve");
        IDisposable? held = null; UnixFileMode savedMode = default;
        if (!OperatingSystem.IsWindows()) { savedMode = File.GetUnixFileMode(root); }
        try
        {
            try
            {
                FileMetadataPublication.Publish(path, writer =>
                {
                    writer.WriteStartObject(); writer.WriteNumber("private", 1); writer.WriteEndObject();
                }, overwrite: false, beforePublish: closedCandidate =>
                {
                    if (OperatingSystem.IsWindows()) { held = new FileStream(closedCandidate, FileMode.Open, FileAccess.Read, FileShare.Read); }
                    else { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute); }
                    // Explicit injected failure; cleanup is a real OS deletion attempt.
                    throw new IOException("Controlled publication failure after candidate close.");
                });
                throw new InvalidOperationException("Publication unexpectedly succeeded.");
            }
            catch (CoreLogInitializationException error) when (error.ErrorCode == "FileMetadataCandidateCleanupFailed") { }
            for (int i = 0; i < 20; i++)
            {
                try { FileMetadataPublication.Publish(path, _ => throw new InvalidOperationException("Occupied slot was opened."), false); throw new InvalidOperationException("Occupied slot was replaced."); }
                catch (CoreLogInitializationException error) when (error.ErrorCode == "FileMetadataCandidateUnavailable") { }
            }
            Require(Directory.EnumerateFiles(root, "*.candidate.tmp").Count() == 1 && !File.Exists(path));
            Require(File.ReadAllText(foreign) == "preserve");
        }
        finally
        {
            held?.Dispose();
            if (!OperatingSystem.IsWindows()) { File.SetUnixFileMode(root, savedMode); }
        }
        return new JsonObject
        {
            ["candidate"] = candidate,
            ["candidateCount"] = 1,
            ["attempts"] = 21,
            ["foreignPreserved"] = true,
            ["control"] = "injected pre-move publication failure; actual OS cleanup denial"
        };
    }
    private static Guid Seed(string path, int retained, bool partial)
    {
        using (FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { }
        JsonObject inventory = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
        Guid group = Guid.ParseExact(inventory["groupId"]!.GetValue<string>(), "N"); JsonArray segments = inventory["segments"]!.AsArray();
        segments.Clear();
        for (int id = 1; id <= retained; id++)
        {
            string name = FileGroupInventory.ClosedName(group, id, FileSegmentState.Closed);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, name), "{\"old\":" + id + "}\n");
            segments.Add(new JsonObject { ["id"] = id, ["fileName"] = name, ["state"] = "closed" });
        }
        segments.Add(new JsonObject { ["id"] = retained + 1, ["fileName"] = Path.GetFileName(path), ["state"] = "active" });
        File.WriteAllText(path + ".cis.inventory.json", inventory.ToJsonString());
        File.WriteAllText(path, partial ? "{\"prior\":true}\n{\"partial\":" : "{\"prior\":true}\n");
        return group;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool value) { if (!value) { throw new InvalidDataException("Logging review fixture invariant failed."); } }
}
