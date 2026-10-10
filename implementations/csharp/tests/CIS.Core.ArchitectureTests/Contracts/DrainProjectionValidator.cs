using System.Globalization;
using System.Text.Json;

namespace CIS.Tests.ArchitectureTests.Contracts;

// Validates already observed result projections, not live workers or atomic snapshots.
internal static class DrainProjectionValidator
{
    internal static bool Accepts(JsonElement fixture)
    {
        try
        {
            return Validate(fixture.GetProperty("boundaryContext"), fixture.GetProperty("result"));
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            return false;
        }
    }

    private static bool Validate(JsonElement context, JsonElement result)
    {
        string captured = Text(result, "boundaryCapturedAtUtc");
        if (captured != Text(context, "capturedAtUtc") || !IsUtc(captured) || !IsUtc(Text(result, "observedAtUtc")))
        {
            return false;
        }

        long invalid = Counter(result, "invalidEventCount");
        long truncated = Counter(result, "truncatedEventCount");
        if (invalid != Counter(context, "invalidEventCount") || truncated != Counter(context, "truncatedEventCount"))
        {
            return false;
        }

        bool completed = true;
        bool succeeded = true;
        bool loss = invalid > 0 || truncated > 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        JsonElement boundaries = context.GetProperty("outputs");
        foreach (JsonElement output in result.GetProperty("outputs").EnumerateArray())
        {
            string name = Text(output, "outputName");
            if (!names.Add(name) || !boundaries.TryGetProperty(name, out JsonElement boundary))
            {
                return false;
            }

            long accepted = Counter(output, "acceptedSequenceBoundary");
            long written = Counter(output, "written");
            long failed = Counter(output, "failed");
            long abandoned = Counter(output, "abandoned");
            long pending = Counter(output, "pending");
            if (accepted != Counter(boundary, "acceptedSequenceBoundary") || accepted != checked(written + failed + abandoned + pending))
            {
                return false;
            }

            string flush = Text(output, "writerFlushStatus");
            if (flush is not ("NotRequested" or "Pending" or "Succeeded" or "Failed") || output.GetProperty("writerFlushSucceeded").GetBoolean() != (flush == "Succeeded"))
            {
                return false;
            }

            JsonElement rejections = output.GetProperty("rejections");
            JsonElement initialRejections = boundary.GetProperty("rejections");
            foreach (string counter in new[] { "filtered", "queueFull", "unavailable" })
            {
                if (Counter(rejections, counter) != Counter(initialRejections, counter))
                {
                    return false;
                }
            }

            completed &= pending == 0 && flush is "Succeeded" or "Failed";
            succeeded &= pending == 0 && failed == 0 && abandoned == 0 && flush == "Succeeded";
            loss |= failed > 0 || abandoned > 0 || flush == "Failed" || Counter(rejections, "queueFull") > 0 || Counter(rejections, "unavailable") > 0;
        }

        return names.Count == boundaries.EnumerateObject().Count()
            && Text(result, "completion") == (completed ? "Completed" : "TimedOut")
            && result.GetProperty("succeeded").GetBoolean() == succeeded
            && result.GetProperty("lossObserved").GetBoolean() == loss;
    }

    private static long Counter(JsonElement element, string property)
    {
        string value = Text(element, property);
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long count) || count < 0 || count.ToString(CultureInfo.InvariantCulture) != value)
        {
            throw new InvalidDataException("Expected an exact nonnegative Int64 counter: " + property);
        }

        return count;
    }

    private static bool IsUtc(string value) => value.EndsWith('Z') && DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);
    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
