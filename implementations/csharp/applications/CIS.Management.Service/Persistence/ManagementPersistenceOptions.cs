using System.Text.RegularExpressions;
using CIS.Management.Wire;

namespace CIS.Management.Service.Persistence;

/// <summary>Finite application-owned startup limits applied against shared database occupancy.</summary>
public sealed class ManagementPersistenceOptions
{
    /// <summary>Captures schema, capacity, freshness and telemetry retention without opening a database.</summary>
    public ManagementPersistenceOptions(string schema = "cis_management", int maximumApplications = 1024,
        int maximumRegistrationsPerApplication = 128, int maximumRetainedRegistrations = 8192,
        long maximumInventoryBytes = 256L * 1024 * 1024, TimeSpan? staleAfter = null,
        TimeSpan? registrationRetention = null, int cleanupBatchSize = 100, ManagementWireLimits? wireLimits = null)
    {
        if (!Regex.IsMatch(schema, "^[A-Za-z_][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("Use one bounded PostgreSQL schema identifier.", nameof(schema));
        }

        Schema = schema;
        MaximumApplications = Limit(maximumApplications, 65536, nameof(maximumApplications));
        MaximumRegistrationsPerApplication = Limit(maximumRegistrationsPerApplication, 65536, nameof(maximumRegistrationsPerApplication));
        MaximumRetainedRegistrations = Limit(maximumRetainedRegistrations, 1048576, nameof(maximumRetainedRegistrations));
        if (maximumInventoryBytes is < 1 or > 8L * 1024 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumInventoryBytes));
        }

        MaximumInventoryBytes = maximumInventoryBytes;
        StaleAfter = Duration(staleAfter ?? TimeSpan.FromSeconds(90), TimeSpan.FromDays(1), nameof(staleAfter));
        RegistrationRetention = Duration(registrationRetention ?? TimeSpan.FromDays(7), TimeSpan.FromDays(365), nameof(registrationRetention));
        CleanupBatchSize = Limit(cleanupBatchSize, 1000, nameof(cleanupBatchSize));
        WireLimits = wireLimits ?? new();
    }

    public string Schema
    {
        get;
    }
    public int MaximumApplications
    {
        get;
    }
    public int MaximumRegistrationsPerApplication
    {
        get;
    }
    public int MaximumRetainedRegistrations
    {
        get;
    }
    public long MaximumInventoryBytes
    {
        get;
    }
    public TimeSpan StaleAfter
    {
        get;
    }
    public TimeSpan RegistrationRetention
    {
        get;
    }
    public int CleanupBatchSize
    {
        get;
    }
    public ManagementWireLimits WireLimits
    {
        get;
    }

    private static int Limit(int value, int maximum, string name)
        => value >= 1 && value <= maximum ? value : throw new ArgumentOutOfRangeException(name);

    private static TimeSpan Duration(TimeSpan value, TimeSpan maximum, string name)
        => value > TimeSpan.Zero && value <= maximum ? value : throw new ArgumentOutOfRangeException(name);
}
