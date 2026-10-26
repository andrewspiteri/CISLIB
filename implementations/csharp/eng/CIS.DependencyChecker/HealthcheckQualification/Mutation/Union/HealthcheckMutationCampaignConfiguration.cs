using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// Complete campaigns keep their maintained source/cohort configuration. Exact
// non-mixing additional-timeout profiles retain both historical aggregate budgets
// and the reviewed short budget; none qualifies an incomplete host or caps wall time.
internal static class HealthcheckMutationCampaignConfiguration
{
    internal static void Validate(string group, JsonObject native, JsonObject maintained,
        NativeAuditPolicy policy = NativeAuditPolicy.LegacySourceProofs)
    {
        Need(policy is NativeAuditPolicy.LegacySourceProofs or NativeAuditPolicy.CisNativeCompletedV1, "Unknown native outcome policy.");
        Need(group is "unit-runtime" or "pipes" or "aspnet", "Unknown complete mutation group.");
        Need(native["stryker-config"] is JsonObject && maintained["stryker-config"] is JsonObject,
            "Complete native and maintained Stryker configurations are required.");
        JsonObject config = (JsonObject)native["stryker-config"]!;
        JsonObject maintainedOptions = (JsonObject)maintained["stryker-config"]!;
        bool nativeBail = policy == NativeAuditPolicy.CisNativeCompletedV1
            && config["disable-bail"] is JsonValue bail && bail.TryGetValue(out bool disabledBail) && !disabledBail;
        JsonObject comparable = (JsonObject)native.DeepClone();
        if (nativeBail)
        {
            Need(Boolean(maintainedOptions, "disable-bail"), "The historical maintained no-bail reference must remain unchanged.");
            comparable["stryker-config"]!["disable-bail"] = true;
        }
        int maintainedBudget = group == "aspnet" ? 750000 : 10000;
        Need(Number(maintainedOptions, "additional-timeout") == maintainedBudget,
            "Reviewed complete profiles require the unchanged maintained budget for their group.");
        Need(Number(maintainedOptions, "concurrency") == 2,
            "Reviewed complete profiles require the unchanged maintained two-worker reference.");
        Need(Number(config, "additional-timeout") is not null,
            "The native additional timeout must be represented as an exact JSON integer.");
        bool reviewedParallel = (group is "unit-runtime" or "pipes") && Number(config, "additional-timeout") == 1000
            && Boolean(config, "disable-mix-mutants") && Number(config, "concurrency") == 8;
        bool reviewedAspNetShort = group == "aspnet" && Number(config, "additional-timeout") == 1000
            && Boolean(config, "disable-mix-mutants") && Number(config, "concurrency") == 2;
        if (config.ContainsKey("disable-mix-mutants"))
        {
            Need((group is "unit-runtime" or "pipes" || reviewedAspNetShort) && config["disable-mix-mutants"] is JsonValue value
                && value.TryGetValue(out bool disabled) && disabled,
                "Only reviewed complete profiles may add disable-mix-mutants as JSON Boolean true.");
            Need(group != "pipes" || Number(config, "additional-timeout") is 1000 or 2100000,
                "Non-mixing Pipes campaigns require their exact reviewed short or aggregate additional budget.");
        }

        if (!JsonNode.DeepEquals(comparable, maintained))
        {
            JsonObject comparison = comparable;
            JsonObject comparisonOptions = (JsonObject)comparison["stryker-config"]!;
            Need((group is "unit-runtime" or "pipes" || reviewedAspNetShort) && Boolean(config, "disable-mix-mutants"),
                "Only reviewed complete non-mixing profiles may use the configuration delta.");
            if (!maintainedOptions.ContainsKey("disable-mix-mutants")) { comparisonOptions.Remove("disable-mix-mutants"); }
            bool reviewedBudget = group == "unit-runtime" && Number(config, "additional-timeout") is 1000 or 300000 or 4500000
                || group == "pipes" && Number(config, "additional-timeout") is 1000 or 2100000 || reviewedAspNetShort;
            if (reviewedBudget && Number(maintainedOptions, "additional-timeout") == maintainedBudget)
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
            && (Number(config, "concurrency") == 2 || reviewedParallel) && (Boolean(config, "disable-bail") || nativeBail),
            "Complete Release settings and the selected policy's explicit native bail configuration are required.");
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
