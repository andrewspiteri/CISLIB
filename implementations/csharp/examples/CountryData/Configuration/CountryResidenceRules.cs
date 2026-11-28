using System.Text.Json;
using CIS.Audit;
using CIS.Configuration;
using CIS.Heartbeat;
using CIS.Hosting;
using CIS.Messaging;
using CIS.RuleEngine;

namespace CIS.Examples.CountryData;

/// <summary>Opt-in ResidenceCountry and Pensioner rules for the CountryData facade.</summary>
public sealed class CountryResidenceRules : RuleEngineConfiguration
{
    public const string ConfigurationId = "country.residence.rules";
    public const string RulesetId = "country.residence";
    public const string SchemaVersion = "1";
    public const string SchemaRevision = "1";
    public const string ResidenceCountry = "ResidenceCountry";
    public const string Pensioner = "Pensioner";
    public const string AuditRoute = "rule-engine-audit";
    private static readonly RuleColumn[] Columns =
    [
        new(ResidenceCountry, "Residence country", RuleValueKind.String,
            [RulePresetKind.String, RulePresetKind.StringList, RulePresetKind.Any, RulePresetKind.Missing], exactScalarCount: 3),
        new(Pensioner, "Pensioner", RuleValueKind.Boolean,
            [RulePresetKind.Boolean, RulePresetKind.Any, RulePresetKind.Missing])
    ];
    private static readonly RuleMetadataShape Metadata = new(RuleMetadataKind.String);

    /// <summary>Declares the one rowless schema understood by this build.</summary>
    public CountryResidenceRules() : base(CreateSchema())
    {
    }

    /// <summary>Creates the rowless schema for trusted Management registration.</summary>
    public static RuleDefinition CreateSchema() => new(RulesetId, SchemaRevision, "country-residence-schema",
        RuleExecutionMode.FirstMatch, Columns, [], "Country residence", Metadata);

    /// <summary>Creates the §7 sample draft. Management allocates the immutable version on save.</summary>
    public static RuleDefinition CreateDraft(RuleExecutionMode mode) => CreateDraft(mode,
        [
            CountryAndPensioner("trial", 10, RuleState.Test, "100", "Trial"),
            CountryAndPensioner("local", 20, RuleState.Enabled, "20", "Local"),
            Unconditional("fallback", 30, RuleState.Enabled, "80", "Fallback"),
            Unconditional("inactive", 40, RuleState.Disabled, "200", "Inactive")
        ]);

    /// <summary>Creates a draft with explicit rows under the same registered schema.</summary>
    public static RuleDefinition CreateDraft(RuleExecutionMode mode, IEnumerable<RuleRow> rows)
        => new(RulesetId, SchemaRevision, "country-residence-draft", mode, Columns, rows, "Country residence", Metadata);

    /// <summary>Creates the scoped evaluator request with native string and Boolean inputs.</summary>
    public static RuleEvaluationRequest CreateRequest(string residenceCountry, bool pensioner, string? exactVersion = null,
        string? correlationId = null)
        => new(RulesetId, new(
            [
                new(ResidenceCountry, RuleValue.FromString(residenceCountry)),
                new(Pensioner, RuleValue.FromBoolean(pensioner))
            ]), exactVersion, correlationId);

    /// <summary>Enrolls the rules through native Management, and selects ordinary Audit and Messaging in the given facade.</summary>
    /// <remarks>Supply the existing Management bootstrap and register the host-owned Messaging store and transport separately.
    /// If CountrySettings is centrally enrolled, select it first with the same bootstrap instance.</remarks>
    public static void Configure(CISApplicationBuilder builder, CISApplicationCentralConfigurationOptions central,
        TimeSpan pollingInterval, MessagingOptions? messaging = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(central);
        if (pollingInterval <= TimeSpan.Zero || pollingInterval > HeartbeatRegistrationOptions.MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(pollingInterval));
        }

        var assembly = typeof(CountryApplication).Assembly;
        string host = assembly.GetName().Name!;
        ConfigurationOptions current = builder.Configuration ?? new(host);
        if (current.HostApplicationName != host || current.SnapshotTarget is not null
            || builder.CentralConfiguration is { } selected && !ReferenceEquals(selected, central)
            || current.Enrollments.Any(item => item.ConfigurationType == typeof(CountryResidenceRules) || item.ConfigurationId == ConfigurationId))
        {
            throw new ArgumentException("Select one CountryData Management bootstrap and one exact residence rules enrollment.");
        }

        if (builder.TypeManager.HostAssembly != assembly)
        {
            builder.TypeManager = new(assembly);
        }
        builder.Configuration = new(host, current.Store)
        {
            IsHealthCritical = current.IsHealthCritical ?? true,
            PollingInterval = pollingInterval,
            Factories = current.Factories,
            Validators = current.Validators,
            Enrollments = [.. current.Enrollments, new(typeof(CountryResidenceRules), ConfigurationId, SchemaVersion, ConfigurationUpdateMode.HotReload)]
        };
        builder.CentralConfiguration = central;
        builder.UseAudit(new AuditOptions(AuditRoute));
        builder.UseMessaging(messaging);
        builder.AddCisRuleEngine();
    }

    private static RuleRow CountryAndPensioner(string id, int ordinal, RuleState state, string weight, string label)
        => new(id, ordinal, state,
            [
                new(ResidenceCountry, RulePresetKind.String, RuleOperator.Equals, RuleValue.FromString("MLT")),
                new(Pensioner, RulePresetKind.Boolean, RuleOperator.Equals, RuleValue.FromBoolean(false))
            ], weight, JsonSerializer.SerializeToElement(label));

    private static RuleRow Unconditional(string id, int ordinal, RuleState state, string weight, string label)
        => new(id, ordinal, state, [new(ResidenceCountry, RulePresetKind.Any), new(Pensioner, RulePresetKind.Any)],
            weight, JsonSerializer.SerializeToElement(label));
}
