using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileAcquisitionFixture
{
    internal static int Run(string mode, string path)
    {
        if (mode == "disabled-locks") { AppContext.SetSwitch("System.IO.DisableFileLocking", true); }
        try
        {
            using var owner = FileOutputOwner.Initialize(path);
            Report("acquired", null, owner.ActiveFile.Length);
            if (mode == "hold")
            {
                if (Console.ReadLine() != "release") { return 3; }
                owner.Dispose();
                Report("released", null, null);
            }
            return 0;
        }
        catch (CoreLogInitializationException error)
        {
            Report("failed", new JsonObject
            {
                ["output"] = error.OutputName,
                ["code"] = error.ErrorCode,
                ["message"] = error.Message,
                ["inner"] = error.InnerException is not null,
                ["dataCount"] = error.Data.Count
            }, null);
            return 0;
        }
    }

    private static void Report(string status, JsonObject? error, long? length)
    {
        string host = typeof(FileAcquisitionFixture).Assembly.Location, core = typeof(CoreLogRuntime).Assembly.Location;
        Console.WriteLine(new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["status"] = status,
            ["error"] = error,
            ["length"] = length,
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["os"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "other",
            ["coreSha256"] = Hash(core),
            ["fixtureSha256"] = Hash(host)
        }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
        Console.Out.Flush();
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
