namespace CIS.Verification;

// Entries are source-reviewed code, never supplied approval booleans. No
// historical proposal becomes accepted merely because its JSON says accepted.
internal static class HealthcheckMutationDispositionCatalog
{
    internal static IReadOnlyList<HealthcheckMutationDispositionValidation.Review> Entries { get; } =
        Array.AsReadOnly(new[]
        {
            new HealthcheckMutationDispositionValidation.Review(
                "aspnet24b.200.diagnostic-wording", "ReviewedNoncritical", "ubuntu", "aspnet",
                "5f15348728dd4443acbfc6d5fd5392fb3c7fbbed45ba9d4405075436d21549c4",
                new HealthcheckMutationUnionModel.Definition("200",
                    "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs",
                    "8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e", 1297, 1375,
                    "String mutation", "\"Supply an explicit details authorization predicate when details are enabled.\"", "\"\"", "Survived", []),
                [new("implementations/csharp/src/CIS.Core/Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs",
                    "8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e", 6372),
                new("implementations/csharp/src/CIS.Core/Healthcheck/Contracts/Options/HealthcheckOptions.cs",
                    "75d3e327b107682e5321b65991825fd51b7c5a6c12f7c539aded2dd758d8d419", 7428),
                new("contracts/healthcheck/v1/README.md", "9a094b107b807d8ceeb0694c1eb27aaad9aab45e7aac1f9dc50570a7e3794354", 7271),
                new("planning/healthcheck/contract-catalog.md", "28a8ae959d30b7e35e8fd3002e585b06d483db7a282bbf78f7528f8e19335ad9", 40303)],
                [new(".artifacts/hcg/aspnet-200-disposition-review-26/review.json",
                    "4b4eb299f08a9d275f35fa06eb1605fa108dae2e0888f1c3fd3bcd297d6cac51", 3174),
                new(".artifacts/hcg/aspnet-200-root-peer-review-26/peer-review.json",
                    "8796d7c4323ccf39e24f6f3c428138419561041c95f00b0085101d203d9dd9d8", 3316)],
                "Only the setup exception's explanatory string changes. The required copied-options/details-predicate guard, " +
                "ArgumentException type, authorizeDetails parameter and throw before app.Use remain. The published explicit-predicate " +
                "requirement is preserved; exact explanatory prose is not specified. Diagnostic wording loss is real and is not equivalence. " +
                "Keep the raw Survived entry, its denominator and zero numerator; no other survivor or native integrity failure is classified.")
        }.Concat(HealthcheckMutationAspNetAwaitDispositionCatalog.Entries).ToArray());
}
