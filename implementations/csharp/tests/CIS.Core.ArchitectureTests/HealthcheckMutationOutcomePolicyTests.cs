using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationOutcomePolicyTests
{
    [TestMethod]
    public void UnchangedLegacyManifestKeepsItsOriginalPolicy()
    {
        var input = new JsonObject { ["schema"] = HealthcheckMutationOutcomePolicy.LegacySchema };
        string before = input.ToJsonString();
        Assert.AreEqual(NativeAuditPolicy.LegacySourceProofs, HealthcheckMutationOutcomePolicy.Read(input));
        Assert.AreEqual(before, input.ToJsonString());
    }

    [TestMethod]
    public void ProspectiveManifestMustSelectTheApprovedVersionExplicitly()
    {
        var input = new JsonObject
        {
            ["schema"] = HealthcheckMutationOutcomePolicy.ProspectiveSchema,
            ["outcomePolicy"] = HealthcheckMutationOutcomePolicy.NativePolicy,
        };
        Assert.AreEqual(NativeAuditPolicy.CisNativeCompletedV1, HealthcheckMutationOutcomePolicy.Read(input));
        Assert.AreEqual("cis-native-completed/v1", HealthcheckMutationOutcomePolicy.Name(HealthcheckMutationOutcomePolicy.Read(input)));
    }

    [TestMethod]
    [DataRow("legacy-upgrade")]
    [DataRow("prospective-missing")]
    [DataRow("unknown-schema")]
    [DataRow("unknown-policy")]
    [DataRow("wrong-type")]
    public void MissingOrUnknownVersionsCannotSilentlyChangeQualification(string defect)
    {
        var input = new JsonObject
        {
            ["schema"] = defect == "legacy-upgrade" ? HealthcheckMutationOutcomePolicy.LegacySchema : HealthcheckMutationOutcomePolicy.ProspectiveSchema,
            ["outcomePolicy"] = HealthcheckMutationOutcomePolicy.NativePolicy,
        };
        if (defect == "prospective-missing") { input.Remove("outcomePolicy"); }
        if (defect == "unknown-schema") { input["schema"] = "unknown"; }
        if (defect == "unknown-policy") { input["outcomePolicy"] = "unknown"; }
        if (defect == "wrong-type") { input["outcomePolicy"] = true; }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationOutcomePolicy.Read(input));
    }
}
