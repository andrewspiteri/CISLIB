using System.Text.Json.Nodes;

namespace CIS.Tests.ArchitectureTests.Healthcheck;

// Applied to the existing metadata inspector's signatures, fields, attributes and IL uses.
// These are current development boundaries, not a published compatibility baseline.
internal static class HealthcheckArchitecturePolicy
{
    internal const string Prefix = "CIS.Healthcheck.";
    private const string HttpPrefix = Prefix + "AspNetCore.";
    internal static readonly string[] PublicNames =
    [
        "HealthCollectionDisposition", "HealthCollectionAttempt", "HealthCollectionResultKind", "HealthCollectionResult",
        "HealthCollectorOptions", "IHealthCollector", "IHealthCollectionLease", "IHealthCollectionRegistration",
        "HealthExecutionBindingOptions", "HealthFactDisposition", "HealthExecutionFactKind", "HealthExecutionPhase",
        "HealthExecutionStepStatus", "HealthExecutionFact", "HealthExecutionSnapshot", "IHealthExecutionBinding",
        "HealthApplicationIdentity", "HealthDiagnostic", "HealthStatus", "MonitorState", "HealthPolicyKind", "NumericComparison",
        "HealthExecutionOutcome", "HealthMeasurementValueKind", "HealthReportView", "HealthMeasurement", "IHealthSnapshotSource",
        "IHealthMonitorFactory", "IHealthMonitor", "IConditionMonitor", "IConsecutiveFailureMonitor", "INumericThresholdMonitor",
        "IAvailabilityMonitor", "HealthReportBuffer", "HealthSnapshot", "HealthApplicationSnapshot", "HealthMonitorCounts",
        "HealthMonitorSnapshot", "HealthOutcomeSnapshot", "HealthNumericSnapshot", "HealthCollectionSnapshot",
        "HealthcheckCapacityException", "HealthcheckOptions", "HealthMonitorOptions", "ConditionMonitorOptions",
        "ConsecutiveFailureMonitorOptions", "NumericThresholdMonitorOptions", "AvailabilityMonitorOptions", "HealthExecutionBinder",
        "HealthcheckService", "HealthCollectorRunner", "HealthPipeServer", "HealthReportFormatter",
        "AspNetCore.HealthcheckApplicationBuilderExtensions"
    ];

    internal static readonly string[] SnapshotNames =
    [
        "HealthSnapshot", "HealthApplicationSnapshot", "HealthMonitorCounts", "HealthMonitorSnapshot",
        "HealthOutcomeSnapshot", "HealthNumericSnapshot", "HealthCollectionSnapshot", "HealthExecutionSnapshot"
    ];

    internal static void Assert(JsonObject facts)
    {
        Require(Text(facts, "schema") == "cis.assembly-inspection/v1" && Text(facts, "name") == "CIS.Core",
            "HC-ARCH-ASSEMBLY", "Expected the actual single Core assembly.");
        JsonObject[] types = Objects(facts["types"]).Where(type => IsHealth(Text(type, "name"))).ToArray();
        string[] actual = types.Where(type => type["publicApi"]!.GetValue<bool>()).Select(type => Text(type, "name")).ToArray();
        Require(actual.ToHashSet(StringComparer.Ordinal).SetEquals(PublicNames.Select(name => Prefix + name))
            && actual.Length == PublicNames.Length, "HC-ARCH-PUBLIC-SURFACE", "Unexpected or missing Healthcheck public type.");

        JsonObject[] uses = Objects(facts["typeUses"]).Where(use => IsHealth(Source(use))).ToArray();
        Require(uses.Length > 0 && uses.Any(use => Source(use).StartsWith(HttpPrefix, StringComparison.Ordinal)
            && Text(use, "Namespace").StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)),
            "HC-ARCH-INSPECTION", "Expected actual body/signature facts and the explicitly approved HTTP integration.");
        foreach (JsonObject use in uses)
        {
            string source = Source(use), target = Text(use, "Type"), space = Text(use, "Namespace");
            Require(!space.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal)
                && !space.StartsWith("CIS.Heartbeat", StringComparison.Ordinal)
                && !space.StartsWith("CIS.Startup", StringComparison.Ordinal),
                "HC-ARCH-INFRASTRUCTURE", Text(use, "Site") + " -> " + target);
            if (!source.StartsWith(HttpPrefix, StringComparison.Ordinal))
            {
                Require(!space.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal),
                    "HC-ARCH-HTTP-DIRECTION", Text(use, "Site") + " -> " + target);
            }
            if (IsPolicyOwner(source))
            {
                Require(!space.StartsWith("System.IO.Pipes", StringComparison.Ordinal)
                    && !space.StartsWith("System.Net", StringComparison.Ordinal)
                    && !IsTransport(target), "HC-ARCH-POLICY-TRANSPORT", Text(use, "Site") + " -> " + target);
            }
            if (IsTransport(source))
            {
                Require(!IsPolicyOwner(target) && !IsOwnerCapability(target),
                    "HC-ARCH-TRANSPORT-OWNER", Text(use, "Site") + " -> " + target);
            }
        }

        foreach (string name in SnapshotNames)
        {
            JsonObject type = types.Single(type => Text(type, "name") == Prefix + name);
            Require(Text(type, "attributes").Split(", ").Contains("Sealed", StringComparer.Ordinal),
                "HC-ARCH-SNAPSHOT-SHAPE", name);
            foreach (JsonObject member in Objects(type["members"]))
            {
                string kind = Text(member, "kind"), memberName = Text(member, "name");
                bool visible = member["publicApi"]!.GetValue<bool>();
                Require(!(kind == "method" && visible && (memberName == ".ctor" || memberName.StartsWith("set_", StringComparison.Ordinal))),
                    "HC-ARCH-SNAPSHOT-MUTATOR", name + ":" + memberName);
                if (kind == "field")
                {
                    string[] attributes = Text(member, "attributes").Split(", ");
                    Require(attributes.Contains("Private", StringComparer.Ordinal) && attributes.Contains("InitOnly", StringComparer.Ordinal),
                        "HC-ARCH-SNAPSHOT-FIELD", name + ":" + memberName);
                    string signature = Text(member, "signature");
                    Require(!signature.Contains("System.Exception", StringComparison.Ordinal)
                        && !signature.Contains("System.Collections.Generic.Dictionary`", StringComparison.Ordinal)
                        && !signature.Contains("System.Collections.Generic.List`", StringComparison.Ordinal)
                        && !signature.EndsWith("[]", StringComparison.Ordinal)
                        && !PublicNames.Where(IsOwnerCapabilityName).Any(owner => signature.Contains(Prefix + owner, StringComparison.Ordinal)),
                        "HC-ARCH-SNAPSHOT-OWNERSHIP", name + ":" + memberName);
                }
            }
        }
    }

    private static bool IsHealth(string name) => name.StartsWith(Prefix, StringComparison.Ordinal);
    private static string Source(JsonObject use) => Text(use, "Site").Split(':')[0];
    private static bool IsPolicyOwner(string name) => name.StartsWith(Prefix + "Internal.", StringComparison.Ordinal)
        || RootType(name) is Prefix + "HealthcheckService" or Prefix + "HealthCollectorRunner" or Prefix + "HealthExecutionBinder";
    private static bool IsTransport(string name) => name.StartsWith(HttpPrefix, StringComparison.Ordinal)
        || name.StartsWith(Prefix + "Pipes.", StringComparison.Ordinal) || RootType(name) == Prefix + "HealthPipeServer";
    private static string RootType(string name) => name.Split('+')[0];
    private static bool IsOwnerCapability(string name) => IsHealth(name) && IsOwnerCapabilityName(name[Prefix.Length..]);
    private static bool IsOwnerCapabilityName(string name) => name is "HealthcheckService" or "HealthCollectorRunner" or "HealthExecutionBinder"
        or "IHealthMonitorFactory" or "IHealthMonitor" or "IConditionMonitor" or "IConsecutiveFailureMonitor" or "INumericThresholdMonitor"
        or "IAvailabilityMonitor" or "IHealthCollector" or "IHealthCollectionLease" or "IHealthCollectionRegistration" or "IHealthExecutionBinding";
    internal static IEnumerable<JsonObject> Objects(JsonNode? node) => node?.AsArray().OfType<JsonObject>() ?? [];
    internal static string Text(JsonNode node, string key) => node[key]?.GetValue<string>() ?? "";
    private static void Require(bool valid, string code, string message)
    {
        if (!valid) { throw new InvalidOperationException(code + ": " + message); }
    }
}
