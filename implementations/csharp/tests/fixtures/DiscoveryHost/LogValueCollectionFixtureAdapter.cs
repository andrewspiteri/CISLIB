using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

// Descriptor generation and independent output measurement; never a Core projection algorithm.
internal static class LogValueCollectionFixtureAdapter
{
    internal static JsonObject Execute(JsonObject row)
    {
        JsonObject input = row["input"]!.AsObject();
        string kind = input["kind"]!.GetValue<string>();
        string scenario = input["scenario"]!.GetValue<string>();
        var observed = new LogValueObservedInputs();
        LogValue? sourceChild = null;
        LogValue value;
        switch (scenario)
        {
            case "flat":
                value = Collection(kind, Enumerable.Repeat(LogValue.Integer(42), input["count"]!.GetValue<int>()).ToArray());
                break;
            case "chain":
                value = LogValue.Integer(42);
                for (int depth = 1; depth < input["depth"]!.GetValue<int>(); depth++)
                {
                    value = Collection(kind, [value]);
                }
                break;
            case "infinite":
                value = kind == "array" ? LogValue.Array(observed.Sequence()) : LogValue.Map(observed.Map());
                break;
            case "nested":
                int childItems = input["childItems"]!.GetValue<int>();
                sourceChild = LogValue.Array(Enumerable.Repeat(LogValue.Integer(42), childItems));
                int groups = input["groups"]!.GetValue<int>();
                LogValue[] children = Enumerable.Repeat(sourceChild, groups).ToArray();
                children[^1] = LogValue.Array(Enumerable.Repeat(LogValue.Integer(42), input["lastItems"]!.GetValue<int>()));
                value = Collection(kind, children);
                break;
            case "prior":
                sourceChild = LogValue.Array(Enumerable.Repeat(LogValue.Integer(42), 1024));
                value = Collection(kind, [sourceChild]);
                break;
            default:
                throw new InvalidDataException("Unknown collection fixture descriptor.");
        }
        (int nodes, int maximumDepth) = Measure(value, depth: 1);
        LogValue[] retained = Children(value).ToArray();
        LogValue deepest = value;
        while (Children(deepest).FirstOrDefault() is { } child)
        {
            deepest = child;
        }
        var actual = new JsonObject
        {
            ["caseId"] = row["id"]!.DeepClone(),
            ["kind"] = value.Kind.ToString(),
            ["nodes"] = nodes,
            ["depth"] = maximumDepth,
            ["rootCount"] = retained.Length,
            ["isTruncated"] = value.IsTruncated,
            ["deepestKind"] = deepest.Kind.ToString(),
            ["metadataMatchesMeasuredShape"] = value.NodeCount == nodes && value.Depth == maximumDepth,
            ["json"] = LogValueFixtureAdapter.Project(value)
        };
        if (retained.LastOrDefault() is { Kind: LogValueKind.Array or LogValueKind.Map } last)
        {
            actual["lastChildCount"] = Children(last).Count();
        }
        if (sourceChild is not null)
        {
            actual["sourceChildCount"] = Children(sourceChild).Count();
            actual["sourceUnchanged"] = Children(sourceChild).All(child => child.Kind == LogValueKind.Integer && child.Scalar.Integer == 42);
        }
        if (scenario == "infinite")
        {
            actual["produced"] = observed.Produced;
            actual["enumerations"] = observed.Enumerations;
            actual["disposed"] = observed.Disposed;
        }
        Validate(row, actual);
        return actual;
    }

    internal static void Validate(JsonObject row, JsonObject actual)
    {
        foreach ((string key, JsonNode? expected) in row["expected"]!.AsObject())
        {
            if (!JsonNode.DeepEquals(expected, actual[key]))
            {
                throw new InvalidDataException("Retained collection observation differs: " + key);
            }
        }
        if (!actual["metadataMatchesMeasuredShape"]!.GetValue<bool>())
        {
            throw new InvalidDataException("Internal shape metadata differs from independent traversal.");
        }
    }

    private static LogValue Collection(string kind, LogValue[] children)
    {
        if (kind == "array")
        {
            return LogValue.Array(children);
        }
        var map = new Dictionary<string, LogValue>(StringComparer.Ordinal);
        for (int index = 0; index < children.Length; index++)
        {
            map.Add("k" + index.ToString("D4", CultureInfo.InvariantCulture), children[index]);
        }
        return LogValue.Map(map);
    }

    private static IEnumerable<LogValue> Children(LogValue value) => value.Kind switch
    {
        LogValueKind.Array => value.ArrayItems,
        LogValueKind.Map => value.MapProperties.Values,
        _ => []
    };

    private static (int Nodes, int Depth) Measure(LogValue value, int depth)
    {
        int nodes = 1;
        int maximumDepth = depth;
        foreach (LogValue child in Children(value))
        {
            (int childNodes, int childDepth) = Measure(child, depth + 1);
            nodes += childNodes;
            maximumDepth = Math.Max(maximumDepth, childDepth);
        }
        if (value.NodeCount != nodes || value.Depth != maximumDepth - depth + 1 ||
            !value.IsTruncated && Children(value).Any(child => child.IsTruncated))
        {
            throw new InvalidDataException("A retained node has inconsistent shape/provenance metadata.");
        }
        return (nodes, maximumDepth);
    }
}
