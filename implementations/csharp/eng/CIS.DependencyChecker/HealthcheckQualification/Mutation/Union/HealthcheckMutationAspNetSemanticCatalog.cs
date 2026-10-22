using static CIS.Verification.HealthcheckMutationUnionModel;
using P = CIS.Verification.HealthcheckMutationAspNetSemanticProofs;

namespace CIS.Verification;

// Exact ordinary semantic rows from the original ASP.NET 24b rawKilled run.
// Reviewed against the retained F638 receiving source and current collateral.
// No timeout, cleanup, lifecycle or child propagation approval is supplied.
internal static class HealthcheckMutationAspNetSemanticCatalog
{
    private static readonly Definition D202 = new("202", "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", "8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e", 1538, 1729,
        "Conditional (true) mutation", "path.StartsWithSegments(\u0022/_health\u0022, StringComparison.OrdinalIgnoreCase)\n                ? HandleAsync(context, path.Value!, source, settings, authorizeDetails)\n                : next(context)", "(true?HandleAsync(context, path.Value!, source, settings, authorizeDetails)\n:next(context))", "Killed", []);
    private static readonly Definition D204 = new("204", "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", "8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e", 1562, 1572,
        "String mutation", "\u0022/_health\u0022", "\u0022\u0022", "Killed", []);
    private static readonly Definition D228 = new("228", "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", "8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e", 2783, 2793,
        "String mutation", "\u0022NotFound\u0022", "\u0022\u0022", "Killed", []);
    private static readonly Definition D272 = new("272", "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", "8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e", 4817, 4839,
        "String mutation", "\u0022ReportingUnavailable\u0022", "\u0022\u0022", "Killed", []);
    private static readonly Definition D229 = new("229", "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", "8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e", 2801, 2806,
        "Boolean mutation", "false", "true", "Killed", []);
    private static readonly Definition D280 = new("280", "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs", "8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e", 5597, 5619,
        "String mutation", "\u0022ReportingUnavailable\u0022", "\u0022\u0022", "Killed", []);

    internal static readonly P.ReviewedCase[] Cases =
    [
        new(D202, "de0c8635dd3983b16933177822b97abdbc1f4c6591adcbf4151a2fca774847d4", "1b2ae3e0-5599-8e81-bbce-89c63ab4ea68",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.SimilarPrefixContinuesNormalApplicationPipeline", "BusinessResponse404",
            "7afb597ba2200b915b7c790e1d09aad240a4c3fdcbfe1587fafa284db6d7f2cf", "fbeb0ba486316f0156270e4887a810d81be6b091a3b8b2a4219f7f3914a0ddf3",
            [],
            "The four DataRows call ordinary business paths /_healthcare, /_healthcare/ready, /_health-other and /business through the actual host. The registered downstream business handler returns business:\u003Cpath\u003E; no health snapshot is required.",
            "The selected route predicate or reserved-prefix literal mutation routes an admitted business request into the health adapter. EnsureSuccessStatusCode observes actual 404 before the expected business-response assertion; this is a response contract failure, not a Core-origin exception. The original full child audit remains mandatory."),
        new(D202, "de0c8635dd3983b16933177822b97abdbc1f4c6591adcbf4151a2fca774847d4", "14bcfcdb-5ba9-8643-8087-8b3b30c4d332",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.SimilarPrefixContinuesNormalApplicationPipeline", "BusinessResponse404",
            "7afb597ba2200b915b7c790e1d09aad240a4c3fdcbfe1587fafa284db6d7f2cf", "fbeb0ba486316f0156270e4887a810d81be6b091a3b8b2a4219f7f3914a0ddf3",
            [],
            "The four DataRows call ordinary business paths /_healthcare, /_healthcare/ready, /_health-other and /business through the actual host. The registered downstream business handler returns business:\u003Cpath\u003E; no health snapshot is required.",
            "The selected route predicate or reserved-prefix literal mutation routes an admitted business request into the health adapter. EnsureSuccessStatusCode observes actual 404 before the expected business-response assertion; this is a response contract failure, not a Core-origin exception. The original full child audit remains mandatory."),
        new(D202, "de0c8635dd3983b16933177822b97abdbc1f4c6591adcbf4151a2fca774847d4", "13491484-bceb-8ba9-92ff-4505d806d64e",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.SimilarPrefixContinuesNormalApplicationPipeline", "BusinessResponse404",
            "7afb597ba2200b915b7c790e1d09aad240a4c3fdcbfe1587fafa284db6d7f2cf", "fbeb0ba486316f0156270e4887a810d81be6b091a3b8b2a4219f7f3914a0ddf3",
            [],
            "The four DataRows call ordinary business paths /_healthcare, /_healthcare/ready, /_health-other and /business through the actual host. The registered downstream business handler returns business:\u003Cpath\u003E; no health snapshot is required.",
            "The selected route predicate or reserved-prefix literal mutation routes an admitted business request into the health adapter. EnsureSuccessStatusCode observes actual 404 before the expected business-response assertion; this is a response contract failure, not a Core-origin exception. The original full child audit remains mandatory."),
        new(D202, "de0c8635dd3983b16933177822b97abdbc1f4c6591adcbf4151a2fca774847d4", "155675e5-1776-8195-ad5f-719236bf562d",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.SimilarPrefixContinuesNormalApplicationPipeline", "BusinessResponse404",
            "7afb597ba2200b915b7c790e1d09aad240a4c3fdcbfe1587fafa284db6d7f2cf", "fbeb0ba486316f0156270e4887a810d81be6b091a3b8b2a4219f7f3914a0ddf3",
            [],
            "The four DataRows call ordinary business paths /_healthcare, /_healthcare/ready, /_health-other and /business through the actual host. The registered downstream business handler returns business:\u003Cpath\u003E; no health snapshot is required.",
            "The selected route predicate or reserved-prefix literal mutation routes an admitted business request into the health adapter. EnsureSuccessStatusCode observes actual 404 before the expected business-response assertion; this is a response contract failure, not a Core-origin exception. The original full child audit remains mandatory."),
        new(D204, "de0c8635dd3983b16933177822b97abdbc1f4c6591adcbf4151a2fca774847d4", "1b2ae3e0-5599-8e81-bbce-89c63ab4ea68",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.SimilarPrefixContinuesNormalApplicationPipeline", "BusinessResponse404",
            "7afb597ba2200b915b7c790e1d09aad240a4c3fdcbfe1587fafa284db6d7f2cf", "fbeb0ba486316f0156270e4887a810d81be6b091a3b8b2a4219f7f3914a0ddf3",
            [],
            "The four DataRows call ordinary business paths /_healthcare, /_healthcare/ready, /_health-other and /business through the actual host. The registered downstream business handler returns business:\u003Cpath\u003E; no health snapshot is required.",
            "The selected route predicate or reserved-prefix literal mutation routes an admitted business request into the health adapter. EnsureSuccessStatusCode observes actual 404 before the expected business-response assertion; this is a response contract failure, not a Core-origin exception. The original full child audit remains mandatory."),
        new(D204, "de0c8635dd3983b16933177822b97abdbc1f4c6591adcbf4151a2fca774847d4", "14bcfcdb-5ba9-8643-8087-8b3b30c4d332",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.SimilarPrefixContinuesNormalApplicationPipeline", "BusinessResponse404",
            "7afb597ba2200b915b7c790e1d09aad240a4c3fdcbfe1587fafa284db6d7f2cf", "fbeb0ba486316f0156270e4887a810d81be6b091a3b8b2a4219f7f3914a0ddf3",
            [],
            "The four DataRows call ordinary business paths /_healthcare, /_healthcare/ready, /_health-other and /business through the actual host. The registered downstream business handler returns business:\u003Cpath\u003E; no health snapshot is required.",
            "The selected route predicate or reserved-prefix literal mutation routes an admitted business request into the health adapter. EnsureSuccessStatusCode observes actual 404 before the expected business-response assertion; this is a response contract failure, not a Core-origin exception. The original full child audit remains mandatory."),
        new(D204, "de0c8635dd3983b16933177822b97abdbc1f4c6591adcbf4151a2fca774847d4", "13491484-bceb-8ba9-92ff-4505d806d64e",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.SimilarPrefixContinuesNormalApplicationPipeline", "BusinessResponse404",
            "7afb597ba2200b915b7c790e1d09aad240a4c3fdcbfe1587fafa284db6d7f2cf", "fbeb0ba486316f0156270e4887a810d81be6b091a3b8b2a4219f7f3914a0ddf3",
            [],
            "The four DataRows call ordinary business paths /_healthcare, /_healthcare/ready, /_health-other and /business through the actual host. The registered downstream business handler returns business:\u003Cpath\u003E; no health snapshot is required.",
            "The selected route predicate or reserved-prefix literal mutation routes an admitted business request into the health adapter. EnsureSuccessStatusCode observes actual 404 before the expected business-response assertion; this is a response contract failure, not a Core-origin exception. The original full child audit remains mandatory."),
        new(D204, "de0c8635dd3983b16933177822b97abdbc1f4c6591adcbf4151a2fca774847d4", "155675e5-1776-8195-ad5f-719236bf562d",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.SimilarPrefixContinuesNormalApplicationPipeline", "BusinessResponse404",
            "7afb597ba2200b915b7c790e1d09aad240a4c3fdcbfe1587fafa284db6d7f2cf", "fbeb0ba486316f0156270e4887a810d81be6b091a3b8b2a4219f7f3914a0ddf3",
            [],
            "The four DataRows call ordinary business paths /_healthcare, /_healthcare/ready, /_health-other and /business through the actual host. The registered downstream business handler returns business:\u003Cpath\u003E; no health snapshot is required.",
            "The selected route predicate or reserved-prefix literal mutation routes an admitted business request into the health adapter. EnsureSuccessStatusCode observes actual 404 before the expected business-response assertion; this is a response contract failure, not a Core-origin exception. The original full child audit remains mandatory."),
        new(D228, "c50ebb8567c78e7ecbbf8d2d27da8a2fce51dbb00a15d845a97394e9113daa40", "15e8095d-7ef0-827c-9276-102d0644de72",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.DisabledDetailsAcceptsOmittedAuthorizationAndStillReservesItsRoute", "AdapterInvalidErrorCode",
            "28f57b8d178f5cd3d00c12dfd43ac3b928331d784a909c5d3c941a5e433ea701", "31757b04ddd1aec5ef9bf20f885c42726345318f615e9bae5cd8b3157d322e1a",
            ["CIS.Healthcheck.HealthReportFormatter.FormatError", "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.HandleAsync"],
            "DetailsEnabled=false and an omitted authorization predicate are admitted. A real /_health/details GET must return complete HTTP NotFound JSON without reading source or running the business handler.",
            "The selected empty NotFound literal reaches the pinned formatter and protocol IsErrorCode guard, which rejects the empty code before the expected 404 response assertions."),
        new(D272, "b90b2c4ba9e5136f17659796b736f560f5592f31883bb8ea136c82cb888bbe53", "14bb9380-76ee-8d79-8fd5-370d1dd40ee6",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.ReportingFailuresPublishHttpErrorsAndHeadUsesTheSameCompleteRepresentation", "AdapterInvalidErrorCode",
            "419f8df39107da358c9986c7b48e14a0c3b2fe23f0b2d771b8ef310b85ceba88", "583b1dda865cf443e3fdb5ae89115024ed416eff744f63372c4b48ac548b32f9",
            ["CIS.Healthcheck.HealthReportFormatter.FormatError", "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.AuthorizeDetailsAsync", "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.HandleAsync"],
            "The public middleware receives a real source IOException, unexpected source cancellation, or failed explicit authorizer ValueTask. These admitted dependency failures must produce complete HTTP ReportingUnavailable error responses.",
            "The selected empty ReportingUnavailable literal reaches the pinned formatter/protocol guard through the exact reviewed adapter method and escapes as ArgumentOutOfRangeException(code), before the complete 500 response assertions. Only this exact message/stack/GUID row is reviewed; other JSON or deadline failures remain unresolved."),
        new(D229, "c50ebb8567c78e7ecbbf8d2d27da8a2fce51dbb00a15d845a97394e9113daa40", "15e8095d-7ef0-827c-9276-102d0644de72",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.DisabledDetailsAcceptsOmittedAuthorizationAndStillReservesItsRoute", "AdapterInvalidErrorCode",
            "28f57b8d178f5cd3d00c12dfd43ac3b928331d784a909c5d3c941a5e433ea701", "31757b04ddd1aec5ef9bf20f885c42726345318f615e9bae5cd8b3157d322e1a",
            ["CIS.Healthcheck.HealthReportFormatter.FormatError", "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.HandleAsync"],
            "DetailsEnabled=false and an omitted authorization predicate are admitted. A real /_health/details GET must return complete HTTP NotFound JSON without reading source or running the business handler.",
            "The selected pipe=true argument rejects HTTP-only NotFound in the pinned formatter/protocol guard before the expected 404 response assertions."),
        new(D280, "92c8a244ccb2dc8260735b182feeed0b2937b985edd9b45e8dcbfc9338a79c8a", "1e814bc0-ea07-85f6-a736-e3f4855993f4",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.ReportingFailuresPublishHttpErrorsAndHeadUsesTheSameCompleteRepresentation", "AdapterInvalidErrorCode",
            "419f8df39107da358c9986c7b48e14a0c3b2fe23f0b2d771b8ef310b85ceba88", "2863422601a733ebaf75d862f58cb0bf20c179d994bf3914b2f7015158f04c07",
            ["CIS.Healthcheck.HealthReportFormatter.FormatError", "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.CaptureReport", "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.HandleAsync"],
            "The public middleware receives a real source IOException, unexpected source cancellation, or failed explicit authorizer ValueTask. These admitted dependency failures must produce complete HTTP ReportingUnavailable error responses.",
            "The selected empty ReportingUnavailable literal reaches the pinned formatter/protocol guard through the exact reviewed adapter method and escapes as ArgumentOutOfRangeException(code), before the complete 500 response assertions. Only this exact message/stack/GUID row is reviewed; other JSON or deadline failures remain unresolved."),
        new(D280, "92c8a244ccb2dc8260735b182feeed0b2937b985edd9b45e8dcbfc9338a79c8a", "19bae036-bd98-8302-ab82-afb9628c0457",
            "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.ReportingFailuresPublishHttpErrorsAndHeadUsesTheSameCompleteRepresentation", "AdapterInvalidErrorCode",
            "419f8df39107da358c9986c7b48e14a0c3b2fe23f0b2d771b8ef310b85ceba88", "2863422601a733ebaf75d862f58cb0bf20c179d994bf3914b2f7015158f04c07",
            ["CIS.Healthcheck.HealthReportFormatter.FormatError", "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.CaptureReport", "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.HandleAsync"],
            "The public middleware receives a real source IOException, unexpected source cancellation, or failed explicit authorizer ValueTask. These admitted dependency failures must produce complete HTTP ReportingUnavailable error responses.",
            "The selected empty ReportingUnavailable literal reaches the pinned formatter/protocol guard through the exact reviewed adapter method and escapes as ArgumentOutOfRangeException(code), before the complete 500 response assertions. Only this exact message/stack/GUID row is reviewed; other JSON or deadline failures remain unresolved."),
    ];
}
