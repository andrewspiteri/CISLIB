using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ExceptionContentExpectations
{
    internal static JsonObject Tree(string id)
    {
        if (id.StartsWith("data-", StringComparison.Ordinal))
        {
            return BclTree(id[5..]);
        }
        if (id == "retention")
        {
            return BclTree("aggregate");
        }
        return id switch
        {
            "complete" => Node("root"),
            "message" => Node(new string('m', 4096), clipped: true),
            "stack" => Node("root", clipped: true, stack: new string('s', 16384)),
            "both" => Node(new string('m', 4096), clipped: true, stack: new string('s', 16384)),
            "child-message" => Node("root", [Node(new string('m', 4096), clipped: true)]),
            "child-stack" => Node("root", [Node("child", clipped: true, stack: new string('s', 16384))]),
            "child-both" => Node("root", [Node(new string('m', 4096), clipped: true, stack: new string('s', 16384))]),
            "root-child" => Node(new string('m', 4096), [Node(new string('m', 4096), clipped: true)], clipped: true),
            "depth" => Chain(["d1", "d2", "d3", "d4"]),
            "depth-sibling" => Node("root", [Chain(["d1", "d2", "d3"]), Node("tail")], aggregate: true),
            "nodes" => Node("root", Leaves("n", 15), aggregate: true, clipped: true),
            "mixed-nodes" => Node("root", [Node("a", Leaves("a", 8), true), Node("b", Leaves("b", 5), true, true)], true, true),
            "nodes-last-branch" => Node("root", [Node("a", Leaves("a", 8), true), Node("b", Leaves("b", 5), true, true)], aggregate: true),
            "combined" => Combined(),
            "empty-stack" => Node("root", stack: string.Empty),
            "null-message" => Node(string.Empty),
            _ => throw new InvalidDataException("Unknown content expectation.")
        };
    }

    internal static string[] OmittedLabels(string id) => id switch
    {
        "depth" => ["d5"],
        "depth-sibling" => ["d4", "d5"],
        "nodes" => ["n15", "n16"],
        "mixed-nodes" => ["b5", "b6", "b7", "tail"],
        "nodes-last-branch" => ["b5", "b6", "b7"],
        "combined" => ["d4", "b11", "b12", "tail"],
        _ => []
    };

    internal static void Verify(string id, JsonObject result)
    {
        JsonObject expected = Tree(id);
        if (result["caseId"]!.GetValue<string>() != id || !JsonNode.DeepEquals(expected, result["exception"]) ||
            !result["stableAfterMutation"]!.GetValue<bool>())
        {
            throw new InvalidDataException("Captured exception content differs.");
        }
        bool hasTrapSources = !id.StartsWith("data-", StringComparison.Ordinal) && id != "retention";
        if (!hasTrapSources && result.ContainsKey("reads")) { throw new InvalidDataException("Unexpected exception read observations."); }
        if (hasTrapSources && result["reads"] is not JsonArray)
        {
            throw new InvalidDataException("Missing exception read observations.");
        }
        if (result["reads"] is JsonArray reads)
        {
            IReadOnlyList<ExceptionContentProbe> probes = ExceptionContentSources.Create(id).Probes;
            string[] omitted = OmittedLabels(id);
            if (reads.Count != probes.Count)
            {
                throw new InvalidDataException("Missing exception read observations.");
            }
            for (int index = 0; index < probes.Count; index++)
            {
                JsonNode read = reads[index]!;
                string label = probes[index].Label;
                int count = omitted.Contains(label, StringComparer.Ordinal) ? 0 : 1;
                if (read["label"]!.GetValue<string>() != label || read["message"]!.GetValue<int>() != count ||
                    read["stack"]!.GetValue<int>() != count || read["forbidden"]!.GetValue<int>() != 0)
                {
                    throw new InvalidDataException("Exception field access differs.");
                }
            }
        }
        if (id == "retention" && (!result["allCallerObjectsCollected"]!.GetValue<bool>() ||
            result["weakReferences"]!.GetValue<int>() != 10))
        {
            throw new InvalidDataException("Caller exception/data graph remains reachable.");
        }
    }

    private static JsonObject BclTree(string kind) => kind switch
    {
        "single" => new JsonObject { ["type"] = "System.InvalidOperationException", ["message"] = "single" },
        "inner" => new JsonObject
        {
            ["type"] = "System.InvalidOperationException",
            ["message"] = "outer",
            ["inner_exceptions"] = new JsonArray(new JsonObject { ["type"] = "System.ArgumentException", ["message"] = "inner" })
        },
        "aggregate" => new JsonObject
        {
            ["type"] = "System.AggregateException",
            ["message"] = "aggregate (left) (right)",
            ["inner_exceptions"] = new JsonArray(new JsonObject { ["type"] = "System.Exception", ["message"] = "left" },
                new JsonObject { ["type"] = "System.Exception", ["message"] = "right" })
        },
        _ => throw new InvalidDataException("Unknown BCL data shape.")
    };

    private static JsonObject Combined()
    {
        JsonObject[] children = Leaves("b", 11);
        children[5] = Node(new string('m', 4096), clipped: true, stack: new string('s', 16384));
        return Node(new string('m', 4096), [Chain(["d1", "d2", "d3"]), Node("b", children, true, true)],
            aggregate: true, clipped: true, stack: new string('s', 16384));
    }

    private static JsonObject Chain(string[] labels)
    {
        JsonObject value = Node(labels[^1], clipped: true);
        foreach (string label in labels[..^1].Reverse())
        {
            value = Node(label, [value]);
        }
        return value;
    }

    private static JsonObject[] Leaves(string prefix, int count) =>
        Enumerable.Range(0, count).Select(index => Node($"{prefix}{index}")).ToArray();

    private static JsonObject Node(string message, JsonObject[]? children = null, bool aggregate = false, bool clipped = false, string? stack = null)
    {
        var value = new JsonObject
        {
            ["type"] = "CIS.Tests.DiscoveryFixture.ExceptionContentSources+" + (aggregate ? "TrapAggregate" : "TrapException"),
            ["message"] = message
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
