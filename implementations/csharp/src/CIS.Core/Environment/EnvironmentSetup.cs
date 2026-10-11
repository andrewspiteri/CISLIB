using System.Reflection;
using CIS.Logging;

namespace CIS;

/// <summary>Caller-owned startup input. Changes after Initialize returns do not affect the published snapshot; do not mutate it during initialization.</summary>
public sealed class EnvironmentSetup
{
    /// <summary>The consuming application assembly used for identity discovery when supplied.</summary>
    public Assembly? ApplicationAssembly { get; set; }

    /// <summary>The application identity, independent of collector destinations.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>The actual application build identity, including prerelease and build text.</summary>
    public string? BuildVersion { get; set; }

    /// <summary>The deployment or telemetry release label, independent of BuildVersion.</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>The application environment label; it does not modify the host environment.</summary>
    public string? EnvironmentName { get; set; }

    /// <summary>The explicitly selected application execution kind.</summary>
    public EnvironmentKind EnvironmentKind { get; set; }

    /// <summary>An optional explicit build commit identifier.</summary>
    public string? CommitId { get; set; }

    /// <summary>An optional explicit build identifier.</summary>
    public string? BuildId { get; set; }

    /// <summary>The logging configuration for this application.</summary>
    public LoggingSetup? Logging { get; set; }
}
