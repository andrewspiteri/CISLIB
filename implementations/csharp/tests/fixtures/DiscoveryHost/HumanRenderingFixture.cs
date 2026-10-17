using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Rendering;

namespace CIS.Tests.DiscoveryFixture;

internal static class HumanRenderingFixture
{
    private sealed class FixedException : Exception
    {
        internal FixedException() : base("failure\n\u001b[31m\u202e", new Exception("inner\r\ntext")) { }
        public override string? StackTrace => "frame1\nframe2\t\u001b[2J";
    }

    internal static JsonObject Execute(JsonObject row)
    {
        string mode = row["mode"]!.GetValue<string>();
        int cap = row["maxEntryBytes"]!.GetValue<int>();
        var metadata = EventQualificationFixture.Metadata();
        var data = EventQualificationFixture.Data();
        data.EventName = "human.event"; data.Message = row["message"]!.GetValue<string>(); data.Exception = new FixedException();
        if (mode == "controls")
        {
            metadata.ApplicationName = "App\u202e"; metadata.HostName = "host\u2066";
            data.EventName += "\u202d"; data.OperationId = "op\u200d";
            data.Attributes = new Dictionary<string, LogValue> { ["key\u202e\"\\"] = LogValue.String(data.Message) };
            metadata.OperatingSystemDescription = data.Message; metadata.RuntimeDescription = data.Message;
            data.MessageTemplate = data.Message;
        }
        if (mode is "absent" or "empty")
        {
            metadata.ProcessStartedAtUtc = null; metadata.ProcessId = null; metadata.HostName = null;
            metadata.OperatingSystemDescription = null; metadata.OSArchitecture = null;
            metadata.ProcessArchitecture = null; metadata.RuntimeDescription = null; metadata.CommitId = null; metadata.BuildId = null;
            data.Attributes = null; data.Exception = null; data.OperationId = null;
            data.MessageTemplate = mode == "empty" ? "" : null;
        }
        if (mode == "cap")
        {
            data.Message = new string('\u200d', 8192);
            data.Attributes = new Dictionary<string, LogValue> { ["payload"] = LogValue.String(new string('\u200d', 8192)) };
        }
        var output = new LoggerFoundationFixture.Output();
        var clock = new LoggerFoundationFixture.Clock();
        var runtime = new CoreLogRuntime(metadata.CaptureMetadata(), new LoggingOptions { Level = CoreLogLevel.Trace, MaxEntryBytes = cap }, output, null, clock);
        CoreLogger logger = runtime.CreateLogger(mode == "controls" ? "Human\u202e" : "Human");
        Activity? previous = Activity.Current; Activity.Current = null;
        try
        {
            using Activity? activity = mode is "absent" or "empty" ? null : new Activity("human").SetIdFormat(ActivityIdFormat.W3C).Start();
            LogWriteResult write = logger.Write((CoreLogLevel)row["level"]!.GetValue<int>(), data);
            var captured = output.Events.Single();
            byte[] before = captured.JsonLine.ToArray();
            data.Message = "changed"; data.Exception = null; data.Attributes = null; metadata.ApplicationName = "changed";
            RenderedHumanEvent normal = HumanRenderer.Render(captured, HumanOutputDetail.Normal, cap);
            RenderedHumanEvent verbose = HumanRenderer.Render(captured, HumanOutputDetail.Verbose, cap);
            return new JsonObject
            {
                ["caseId"] = row["id"]!.DeepClone(),
                ["json"] = Convert.ToBase64String(before),
                ["normal"] = Convert.ToBase64String(normal.Line.Span),
                ["verbose"] = Convert.ToBase64String(verbose.Line.Span),
                ["captureTruncated"] = write.IsTruncated,
                ["normalTruncated"] = normal.IsTruncated,
                ["verboseTruncated"] = verbose.IsTruncated,
                ["captureUnchanged"] = before.AsSpan().SequenceEqual(captured.JsonLine.Span),
                ["clockCalls"] = clock.Calls
            };
        }
        finally { Activity.Current = previous; }
    }

    internal static int Run(string path, string id)
    {
        byte[] inputBytes = File.ReadAllBytes(path);
        JsonObject row = JsonNode.Parse(inputBytes)!["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = Execute(row); HumanRenderingExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unknown",
            ["fixtureSha256"] = Hash(typeof(HumanRenderingFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(LogEventData).Assembly.Location),
            ["inputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(inputBytes))
        };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
