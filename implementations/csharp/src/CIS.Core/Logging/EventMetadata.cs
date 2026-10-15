using CIS.Configuration;
using CIS.Logging.Events;
using CIS.Text;

namespace CIS.Logging;
/// <summary>Immutable allowlisted application/process facts, excluding settings, paths and operation context.</summary>
public sealed class EventMetadata
{
    /// <summary>Logging captures the automatically populated static facts without retaining module settings.</summary>
    public EventMetadata() : this(EnvironmentOptions.ApplicationName, EnvironmentOptions.BuildVersion, EnvironmentOptions.ServiceVersion, EnvironmentOptions.EnvironmentName, EnvironmentOptions.EnvironmentKind, EnvironmentOptions.InstanceId, EnvironmentOptions.ProcessStartedAtUtc, EnvironmentOptions.InitializedAtUtc, EnvironmentOptions.OperatingSystem, EnvironmentOptions.ProcessId, EnvironmentOptions.CommitId, EnvironmentOptions.BuildId, EnvironmentOptions.HostName, EnvironmentOptions.OperatingSystemDescription, EnvironmentOptions.RuntimeDescription, EnvironmentOptions.OSArchitecture, EnvironmentOptions.ProcessArchitecture)
    {
    }

    // Direct fact capture also supports internal projection qualification without writable runtime facts.
    internal EventMetadata(string applicationName, string buildVersion, string serviceVersion, string environmentName, EnvironmentKind environmentKind, string instanceId, DateTimeOffset? processStartedAtUtc, DateTimeOffset initializedAtUtc, string operatingSystem, int? processId, string? commitId, string? buildId, string? hostName, string? operatingSystemDescription, string? runtimeDescription, string? osArchitecture, string? processArchitecture)
    {
        ApplicationName = applicationName;
        BuildVersion = buildVersion;
        ServiceVersion = serviceVersion;
        EnvironmentName = environmentName;
        EnvironmentKind = environmentKind;
        InstanceId = instanceId;
        ProcessStartedAtUtc = processStartedAtUtc?.ToUniversalTime();
        InitializedAtUtc = initializedAtUtc.ToUniversalTime();
        OperatingSystem = operatingSystem;
        ProcessId = processId;
        CommitId = commitId;
        BuildId = buildId;
        var issues = new List<ConfigurationIssue>();
        HostName = OptionalText(hostName, nameof(HostName), issues);
        OperatingSystemDescription = OptionalText(operatingSystemDescription, nameof(OperatingSystemDescription), issues);
        RuntimeDescription = OptionalText(runtimeDescription, nameof(RuntimeDescription), issues);
        OSArchitecture = ArchitectureText(osArchitecture, nameof(OSArchitecture), issues);
        ProcessArchitecture = ArchitectureText(processArchitecture, nameof(ProcessArchitecture), issues);
        OperatingSystem = OperatingSystem?.Trim().ToLowerInvariant() ?? string.Empty;
        if (OperatingSystem is not ("windows" or "linux" or "macos" or "unknown"))
        {
            issues.Add(new(ConfigurationIssueCodes.InvalidIdentity, "EnvironmentOptions.OperatingSystem", "Use windows, linux, macos or unknown."));
        }

        if (ProcessId is <= 0)
        {
            issues.Add(new(ConfigurationIssueCodes.OutOfRange, "EnvironmentOptions.ProcessId", "Supply a positive process identifier or null."));
        }

        if (InitializedAtUtc == default)
        {
            issues.Add(new(ConfigurationIssueCodes.InvalidIdentity, "EnvironmentOptions.InitializedAtUtc", "Supply the application startup timestamp before using logging metadata."));
        }

        CheckIdentity(ApplicationName, "ApplicationName", 255, issues);
        CheckIdentity(EnvironmentName, "EnvironmentName", 255, issues);
        CheckIdentity(ServiceVersion, "ServiceVersion", 512, issues);
        CheckIdentity(BuildVersion, "BuildVersion", 512, issues);
        if (CommitId is not null)
        {
            CheckIdentity(CommitId, "CommitId", 512, issues);
        }

        if (BuildId is not null)
        {
            CheckIdentity(BuildId, "BuildId", 512, issues);
        }

        if (!Enum.IsDefined(EnvironmentKind))
        {
            issues.Add(new(ConfigurationIssueCodes.InvalidEnvironmentKind, "EnvironmentOptions.EnvironmentKind", "Use a declared environment kind."));
        }

        if (!Guid.TryParseExact(InstanceId, "D", out Guid instance) || instance == Guid.Empty || InstanceId.Length != 36)
        {
            issues.Add(new(ConfigurationIssueCodes.InvalidIdentity, "EnvironmentOptions.InstanceId", "Supply a nonempty UUID in D format."));
        }
        else
        {
            InstanceId = instance.ToString("D");
        }

        if (issues.Count != 0)
        {
            throw new CoreConfigurationException(issues);
        }

        ResourceProjection = EventProjection.CreateResource(this);
    }

    internal LogValue ResourceProjection { get; }
    /// <summary>The application identity, independent of collector destinations.</summary>
    public string ApplicationName { get; }
    /// <summary>The actual application build identity, including prerelease and build text.</summary>
    public string BuildVersion { get; }
    /// <summary>The deployment or telemetry release label, independent of BuildVersion.</summary>
    public string ServiceVersion { get; }
    /// <summary>The application environment label; it does not modify the host environment.</summary>
    public string EnvironmentName { get; }
    /// <summary>The explicitly selected application execution kind.</summary>
    public EnvironmentKind EnvironmentKind { get; }
    /// <summary>The application instance UUID generated by the container.</summary>
    public string InstanceId { get; }
    /// <summary>The actual process start instant in UTC, or null when unavailable.</summary>
    public DateTimeOffset? ProcessStartedAtUtc { get; }
    /// <summary>The container construction instant in UTC.</summary>
    public DateTimeOffset InitializedAtUtc { get; }
    /// <summary>The host name observed by this application, or null when unavailable.</summary>
    public string? HostName { get; }
    /// <summary>The lowercase operating-system family.</summary>
    public string OperatingSystem { get; }
    /// <summary>The observed operating-system description, or null when unavailable.</summary>
    public string? OperatingSystemDescription { get; }
    /// <summary>The lowercase operating-system architecture, or null when unavailable.</summary>
    public string? OSArchitecture { get; }
    /// <summary>The lowercase process architecture, or null when unavailable.</summary>
    public string? ProcessArchitecture { get; }
    /// <summary>The observed runtime description, or null when unavailable.</summary>
    public string? RuntimeDescription { get; }
    /// <summary>The observed positive process identifier, or null when unavailable.</summary>
    public int? ProcessId { get; }
    /// <summary>An optional explicit build commit identifier.</summary>
    public string? CommitId { get; }
    /// <summary>An optional explicit build identifier.</summary>
    public string? BuildId { get; }

    private static string? OptionalText(string? value, string name, List<ConfigurationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (!UnicodeScalarText.IsValid(value, int.MaxValue, allowControls: true))
        {
            issues.Add(new(ConfigurationIssueCodes.InvalidIdentity, "EnvironmentOptions." + name, "Supply valid Unicode text or omit the optional fact."));
            return null;
        }

        return value;
    }

    private static string? ArchitectureText(string? value, string name, List<ConfigurationIssue> issues)
    {
        value = OptionalText(value, name, issues)?.ToLowerInvariant();
        if (value is not null && value.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
        {
            issues.Add(new(ConfigurationIssueCodes.InvalidIdentity, "EnvironmentOptions." + name, "Use lowercase letters, digits and underscores for architecture text."));
        }

        return value;
    }

    private static void CheckIdentity(string? value, string name, int maximum, List<ConfigurationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || !UnicodeScalarText.IsValid(value, maximum, allowControls: false))
        {
            string code = name == "ApplicationName" && string.IsNullOrWhiteSpace(value) ? ConfigurationIssueCodes.MissingApplicationName : ConfigurationIssueCodes.InvalidIdentity;
            issues.Add(new(code, "EnvironmentOptions." + name, "Supply a nonblank bounded identity without controls or malformed Unicode."));
        }
    }
}
