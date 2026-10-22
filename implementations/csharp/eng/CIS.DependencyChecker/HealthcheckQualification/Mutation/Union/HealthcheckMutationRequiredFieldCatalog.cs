using static CIS.Verification.HealthcheckMutationUnionModel;
using Binding = CIS.Verification.HealthcheckMutationSemanticProofs.SourceBinding;
using Review = CIS.Verification.HealthcheckMutationRequiredFieldProofs.ReviewedCase;

namespace CIS.Verification;

// Sixty independently source-reviewed GUID observations across fifty-eight
// immutable Unit18 definitions. Other failed GUIDs still need their own review.
internal static class HealthcheckMutationRequiredFieldCatalog
{
    private const string Source = "Healthcheck/Reporting/HealthReportFormatter.cs";
    private const string SourceSha256 = "82e9b67bc17492fba6d445f63404ce82ac2ffe116fb5144c35342295dcd81665";
    private const string Guid = "165919c9-e592-826f-ac5a-aad67a79e9f1";
    private const string Method = "CIS.Tests.UnitTests.Healthcheck.HealthReportFormatterMutationQualificationTests.DetailedFormattingPreservesEveryActualExecutionCollectionAndIdentityField";
    private const string TestPath = "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthReportFormatterMutationQualificationTests.cs";
    private const string TestSha256 = "7fe2c7a4d21190c4168c7e392ae4d910379275399edb83add836795066e347dc";
    private const string Route = "The actual False/HTTP row uses a real public service and CaptureSnapshot, then public FormatSnapshot. " +
        "IsError is false and JsonDocument.Parse succeeds before the required GetProperty read. No snapshot DTO is fabricated. ";

    internal static IReadOnlyList<Review> Cases { get; } = Array.AsReadOnly(new[]
    {
        Case(new Definition("3255", Source, SourceSha256, 6285, 6291, "String mutation", "\"name\"", "\"\"", "Killed",
                [Guid, "1841d608-0bed-858e-8fe7-19508a0c965e", "14ccfba9-cf3e-859e-ab43-de260eef453e"]),
            "$.monitors[0].name", "545db100a7093bbff49a6bbe97e4255aee1e06303141158de660253c387ef847",
            "CreateConsecutiveFailures receives Name=owned-execution and the live monitor is in the public snapshot.",
            "C03/C06 preserve supplied monitor metadata. Changing its field name to empty removes the actual name key; " +
            "optional omission when unavailable does not apply to this supplied name."),
        Case(new Definition("3288", Source, SourceSha256, 7382, 7443, "Statement mutation",
                "WriteEnum(writer, \"lastOutcome\", outcomes.LastOutcome.Value);", ";", "Killed",
                [Guid, "1841d608-0bed-858e-8fe7-19508a0c965e", "162456bb-247c-8b21-bc34-1f75b65cad73"]),
            "$.monitors[0].outcomes.lastOutcome", "9ba11093557ff34e511e45425340b8369e191e57583001c540df76ef9365ba0d",
            "The public binding records execution42 Failed and its lifecycle completion after earlier successful/skipped/canceled observations.",
            "C02/C03/C06 preserve the latest applicable accepted outcome, which is present and Failed. " +
            "Removing its writer call removes that required available observation."),
        Case(new Definition("3334", Source, SourceSha256, 8973, 9016, "Statement mutation",
                "writer.WriteString(\"key\", measurement.Key);", ";", "Killed",
                [Guid, "1841d608-0bed-858e-8fe7-19508a0c965e"]),
            "$.monitors[0].measurements[*].key", "ec3f110e1d8a5a67755ba9e5de30583228b5fee961d7e95cb9c57fbb108d0422",
            "Five valid public ReportMeasurement calls supply ordinal keys a through e and supported scalar kinds.",
            "C02/C06 and report.schema.json require each measurement key. Removing WriteString removes the key " +
            "observed by the declared receiving method's Select/ToArray projection; the pinned full stack retains that lambda."),
        Case(new Definition("3354", Source, SourceSha256, 9975, 10037, "Statement mutation",
                "WriteTime(writer, \"observedAtUtc\", measurement.ObservedAtUtc);", ";", "Killed",
                [Guid, "1841d608-0bed-858e-8fe7-19508a0c965e", "1fbf2b20-d930-818c-9c78-ed14afb7a552"]),
            "$.monitors[0].measurements[*].observedAtUtc", "658c06bb9b3d26bb504d5f0bd7a3f213d1f8ba75c453bce4c28aa50742b9426e",
            "The five public measurements contain explicit Epoch+1 through Epoch+5 second observation instants.",
            "C02/C06 and report.schema.json require the explicit measurement observation instant. " +
            "Removing WriteTime removes the observedAtUtc key before the receiving time-value assertion.")
    }.Concat(HealthcheckMutationRequiredFieldAdditionalCatalog.AdditionalCases).ToArray());

    private static Review Case(Definition definition, string jsonPath, string stackSha256, string admittedInput, string causalReview) =>
        new(new Binding(HealthcheckMutationSemanticCatalog.Unit18ReportSha256, definition, Guid, Method, TestPath, TestSha256),
            jsonPath, stackSha256, Route + admittedInput, causalReview);
}
