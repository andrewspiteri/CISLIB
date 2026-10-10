using System.Globalization;
using System.Text.Json;

namespace CIS.Tests.ArchitectureTests.Contracts;

// Pure specification evaluator, never a shipping logger or queue implementation.
internal static class AdmissionContractEvaluator
{
    private static readonly string[] Levels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

    internal static JsonElement Evaluate(JsonElement input)
    {
        string level = input.GetProperty("level").GetString()!;
        bool closed = input.GetProperty("admissionClosed").GetBoolean();
        JsonElement[] outputs = input.GetProperty("outputs").EnumerateArray().Where(output => Text(output, "threshold") != "None").ToArray();
        bool? enabled = Array.IndexOf(Levels, level) < 0 ? null : !closed && level != "None" && outputs.Any(output => Interested(output, level) && Text(output, "state") == "Running");
        var result = new Decision { IsEnabled = enabled };
        if (input.GetProperty("argumentNull").GetBoolean())
        {
            result.Exception = "ArgumentNullException";
        }
        else if (Array.IndexOf(Levels, level) < 0)
        {
            result.Exception = "ArgumentOutOfRangeException";
        }
        else if (closed)
        {
            result.Stage = result.Outcome = "Stopped";
            result.Outputs = outputs.Select(output => Projection(output, "Stopped")).ToArray();
        }
        else if (!outputs.Any(output => Interested(output, level)))
        {
            result.Stage = result.Outcome = "Filtered";
            result.Outputs = outputs.Select(output => Projection(output, "Filtered")).ToArray();
        }
        else if (outputs.Where(output => Interested(output, level)).All(output => Text(output, "state") == "Faulted"))
        {
            result.Stage = "AllFaulted";
            result.Outcome = "Rejected";
            result.Outputs = outputs.Select(output => Projection(output, Interested(output, level) ? "Faulted" : "Filtered")).ToArray();
        }
        else
        {
            CaptureAndProject(input, outputs, level, result);
        }

        return JsonSerializer.SerializeToElement(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static void CaptureAndProject(JsonElement input, JsonElement[] outputs, string level, Decision result)
    {
        bool lazy = Text(input, "operation") == "WriteLazy";
        string capture = Text(input, "capture");
        result.FactoryCalls = lazy ? 1 : 0;
        if (lazy && capture == "FactoryThrows")
        {
            result.Stage = "Factory";
            result.Exception = "CallerFactoryException";
            return;
        }

        result.Captures = 1;
        if (capture is "Invalid" or "NullFactoryResult")
        {
            result.Stage = "InvalidCapture";
            result.Outcome = "InvalidEvent";
            result.ErrorCode = capture == "NullFactoryResult" ? "InvalidFactoryResult" : Text(input, "captureErrorCode");
            result.InvalidEventDelta = 1;
            return;
        }

        result.Stage = "Admission";
        result.IsTruncated = capture == "Truncated";
        result.TruncatedEventDelta = result.IsTruncated ? 1 : 0;
        result.Outputs = outputs.Select(output => Projection(output, Status(output, level))).ToArray();
        int accepted = result.Outputs.Count(output => output.Status == "Accepted");
        bool rejected = result.Outputs.Any(output => output.Status is "Faulted" or "QueueFull");
        result.Outcome = accepted == 0 ? "Rejected" : rejected ? "PartiallyAccepted" : "Accepted";
    }

    private static string Status(JsonElement output, string level)
    {
        if (!Interested(output, level))
        {
            return "Filtered";
        }

        if (Text(output, "state") == "Faulted")
        {
            return "Faulted";
        }

        return output.GetProperty("queueFull").GetBoolean() ? "QueueFull" : "Accepted";
    }

    private static OutputDecision Projection(JsonElement output, string status) => new()
    {
        Name = Text(output, "name"),
        Status = status,
        AcceptedSequence = status == "Accepted" ? checked(long.Parse(Text(output, "acceptedSequence"), CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture) : null
    };

    private static bool Interested(JsonElement output, string level) => level != "None" && Array.IndexOf(Levels, level) >= Array.IndexOf(Levels, Text(output, "threshold"));
    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;

    private sealed class Decision
    {
        public string Stage { get; set; } = "ArgumentValidation";
        public string? Exception { get; set; }
        public string? Outcome { get; set; }
        public string? ErrorCode { get; set; }
        public bool IsTruncated { get; set; }
        public int FactoryCalls { get; set; }
        public int Captures { get; set; }
        public int InvalidEventDelta { get; set; }
        public int TruncatedEventDelta { get; set; }
        public OutputDecision[] Outputs { get; set; } = [];
        public bool? IsEnabled { get; set; }
    }

    private sealed class OutputDecision
    {
        public required string Name { get; init; }
        public required string Status { get; init; }
        public string? AcceptedSequence { get; init; }
    }
}
