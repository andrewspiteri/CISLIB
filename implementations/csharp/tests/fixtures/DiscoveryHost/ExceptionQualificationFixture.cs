using System.Text.Json.Nodes;
using CIS.Logging.Exceptions;

namespace CIS.Tests.DiscoveryFixture;

internal static class ExceptionQualificationFixture
{
    internal static JsonObject Execute(JsonObject row, JsonObject nativeBindings)
    {
        ExceptionQualificationSource source = ExceptionQualificationSources.Create(row["input"]!.AsObject());
        CapturedException? captured = null;
        ArgumentException? failure = null;
        try { captured = ExceptionProjection.Capture(source.Root); }
        catch (ArgumentException error) { failure = error; }
        JsonNode? before = Projection(captured);
        foreach (ExceptionQualificationProbe probe in source.Probes)
        {
            probe.Text = "changed message";
            probe.Stack = "changed stack";
            probe.RawData.Clear();
            probe.RawData["changed"] = ExceptionQualificationSources.PrivateSentinel;
            probe.Attached.Source = ExceptionQualificationSources.PrivateSentinel;
            probe.Attached.HelpLink = ExceptionQualificationSources.PrivateSentinel;
            if (probe.SourceText != ExceptionQualificationSources.PrivateSentinel || probe.HelpText != ExceptionQualificationSources.PrivateSentinel)
            {
                throw new InvalidDataException("Fixture metadata mutation did not occur.");
            }
        }
        JsonNode? after = Projection(captured);
        var reads = new JsonObject();
        foreach (ExceptionQualificationProbe probe in source.Probes.OrderBy(probe => probe.Label, StringComparer.Ordinal))
        {
            reads.Add(probe.Label, new JsonObject { ["message"] = probe.MessageReads, ["stack"] = probe.StackReads, ["forbidden"] = probe.ForbiddenReads });
        }
        (int nodes, int depth) = captured is null ? (0, 0) : ExceptionBoundaryFixture.Measure(captured);
        if (captured is not null)
        {
            foreach (CapturedException item in ExceptionBoundaryFixture.Preorder(captured))
            {
                (int childNodes, int childDepth) = ExceptionBoundaryFixture.Measure(item);
                bool loss = ExceptionBoundaryFixture.Preorder(item).Any(child => child.IsTruncated);
                if (item.NodeCount != childNodes || item.Depth != childDepth || item.HasTruncatedContent != loss)
                {
                    throw new InvalidDataException("Owned subtree shape/loss differs.");
                }
            }
        }
        var result = new JsonObject
        {
            ["caseId"] = row["id"]!.DeepClone(),
            ["valid"] = failure is null,
            ["exception"] = after,
            ["nodes"] = nodes,
            ["depth"] = depth,
            ["cachedNodes"] = captured?.NodeCount ?? 0,
            ["cachedDepth"] = captured?.Depth ?? 0,
            ["hasTruncatedContent"] = captured?.HasTruncatedContent ?? false,
            ["error"] = failure is null ? null : "argument-invalid",
            ["nativeError"] = failure?.GetType().Name,
            ["parameter"] = failure?.ParamName,
            ["stableAfterMutation"] = JsonNode.DeepEquals(before, after),
            ["safeError"] = failure is null || !failure.Message.Contains(ExceptionQualificationSources.PrivateSentinel, StringComparison.Ordinal),
            ["reads"] = reads
        };
        ExceptionQualificationExpectations.Validate(row, result, nativeBindings);
        return result;
    }

    // Development observation only; the shipping event renderer is not implemented here.
    private static JsonNode? Projection(CapturedException? root)
    {
        if (root is null) { return null; }
        JsonObject observed = ExceptionBoundaryFixture.Observe("owned", root);
        return observed["exception"]!.DeepClone();
    }
}
