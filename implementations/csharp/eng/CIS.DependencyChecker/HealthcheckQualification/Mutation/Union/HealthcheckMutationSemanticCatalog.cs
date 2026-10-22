using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// These are reviewed observations from one immutable native report, not a
// namespace/exception allowlist or a way to reclassify Timeout mutants.
internal static class HealthcheckMutationSemanticCatalog
{
    internal const string Unit18ReportSha256 = "056cc405715ac814bbad9ff45e276e77708c56463beea24ad76e23d19f3bf89d";
    private const string SourcePath = "Healthcheck/Contracts/Collection/HealthCollectionResult.cs";
    private const string SourceSha256 = "245a7c84b20541ac1d3dd69d7301aa7dab9103f17c11d27fe23a021fee046209";
    private static readonly string[] KilledBy =
    [
        "1f8acea8-41a6-8e2e-a734-3abc11a5662e",
        "150cb59c-8a3d-8aad-89d9-f034ec82f2d8",
        "1fd70a80-f519-89b3-adc6-4d253e51113c"
    ];

    private static Definition CountBoundary => new("312", SourcePath, SourceSha256, 5284, 5324,
        "Equality mutation", "measurements.Count > MaximumMeasurements", "measurements.Count >= MaximumMeasurements",
        "Killed", KilledBy.ToArray());

    internal static IReadOnlyList<HealthcheckMutationSemanticProofs.ReviewedCase> Cases { get; } = Array.AsReadOnly(new[]
    {
        Case(KilledBy[0], "CIS.Tests.UnitTests.HealthcheckValueContractTests.CollectionAllowsExactly32OrdinalUniqueMeasurementKeys",
            "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Foundation/HealthcheckValueContractTests.cs",
            "e95e0791d50d8435287faf3511f8eedd00326f77e49011db4df49a91ab211498", "Diagnostics",
            "The test constructs exactly32 diagnostic integer measurements with distinct ordinal item-N keys. " +
            "The receiving collection contract permits all32; evaluation of Diagnostics must return before Assert.HasCount.",
            "contracts/healthcheck/v1/README.md and planning/healthcheck/contract-catalog.md:137 retain the inclusive32-key contract."),
        Case(KilledBy[1], "CIS.Tests.UnitTests.Healthcheck.HealthCollectionCapacityAndFairnessBoundaryTests.FullMetricCapacityDistinguishesUpdatesFromEntirelyNewKeys",
            "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Collection/HealthCollectionCapacityAndFairnessBoundaryTests.cs",
            "47755be42aadf2f8f9fc863b3c4779eb2c664a8092391c7713e26629ebfe7cd9", "Diagnostics",
            "The actual (False,False) row builds exactly32 distinct diagnostic measurements before any lease or runner work. " +
            "Its initial Diagnostics call is admitted regardless of the later update-only capacity scenario.",
            "The pinned row constructs the full allowed measurement map; no callback or cleanup precedes the failing call."),
        Case(KilledBy[2], "CIS.Tests.UnitTests.Healthcheck.HealthCollectionCapacityAndFairnessBoundaryTests.FullMetricCapacityDistinguishesUpdatesFromEntirelyNewKeys",
            "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Collection/HealthCollectionCapacityAndFairnessBoundaryTests.cs",
            "47755be42aadf2f8f9fc863b3c4779eb2c664a8092391c7713e26629ebfe7cd9", "WithMeasurements",
            "The actual (True,False) row builds exactly32 distinct diagnostic measurements before any lease or runner work. " +
            "Condition(true).WithMeasurements must admit that full diagnostic map before collection starts.",
            "The pinned row has a valid required condition result plus the inclusive32-key map; no callback precedes the failing call.")
    });

    private static HealthcheckMutationSemanticProofs.ReviewedCase Case(string guid, string method, string testPath,
        string testSha256, string factory, string admittedInput, string contractReview) => new(Unit18ReportSha256, CountBoundary,
            guid, method, testPath, testSha256, "System.ArgumentException",
            ["CIS.Healthcheck.HealthCollectionResult.CopyMeasurements", "CIS.Healthcheck.HealthCollectionResult..ctor",
                "CIS.Healthcheck.HealthCollectionResult." + factory],
            admittedInput,
            "Changing >32 to >=32 rejects the valid32-element call at the selected guard, before its result assertion. " +
            "The actual native stack is CopyMeasurements -> constructor -> " + factory + " -> the exact reported test. " +
            "This is an ordinary rawKilled semantic failure, not a Timeout reclassification. " + contractReview);
}
