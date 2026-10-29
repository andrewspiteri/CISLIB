using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private static Task<HealthcheckSharedMutationChildren.Result> ReadSharedChildEvidenceAsync(PinnedInputs files, string snapshot,
        JsonObject descriptor, JsonObject profile, JsonObject batch, Source source, Definition[] definitions, string report, string output)
        => HealthcheckSharedMutationChildren.ReadAsync(new(files.Pin, files.ReadDocument, files.GuardRoot, root => files.TrackInventory(root)),
            snapshot, descriptor, profile, batch, source, definitions, report, output);
}
