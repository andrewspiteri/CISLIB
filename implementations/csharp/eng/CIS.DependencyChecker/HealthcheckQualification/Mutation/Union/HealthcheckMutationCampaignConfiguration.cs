using System.Text.Json.Nodes;

namespace CIS.Verification;

// Complete campaigns keep their maintained source/cohort configuration. Exact
// non-mixing additional-timeout profiles retain both historical aggregate budgets
// and the reviewed short budget; none qualifies an incomplete host or caps wall time.
internal static class HealthcheckMutationCampaignConfiguration
{
    internal static void Validate(string group, JsonObject native, JsonObject maintained)
    {
        Need(group is "unit-runtime" or "pipes" or "aspnet", "Unknown complete mutation group.");
        Need(native["stryker-config"] is JsonObject && maintained["stryker-config"] is JsonObject,
            "Complete native and maintained Stryker configurations are required.");
        JsonObject config = (JsonObject)native["stryker-config"]!;
        JsonObject maintainedOptions = (JsonObject)maintained["stryker-config"]!;
        int maintainedBudget = group == "aspnet" ? 750000 : 10000;
        Need(Number(maintainedOptions, "additional-timeout") == maintainedBudget,
            "Reviewed complete profiles require the unchanged maintained budget for their group.");
        Need(Number(maintainedOptions, "concurrency") == 2,
            "Reviewed complete profiles require the unchanged maintained two-worker reference.");
        Need(Number(config, "additional-timeout") is not null,
            "The native additional timeout must be represented as an exact JSON integer.");
        bool reviewedParallel = (group is "unit-runtime" or "pipes") && Number(config, "additional-timeout") == 1000
            && Boolean(config, "disable-mix-mutants") && Number(config, "concurrency") == 8;
        if (config.ContainsKey("disable-mix-mutants"))
        {
            Need(group is "unit-runtime" or "pipes" && config["disable-mix-mutants"] is JsonValue value
                && value.TryGetValue(out bool disabled) && disabled,
                "Only reviewed complete Unit/Pipes campaigns may add disable-mix-mutants as JSON Boolean true.");
            Need(group != "pipes" || Number(config, "additional-timeout") is 1000 or 2100000,
                "Non-mixing Pipes campaigns require their exact reviewed short or aggregate additional budget.");
        }

        if (!JsonNode.DeepEquals(native, maintained))
        {
            JsonObject comparison = (JsonObject)native.DeepClone();
            JsonObject comparisonOptions = (JsonObject)comparison["stryker-config"]!;
            Need(group is "unit-runtime" or "pipes" && Boolean(config, "disable-mix-mutants"),
                "Only complete non-mixing Unit/Pipes campaigns may use the reviewed configuration delta.");
            if (!maintainedOptions.ContainsKey("disable-mix-mutants")) { comparisonOptions.Remove("disable-mix-mutants"); }
            bool reviewedBudget = group == "unit-runtime" && Number(config, "additional-timeout") is 1000 or 300000 or 4500000
                || group == "pipes" && Number(config, "additional-timeout") is 1000 or 2100000;
            if (reviewedBudget && Number(maintainedOptions, "additional-timeout") == 10000)
            { comparisonOptions["additional-timeout"] = maintainedOptions["additional-timeout"]!.DeepClone(); }
            if (reviewedParallel) { comparisonOptions["concurrency"] = maintainedOptions["concurrency"]!.DeepClone(); }
            Need(JsonNode.DeepEquals(comparison, maintained),
                "Native configuration differs from its complete maintained scope: " + group);
        }

        string[] allowed = ["project", "test-projects", "test-case-filter", "test-runner", "target-framework", "configuration", "mutate",
            "mutation-level", "reporters", "coverage-analysis", "concurrency", "disable-bail", "additional-timeout", "thresholds", "disable-mix-mutants"];
        Need(config.All(property => allowed.Contains(property.Key, StringComparer.Ordinal)), "Unapproved native filter/ignore/tool override is forbidden.");
        Need(Text(config, "mutation-level") == "Complete" && Text(config, "configuration") == "Release"
            && Text(config, "test-runner") == "vstest" && Text(config, "target-framework") == "net10.0"
            && (Number(config, "concurrency") == 2 || reviewedParallel) && Boolean(config, "disable-bail"),
            "Complete Release/no-bail native settings are required.");
        Need(group == "unit-runtime" || Text(config, "coverage-analysis") == "off", "Child-process groups require coverage-analysis off.");
        Need(config["thresholds"] is JsonObject, "Native thresholds are required.");
        JsonObject thresholds = (JsonObject)config["thresholds"]!;
        Need(Number(thresholds, "high") == 100 && Number(thresholds, "low") == 90 && Number(thresholds, "break") == 90,
            "Native thresholds must remain 100/90/90.");
    }

    private static string? Text(JsonObject value, string name) => value[name] is JsonValue scalar && scalar.TryGetValue(out string? text) ? text : null;
    private static int? Number(JsonObject value, string name) => value[name] is JsonValue scalar && scalar.TryGetValue(out int number) ? number : null;
    private static bool Boolean(JsonObject value, string name) => value[name] is JsonValue scalar && scalar.TryGetValue(out bool boolean) && boolean;
    private static void Need(bool value, string detail) { if (!value) { throw new InvalidDataException(detail); } }
}
