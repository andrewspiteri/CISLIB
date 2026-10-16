using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging.Exceptions;

namespace CIS.Tests.DiscoveryFixture;

internal static class ExceptionProjectionProcessFixture
{
    internal static int Run(string mode)
    {
        Exception? source = Create(mode);
        int captureCalls = 0;
        captureCalls++;
        CapturedException? captured = ExceptionProjection.Capture(source);
        if (source is MutableException mutable)
        {
            mutable.Text = "after";
            mutable.Trace = "changed stack";
            mutable.Data["private"] = "after-data";
        }
        var result = new JsonObject
        {
            ["caseId"] = mode,
            ["exception"] = Project(captured),
            ["captureCalls"] = captureCalls,
            ["nodes"] = captured?.NodeCount ?? 0,
            ["depth"] = captured?.Depth ?? 0,
            ["messageReads"] = source is MutableException readMessage ? readMessage.MessageReads : null,
            ["stackReads"] = source is MutableException readStack ? readStack.StackReads : null,
            ["observed"] = new JsonObject
            {
                ["processId"] = System.Environment.ProcessId,
                ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
                ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
                ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
                ["fixtureSha256"] = Hash(typeof(ExceptionProjectionProcessFixture).Assembly.Location),
                ["coreSha256"] = Hash(typeof(ExceptionProjection).Assembly.Location)
            }
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    internal static int Boundary(string id)
    {
        JsonObject result = ExceptionBoundaryFixture.Execute(id);
        result["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(ExceptionProjectionProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(ExceptionProjection).Assembly.Location)
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    internal static int Content(string id)
    {
        JsonObject result = ExceptionContentFixture.Execute(id);
        result["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["lifecycleApiPresent"] = (typeof(EnvironmentOptions).GetMethod("Initialize") is not null),
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(ExceptionProjectionProcessFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(ExceptionProjection).Assembly.Location)
        };
        Console.WriteLine(result.ToJsonString());
        return 0;
    }

    private static Exception? Create(string mode) => mode switch
    {
        "plain" => new InvalidOperationException("plain😀\n"),
        "thrown" => ThrowMeasured(),
        "inner" => new InvalidOperationException("outer", new ArgumentException("inner")),
        "aggregate" => new AggregateException("aggregate", new Exception("first"), new Exception("second"), new Exception("third")),
        "mutation" => new MutableException(),
        "null" => null,
        _ => throw new InvalidDataException("Unknown exception capture scenario.")
    };

    // Development-only observation; the shipping event serializer is later roadmap work.
    private static JsonObject? Project(CapturedException? value)
    {
        if (value is null)
        {
            return null;
        }
        var result = new JsonObject { ["type"] = value.Type, ["message"] = value.Message };
        if (value.StackTrace is not null)
        {
            result["stack_trace"] = value.StackTrace;
        }
        if (value.InnerExceptions.Count > 0)
        {
            result["inner_exceptions"] = new JsonArray(value.InnerExceptions.Select(child => (JsonNode?)Project(child)).ToArray());
        }
        if (value.IsTruncated)
        {
            result["truncated"] = true;
        }
        return result;
    }

    private static Exception ThrowMeasured()
    {
        try
        {
            throw new InvalidOperationException("thrown");
        }
        catch (InvalidOperationException error)
        {
            return error;
        }
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class MutableException : Exception
    {
        internal string Text { get; set; } = "before";
        internal string? Trace { get; set; } = "captured stack";
        internal int MessageReads { get; private set; }
        internal int StackReads { get; private set; }
        public override string Message
        {
            get
            {
                MessageReads++;
                return Text;
            }
        }
        public override string? StackTrace
        {
            get
            {
                StackReads++;
                return Trace;
            }
        }
    }
}
