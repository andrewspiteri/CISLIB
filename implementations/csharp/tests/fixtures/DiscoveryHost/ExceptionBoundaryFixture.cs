using System.Text.Json.Nodes;
using CIS.Logging.Exceptions;

namespace CIS.Tests.DiscoveryFixture;

internal static class ExceptionBoundaryFixture
{
    internal static JsonObject Execute(string id)
    {
        ExceptionBoundarySource source = ExceptionBoundarySources.Create(id);
        CapturedException root = ExceptionProjection.Capture(source.Root)!;
        JsonObject result = Observe(id, root);
        result["reads"] = new JsonArray(source.Probes.Select(probe => (JsonNode?)new JsonObject
        {
            ["label"] = probe.Label,
            ["message"] = probe.MessageReads,
            ["stack"] = probe.StackReads
        }).ToArray());
        Verify(id, result);
        ExceptionBoundaryExpectations.VerifyReads(id, result);
        return result;
    }

    internal static JsonObject Observe(string id, CapturedException root)
    {
        (int nodes, int depth) = Measure(root);
        return new JsonObject
        {
            ["caseId"] = id,
            ["exception"] = Project(root),
            ["nodes"] = nodes,
            ["depth"] = depth,
            ["cachedNodes"] = root.NodeCount,
            ["cachedDepth"] = root.Depth,
            ["hasTruncatedContent"] = root.HasTruncatedContent,
            ["preorder"] = new JsonArray(Preorder(root).Select(node => (JsonNode?)JsonValue.Create(node.Message)).ToArray())
        };
    }

    internal static void Verify(string id, JsonObject actual) => ExceptionBoundaryExpectations.Verify(id, actual);

    internal static JsonObject Expected(string id) => ExceptionBoundaryExpectations.ForCase(id);

    internal static IEnumerable<CapturedException> Preorder(CapturedException root)
    {
        yield return root;
        foreach (CapturedException child in root.InnerExceptions)
        {
            foreach (CapturedException descendant in Preorder(child))
            {
                yield return descendant;
            }
        }
    }

    // Stack-based shape measurement is independent of the production constructor's metadata fold.
    internal static (int Nodes, int Depth) Measure(CapturedException root)
    {
        var pending = new Stack<(CapturedException Node, int Depth)>();
        pending.Push((root, 1));
        int nodes = 0;
        int depth = 0;
        while (pending.TryPop(out var item))
        {
            nodes++;
            depth = Math.Max(depth, item.Depth);
            foreach (CapturedException child in item.Node.InnerExceptions)
            {
                pending.Push((child, item.Depth + 1));
            }
        }
        return (nodes, depth);
    }

    private static JsonObject Project(CapturedException node)
    {
        var value = new JsonObject { ["type"] = node.Type, ["message"] = node.Message };
        if (node.StackTrace is not null)
        {
            value["stack_trace"] = node.StackTrace;
        }
        if (node.InnerExceptions.Count > 0)
        {
            value["inner_exceptions"] = new JsonArray(node.InnerExceptions.Select(child => (JsonNode?)Project(child)).ToArray());
        }
        if (node.IsTruncated)
        {
            value["truncated"] = true;
        }
        return value;
    }
}
