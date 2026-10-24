using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationCampaignConfigurationTests
{
    [TestMethod]
    [DataRow("unit-runtime", 10000)]
    [DataRow("pipes", 10000)]
    [DataRow("aspnet", 750000)]
    public void ActualMaintainedCampaignFilesRemainAcceptedWithTheirExistingExactBudgets(string group, int expectedBudget)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "planning", "healthcheck", "qualification", "gate-closure", group + ".json")))
        { root = root.Parent; }
        Assert.IsNotNull(root, "The maintained campaign file must exist in the repository checkout.");
        string path = Path.Combine(root.FullName, "planning", "healthcheck", "qualification", "gate-closure", group + ".json");
        JsonObject maintained = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        Assert.AreEqual(expectedBudget, (int?)Options(maintained)["additional-timeout"]);
        JsonObject native = (JsonObject)maintained.DeepClone();
        string maintainedBefore = maintained.ToJsonString(), nativeBefore = native.ToJsonString();
        HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained);
        Assert.AreEqual(maintainedBefore, maintained.ToJsonString());
        Assert.AreEqual(nativeBefore, native.ToJsonString());
    }

    [TestMethod]
    [DataRow("10000", false)]
    [DataRow("10000", true)]
    [DataRow("750001", false)]
    [DataRow("750001", true)]
    [DataRow("\"750000\"", false)]
    [DataRow("\"750000\"", true)]
    [DataRow("750000.0", false)]
    [DataRow("750000.0", true)]
    [DataRow("null", false)]
    [DataRow("null", true)]
    [DataRow("missing", false)]
    [DataRow("missing", true)]
    public void AspNetBudgetCannotBeRewrittenOrApprovedByMatchingTheSameWrongPreimage(string literal, bool rewriteMaintained)
    {
        JsonObject maintained = Configuration("aspnet"), native = (JsonObject)maintained.DeepClone();
        foreach (JsonObject document in rewriteMaintained ? new[] { maintained, native } : new[] { native })
        {
            if (literal == "missing") { Options(document).Remove("additional-timeout"); }
            else { Options(document)["additional-timeout"] = JsonNode.Parse(literal); }
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate("aspnet", native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime")]
    [DataRow("pipes")]
    public void ADecimalNativeBudgetCannotPassNumericDeepEqualityWithItsMaintainedInteger(string group)
    {
        JsonObject maintained = Configuration(group), native = (JsonObject)maintained.DeepClone();
        Options(native)["additional-timeout"] = JsonNode.Parse("10000.0");
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime")]
    [DataRow("pipes")]
    [DataRow("aspnet")]
    public void ExistingCompleteCampaignsRemainAcceptedWithoutRewritingTheirConfiguration(string group)
    {
        JsonObject maintained = Configuration(group), native = (JsonObject)maintained.DeepClone();
        string before = native.ToJsonString();
        HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained);
        Assert.AreEqual(before, native.ToJsonString());
        Assert.IsTrue(JsonNode.DeepEquals(native, maintained));
    }

    [TestMethod]
    public void CompleteUnitMayDisableMixingWhileEveryMaintainedSourceCohortAndBudgetRemainsUnchanged()
    {
        JsonObject maintained = Configuration("unit-runtime"), native = NoMix(maintained);
        string maintainedBefore = maintained.ToJsonString(), nativeBefore = native.ToJsonString();
        HealthcheckMutationCampaignConfiguration.Validate("unit-runtime", native, maintained);
        Assert.AreEqual(maintainedBefore, maintained.ToJsonString());
        Assert.AreEqual(nativeBefore, native.ToJsonString());
        Assert.IsFalse(Options(maintained).ContainsKey("disable-mix-mutants"));
        Assert.IsFalse(Options(native).ContainsKey("coverage-analysis"));
        Assert.AreEqual(10000, (int?)Options(native)["additional-timeout"]);
    }

    [TestMethod]
    public void AnExplicitlyMaintainedUnitNoMixConfigurationAlsoRemainsAccepted()
    {
        JsonObject maintained = NoMix(Configuration("unit-runtime"));
        HealthcheckMutationCampaignConfiguration.Validate("unit-runtime", (JsonObject)maintained.DeepClone(), maintained);
    }

    [TestMethod]
    public void CompleteNonMixingUnitMayUseTheExactReviewedRelaxedHostBudgetWithoutChangingTheMaintainedInputs()
    {
        JsonObject maintained = Configuration("unit-runtime"), native = Relaxed(maintained);
        string maintainedBefore = maintained.ToJsonString(), nativeBefore = native.ToJsonString();
        HealthcheckMutationCampaignConfiguration.Validate("unit-runtime", native, maintained);
        Assert.AreEqual(maintainedBefore, maintained.ToJsonString());
        Assert.AreEqual(nativeBefore, native.ToJsonString());
        Assert.AreEqual(10000, (int?)Options(maintained)["additional-timeout"]);
        Assert.AreEqual(300000, (int?)Options(native)["additional-timeout"]);
        Assert.IsFalse(Options(native).ContainsKey("coverage-analysis"));
    }

    [TestMethod]
    [DataRow("unit-runtime", 4500000)]
    [DataRow("pipes", 2100000)]
    public void CompleteNonMixingCampaignMayUseItsExactAggregateHostBudgetWithoutRewritingEitherInput(string group, int budget)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, budget);
        string maintainedBefore = maintained.ToJsonString(), nativeBefore = native.ToJsonString();
        HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained);
        Assert.AreEqual(maintainedBefore, maintained.ToJsonString());
        Assert.AreEqual(nativeBefore, native.ToJsonString());
        Assert.AreEqual(10000, (int?)Options(maintained)["additional-timeout"]);
        Assert.AreEqual(budget, (int?)Options(native)["additional-timeout"]);
        Assert.AreEqual(true, (bool?)Options(native)["disable-mix-mutants"]);
    }

    [TestMethod]
    [DataRow("unit-runtime")]
    [DataRow("pipes")]
    public void CompleteNonMixingCampaignMayUseTheExactShortAdditionalBudgetWithoutRewritingMaintainedInputs(string group)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, 1000);
        string maintainedBefore = maintained.ToJsonString(), nativeBefore = native.ToJsonString();
        HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained);
        Assert.AreEqual(maintainedBefore, maintained.ToJsonString());
        Assert.AreEqual(nativeBefore, native.ToJsonString());
        Assert.AreEqual(10000, (int?)Options(maintained)["additional-timeout"]);
        Assert.AreEqual(1000, (int?)Options(native)["additional-timeout"]);
        Assert.AreEqual(true, (bool?)Options(native)["disable-mix-mutants"]);
        Assert.IsTrue(JsonNode.DeepEquals(Options(maintained)["coverage-analysis"], Options(native)["coverage-analysis"]));
    }

    [TestMethod]
    [DataRow("unit-runtime")]
    [DataRow("pipes")]
    public void ExactShortNonMixingCampaignMayUseEightWorkersWithItsMaintainedTwoWorkerReferenceUnchanged(string group)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, 1000);
        Options(native)["concurrency"] = 8;
        string maintainedBefore = maintained.ToJsonString(), nativeBefore = native.ToJsonString();
        HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained);
        Assert.AreEqual(maintainedBefore, maintained.ToJsonString());
        Assert.AreEqual(nativeBefore, native.ToJsonString());
        Assert.AreEqual(2, (int?)Options(maintained)["concurrency"]);
        Assert.AreEqual(8, (int?)Options(native)["concurrency"]);
        Assert.AreEqual(10000, (int?)Options(maintained)["additional-timeout"]);
        Assert.AreEqual(1000, (int?)Options(native)["additional-timeout"]);
        Assert.AreEqual(true, (bool?)Options(native)["disable-mix-mutants"]);
    }

    [TestMethod]
    [DataRow("unit-runtime", "7")]
    [DataRow("unit-runtime", "9")]
    [DataRow("unit-runtime", "\"8\"")]
    [DataRow("unit-runtime", "8.0")]
    [DataRow("unit-runtime", "null")]
    [DataRow("unit-runtime", "missing")]
    [DataRow("pipes", "7")]
    [DataRow("pipes", "9")]
    [DataRow("pipes", "\"8\"")]
    [DataRow("pipes", "8.0")]
    [DataRow("pipes", "null")]
    [DataRow("pipes", "missing")]
    public void ShortParallelCampaignAcceptsOnlyTheExactReviewedWorkerInteger(string group, string literal)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, 1000);
        if (literal == "missing") { Options(native).Remove("concurrency"); }
        else { Options(native)["concurrency"] = JsonNode.Parse(literal); }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", 10000)]
    [DataRow("unit-runtime", 300000)]
    [DataRow("unit-runtime", 4500000)]
    [DataRow("pipes", 10000)]
    [DataRow("pipes", 2100000)]
    [DataRow("aspnet", 1000)]
    [DataRow("aspnet", 750000)]
    public void EightWorkersCannotChangeHistoricalOrAspNetProfileAcceptance(string group, int budget)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, budget);
        Options(native)["concurrency"] = 8;
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", "missing")]
    [DataRow("unit-runtime", "false")]
    [DataRow("unit-runtime", "\"true\"")]
    [DataRow("pipes", "missing")]
    [DataRow("pipes", "false")]
    [DataRow("pipes", "\"true\"")]
    public void EightWorkersRequireTheExactShortNonMixingProfile(string group, string literal)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, 1000);
        Options(native)["concurrency"] = 8;
        if (literal == "missing") { Options(native).Remove("disable-mix-mutants"); }
        else { Options(native)["disable-mix-mutants"] = JsonNode.Parse(literal); }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", "8")]
    [DataRow("unit-runtime", "\"2\"")]
    [DataRow("unit-runtime", "2.0")]
    [DataRow("pipes", "8")]
    [DataRow("pipes", "\"2\"")]
    [DataRow("pipes", "2.0")]
    public void EightWorkersCannotSelfApproveAChangedMaintainedReference(string group, string literal)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, 1000);
        Options(native)["concurrency"] = 8;
        Options(maintained)["concurrency"] = JsonNode.Parse(literal);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", "filter")]
    [DataRow("unit-runtime", "source")]
    [DataRow("unit-runtime", "coverage")]
    [DataRow("unit-runtime", "threshold")]
    [DataRow("unit-runtime", "bail")]
    [DataRow("unit-runtime", "timeout-factor")]
    [DataRow("pipes", "filter")]
    [DataRow("pipes", "source")]
    [DataRow("pipes", "coverage")]
    [DataRow("pipes", "threshold")]
    [DataRow("pipes", "bail")]
    [DataRow("pipes", "timeout-factor")]
    public void EightWorkersCannotConcealAnotherScopeOrExecutionSettingChange(string group, string changed)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, 1000), options = Options(native);
        options["concurrency"] = 8;
        switch (changed)
        {
            case "filter": options["test-case-filter"] = "FullyQualifiedName=Only.One.Test"; break;
            case "source": options["mutate"]!.AsArray().RemoveAt(1); break;
            case "coverage": options["coverage-analysis"] = group == "unit-runtime" ? "off" : "perTest"; break;
            case "threshold": options["thresholds"]!["break"] = 0; break;
            case "bail": options["disable-bail"] = false; break;
            case "timeout-factor": options["timeout-factor"] = 0; break;
            default: throw new InvalidOperationException("Unknown parallel short-profile receiving control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", "999")]
    [DataRow("unit-runtime", "1001")]
    [DataRow("unit-runtime", "15000")]
    [DataRow("unit-runtime", "\"1000\"")]
    [DataRow("unit-runtime", "1000.0")]
    [DataRow("pipes", "999")]
    [DataRow("pipes", "1001")]
    [DataRow("pipes", "15000")]
    [DataRow("pipes", "\"1000\"")]
    [DataRow("pipes", "1000.0")]
    public void ShortAdditionalBudgetIsOneExactIntegerRatherThanAnArbitraryTimeoutOverride(string group, string literal)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, 1000);
        Options(native)["additional-timeout"] = JsonNode.Parse(literal);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", "missing")]
    [DataRow("unit-runtime", "false")]
    [DataRow("unit-runtime", "\"true\"")]
    [DataRow("pipes", "missing")]
    [DataRow("pipes", "false")]
    [DataRow("pipes", "\"true\"")]
    public void ShortAdditionalBudgetRequiresAnExplicitLiteralTrueNonMixingOption(string group, string literal)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, 1000);
        if (literal == "missing") { Options(native).Remove("disable-mix-mutants"); }
        else { Options(native)["disable-mix-mutants"] = JsonNode.Parse(literal); }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", "filter")]
    [DataRow("unit-runtime", "source")]
    [DataRow("unit-runtime", "coverage")]
    [DataRow("unit-runtime", "concurrency")]
    [DataRow("unit-runtime", "threshold")]
    [DataRow("unit-runtime", "bail")]
    [DataRow("pipes", "filter")]
    [DataRow("pipes", "source")]
    [DataRow("pipes", "coverage")]
    [DataRow("pipes", "concurrency")]
    [DataRow("pipes", "threshold")]
    [DataRow("pipes", "bail")]
    public void ShortAdditionalBudgetCannotConcealAChangedCompleteScopeOrExecutionPolicy(string group, string changed)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, 1000), options = Options(native);
        switch (changed)
        {
            case "filter": options["test-case-filter"] = "FullyQualifiedName=Only.One.Test"; break;
            case "source": options["mutate"]!.AsArray().RemoveAt(1); break;
            case "coverage": options["coverage-analysis"] = group == "unit-runtime" ? "off" : "perTest"; break;
            case "concurrency": options["concurrency"] = 1; break;
            case "threshold": options["thresholds"]!["break"] = 0; break;
            case "bail": options["disable-bail"] = false; break;
            default: throw new InvalidOperationException("Unknown short-budget receiving control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime")]
    [DataRow("pipes")]
    public void ShortAdditionalBudgetCannotSelfApproveAnUnsupportedTimeoutFactorOrMaintainedBudget(string group)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, 1000);
        Options(maintained)["timeout-factor"] = 0;
        Options(native)["timeout-factor"] = 0;
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
        Options(maintained).Remove("timeout-factor");
        Options(native).Remove("timeout-factor");
        Options(maintained)["additional-timeout"] = 1000;
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    public void ShortAdditionalBudgetDoesNotAlterAspNetCampaignAcceptance()
    {
        JsonObject maintained = Configuration("aspnet"), native = ReviewedBudget(maintained, 1000);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate("aspnet", native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", "4500001")]
    [DataRow("unit-runtime", "2100000")]
    [DataRow("unit-runtime", "\"4500000\"")]
    [DataRow("unit-runtime", "4500000.0")]
    [DataRow("unit-runtime", "null")]
    [DataRow("unit-runtime", "missing")]
    [DataRow("pipes", "10000")]
    [DataRow("pipes", "300000")]
    [DataRow("pipes", "4500000")]
    [DataRow("pipes", "2100001")]
    [DataRow("pipes", "\"2100000\"")]
    [DataRow("pipes", "2100000.0")]
    [DataRow("pipes", "null")]
    [DataRow("pipes", "missing")]
    public void AggregateHostBudgetProfilesRejectOtherGroupsValuesAndJsonRepresentations(string group, string literal)
    {
        JsonObject maintained = Configuration(group), native = NoMix(maintained);
        if (literal == "missing") { Options(native).Remove("additional-timeout"); }
        else { Options(native)["additional-timeout"] = JsonNode.Parse(literal); }
        string maintainedBefore = maintained.ToJsonString(), nativeBefore = native.ToJsonString();
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
        Assert.AreEqual(maintainedBefore, maintained.ToJsonString());
        Assert.AreEqual(nativeBefore, native.ToJsonString());
    }

    [TestMethod]
    [DataRow(4500000)]
    [DataRow(2100000)]
    public void AggregateUnitAndPipesProfilesDoNotAlterAspNetCampaignAcceptance(int budget)
    {
        JsonObject maintained = Configuration("aspnet"), native = ReviewedBudget(maintained, budget);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate("aspnet", native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", 4500000, "missing")]
    [DataRow("unit-runtime", 4500000, "false")]
    [DataRow("unit-runtime", 4500000, "\"true\"")]
    [DataRow("pipes", 2100000, "missing")]
    [DataRow("pipes", 2100000, "false")]
    [DataRow("pipes", 2100000, "\"true\"")]
    public void AggregateHostBudgetsRequireAnExplicitLiteralTrueNonMixingOption(string group, int budget, string literal)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, budget);
        if (literal == "missing") { Options(native).Remove("disable-mix-mutants"); }
        else { Options(native)["disable-mix-mutants"] = JsonNode.Parse(literal); }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", 4500000, "filter")]
    [DataRow("unit-runtime", 4500000, "source")]
    [DataRow("unit-runtime", 4500000, "range")]
    [DataRow("unit-runtime", 4500000, "coverage")]
    [DataRow("unit-runtime", 4500000, "concurrency")]
    [DataRow("unit-runtime", 4500000, "threshold")]
    [DataRow("unit-runtime", 4500000, "root-approval")]
    [DataRow("unit-runtime", 4500000, "option-approval")]
    [DataRow("pipes", 2100000, "filter")]
    [DataRow("pipes", 2100000, "source")]
    [DataRow("pipes", 2100000, "range")]
    [DataRow("pipes", 2100000, "coverage")]
    [DataRow("pipes", 2100000, "concurrency")]
    [DataRow("pipes", 2100000, "threshold")]
    [DataRow("pipes", 2100000, "root-approval")]
    [DataRow("pipes", 2100000, "option-approval")]
    public void AggregateHostBudgetCannotConcealANarrowedCohortOrAnotherConfigurationOverride(string group, int budget, string changed)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, budget), options = Options(native);
        switch (changed)
        {
            case "filter": options["test-case-filter"] = "FullyQualifiedName=Only.One.Test"; break;
            case "source": options["mutate"]!.AsArray().RemoveAt(1); break;
            case "range": options["mutate"]![0] = "Healthcheck/Example.cs{1..2}"; break;
            case "coverage": options["coverage-analysis"] = group == "unit-runtime" ? "off" : "perTest"; break;
            case "concurrency": options["concurrency"] = 1; break;
            case "threshold": options["thresholds"]!["low"] = 89; break;
            case "root-approval": native["approved"] = true; break;
            case "option-approval": options["approved"] = true; break;
            default: throw new InvalidOperationException("Unknown aggregate-budget receiving control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", 4500000, 9999)]
    [DataRow("unit-runtime", 4500000, 4500000)]
    [DataRow("pipes", 2100000, 9999)]
    [DataRow("pipes", 2100000, 2100000)]
    public void AggregateNativeBudgetCannotRewriteOrSelfApproveItsMaintainedBudgetPreimage(string group, int budget, int maintainedBudget)
    {
        JsonObject maintained = Configuration(group), native = ReviewedBudget(maintained, budget);
        Options(maintained)["additional-timeout"] = maintainedBudget;
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("unit-runtime", "\"10000\"")]
    [DataRow("unit-runtime", "10000.0")]
    [DataRow("unit-runtime", "null")]
    [DataRow("pipes", "\"10000\"")]
    [DataRow("pipes", "10000.0")]
    [DataRow("pipes", "null")]
    [DataRow("aspnet", "\"750000\"")]
    [DataRow("aspnet", "750000.0")]
    [DataRow("aspnet", "null")]
    public void MatchingMaintainedBudgetsWithWrongJsonTypesCannotApproveThemselves(string group, string literal)
    {
        JsonObject maintained = Configuration(group);
        Options(maintained)["additional-timeout"] = JsonNode.Parse(literal);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, (JsonObject)maintained.DeepClone(), maintained));
    }

    [TestMethod]
    [DataRow("60000")]
    [DataRow("300001")]
    [DataRow("\"300000\"")]
    [DataRow("300000.0")]
    [DataRow("null")]
    [DataRow("false")]
    [DataRow("-1")]
    [DataRow("0")]
    [DataRow("[]")]
    [DataRow("{}")]
    public void RelaxedHostBudgetIsOneExactIntegerProfileRatherThanAnArbitraryTimeoutOverride(string literal)
    {
        JsonObject maintained = Configuration("unit-runtime"), native = Relaxed(maintained);
        Options(native)["additional-timeout"] = JsonNode.Parse(literal);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate("unit-runtime", native, maintained));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RelaxedHostBudgetRequiresNonMixingRatherThanACombinedSelectorCampaign(bool absent)
    {
        JsonObject maintained = Configuration("unit-runtime"), native = Relaxed(maintained);
        if (absent) { Options(native).Remove("disable-mix-mutants"); }
        else { Options(native)["disable-mix-mutants"] = false; }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate("unit-runtime", native, maintained));
    }

    [TestMethod]
    [DataRow("pipes")]
    [DataRow("aspnet")]
    public void RelaxedUnitBudgetCannotAlterAnUnchangedChildTransportCampaign(string group)
    {
        JsonObject maintained = Configuration(group), native = Relaxed(maintained);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("filter")]
    [DataRow("source")]
    [DataRow("coverage")]
    [DataRow("concurrency")]
    [DataRow("threshold")]
    [DataRow("approval")]
    public void ReviewedBudgetAndNonMixingCannotConcealAnotherConfigurationDelta(string changed)
    {
        JsonObject maintained = Configuration("unit-runtime"), native = Relaxed(maintained);
        switch (changed)
        {
            case "filter": Options(native)["test-case-filter"] = "FullyQualifiedName=Only.One.Test"; break;
            case "source": Options(native)["mutate"]!.AsArray().RemoveAt(1); break;
            case "coverage": Options(native)["coverage-analysis"] = "off"; break;
            case "concurrency": Options(native)["concurrency"] = 1; break;
            case "threshold": Options(native)["thresholds"]!["break"] = 0; break;
            case "approval": native["approved"] = true; break;
            default: throw new InvalidOperationException("Unknown receiving control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate("unit-runtime", native, maintained));
    }

    [TestMethod]
    public void ReviewedBudgetDeltaRequiresTheExactOriginalMaintainedBudget()
    {
        JsonObject maintained = Configuration("unit-runtime"), native = Relaxed(maintained);
        Options(maintained)["additional-timeout"] = 9999;
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate("unit-runtime", native, maintained));
    }

    [TestMethod]
    [DataRow("false")]
    [DataRow("\"true\"")]
    [DataRow("\"false\"")]
    [DataRow("0")]
    [DataRow("1")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    public void TheAdditionalSchedulingOptionMustBeLiteralJsonBooleanTrue(string literal)
    {
        JsonObject maintained = Configuration("unit-runtime"), native = NoMix(maintained);
        Options(native)["disable-mix-mutants"] = JsonNode.Parse(literal);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate("unit-runtime", native, maintained));
    }

    [TestMethod]
    [DataRow("pipes")]
    [DataRow("aspnet")]
    public void ThisUnitSchedulingIncrementDoesNotChangeChildTransportCampaignAcceptance(string group)
    {
        JsonObject maintained = Configuration(group), native = NoMix(maintained);
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, native, maintained));
    }

    [TestMethod]
    [DataRow("project")]
    [DataRow("test-project")]
    [DataRow("extra-test-project")]
    [DataRow("filter")]
    [DataRow("remove-source")]
    [DataRow("extra-source")]
    [DataRow("range-source")]
    [DataRow("source-order")]
    [DataRow("coverage-off")]
    [DataRow("explicit-coverage")]
    [DataRow("timeout")]
    [DataRow("missing-timeout")]
    [DataRow("concurrency")]
    [DataRow("bail")]
    [DataRow("level")]
    [DataRow("configuration")]
    [DataRow("framework")]
    [DataRow("runner")]
    [DataRow("reporters")]
    [DataRow("high")]
    [DataRow("low")]
    [DataRow("break")]
    [DataRow("ignored-methods")]
    [DataRow("ignored-mutations")]
    [DataRow("excluded-linq")]
    [DataRow("extra-root-setting")]
    public void DisablingMixingDoesNotAdmitASecondScopeFilterToolOrBudgetChange(string changed)
    {
        JsonObject maintained = Configuration("unit-runtime"), native = NoMix(maintained), options = Options(native);
        switch (changed)
        {
            case "project": options["project"] = "Other.csproj"; break;
            case "test-project": options["test-projects"]![0] = "../../tests/Other.csproj"; break;
            case "extra-test-project": options["test-projects"]!.AsArray().Add("../../tests/Other.csproj"); break;
            case "filter": options["test-case-filter"] = "FullyQualifiedName=Only.One.Test"; break;
            case "remove-source": options["mutate"]!.AsArray().RemoveAt(1); break;
            case "extra-source": options["mutate"]!.AsArray().Add("Healthcheck/Other.cs"); break;
            case "range-source": options["mutate"]![0] = "Healthcheck/Example.cs{1..2}"; break;
            case "source-order": options["mutate"] = new JsonArray("Healthcheck/OtherExample.cs", "Healthcheck/Example.cs"); break;
            case "coverage-off": options["coverage-analysis"] = "off"; break;
            case "explicit-coverage": options["coverage-analysis"] = "perTest"; break;
            case "timeout": options["additional-timeout"] = 60000; break;
            case "missing-timeout": options.Remove("additional-timeout"); break;
            case "concurrency": options["concurrency"] = 1; break;
            case "bail": options["disable-bail"] = false; break;
            case "level": options["mutation-level"] = "Basic"; break;
            case "configuration": options["configuration"] = "Debug"; break;
            case "framework": options["target-framework"] = "net9.0"; break;
            case "runner": options["test-runner"] = "dotnettest"; break;
            case "reporters": options["reporters"] = new JsonArray("progress"); break;
            case "high": options["thresholds"]!["high"] = 99; break;
            case "low": options["thresholds"]!["low"] = 89; break;
            case "break": options["thresholds"]!["break"] = 0; break;
            case "ignored-methods": options["ignore-methods"] = new JsonArray("*.CaptureSnapshot"); break;
            case "ignored-mutations": options["ignore-mutations"] = new JsonArray("Boolean"); break;
            case "excluded-linq": options["excluded-linq-expressions"] = new JsonArray("First"); break;
            case "extra-root-setting": native["approval"] = true; break;
            default: throw new InvalidOperationException("Unknown receiving control.");
        }
        string before = native.ToJsonString();
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate("unit-runtime", native, maintained));
        Assert.AreEqual(before, native.ToJsonString());
    }

    [TestMethod]
    [DataRow("missing-options")]
    [DataRow("null-options")]
    [DataRow("array-options")]
    [DataRow("unknown-group")]
    [DataRow("invalid-level")]
    [DataRow("invalid-threshold")]
    [DataRow("invalid-bail-type")]
    [DataRow("invalid-concurrency-type")]
    [DataRow("unknown-override")]
    public void MatchingMalformedOrUnsupportedConfigurationsCannotApproveThemselves(string changed)
    {
        JsonObject maintained = Configuration("unit-runtime"); string group = "unit-runtime";
        switch (changed)
        {
            case "missing-options": maintained.Remove("stryker-config"); break;
            case "null-options": maintained["stryker-config"] = null; break;
            case "array-options": maintained["stryker-config"] = new JsonArray(); break;
            case "unknown-group": group = "whole-core"; break;
            case "invalid-level": Options(maintained)["mutation-level"] = "Basic"; break;
            case "invalid-threshold": Options(maintained)["thresholds"]!["low"] = 80; break;
            case "invalid-bail-type": Options(maintained)["disable-bail"] = "true"; break;
            case "invalid-concurrency-type": Options(maintained)["concurrency"] = "2"; break;
            case "unknown-override": Options(maintained)["mutator"] = "custom"; break;
            default: throw new InvalidOperationException("Unknown receiving control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationCampaignConfiguration.Validate(group, (JsonObject)maintained.DeepClone(), maintained));
    }

    private static JsonObject Options(JsonObject document) => document["stryker-config"]!.AsObject();
    private static JsonObject NoMix(JsonObject maintained)
    {
        JsonObject result = (JsonObject)maintained.DeepClone(); Options(result)["disable-mix-mutants"] = true; return result;
    }

    private static JsonObject Relaxed(JsonObject maintained)
    {
        JsonObject result = NoMix(maintained); Options(result)["additional-timeout"] = 300000; return result;
    }

    private static JsonObject ReviewedBudget(JsonObject maintained, int budget)
    {
        JsonObject result = NoMix(maintained); Options(result)["additional-timeout"] = budget; return result;
    }

    // Small parser fixture only: complete source/cohort inventories are checked
    // independently by the maintained reader, not manufactured by this test.
    private static JsonObject Configuration(string group)
    {
        JsonObject result = JsonNode.Parse("""
            {"stryker-config":{"project":"CIS.Core.csproj","test-projects":["../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj"],
            "test-case-filter":"FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck.|FullyQualifiedName=CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode",
            "test-runner":"vstest","target-framework":"net10.0","configuration":"Release","mutate":["Healthcheck/Example.cs","Healthcheck/OtherExample.cs"],
            "mutation-level":"Complete","reporters":["json","html","progress","cleartext"],"concurrency":2,"disable-bail":true,
            "additional-timeout":10000,"thresholds":{"high":100,"low":90,"break":90}}}
            """)!.AsObject();
        if (group != "unit-runtime") { Options(result)["coverage-analysis"] = "off"; }
        if (group == "aspnet") { Options(result)["additional-timeout"] = 750000; }
        return result;
    }
}
