using static CIS.Verification.HealthcheckMutationUnionModel;
using Binding = CIS.Verification.HealthcheckMutationSemanticProofs.SourceBinding;
using Review = CIS.Verification.HealthcheckMutationAdditionalUnitSemanticProofs.ReviewedCase;

namespace CIS.Verification;

// Four exact caller-owned output failures from the original Pipes20 report.
// Primary 9490d862b93f9f359df4aabd6635f0ac980da21a5da868b294baa0b8effd053f;
// peer 534b3f3d4ebc2eec8b9942029c8a4b170af5b8c5cf5fdcaa8e3cedcd81d04330;
// source-rendering clarification 3a2477d690ad4161be9a3d6984f68f5cdb08faa55c26fd127d7baab0c4d08f2e.
// Stryker replacement text is identity data, not a standalone source replay:
// nested pattern grouping is not preserved by that rendered string. Actual
// native selected syntax-tree execution and the exact receiving row supply causality.
// Three definitions already have other direct rows; never count a definition twice.
// Controller exits1, all336 child failures/unknown selector and80 aborts remain.
internal static class HealthcheckMutationPipeOutputCatalog
{
    internal const string ReportSha256 = "847327cc1bb2465664ad095ed06b3b1bd26707b5a10cc15c50cc2cee81c67836";
    private static readonly Definition D3023 = new("3023", "Healthcheck/Pipes/HealthProbeClient.cs",
        "ad5afc9bf1f19fddcbd478efd88616607c87262061c75da6cf97407a2994a512", 2661, 2804,
        "Equality mutation", "exception is IOException or SocketException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException", "exception is not IOException or SocketException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException", "Killed",
        [
            "12195f68-1cc1-8ccc-9c48-0672e31d1faa",
            "18266aea-89b4-8b1f-bf6d-cc410a77f9f6",
            "112bbcbc-2fa1-8eb5-a729-ff9fa1ed29ff",
            "1743c149-c815-8010-9ddc-cd27331a1d67",
            "16562823-8a84-8b02-9d0e-4a8dab176c81",
            "1d70c0b1-5293-8759-9285-f4ba0724d7a5",
            "1882b6f0-18e7-8729-b054-e9ebc7835c73",
            "110e0adf-b816-8b24-937e-99b2aabb95b4",
            "16095166-f89a-872f-b06f-9690a67ca158",
            "1d5cc7e9-7198-888a-b6d7-19cb7a884fd9",
            "1612580c-f11d-8609-842c-8b9ea83f8299",
            "1841270b-7476-8e7d-af03-b7c6cd5ee3a4",
            "14c680d1-bb46-8c9c-ad82-cadba24488b6",
            "18e8a6dc-2e66-8124-aab3-17cc26173d13",
            "1684524e-7466-84a8-94cf-b1599f7313de",
            "13c068e6-fc5c-82ee-a4c9-a089534e5757",
            "15e948ee-aeaa-8722-8861-e8cee6081032",
            "15bb871e-6380-802d-8d32-da44629f30fa",
            "1aa18643-40b4-8611-80d1-333924489ae6",
            "12397c7c-c04b-8d89-9e89-1461f2335a8d",
            "1ae5dbcb-34fa-802f-8aef-b14726e19265",
            "1039b74e-43f3-80be-b841-9b2f11f8721d",
            "136fd481-ac9d-8944-bace-4dc18a9ae6ec",
        ]);

    private static readonly Definition D3025 = new("3025", "Healthcheck/Pipes/HealthProbeClient.cs",
        "ad5afc9bf1f19fddcbd478efd88616607c87262061c75da6cf97407a2994a512", 2674, 2783,
        "Logical mutation", "IOException or SocketException or UnauthorizedAccessException or FormatException or InvalidOperationException", "IOException or SocketException or UnauthorizedAccessException or FormatException and InvalidOperationException", "Killed",
        [
            "12195f68-1cc1-8ccc-9c48-0672e31d1faa",
            "18266aea-89b4-8b1f-bf6d-cc410a77f9f6",
            "112bbcbc-2fa1-8eb5-a729-ff9fa1ed29ff",
            "1743c149-c815-8010-9ddc-cd27331a1d67",
            "16562823-8a84-8b02-9d0e-4a8dab176c81",
            "1d70c0b1-5293-8759-9285-f4ba0724d7a5",
            "1882b6f0-18e7-8729-b054-e9ebc7835c73",
            "110e0adf-b816-8b24-937e-99b2aabb95b4",
            "16095166-f89a-872f-b06f-9690a67ca158",
            "1d5cc7e9-7198-888a-b6d7-19cb7a884fd9",
            "1612580c-f11d-8609-842c-8b9ea83f8299",
            "1841270b-7476-8e7d-af03-b7c6cd5ee3a4",
            "14c680d1-bb46-8c9c-ad82-cadba24488b6",
            "18e8a6dc-2e66-8124-aab3-17cc26173d13",
            "1684524e-7466-84a8-94cf-b1599f7313de",
            "13c068e6-fc5c-82ee-a4c9-a089534e5757",
            "15e948ee-aeaa-8722-8861-e8cee6081032",
            "15bb871e-6380-802d-8d32-da44629f30fa",
            "1aa18643-40b4-8611-80d1-333924489ae6",
            "12397c7c-c04b-8d89-9e89-1461f2335a8d",
            "1ae5dbcb-34fa-802f-8aef-b14726e19265",
            "1039b74e-43f3-80be-b841-9b2f11f8721d",
            "136fd481-ac9d-8944-bace-4dc18a9ae6ec",
        ]);

    private static readonly Definition D3026 = new("3026", "Healthcheck/Pipes/HealthProbeClient.cs",
        "ad5afc9bf1f19fddcbd478efd88616607c87262061c75da6cf97407a2994a512", 2674, 2754,
        "Logical mutation", "IOException or SocketException or UnauthorizedAccessException or FormatException", "IOException or SocketException or UnauthorizedAccessException and FormatException", "Killed",
        [
            "12195f68-1cc1-8ccc-9c48-0672e31d1faa",
            "18266aea-89b4-8b1f-bf6d-cc410a77f9f6",
            "112bbcbc-2fa1-8eb5-a729-ff9fa1ed29ff",
            "1743c149-c815-8010-9ddc-cd27331a1d67",
            "16562823-8a84-8b02-9d0e-4a8dab176c81",
            "1d70c0b1-5293-8759-9285-f4ba0724d7a5",
            "1882b6f0-18e7-8729-b054-e9ebc7835c73",
            "110e0adf-b816-8b24-937e-99b2aabb95b4",
            "16095166-f89a-872f-b06f-9690a67ca158",
            "1d5cc7e9-7198-888a-b6d7-19cb7a884fd9",
            "1612580c-f11d-8609-842c-8b9ea83f8299",
            "1841270b-7476-8e7d-af03-b7c6cd5ee3a4",
            "14c680d1-bb46-8c9c-ad82-cadba24488b6",
            "18e8a6dc-2e66-8124-aab3-17cc26173d13",
            "1684524e-7466-84a8-94cf-b1599f7313de",
            "13c068e6-fc5c-82ee-a4c9-a089534e5757",
            "15e948ee-aeaa-8722-8861-e8cee6081032",
            "15bb871e-6380-802d-8d32-da44629f30fa",
            "1aa18643-40b4-8611-80d1-333924489ae6",
            "12397c7c-c04b-8d89-9e89-1461f2335a8d",
            "1ae5dbcb-34fa-802f-8aef-b14726e19265",
            "1039b74e-43f3-80be-b841-9b2f11f8721d",
            "136fd481-ac9d-8944-bace-4dc18a9ae6ec",
        ]);

    private static readonly Definition D3027 = new("3027", "Healthcheck/Pipes/HealthProbeClient.cs",
        "ad5afc9bf1f19fddcbd478efd88616607c87262061c75da6cf97407a2994a512", 2674, 2735,
        "Logical mutation", "IOException or SocketException or UnauthorizedAccessException", "IOException or SocketException and UnauthorizedAccessException", "Killed",
        [
            "12195f68-1cc1-8ccc-9c48-0672e31d1faa",
        ]);

    internal static IReadOnlyList<Review> Cases { get; } = Array.AsReadOnly(new[]
    {
        Case(D3023, "c79153d0237569199014be5a5e275c38f469363f12c87c226d0d677988e2265f", "7076ae4cf3de53587c07bdfaf2f03e0baee39a3266c9eee042d268ab41aeba18"),
        Case(D3025, "c79153d0237569199014be5a5e275c38f469363f12c87c226d0d677988e2265f", "7076ae4cf3de53587c07bdfaf2f03e0baee39a3266c9eee042d268ab41aeba18"),
        Case(D3026, "c79153d0237569199014be5a5e275c38f469363f12c87c226d0d677988e2265f", "7076ae4cf3de53587c07bdfaf2f03e0baee39a3266c9eee042d268ab41aeba18"),
        Case(D3027, "c79153d0237569199014be5a5e275c38f469363f12c87c226d0d677988e2265f", "7076ae4cf3de53587c07bdfaf2f03e0baee39a3266c9eee042d268ab41aeba18"),
    });

    private static Review Case(Definition definition, string messageSha256, string stackSha256)
    {
        const string method = "CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException";
        const string testPath = "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs";
        return new(new Binding(ReportSha256, definition, "12195f68-1cc1-8ccc-9c48-0672e31d1faa", method, testPath,
            "115c483734dc6600331daa5d4d4cff43a75dcf40ae6af69c41c7c66e89ab7691"),
            "System.IO.IOException", messageSha256, stackSha256,
            [new("CIS.Healthcheck.Pipes.HealthProbeClient.RunAsync", "implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthProbeClient.cs"), new(method, testPath)],
            "The exact healthy service and running server use a caller-owned UnavailableStream whose WriteAsync increments Attempts and returns a faulted ValueTask carrying the supplied IOException. No private state is injected.",
            "C08 and the exact RunAsync output catch route require TransportOrProtocolFailure when caller output fails, without leaking the exception/private path. The receiving source asserts that result, redacted stderr and one output attempt.",
            "Actual selected Stryker syntax-tree execution lets this supplied output IOException escape from RunAsync into the exact receiving call. The source catch route, first Core frame and complete pinned stack distinguish output cause from cleanup. Repeated receiving unwind frames alone prove no cleanup. Rendered replacement strings are identity data, not standalone source replay.");
    }
}
