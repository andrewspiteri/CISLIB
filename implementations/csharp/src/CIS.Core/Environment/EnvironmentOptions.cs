using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CIS.Configuration;
using CIS.Logging;
using CIS.Text;

namespace CIS;

/// <summary>Shared application and runtime facts populated automatically on first access.</summary>
/// <remarks>A static constructor applies discovered defaults followed by known environment overrides. It starts no module runtime.</remarks>
public static class EnvironmentOptions
{
    static EnvironmentOptions()
    {
        Assembly? application = Assembly.GetEntryAssembly();
        BuildVersion = ReadBuildVersion(application);
        ApplicationName = application?.GetName().Name ?? "unknown";
        EnvironmentName = "unknown";
        ServiceVersion = BuildVersion;
        HostName = ReadHostName();
        OperatingSystem = ReadOperatingSystemFamily();
        OperatingSystemDescription = RuntimeInformation.OSDescription;
        OSArchitecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        RuntimeDescription = RuntimeInformation.FrameworkDescription;
        ProcessId = System.Environment.ProcessId;
        ProcessStartedAtUtc = ReadProcessStartedAtUtc();
        InstanceId = Guid.NewGuid().ToString("D");
        InitializedAtUtc = DateTimeOffset.UtcNow;
        Logging = new LoggingOptions();
        var issues = new List<ConfigurationIssue>();
        (ApplicationName, EnvironmentName, ServiceVersion) = EnvironmentOverrides.Apply(
            System.Environment.GetEnvironmentVariable, ApplicationName, EnvironmentName, ServiceVersion, Logging, issues);
        if (issues.Count != 0) { throw new CoreConfigurationException(issues); }
        CheckIdentity(ApplicationName, nameof(ApplicationName), 255, issues);
        CheckIdentity(EnvironmentName, nameof(EnvironmentName), 255, issues);
        CheckIdentity(BuildVersion, nameof(BuildVersion), 512, issues);
        CheckIdentity(ServiceVersion, nameof(ServiceVersion), 512, issues);
        if (issues.Count != 0) { throw new CoreConfigurationException(issues); }
        // Intern only validated, bounded identities after all overrides. Never intern UUIDs, paths or payloads.
        ApplicationName = string.Intern(ApplicationName);
        EnvironmentName = string.Intern(EnvironmentName);
        BuildVersion = string.Intern(BuildVersion);
        ServiceVersion = string.Intern(ServiceVersion);
    }

    /// <summary>The selected application name; interned after overrides.</summary>
    public static string ApplicationName { get; }
    /// <summary>The actual application build version; independent of service-version overrides and interned.</summary>
    public static string BuildVersion { get; }
    /// <summary>The selected deployment version; interned after overrides.</summary>
    public static string ServiceVersion { get; }
    /// <summary>The selected deployment environment label; interned after overrides.</summary>
    public static string EnvironmentName { get; }
    /// <summary>The application-selected execution kind; defaults to Unknown.</summary>
    public static EnvironmentKind EnvironmentKind { get; set; }
    /// <summary>The process-wide application instance UUID, without interning.</summary>
    public static string InstanceId { get; }
    /// <summary>The process start instant in UTC, or null when host inspection is unavailable.</summary>
    public static DateTimeOffset? ProcessStartedAtUtc { get; }
    /// <summary>The UTC instant at which the static container was populated.</summary>
    public static DateTimeOffset InitializedAtUtc { get; }
    /// <summary>The current host name.</summary>
    public static string? HostName { get; }
    /// <summary>The lowercase operating-system family.</summary>
    public static string OperatingSystem { get; }
    /// <summary>The observed operating-system description.</summary>
    public static string? OperatingSystemDescription { get; }
    /// <summary>The lowercase operating-system architecture.</summary>
    public static string? OSArchitecture { get; }
    /// <summary>The lowercase process architecture.</summary>
    public static string? ProcessArchitecture { get; }
    /// <summary>The observed runtime description.</summary>
    public static string? RuntimeDescription { get; }
    /// <summary>The current positive process identifier.</summary>
    public static int? ProcessId { get; }
    /// <summary>The optional caller-supplied build commit identifier.</summary>
    public static string? CommitId { get; set; }
    /// <summary>The optional caller-supplied build identifier.</summary>
    public static string? BuildId { get; set; }
    /// <summary>Mutable module settings; logging owns their preparation and runtime.</summary>
    public static LoggingOptions Logging { get; }

    internal static string ReadBuildVersion(Assembly? application)
    {
        string? information = application?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(information) ? application?.GetName().Version?.ToString() ?? "unknown" : information.Trim();
    }

    private static void CheckIdentity(string value, string name, int maximum, List<ConfigurationIssue> issues)
    {
        if (!UnicodeScalarText.IsValid(value, maximum, allowControls: false))
        {
            issues.Add(new(ConfigurationIssueCodes.InvalidIdentity, "EnvironmentOptions." + name, "Supply a bounded identity without controls or malformed Unicode."));
        }
    }

    private static string ReadOperatingSystemFamily()
    {
        if (System.OperatingSystem.IsWindows()) { return "windows"; }
        if (System.OperatingSystem.IsLinux()) { return "linux"; }
        if (System.OperatingSystem.IsMacOS()) { return "macos"; }
        return "unknown";
    }

    private static string? ReadHostName()
    {
        try { return System.Environment.MachineName; }
        catch (InvalidOperationException) { return null; }
    }

    private static DateTimeOffset? ReadProcessStartedAtUtc()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return new DateTimeOffset(process.StartTime.ToUniversalTime());
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
