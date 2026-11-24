using System.Globalization;
using System.Security.Cryptography;
using CIS.Configuration;
using CIS.Management.Wire;

namespace CIS.Management;

public sealed partial class ManagementRegistration
{
    private IManagementConfigurationClient? configurationClient;
    private ConfigurationRuntime? configurationRuntime;
    private ConfigurationEnrollment[] configurationEnrollments = [];
    private ManagementConfigurationStatusReport? pendingConfigurationStatus;
    private byte[]? pendingConfigurationDigest;
    private long configurationSequence;

    // The final host attaches borrowed module owners once, before the existing sequential Start.
    internal void AttachConfigurationStatus(IManagementConfigurationClient selectedClient, ConfigurationRuntime runtime,
        IReadOnlyList<ConfigurationEnrollment> enrollments)
    {
        ArgumentNullException.ThrowIfNull(selectedClient);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(enrollments);
        ConfigurationEnrollment[] captured = enrollments.Take(257).ToArray();
        if (captured.Length > 256 || captured.Any(item => item is null)
            || captured.Select(item => item.ConfigurationType).Distinct().Count() != captured.Length)
        {
            throw new ArgumentException("Attach at most 256 unique captured enrollments.", nameof(enrollments));
        }
        lock (gate)
        {
            if (startup is not null || closing || configurationClient is not null)
            {
                throw new InvalidOperationException("Attach configuration reporting once before Management startup.");
            }
            configurationClient = selectedClient;
            configurationRuntime = runtime;
            configurationEnrollments = captured;
        }
    }

    private void PrepareConfigurationStatus(CancellationToken token)
    {
        if (configurationRuntime is not { } runtime)
        {
            return;
        }
        token.ThrowIfCancellationRequested();
        ConfigurationSnapshotTarget? target = runtime.CaptureCentralReadiness().Target;
        ConfigurationRefreshStatus current = runtime.CaptureStatus();
        ManagementConfigurationStatusEntry[] entries = CaptureConfigurationEntries(current, configurationEnrollments,
            target, registrationId);
        var fingerprint = new ManagementConfigurationStatusReport("1", FingerprintTime, entries);
        byte[] encoded = ManagementConfigurationWire.EncodeRequest(fingerprint);
        byte[] digest = SHA256.HashData(encoded);
        lock (gate)
        {
            // One uncertain capture is retried byte-for-byte. A changed complete capture supersedes it;
            // after confirmation, even unchanged content gets a higher periodic observation sequence.
            if (pendingConfigurationDigest is not null && digest.AsSpan().SequenceEqual(pendingConfigurationDigest))
            {
                return;
            }
            long next = checked(configurationSequence + 1);
            var report = fingerprint with
            {
                Sequence = Decimal(next),
                ObservedAtUtc = Timestamp(clock.GetUtcNow())
            };
            byte[] body = ManagementConfigurationWire.EncodeRequest(report);
            pendingConfigurationStatus = ManagementConfigurationWire.DecodeRequest<ManagementConfigurationStatusReport>(body)
                ?? throw new InvalidOperationException("A validated complete configuration status could not be detached.");
            pendingConfigurationDigest = digest;
            configurationSequence = next;
        }
    }

    internal static ManagementConfigurationStatusEntry[] CaptureConfigurationEntries(ConfigurationRefreshStatus status,
        IReadOnlyList<ConfigurationEnrollment> enrollments, ConfigurationSnapshotTarget? currentTarget, string registration)
    {
        var result = new List<ManagementConfigurationStatusEntry>(enrollments.Count);
        foreach (ConfigurationEnrollment enrollment in enrollments)
        {
            ConfigurationEntryRefreshStatus? entry = status.Entries.SingleOrDefault(item => item.ConfigurationType == enrollment.ConfigurationType);
            ConfigurationAcceptanceStatus? acceptance = entry?.Acceptance;
            ConfigurationDesiredIdentity? desired = acceptance?.Desired;
            bool current = currentTarget?.RegistrationId == registration && desired?.Target == currentTarget;
            ConfigurationDesiredIdentity? accepted = acceptance?.LastAccepted?.Desired;
            ManagementConfigurationAppliedVersion? history = accepted is null ? null
                : new(accepted.ReleaseId, accepted.AssignmentGeneration, (acceptance?.Revision ?? 0).ToString(CultureInfo.InvariantCulture));
            bool live = entry?.Disabled is false && acceptance?.HasIntactAcceptedState is true;
            string state = entry?.Disabled is true || acceptance?.Outcome == ConfigurationAcceptanceOutcome.Faulted
                ? "Faulted" : current ? acceptance!.Outcome.ToString() : "Pending";
            if (!live && state is "Rejected" or "PendingRestart")
            {
                state = "Pending";
            }
            result.Add(new(enrollment.ConfigurationId, enrollment.SchemaVersion, enrollment.UpdateMode.ToString(), state,
                live, current ? new(desired!.ReleaseId, desired.AssignmentGeneration) : null,
                live ? history : null, history, current || state == "Faulted" ? acceptance?.FailureCode : null,
                acceptance?.NotificationFailureCode));
        }
        return result.ToArray();
    }

    private async Task<bool> ReportConfigurationStatusAsync(CancellationToken token)
    {
        if (configurationClient is not { } selected || pendingConfigurationStatus is not { } report)
        {
            return true;
        }
        ManagementConfigurationCallResult<ManagementReportReceipt> result = await selected.ReportStatusAsync(registrationId,
            report, cancellationToken: token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        // Reuse the original owner's refusal/session replacement and exact receipt checks.
        var common = new ManagementCallResult<ManagementReportReceipt>(result.Exchange,
            result.Problem is null ? null : new ManagementProblem(result.Problem.Type, result.Problem.Title,
                result.Problem.Status, result.Problem.Code));
        if (!Accepted(common, "configuration-status", report.Sequence))
        {
            return false;
        }
        lock (gate)
        {
            pendingConfigurationStatus = null;
            pendingConfigurationDigest = null;
        }
        return true;
    }

    private void ResetConfigurationStatus()
    {
        configurationSequence = 0;
        pendingConfigurationStatus = null;
        pendingConfigurationDigest = null;
    }
}
