using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// Version selection belongs to new campaign inputs. Retained v1 inputs keep
// their original interpretation; a policy field cannot silently upgrade them.
internal static class HealthcheckMutationOutcomePolicy
{
    internal const string LegacySchema = "cis.healthcheck.mutation-union-inputs/v1-development";
    internal const string ProspectiveSchema = "cis.healthcheck.mutation-union-inputs/v2-native-completed";
    internal const string NativePolicy = "cis-native-completed/v1";

    internal static NativeAuditPolicy Read(JsonObject manifest)
    {
        string? schema = manifest["schema"] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
        if (schema == LegacySchema && !manifest.ContainsKey("outcomePolicy"))
        {
            return NativeAuditPolicy.LegacySourceProofs;
        }
        if (schema == ProspectiveSchema && manifest["outcomePolicy"] is JsonValue policy
            && policy.TryGetValue(out string? selected) && selected == NativePolicy)
        {
            return NativeAuditPolicy.CisNativeCompletedV1;
        }
        throw new InvalidDataException("Use unchanged v1 inputs for legacy interpretation, or explicit v2 inputs with outcomePolicy cis-native-completed/v1 for a new campaign.");
    }

    internal static string Name(NativeAuditPolicy policy) => policy switch
    {
        NativeAuditPolicy.LegacySourceProofs => "legacy-source-proofs/v1",
        NativeAuditPolicy.CisNativeCompletedV1 => NativePolicy,
        _ => throw new InvalidDataException("Unknown native outcome policy."),
    };
}
