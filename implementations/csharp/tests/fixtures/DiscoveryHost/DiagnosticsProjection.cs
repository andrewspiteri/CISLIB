using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class DiagnosticsProjection
{
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    internal static JsonObject Project(string phase, CoreLogDiagnostics value) => new()
    {
        ["phase"] = phase,
        ["state"] = value.State.ToString(),
        ["closed"] = value.AdmissionClosed,
        ["observedAtUtc"] = value.ObservedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ["shutdownStartedAtUtc"] = value.ShutdownStartedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
        ["invalid"] = Number(value.InvalidEventCount),
        ["truncated"] = Number(value.TruncatedEventCount),
        ["outputs"] = new JsonArray(value.Outputs.Select(item => (JsonNode)new JsonObject
        {
            ["name"] = item.OutputName,
            ["state"] = item.State.ToString(),
            ["sequence"] = Number(item.AcceptedSequence),
            ["filtered"] = Number(item.Filtered),
            ["attempts"] = Number(item.AdmissionAttempts),
            ["accepted"] = Number(item.Accepted),
            ["queueFull"] = Number(item.QueueFull),
            ["unavailable"] = Number(item.Unavailable),
            ["written"] = Number(item.Written),
            ["failed"] = Number(item.Failed),
            ["abandoned"] = Number(item.Abandoned),
            ["pending"] = Number(item.Pending),
            ["fault"] = item.LastFaultCode,
            ["faultAtUtc"] = item.LastFaultAtUtc?.ToString("O", CultureInfo.InvariantCulture)
        }).ToArray())
    };
}
