using System.Text.Json.Nodes;
using CIS.Logging.Exceptions;

namespace CIS.Tests.DiscoveryFixture;

internal static class ExceptionContentFixture
{
    internal static JsonObject Execute(string id)
    {
        if (id.StartsWith("data-", StringComparison.Ordinal))
        {
            return Data(id);
        }
        if (id == "retention")
        {
            return Retention();
        }
        ExceptionContentSource source = ExceptionContentSources.Create(id);
        CapturedException captured = ExceptionProjection.Capture(source.Root)!;
        JsonObject before = ExceptionBoundaryFixture.Observe(id, captured);
        foreach (ExceptionContentProbe probe in source.Probes)
        {
            probe.Text = "changed";
            probe.Trace = "changed stack";
            probe.RawData.Clear();
            probe.RawData["after"] = ExceptionContentSources.PrivateSentinel;
        }
        JsonObject after = ExceptionBoundaryFixture.Observe(id, captured);
        after["stableAfterMutation"] = JsonNode.DeepEquals(before, after);
        after["reads"] = new JsonArray(source.Probes.Select(probe => (JsonNode?)new JsonObject
        {
            ["label"] = probe.Label,
            ["message"] = probe.MessageReads,
            ["stack"] = probe.StackReads,
            ["forbidden"] = probe.ForbiddenCalls
        }).ToArray());
        VerifyModel(captured);
        ExceptionContentExpectations.Verify(id, after);
        return after;
    }

    private static JsonObject Data(string id)
    {
        Exception source = ExceptionContentSources.CreateBcl(id[5..]);
        CapturedException captured = ExceptionProjection.Capture(source)!;
        JsonObject before = ExceptionBoundaryFixture.Observe(id, captured);
        foreach (Exception node in ExceptionContentSources.BclNodes(source))
        {
            node.Data.Clear();
            node.Data["different"] = new ExceptionPrivatePayload();
            node.Data["private"] = ExceptionContentSources.PrivateSentinel;
            node.Source = ExceptionContentSources.PrivateSentinel;
            node.HelpLink = ExceptionContentSources.PrivateSentinel;
        }
        JsonObject after = ExceptionBoundaryFixture.Observe(id, captured);
        after["stableAfterMutation"] = JsonNode.DeepEquals(before, after);
        VerifyModel(captured);
        ExceptionContentExpectations.Verify(id, after);
        return after;
    }

    private static JsonObject Retention()
    {
        ExceptionRetention state = ExceptionRetentionFixture.Create("aggregate");
        JsonObject before = ExceptionBoundaryFixture.Observe("retention", state.Captured);
        bool collected = ExceptionRetentionFixture.Collect(state);
        JsonObject after = ExceptionBoundaryFixture.Observe("retention", state.Captured);
        after["stableAfterMutation"] = JsonNode.DeepEquals(before, after);
        after["allCallerObjectsCollected"] = collected;
        after["weakReferences"] = state.CallerReferences.Length;
        VerifyModel(state.Captured);
        ExceptionContentExpectations.Verify("retention", after);
        return after;
    }

    private static void VerifyModel(CapturedException root)
    {
        foreach (CapturedException node in ExceptionBoundaryFixture.Preorder(root))
        {
            (int nodes, int depth) = ExceptionBoundaryFixture.Measure(node);
            bool loss = ExceptionBoundaryFixture.Preorder(node).Any(child => child.IsTruncated);
            if (node.NodeCount != nodes || node.Depth != depth || node.HasTruncatedContent != loss)
            {
                throw new InvalidDataException("Stored exception shape/loss differs.");
            }
        }
    }
}
