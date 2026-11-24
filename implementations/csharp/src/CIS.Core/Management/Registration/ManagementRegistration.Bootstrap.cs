using CIS.Management.Wire;

namespace CIS.Management;

// Only the facade's successful direct native register/control sequence can create this handoff.
internal sealed record ManagementBootstrapSession(ManagedWorkAdmission Admission, string RegistrationId,
    ManagementRegistrationRequest Request, ManagementRegistrationReceipt Receipt, string AuthorityId);

public sealed partial class ManagementRegistration
{
    private Action? configurationGap;

    internal void AdoptBootstrap(ManagementBootstrapSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (gate)
        {
            if (startup is not null || registered || !ReferenceEquals(admission, session.Admission)
                || session.RegistrationId != session.Receipt.RegistrationId || !ManagementWire.IsUuid(session.Receipt.ApplicationId)
                || !ManagementWire.IsUuid(session.AuthorityId))
            {
                throw new InvalidOperationException("Adopt the same closed native bootstrap session before registration startup.");
            }
            registrationId = session.RegistrationId;
            request = session.Request;
            applicationId = session.Receipt.ApplicationId;
            registered = true;
        }
    }

    internal void BindConfigurationGap(Action notify)
    {
        ArgumentNullException.ThrowIfNull(notify);
        lock (gate)
        {
            if (configurationGap is not null)
            {
                throw new InvalidOperationException("Bind the selected central Configuration gap observer once.");
            }
            configurationGap = notify;
        }
    }

    internal static ManagementRegistrationRequest BootstrapRequest(ManagementRegistrationOptions options)
        => new(1, options.ReportedInstanceId ?? EnvironmentOptions.InstanceId,
            Timestamp(options.ProcessStartedAtUtc ?? EnvironmentOptions.ProcessStartedAtUtc
                ?? throw new ArgumentException("Select an actual process start when the native fact is unavailable.")),
            options.ApplicationVersion ?? EnvironmentOptions.ServiceVersion);
}
