using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

// Literal expected trees are independent of Core's capture/budget algorithm.
internal static class ExceptionBoundaryExpectations
{
    internal static JsonObject ForCase(string id)
    {
        JsonObject tree = Tree(id);
        var pending = new Stack<(JsonObject Node, int Depth)>();
        pending.Push((tree, 1));
        var labels = new List<string>();
        int depth = 0;
        bool loss = false;
        while (pending.TryPop(out var item))
        {
            labels.Add(item.Node["message"]!.GetValue<string>());
            depth = Math.Max(depth, item.Depth);
            loss |= item.Node["truncated"]?.GetValue<bool>() ?? false;
            if (item.Node["inner_exceptions"] is JsonArray children)
            {
                foreach (JsonNode? child in children.Reverse())
                {
                    pending.Push((child!.AsObject(), item.Depth + 1));
                }
            }
        }
        return new JsonObject
        {
            ["exception"] = tree,
            ["nodes"] = labels.Count,
            ["depth"] = depth,
            ["hasTruncatedContent"] = loss,
            ["preorder"] = new JsonArray(labels.Select(label => (JsonNode?)JsonValue.Create(label)).ToArray())
        };
    }

    internal static void Verify(string id, JsonObject actual)
    {
        foreach ((string name, JsonNode? value) in ForCase(id))
        {
            if (!JsonNode.DeepEquals(value, actual[name]))
            {
                throw new InvalidDataException("Exception boundary differs: " + name);
            }
        }
        if (actual["cachedNodes"]!.GetValue<int>() != actual["nodes"]!.GetValue<int>() ||
            actual["cachedDepth"]!.GetValue<int>() != actual["depth"]!.GetValue<int>())
        {
            throw new InvalidDataException("Cached exception shape differs from independent traversal.");
        }
    }

    internal static void VerifyReads(string id, JsonObject actual)
    {
        JsonArray reads = actual["reads"]!.AsArray();
        IReadOnlyList<ExceptionReadProbe> probes = ExceptionBoundarySources.Create(id).Probes;
        string[] labels = ForCase(id)["preorder"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
        if (reads.Count != probes.Count)
        {
            throw new InvalidDataException("Missing exception accessor observations.");
        }
        for (int index = 0; index < probes.Count; index++)
        {
            string label = probes[index].Label;
            int expected = id.StartsWith("message-", StringComparison.Ordinal) || id.StartsWith("stack-", StringComparison.Ordinal)
                ? 1 : labels.Count(value => value == label);
            JsonNode read = reads[index]!;
            if (read["label"]!.GetValue<string>() != label ||
                read["message"]!.GetValue<int>() != expected || read["stack"]!.GetValue<int>() != expected)
            {
                throw new InvalidDataException("Exception accessor observation differs.");
            }
        }
    }

    private static JsonObject Tree(string id)
    {
        string[] parts = id.Split('-');
        if (parts[0] == "nodes")
        {
            int count = int.Parse(parts[1], CultureInfo.InvariantCulture);
            return Node("root", Leaves("n", Math.Min(count, 15)), aggregate: true, clipped: count > 15);
        }
        if (parts[0] == "depth" && id != "depth-sibling")
        {
            int count = int.Parse(parts[1], CultureInfo.InvariantCulture);
            return Chain(Enumerable.Range(1, Math.Min(count, 4)).Select(index => $"d{index}").ToArray(), clipped: count > 4);
        }
        if (parts[0] is "message" or "stack")
        {
            int count = int.Parse(parts[2], CultureInfo.InvariantCulture);
            int limit = ExceptionBoundarySources.Limit(parts[0]);
            string prefix = ExceptionBoundarySources.Text(parts[1], Math.Min(count, limit));
            return Node(parts[0] == "message" ? prefix : "text", clipped: count > limit, stack: parts[0] == "stack" ? prefix : null);
        }
        return id switch
        {
            "mixed-exact" => Node("root", [Node("a", Leaves("a", 6), true), Node("b", Leaves("b", 7), true)], true),
            "mixed-over" => Node("root", [Node("a", Leaves("a", 8), true), Node("b", Leaves("b", 5), true, true)], true, true),
            "depth-sibling" => Node("root", [Chain(["c1", "c2", "c3"], true), Node("tail")], true),
            "shared" => Node("root", Enumerable.Range(0, 15).Select(_ => Node("shared")).ToArray(), true),
            "empty-depth" => Chain(["d1", "d2", "d3", "empty"], aggregateLeaf: true),
            _ => throw new InvalidDataException("Unknown boundary expectation.")
        };
    }

    private static JsonObject[] Leaves(string prefix, int count) =>
        Enumerable.Range(0, count).Select(index => Node(prefix + index.ToString(CultureInfo.InvariantCulture))).ToArray();

    private static JsonObject Chain(string[] labels, bool clipped = false, bool aggregateLeaf = false)
    {
        JsonObject value = Node(labels[^1], aggregate: aggregateLeaf, clipped: clipped);
        foreach (string label in labels[..^1].Reverse())
        {
            value = Node(label, [value]);
        }
        return value;
    }

    private static JsonObject Node(string label, JsonObject[]? children = null, bool aggregate = false, bool clipped = false, string? stack = null)
    {
        var value = new JsonObject
        {
            ["type"] = "CIS.Tests.DiscoveryFixture.ExceptionBoundarySources+" + (aggregate ? "TrackedAggregate" : "TrackedException"),
            ["message"] = label
        };
        if (children is { Length: > 0 })
        {
            value["inner_exceptions"] = new JsonArray(children.Cast<JsonNode?>().ToArray());
        }
        if (clipped)
        {
            value["truncated"] = true;
        }
        if (stack is not null)
        {
            value["stack_trace"] = stack;
        }
        return value;
    }
}
