using CIS.CommonTesting;
using CIS.Configuration;
using CIS.Management;
using CIS.RestClient;
using CIS.Tests.UnitTests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Configuration.ConfigurationCentralTestSupport;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementConfigurationReportingTests
{
    [TestMethod]
    [DataRow(ConfigurationAcceptanceOutcome.Applied, "Applied", true)]
    [DataRow(ConfigurationAcceptanceOutcome.AppliedUnchanged, "AppliedUnchanged", true)]
    [DataRow(ConfigurationAcceptanceOutcome.Rejected, "Rejected", true)]
    [DataRow(ConfigurationAcceptanceOutcome.PendingRestart, "PendingRestart", true)]
    [DataRow(ConfigurationAcceptanceOutcome.Faulted, "Faulted", false)]
    public void ActualAcceptanceMapsRevisionZeroHistoryAndSeparateWarningWithoutContent(
        ConfigurationAcceptanceOutcome outcome, string expected, bool intact)
    {
        ConfigurationDesiredIdentity accepted = Desired();
        ConfigurationDesiredIdentity desired = outcome is ConfigurationAcceptanceOutcome.Rejected or ConfigurationAcceptanceOutcome.PendingRestart
            ? Desired(2) : accepted;
        var acceptance = new ConfigurationAcceptanceStatus(outcome, desired, new(accepted, null, null), 0, intact,
            outcome is ConfigurationAcceptanceOutcome.Rejected or ConfigurationAcceptanceOutcome.Faulted ? "Configuration.SourceIntegrity" : null,
            "Configuration.Subscriber");
        ManagementConfigurationStatusEntry entry = Map(acceptance);
        Assert.AreEqual(expected, entry.State);
        Assert.AreEqual(intact, entry.LiveStateValid);
        Assert.AreEqual("0", entry.LastSuccessful!.LocalRevision);
        Assert.AreEqual(accepted.ReleaseId, entry.LastSuccessful.ReleaseId);
        Assert.AreEqual(intact, entry.Applied is not null);
        Assert.AreEqual(desired.ReleaseId, entry.Desired!.ReleaseId);
        Assert.AreEqual("Configuration.Subscriber", entry.NotificationFailureCode);
        byte[] body = CIS.Management.Wire.ManagementConfigurationWire.EncodeRequest(new ManagementConfigurationStatusReport("1",
            "2026-11-24T00:00:00.0000000Z", [entry]));
        string json = System.Text.Encoding.UTF8.GetString(body);
        Assert.IsFalse(json.Contains("checksum", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("payload", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void NewRegistrationDoesNotRelabelOldSessionAndDisabledFaultOverridesLaggingAcceptance()
    {
        ConfigurationDesiredIdentity accepted = Desired();
        var acceptance = new ConfigurationAcceptanceStatus(ConfigurationAcceptanceOutcome.Applied, accepted,
            new(accepted, null, null), 3, true);
        ManagementConfigurationStatusEntry pending = Map(acceptance, registration: NewRegistration);
        Assert.AreEqual("Pending", pending.State);
        Assert.IsNull(pending.Desired);
        Assert.AreEqual(accepted.ReleaseId, pending.Applied!.ReleaseId);
        ManagementConfigurationStatusEntry fault = Map(acceptance, disabled: true);
        Assert.AreEqual("Faulted", fault.State);
        Assert.IsFalse(fault.LiveStateValid);
        Assert.IsNull(fault.Applied);
        Assert.AreEqual("3", fault.LastSuccessful!.LocalRevision);
    }

    [TestMethod]
    public async Task OneOwnerRefreshesIndependentSequenceAndRetainsTheExactUncertainCapture()
    {
        await using var fixture = await ReportingFixture.Create();
        await fixture.Start();
        Assert.HasCount(1, fixture.Heartbeat.Registrations);
        Assert.AreEqual("1", fixture.Configuration.Reports[0].Sequence);
        Assert.AreEqual("Applied", fixture.Configuration.Reports[0].Entries[0].State);
        Assert.AreEqual("0", fixture.Configuration.Reports[0].Entries[0].Applied!.LocalRevision);
        int reads = fixture.Store.SnapshotReads;
        await fixture.Tick();
        Assert.AreEqual("2", fixture.Configuration.Reports[1].Sequence);
        Assert.AreNotEqual(fixture.Configuration.Reports[0].ObservedAtUtc, fixture.Configuration.Reports[1].ObservedAtUtc);
        Assert.AreEqual(reads, fixture.Store.SnapshotReads, "Reporting captures retained facts without polling Configuration.");
        fixture.Configuration.Fail = true;
        await fixture.Tick();
        ManagementConfigurationStatusReport uncertain = fixture.Configuration.Reports[2];
        await fixture.Tick();
        Assert.AreSame(uncertain, fixture.Configuration.Reports[3]);
        Assert.AreEqual("3", uncertain.Sequence);
        fixture.Store.Current = Snapshot(2, common: "{\"Value\":8}", target: fixture.Target);
        fixture.Values.RequireCentralReconciliation();
        Assert.IsTrue(await fixture.Values.ReconcileCentralAsync());
        await fixture.Tick();
        Assert.AreEqual("4", fixture.Configuration.Reports[4].Sequence);
        Assert.AreEqual("2", fixture.Configuration.Reports[4].Entries[0].Applied!.Generation);
        fixture.Store.Current = Snapshot(3, target: fixture.Target);
        fixture.Values.RequireCentralReconciliation();
        Assert.IsTrue(await fixture.Values.ReconcileCentralAsync());
        fixture.Configuration.Fail = false;
        await fixture.Tick();
        Assert.AreEqual("5", fixture.Configuration.Reports[5].Sequence);
        Assert.IsTrue(fixture.Registration.CaptureStatus().IsConnected);
        Assert.HasCount(1, fixture.Heartbeat.Registrations);
    }

    [TestMethod]
    public async Task StopSignalsAndJoinsTheSameActualHeldConfigurationUpload()
    {
        await using var fixture = await ReportingFixture.Create();
        await fixture.Start();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Configuration.Hold = async token =>
        {
            entered.TrySetResult();
            await released.Task;
            token.ThrowIfCancellationRequested();
        };
        Task tick = fixture.Tick();
        try
        {
            await entered.Task.WaitAsync(Deadline);
            Task stop = fixture.Registration.StopAsync();
            Assert.IsFalse(stop.IsCompleted, "Cancellation cannot abandon the actual held reporting call.");
            Assert.IsFalse(tick.IsCompleted);
        }
        finally
        {
            released.TrySetResult();
        }
        await tick.WaitAsync(Deadline);
        await fixture.Registration.StopAsync().WaitAsync(Deadline);
    }

    private static ManagementConfigurationStatusEntry Map(ConfigurationAcceptanceStatus acceptance, bool disabled = false,
        string registration = Registration)
        => ManagementRegistration.CaptureConfigurationEntries(new(false, false, ConfigurationWatchState.Inactive, null,
            [new ConfigurationEntryRefreshStatus(typeof(RefreshSettings), !disabled, disabled, acceptance.Revision, null, null, null, null)
                { Acceptance = acceptance }]), [new(typeof(RefreshSettings), "settings", "1", ConfigurationUpdateMode.HotReload)],
            Target(), registration).Single();

    private sealed class ReportingFixture : IAsyncDisposable
    {
        internal ManualTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-24T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        internal ManualHeartbeat Heartbeat { get; } = new();
        internal ScriptedManagementClient Script { get; } = new(64);
        internal ReportingClient Configuration { get; } = new();
        internal CentralStore Store { get; } = new();
        internal ManagementRegistration Registration { get; private set; } = null!;
        internal ConfigurationRuntime Values { get; private set; } = null!;
        internal ConfigurationSnapshotTarget Target { get; private set; } = null!;
        private int observation = 1;

        internal static async Task<ReportingFixture> Create()
        {
            var fixture = new ReportingFixture();
            fixture.Registration = new(fixture.Script, fixture.Heartbeat, new ObservationSource(), new InventorySource(),
                new("management")
                {
                    ProcessStartedAtUtc = fixture.Clock.GetUtcNow(),
                    ApplicationVersion = "1"
                }, fixture.Clock);
            fixture.Target = ConfigurationCentralTestSupport.Target(fixture.Registration.CaptureStatus().RegistrationId);
            fixture.Store.Current = Snapshot(target: fixture.Target);
            ConfigurationOptions options = ConfigurationCentralTestSupport.Options(fixture.Store, target: fixture.Target);
            fixture.Values = await ConfigurationRuntime.PrepareAsync(new ConfigurationObjectTestSupport.SelectedTypes(typeof(RefreshSettings)), options);
            fixture.Registration.AttachConfigurationStatus(fixture.Configuration, fixture.Values, options.Enrollments);
            return fixture;
        }

        internal async Task Start()
        {
            string id = Registration.CaptureStatus().RegistrationId;
            Script.EnqueueResult(ScriptedManagementCallKind.Registration, Value(new ManagementRegistrationReceipt(id,
                ConfigurationCentralTestSupport.Application, "2026-11-24T00:00:00.0000000Z")));
            QueueControl();
            Script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(id, "observation", "1"));
            Script.EnqueueResult(ScriptedManagementCallKind.InventoryReport, Receipt(id, "inventory", "1"));
            await Registration.StartAsync();
        }

        internal async Task Tick()
        {
            Clock.Advance(TimeSpan.FromSeconds(31));
            QueueControl();
            Script.EnqueueResult(ScriptedManagementCallKind.Observation, Receipt(Registration.CaptureStatus().RegistrationId,
                "observation", (++observation).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            await Heartbeat.TriggerAsync(Registration);
        }

        private void QueueControl() => Script.EnqueueControl(call => Value(new ManagementControlConfirmation(Authority,
            call.RegistrationId!, "opaque-current", true, 15000, call.AttemptId!)));

        public async ValueTask DisposeAsync()
        {
            await Registration.StopAsync().WaitAsync(Deadline);
            await Values.DisposeAsync();
            await Heartbeat.DisposeAsync();
        }
    }

    private static ManagementCallResult<T> Value<T>(T value) => new(RestResult<T>.FromValue(value, new(200, hasValue: true)));
    private static ManagementCallResult<ManagementReportReceipt> Receipt(string id, string stream, string sequence)
        => Value(new ManagementReportReceipt(id, stream, sequence, sequence, "accepted", "2026-11-24T00:00:00.0000000Z"));

    private sealed class ObservationSource : IManagementObservationSource
    {
        public ManagementObservationContent CaptureObservation(CancellationToken cancellationToken = default) => new("ready", "healthy");
    }

    private sealed class InventorySource : IManagementInventorySource
    {
        public ManagementInventoryContent CaptureInventory(CancellationToken cancellationToken = default) => new([], [], []);
    }

    private sealed class ReportingClient : IManagementConfigurationClient
    {
        internal List<ManagementConfigurationStatusReport> Reports { get; } = [];
        internal bool Fail
        {
            get; set;
        }
        internal Func<CancellationToken, Task>? Hold
        {
            get; set;
        }

        public async Task<ManagementConfigurationCallResult<ManagementReportReceipt>> ReportStatusAsync(string registrationId,
            ManagementConfigurationStatusReport report, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        {
            Reports.Add(report);
            if (Hold is { } hold)
            {
                await hold(cancellationToken);
            }
            return Fail ? new(RestResult<ManagementReportReceipt>.Failed("client.timeout", "Expected lost response",
                new(failureKind: RestFailureKind.Timeout)))
                : new(RestResult<ManagementReportReceipt>.FromValue(new(registrationId, "configuration-status", report.Sequence,
                    report.Sequence, "accepted", "2026-11-24T00:00:00.0000000Z"), new(200, hasValue: true)));
        }

        public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationDefinition>>> GetDefinitionsAsync(ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationValidationResult>> ValidateAsync(ManagementConfigurationValidationCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationPreview>> PreviewAsync(ManagementConfigurationPreviewCommand command, ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationSource>> GetSourceAsync(string sourceId, ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> PublishSourceAsync(string sourceId, string operationKey, string ifMatch, ManagementConfigurationPublicationCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationPublication>> GetPublicationAsync(string publicationId, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> CreateReleaseAsync(string applicationId, string configurationId, string schemaVersion, string operationKey, string ifMatch, ManagementConfigurationReleaseCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationRelease>> GetReleaseAsync(string releaseId, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationAssignmentMap>> GetAssignmentsAsync(string applicationId, string configurationId, string schemaVersion, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> ReplaceAssignmentsAsync(string applicationId, string configurationId, string schemaVersion, string operationKey, string ifMatch, ManagementConfigurationAssignmentCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationSnapshot>> GetRegistrationConfigurationAsync(ManagementConfigurationSnapshotTarget expectedTarget, ManagementConfigurationSnapshot? retainedSnapshot = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationAdoptionEntry>>> GetStatusAsync(string applicationId, ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationCatalog>> GetCatalogAsync(ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> ApplyCatalogAsync(string operationKey, string ifMatch, ManagementConfigurationCatalogCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetCatalogOperationAsync(string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetPublicationOperationAsync(string sourceId, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetReleaseOperationAsync(string applicationId, string configurationId, string schemaVersion, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetAssignmentOperationAsync(string applicationId, string configurationId, string schemaVersion, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
