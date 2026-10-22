using Review = CIS.Verification.HealthcheckMutationDispositionValidation.Review;

namespace CIS.Verification;

// Severity decisions about seven exact original survivors, with no equivalence
// exclusion or numerator credit. Each primary/peer review discloses observable
// scheduling and progress differences; an adopted-contract violation reopens it.
internal static class HealthcheckMutationAspNetAwaitDispositionCatalog
{
    internal static IReadOnlyList<Review> Entries { get; } = Array.AsReadOnly(new[]
    {
        Await("230", 2855, 2860, "NotFound404 response write",
            "Route selection and the bounded response start precede this await; its continuation finishes the handler."),
        Await("241", 3248, 3253, "MethodNotAllowed405 response write",
            "Method rejection and Allow/body selection precede this await; its continuation finishes the handler."),
        Await("245", 3482, 3487, "authorization-helper completion",
            "Authorization still runs once. Later cancellation checks, denied handling and the source callback can run in a different context."),
        Await("255", 3878, 3883, "Forbidden403 response write",
            "False authorization still prevents capture and business fallthrough; response completion or the cancellation catch can be rescheduled."),
        Await("260", 4227, 4232, "formatted-report response write",
            "The one snapshot capture, formatting and output selection precede this await; handler completion or its cancellation catch can be rescheduled."),
        Await("267", 4537, 4542, "application ValueTask<bool> authorization",
            "The predicate remains present and its bool is not inverted. A pending application IValueTaskSource<bool> can observe the changed UseSchedulingContext flag; registration behavior and helper progress can differ."),
        Await("274", 4912, 4917, "ReportingUnavailable500 response after authorization failure",
            "The bounded error response starts before this await. Returning Handled, write-failure propagation and cancellation can be rescheduled; exception details are still suppressed.")
    });

    private static Review Await(string id, int start, int end, string operation, string behavior) => new(
        "aspnet24b." + id + ".continuation-policy", "ReviewedNoncritical", "ubuntu", "aspnet",
        "5f15348728dd4443acbfc6d5fd5392fb3c7fbbed45ba9d4405075436d21549c4",
        new HealthcheckMutationUnionModel.Definition(id,
            "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs",
            "8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e",
            start, end, "Boolean mutation", "false", "true", "Survived", []),
        [new("implementations/csharp/src/CIS.Core/Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs",
            "8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e", 6372),
        new("contracts/healthcheck/v1/README.md", "9a094b107b807d8ceeb0694c1eb27aaad9aab45e7aac1f9dc50570a7e3794354", 7271),
        new("planning/healthcheck/contract-catalog.md", "28a8ae959d30b7e35e8fd3002e585b06d483db7a282bbf78f7528f8e19335ad9", 40303),
        new("planning/healthcheck/technical-intent.md", "b7e8fc3f8692ab6d5a28f1eb11d1e6582196afd669a9e27197c285ff8593dab8", 33749),
        new("docs/modules/healthcheck/transports.md", "3f2fa4ffdb13aded82fbe8d5d98c1b57389cfcfa079658c7980187d5c1270f1b", 11593),
        new("planning/logging/sources/core-library-implementation-guide-v1.md", "01b4b234fd460088f6c94abb0fc151ca7089375edaf473bf90bc1215d780a675", 110181)],
        [new(".artifacts/hcg/aspnet-await-disposition-review-28/review.json",
            "db063b56f8a75570aea4dc0cb02e933f3c51e1a2b2928eec3eb3908f8f7928df", 26399),
        new(".artifacts/hcg/aspnet-await-root-peer-review-28/peer-review.json",
            "9a0fc8ebe6888a67831a3a07addfd103d2b3e069bb660fefa9d369dd8bfb6010", 3244)],
        operation + ": " + behavior + " The adopted C07 contract fixes route, authorization, capture, response and cancellation behavior, " +
        "without prescribing continuation-context placement. This source-bound noncritical judgment retains real context-posting, " +
        "completion-order and progress differences; custom contexts are not declared unsupported. It does not waive supported async " +
        "or cancellation obligations. An admitted counterexample to an adopted requirement reopens this disposition. " +
        "Keep the raw Survived entry, denominator and zero numerator; unresolved native failures remain blocking.");
}
