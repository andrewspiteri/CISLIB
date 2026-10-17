using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Rendering;

namespace CIS.Tests.DiscoveryFixture;

// Actual capture/admission plus explicit rendering. Admission controls perform no I/O or delivery.
internal static class PresentationFixture
{
    internal const string Message = "literal {user} {missing:format} unmatched { } quote \" slash \\\n\u001b[2J caf\u00e9 \U0001f600\u202e";
    internal const string Template = "raw {user} {missing:format} unmatched { {{ }}\n\u001b[31m";
    internal static JsonObject Execute(JsonObject row)
    {
        int consoleLevel = row["consoleLevel"]!.GetValue<int>(), fileLevel = row["fileLevel"]!.GetValue<int>();
        var options = new LoggingOptions
        {
            Level = CoreLogLevel.Information,
            ConsoleLevel = (CoreLogLevel)consoleLevel,
            FileLevel = (CoreLogLevel)fileLevel,
            FilePath = Path.Combine(Directory.GetCurrentDirectory(), "cis-presentation-no-file-created.jsonl"),
            ConsoleFormat = (LogOutputFormat)row["consoleFormat"]!.GetValue<int>(),
            FileFormat = (LogOutputFormat)row["fileFormat"]!.GetValue<int>(),
            HumanDetail = (HumanOutputDetail)row["detail"]!.GetValue<int>(),
            ConsoleStream = (ConsoleOutputStream)row["stream"]!.GetValue<int>()
        };
        var console = new LoggerFoundationFixture.Output(); var file = new LoggerFoundationFixture.Output();
        var clock = new LoggerFoundationFixture.Clock(); var metadata = EventQualificationFixture.Metadata(); metadata.ApplicationName = "Presentation.App";
        var runtime = new CoreLogRuntime(metadata.CaptureMetadata(), options, options.ConsoleEnabled ? console : null, options.FileEnabled ? file : null, clock);
        CoreLogger logger = runtime.CreateLogger("Presentation");
        var data = new LogEventData
        {
            EventName = "presentation.event",
            Message = Message,
            MessageTemplate = Template,
            Attributes = new Dictionary<string, LogValue> { ["user"] = LogValue.String("CIS_MUST_NOT_INTERPOLATE"), ["number"] = LogValue.Integer(42) }
        };
        int calls = 0; var outcomes = new JsonArray(); var enabled = new JsonArray();
        Activity? previous = Activity.Current; Activity.Current = null;
        try
        {
            JsonObject sampled;
            using (Activity activity = EventQualificationFixture.StartActivity("presentation-before"))
            using (runtime.Scopes.BeginScope(new OperationContextData { OperationId = "before-operation" }))
            {
                sampled = EventQualificationFixture.Sample(activity);
                for (int level = 0; level <= 6; level++)
                {
                    enabled.Add(logger.IsEnabled((CoreLogLevel)level));
                    outcomes.Add(LoggerFoundationFixture.Observe(logger.WriteLazy((CoreLogLevel)level, () => { calls++; return data; })));
                }
            }
            // Mutate caller, ambient context and caller policy after capture. Choice values below
            // are explicit arguments from the case, not a claim of runtime presentation composition.
            metadata.ApplicationName = "changed"; data.Message = "changed"; data.MessageTemplate = "changed"; data.Attributes = null;
            options.ConsoleFormat = LogOutputFormat.JsonLines; options.FileFormat = LogOutputFormat.Human;
            options.HumanDetail = HumanOutputDetail.Verbose; options.ConsoleStream = ConsoleOutputStream.StandardOutput;
            using Activity replacement = new Activity("presentation-after").SetIdFormat(ActivityIdFormat.W3C).Start();
            using (runtime.Scopes.BeginScope(new OperationContextData { OperationId = "after-operation" }))
            {
                JsonArray consoleFrames = Frames(console, (LogOutputFormat)row["consoleFormat"]!.GetValue<int>(), (HumanOutputDetail)row["detail"]!.GetValue<int>());
                JsonArray fileFrames = Frames(file, (LogOutputFormat)row["fileFormat"]!.GetValue<int>(), (HumanOutputDetail)row["detail"]!.GetValue<int>());
                return new JsonObject
                {
                    ["caseId"] = row["id"]!.DeepClone(),
                    ["enabled"] = enabled,
                    ["outcomes"] = outcomes,
                    ["factoryCalls"] = calls,
                    ["clockCalls"] = clock.Calls,
                    ["invalidCount"] = runtime.InvalidEventCount,
                    ["truncatedCount"] = runtime.TruncatedEventCount,
                    ["consoleFrames"] = consoleFrames,
                    ["fileFrames"] = fileFrames,
                    ["sharedCaptures"] = console.Events.Count(item => file.Events.Any(other => ReferenceEquals(item, other))),
                    ["consoleFiltered"] = console.Filtered,
                    ["fileFiltered"] = file.Filtered,
                    ["sampled"] = sampled,
                    ["afterSampled"] = EventQualificationFixture.Sample(replacement),
                    ["ambientOperation"] = runtime.Scopes.Capture().OperationId,
                    ["outputRedirected"] = Console.IsOutputRedirected,
                    ["errorRedirected"] = Console.IsErrorRedirected,
                    ["debuggerAttached"] = Debugger.IsAttached
                };
            }
        }
        finally { Activity.Current = previous; }
    }

    private static JsonArray Frames(LoggerFoundationFixture.Output output, LogOutputFormat format, HumanOutputDetail detail)
    {
        var frames = new JsonArray();
        foreach (EncodedEvent captured in output.Events)
        {
            byte[] original = captured.JsonLine.ToArray();
            RenderedEvent view = EventRenderer.Render(captured, format, detail, 65536);
            frames.Add(new JsonObject
            {
                ["source"] = Convert.ToBase64String(original),
                ["after"] = Convert.ToBase64String(captured.JsonLine.Span),
                ["line"] = Convert.ToBase64String(view.Line.Span),
                ["truncated"] = view.IsTruncated,
                ["jsonMemoryReused"] = view.Line.Equals(captured.JsonLine)
            });
        }
        return frames;
    }
    internal static int Run(string path, string id)
    {
        byte[] inputBytes = File.ReadAllBytes(path);
        JsonObject row = JsonNode.Parse(inputBytes)!["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = Execute(row); PresentationExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unknown",
            ["fixtureSha256"] = Hash(typeof(PresentationFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(LogEventData).Assembly.Location),
            ["inputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(inputBytes))
        };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
