using System.Text.Json;
using CIS.Jobs;
using CIS.Jobs.Registration;
using CIS.Messaging;

namespace CIS.Examples.CountryData.Coordination;

/// <summary>Explicit opt-in registrations for two different maintenance responsibilities.</summary>
public static class CountryMaintenanceJobs
{
    public const string Database = "Maintenance";
    public const string WalletScope = "wallet-service";
    public const string WalletJob = "wallet.compaction";
    public const string CustomerScope = "customer-service";
    public const string CustomerJob = "customer.dormancy";

    public static JobDefinition WalletDefinition { get; } = new(WalletScope, WalletJob, "Wallet compaction", "maintenance",
        [JobExecutionMode.Continuous], ["wallet.compact"], resourceScopeDescription: "Stable wallet partition");

    public static JobDefinition CustomerDefinition { get; } = new(CustomerScope, CustomerJob, "Customer dormancy", "lifecycle",
        [JobExecutionMode.Scheduled], ["customer.mark.dormant"], resourceScopeDescription: "Stable customer partition");

    /// <summary>Captures metadata and scoped handler registrations; it performs no storage or credential work.</summary>
    /// <remarks>Applications separately supply transactions, guard, work ports, durable-operation definition,
    /// trusted scopes and coordination hosting. The normal CountryData startup does not select this recipe.</remarks>
    public static IServiceCollection AddCountryMaintenanceJobs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCisJobCategory(new(WalletScope, "maintenance", "Maintenance"));
        services.AddCisJobCategory(new(CustomerScope, "lifecycle", "Lifecycle"));
        services.AddCisContinuousJob<WalletCompactionJob>(WalletDefinition);
        services.AddCisScheduledJob<CustomerDormancyInput, CustomerDormancyJob>(CustomerDefinition, new CustomerDormancyCodec());
        services.AddCisJobCatalog();
        return services;
    }

    /// <summary>Creates a whole settings snapshot. Host and both jobs remain disabled unless explicitly enabled.</summary>
    public static JobConfigurationSnapshot Settings(long revision = 0, bool enabled = false,
        bool walletEnabled = false, bool customerEnabled = false)
        => new(revision, enabled,
            [new(WalletScope, "maintenance", maximumInvocations: 1), new(CustomerScope, "lifecycle", maximumInvocations: 1)],
            [new(WalletScope, WalletJob, new(JobExecutionMode.Continuous), walletEnabled),
                new(CustomerScope, CustomerJob, new(JobExecutionMode.Scheduled,
                    schedule: JobScheduleDescriptor.Daily(new(2, 0), "Europe/Malta", JobMissedRunPolicy.Skip),
                    scheduledActivation: JobScheduledActivation.ExternalOccurrences), customerEnabled)]);
}

/// <summary>The scheduler retains this meaningful input and the original operation identity across redelivery.</summary>
public sealed record CustomerDormancyInput(string PartitionId, DateOnly InactiveBefore);

/// <summary>The original durable result; the application registers a stable Operations result codec for this type.</summary>
public sealed record CustomerDormancyResult(int ChangedCustomers);

/// <summary>One bounded existing Messaging payload contract, independent of the Operations fingerprint/result codecs.</summary>
public sealed class CustomerDormancyCodec : IMessageCodec<CustomerDormancyInput>
{
    public MessageContract Contract { get; } = new("country.customer-dormancy", 1);

    public byte[] Encode(CustomerDormancyInput value)
    {
        Validate(value);
        return JsonSerializer.SerializeToUtf8Bytes(value);
    }

    public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out CustomerDormancyInput? value)
    {
        value = null;
        if (contract != Contract)
        {
            return false;
        }

        try
        {
            CustomerDormancyInput? decoded = JsonSerializer.Deserialize<CustomerDormancyInput>(json.Span);
            Validate(decoded);
            value = decoded;
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static void Validate(CustomerDormancyInput? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.PartitionId) || value.PartitionId.Length > 128
            || value.PartitionId.Any(char.IsControl) || value.InactiveBefore == default)
        {
            throw new ArgumentException("Supply a bounded stable partition and explicit dormancy cutoff.");
        }
    }
}
