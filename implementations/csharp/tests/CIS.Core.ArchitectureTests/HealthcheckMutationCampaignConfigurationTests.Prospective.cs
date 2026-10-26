using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Tests.ArchitectureTests;

public sealed partial class HealthcheckMutationCampaignConfigurationTests
{
    [TestMethod]
    [DataRow("unit-runtime")]
    [DataRow("pipes")]
    [DataRow("aspnet")]
    public void ProspectiveNativeBailChangesOnlyTheApprovedSetting(string group)
    {
        JsonObject maintained = Configuration(group), native = (JsonObject)maintained.DeepClone();
        Options(native)["disable-bail"] = false;
        string original = maintained.ToJsonString(), selected = native.ToJsonString();
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
        HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained, NativeAuditPolicy.CisNativeCompletedV1);
        Assert.AreEqual(original, maintained.ToJsonString());
        Assert.AreEqual(selected, native.ToJsonString());
    }

    [TestMethod]
    [DataRow("unit-runtime")]
    [DataRow("pipes")]
    [DataRow("aspnet")]
    public void ProspectiveBailMayCombineWithTheExistingReviewedShortProfile(string group)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, 1000);
        Options(native)["disable-bail"] = false;
        HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained, NativeAuditPolicy.CisNativeCompletedV1);
    }

    [TestMethod]
    [DataRow("threshold")]
    [DataRow("scope")]
    [DataRow("filter")]
    [DataRow("level")]
    [DataRow("ignore")]
    [DataRow("wrong-bail-type")]
    public void ProspectiveBailCannotRelaxTheRequiredPopulationOrThreshold(string defect)
    {
        JsonObject maintained = Configuration("unit-runtime"), native = (JsonObject)maintained.DeepClone();
        JsonObject options = Options(native);
        options["disable-bail"] = false;
        switch (defect)
        {
            case "threshold": options["thresholds"]!["break"] = 89; break;
            case "scope": options["mutate"] = new JsonArray("Healthcheck/Example.cs"); break;
            case "filter": options["test-case-filter"] = "FullyQualifiedName~Different"; break;
            case "level": options["mutation-level"] = "Basic"; break;
            case "ignore": options["ignore-methods"] = new JsonArray("*"); break;
            case "wrong-bail-type": options["disable-bail"] = "false"; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(
            "unit-runtime", native, maintained, NativeAuditPolicy.CisNativeCompletedV1));
    }
}
