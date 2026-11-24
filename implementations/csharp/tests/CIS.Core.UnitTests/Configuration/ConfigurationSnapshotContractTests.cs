using System.Text.Json;
using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationSnapshotContractTests
{
    private const string Authority = "11111111-1111-1111-1111-111111111111";
    private const string Application = "22222222-2222-2222-2222-222222222222";
    private const string Registration = "33333333-3333-3333-3333-333333333333";
    private const string Release = "44444444-4444-4444-4444-444444444444";
    private const string Source = "55555555-5555-5555-5555-555555555555";
    private const string Publication = "66666666-6666-6666-6666-666666666666";
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public async Task LegacyStoreAndDefaultsOnlyOptionsAcquireNoCentralRequirement()
    {
        var legacy = new LegacyStore();
        var options = new ConfigurationOptions("Host", legacy);
        Assert.AreSame(legacy, options.Store);
        Assert.IsEmpty(options.Enrollments);
        Assert.IsNull(options.SnapshotTarget);
        Assert.IsFalse((object)legacy is IConfigurationSnapshotStore);
        ConfigurationReadResult read = await options.Store.ReadAsync(new("Host", "definition"), null, CancellationToken.None);
        Assert.AreEqual(ConfigurationReadKind.Missing, read.Kind);

        var defaultsOnly = new ConfigurationOptions("Host");
        Assert.IsEmpty(defaultsOnly.Enrollments);
        Assert.IsNull(defaultsOnly.SnapshotTarget);
    }

    [TestMethod]
    public void ExplicitEnrollmentUsesTheVersionlessDefinitionWithoutConstructingAConfigurationRoot()
    {
        var enrollment = new ConfigurationEnrollment(typeof(NeverConstructedSettings), "Country.Settings", "1",
            ConfigurationUpdateMode.RestartRequired);
        Assert.AreSame(typeof(NeverConstructedSettings), enrollment.ConfigurationType);
        Assert.AreEqual(ConfigurationIdentity.Create(typeof(NeverConstructedSettings)), enrollment.PersistedDefinition);
        Assert.IsFalse(enrollment.PersistedDefinition.Contains("Version=", StringComparison.Ordinal));
        StringAssert.Contains(enrollment.PersistedDefinition, "Culture=neutral");
        StringAssert.Contains(enrollment.PersistedDefinition, "PublicKeyToken=null");
        Assert.AreEqual(ConfigurationUpdateMode.RestartRequired, enrollment.UpdateMode);
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationEnrollment(typeof(IConfigurationObject), "settings", "1", ConfigurationUpdateMode.HotReload));
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationEnrollment(typeof(string), "settings", "1", ConfigurationUpdateMode.HotReload));
    }

    [TestMethod]
    [DataRow("9007199254740993")]
    [DataRow("9223372036854775807")]
    public void SchemaAndAssignmentGenerationRemainExactBeyondJavaScriptIntegerPrecision(string number)
    {
        ConfigurationDesiredIdentity desired = Desired(schema: number, generation: number);
        Assert.AreEqual(number, desired.SchemaVersion);
        Assert.AreEqual(number, desired.AssignmentGeneration);
        var enrollment = new ConfigurationEnrollment(typeof(W1Settings), "settings", number, ConfigurationUpdateMode.HotReload);
        Assert.AreEqual(number, enrollment.SchemaVersion);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("01")]
    [DataRow("+1")]
    [DataRow("1e2")]
    [DataRow("9223372036854775808")]
    public void OrderedIdentitiesRejectAlternateOrOverflowingRepresentations(string number)
    {
        Assert.ThrowsExactly<ArgumentException>(() => Desired(schema: number));
        Assert.ThrowsExactly<ArgumentException>(() => Desired(generation: number));
    }

    [TestMethod]
    public void RequestRequiresIndependentExactTargetAndFixedDefinitionBeforeAProviderCanRead()
    {
        ConfigurationEnrollment enrollment = Enrollment();
        ConfigurationSnapshotTarget expected = Target();
        var common = new ConfigurationRecordKey("CommonConfiguration", enrollment.PersistedDefinition);
        var application = new ConfigurationRecordKey("Host", enrollment.PersistedDefinition);
        var request = new ConfigurationSnapshotRequest(common, application, "pair-etag", enrollment, expected);
        Assert.IsTrue(request.MatchesTarget(Desired()));
        Assert.IsFalse(request.MatchesTarget(Desired(target: Target(cohort: "canary"))));
        Assert.IsFalse(request.MatchesTarget(Desired(target: Target(registration: Release))));
        Assert.IsFalse(request.MatchesTarget(Desired(target: Target(environment: "production"))));
        Assert.IsFalse(request.MatchesTarget(Desired(schema: "2")));
        Assert.IsFalse(request.MatchesTarget(Desired(configuration: "Settings")));
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationSnapshotRequest(common, application, enrollment: enrollment));
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationSnapshotRequest(common, application, expectedTarget: expected));
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationSnapshotRequest(common, new("OtherHost", enrollment.PersistedDefinition), enrollment: enrollment, expectedTarget: expected));
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationSnapshotRequest(common, new("Host", "OtherDefinition")));
    }

    [TestMethod]
    public void CompletePairsOwnDetachedPayloadsAndExplicitAbsenceIndependentlyOfProviderDocuments()
    {
        ConfigurationSnapshotReadResult snapshot;
        ConfigurationDesiredIdentity desired = Desired();
        var reference = new ConfigurationPublicationReference(Source, Publication, Digest);
        var provenance = new ConfigurationSnapshotProvenance(desired, reference);
        using (JsonDocument document = JsonDocument.Parse("{\"First\":23}"))
        {
            snapshot = ConfigurationSnapshotReadResult.Complete(ConfigurationReadResult.Found(document.RootElement),
                ConfigurationReadResult.Missing(), "etag", provenance);
        }
        Assert.AreEqual(23, snapshot.Common!.Payload!.Value.GetProperty("First").GetInt32());
        Assert.AreEqual(ConfigurationReadKind.Missing, snapshot.Application!.Kind);
        Assert.AreSame(provenance, snapshot.Provenance);
        Assert.AreSame(desired, snapshot.Desired);

        ConfigurationSnapshotReadResult defaults = ConfigurationSnapshotReadResult.Complete(ConfigurationReadResult.Missing(),
            ConfigurationReadResult.Missing(), "defaults-etag", new ConfigurationSnapshotProvenance(desired));
        Assert.AreEqual(ConfigurationSnapshotReadKind.Complete, defaults.Kind);
        Assert.IsNull(defaults.Provenance!.CommonSource);
        Assert.IsNull(defaults.Provenance.ApplicationSource);
    }

    [TestMethod]
    public void CompletePairCannotMixMemberUnchangedFailureOrFalseSourceAbsence()
    {
        ConfigurationReadResult missing = ConfigurationReadResult.Missing();
        Assert.ThrowsExactly<ArgumentException>(() => ConfigurationSnapshotReadResult.Complete(ConfigurationReadResult.Unchanged(), missing, "etag"));
        Assert.ThrowsExactly<ArgumentException>(() => ConfigurationSnapshotReadResult.Complete(missing, ConfigurationReadResult.Failure(), "etag"));
        var provenance = new ConfigurationSnapshotProvenance(Desired(), new ConfigurationPublicationReference(Source, Publication, Digest));
        Assert.ThrowsExactly<ArgumentException>(() => ConfigurationSnapshotReadResult.Complete(missing, missing, "etag", provenance));
        using JsonDocument document = JsonDocument.Parse("{}");
        ConfigurationReadResult present = ConfigurationReadResult.Found(document.RootElement);
        Assert.ThrowsExactly<ArgumentException>(() => ConfigurationSnapshotReadResult.Complete(present, missing, "etag", new ConfigurationSnapshotProvenance(Desired())));
    }

    [TestMethod]
    public void IdentifiedRejectionRetainsOnlyDesiredFactsAndUnidentifiedFailureNeverClaimsAbsence()
    {
        ConfigurationDesiredIdentity desired = Desired();
        ConfigurationSnapshotReadResult rejected = ConfigurationSnapshotReadResult.Failure("Configuration.SourceDigest", desired);
        Assert.AreSame(desired, rejected.Desired);
        Assert.IsNull(rejected.Provenance);
        Assert.IsNull(rejected.Common);
        Assert.IsNull(rejected.Application);
        Assert.IsNull(rejected.SnapshotToken);
        ConfigurationSnapshotReadResult failed = ConfigurationSnapshotReadResult.Failure();
        Assert.IsNull(failed.Desired);
        Assert.AreEqual(ConfigurationSnapshotReadKind.Failure, failed.Kind);
        ConfigurationSnapshotReadResult unchanged = ConfigurationSnapshotReadResult.Unchanged();
        Assert.AreEqual(ConfigurationSnapshotReadKind.Unchanged, unchanged.Kind);
        Assert.IsNull(unchanged.Common);
        Assert.IsNull(unchanged.Provenance);
        Assert.ThrowsExactly<ArgumentException>(() => ConfigurationSnapshotReadResult.Failure("secret payload or exception"));
    }

    [TestMethod]
    public void AcceptanceKeepsSubscriberDiagnosticsSeparateAndFaultedProvenanceHistorical()
    {
        ConfigurationDesiredIdentity desired = Desired();
        var accepted = new ConfigurationSnapshotProvenance(desired);
        var unchanged = new ConfigurationAcceptanceStatus(ConfigurationAcceptanceOutcome.AppliedUnchanged, desired,
            accepted, 0, true, notificationFailureCode: "Configuration.Subscriber");
        Assert.AreEqual(0L, unchanged.Revision);
        Assert.IsTrue(unchanged.HasIntactAcceptedState);
        Assert.IsNull(unchanged.FailureCode);
        Assert.AreEqual("Configuration.Subscriber", unchanged.NotificationFailureCode);

        var rejected = new ConfigurationAcceptanceStatus(ConfigurationAcceptanceOutcome.Rejected, Desired(generation: "2"),
            accepted, 0, true, "Configuration.Validation");
        Assert.AreSame(accepted, rejected.LastAccepted);
        Assert.AreNotEqual(rejected.Desired, rejected.LastAccepted!.Desired);
        var faulted = new ConfigurationAcceptanceStatus(ConfigurationAcceptanceOutcome.Faulted, desired, accepted, 0, false,
            "Configuration.LiveSetter");
        Assert.AreSame(accepted, faulted.LastAccepted);
        Assert.IsFalse(faulted.HasIntactAcceptedState);
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationAcceptanceStatus(ConfigurationAcceptanceOutcome.Faulted, desired, accepted, 0, true));
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationAcceptanceStatus(ConfigurationAcceptanceOutcome.Applied, Desired(generation: "2"), accepted, 0, true));
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationAcceptanceStatus(ConfigurationAcceptanceOutcome.Rejected, null, accepted, 0, true));
    }

    [TestMethod]
    public void SessionTextUsesCanonicalIdsAndBoundedUnicodeWithoutPathInterpretation()
    {
        string unicode = string.Concat(Enumerable.Repeat("\U0001f30d", 255));
        Assert.AreEqual(unicode, Target(environment: unicode).Environment);
        Assert.ThrowsExactly<ArgumentException>(() => Target(environment: unicode + "a"));
        Assert.AreEqual(unicode, new ConfigurationSnapshotTarget(Authority, Application, "test", Registration, unicode).HostApplicationName);
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationSnapshotTarget(Authority, Application, "test", Registration, unicode + "a"));
        Assert.ThrowsExactly<ArgumentException>(() => Target(environment: "invalid\ud800"));
        Assert.ThrowsExactly<ArgumentException>(() => Target(environment: "line\nbreak"));
        Assert.ThrowsExactly<ArgumentException>(() => Target(cohort: ".."));
        Assert.ThrowsExactly<ArgumentException>(() => Target(registration: "ABCDEFAB-1111-1111-1111-111111111111"));
        Assert.ThrowsExactly<ArgumentException>(() => Target(registration: Guid.Empty.ToString("D")));
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationPublicationReference(Guid.Empty.ToString("D"), Publication, Digest));
        Assert.ThrowsExactly<ArgumentException>(() => new ConfigurationPublicationReference(Source, Publication, Digest.ToUpperInvariant()));
    }

    private static ConfigurationEnrollment Enrollment()
        => new(typeof(W1Settings), "settings", "1", ConfigurationUpdateMode.HotReload);

    private static ConfigurationSnapshotTarget Target(string environment = "test", string registration = Registration,
        string? cohort = null)
        => new(Authority, Application, environment, registration, "Host", cohort);

    private static ConfigurationDesiredIdentity Desired(string schema = "1", string generation = "1",
        string configuration = "settings", ConfigurationSnapshotTarget? target = null)
        => new(target ?? Target(), configuration, schema, Release, generation);

    private sealed class NeverConstructedSettings : ConfigurationObject
    {
        public NeverConstructedSettings()
        {
            throw new AssertFailedException("Enrollment must not construct roots or execute defaults.");
        }
    }

    private sealed class LegacyStore : IConfigurationStore
    {
        public ConfigurationNotificationMode NotificationMode => ConfigurationNotificationMode.None;

        public ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? sourceToken,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ConfigurationReadResult.Missing());
        }
    }
}
