using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileInventoryFixture
{
    internal static int Run(string mode, string path)
    {
        LogOutputFormat format = mode.EndsWith("human", StringComparison.Ordinal) ? LogOutputFormat.Human : LogOutputFormat.JsonLines;
        try
        {
            using var owner = FileOutputOwner.Initialize(path, format);
            Report("acquired", owner, null);
            if (mode.StartsWith("hold-", StringComparison.Ordinal))
            {
                if (Console.ReadLine() != "release") { return 3; }
                owner.Dispose(); Report("released", null, null);
            }
            return 0;
        }
        catch (CoreLogInitializationException error)
        {
            Report("failed", null, new JsonObject
            {
                ["output"] = error.OutputName,
                ["code"] = error.ErrorCode,
                ["message"] = error.Message,
                ["inner"] = error.InnerException is not null,
                ["dataCount"] = error.Data.Count
            });
            return 0;
        }
    }
    private static void Report(string status, FileOutputOwner? owner, JsonObject? error)
    {
        string host = typeof(FileInventoryFixture).Assembly.Location, core = typeof(CoreLogRuntime).Assembly.Location;
        FileGroupInventory? inventory = owner?.Inventory;
        Console.WriteLine(new JsonObject
        {
            ["status"] = status,
            ["error"] = error,
            ["length"] = owner?.ActiveFile.Length,
            ["created"] = owner?.ActiveWasCreated,
            ["groupId"] = inventory?.GroupId.ToString("N"),
            ["segmentId"] = inventory?.Active.Id,
            ["segments"] = inventory?.Segments.Count,
            ["format"] = inventory is null ? null : inventory.Format == LogOutputFormat.JsonLines ? "jsonl" : "human",
            ["inventorySha256"] = owner is null ? null : Hash(owner.AbsolutePath + ".cis.inventory.json"),
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["coreSha256"] = Hash(core),
            ["fixtureSha256"] = Hash(host)
        }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
        Console.Out.Flush();
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
