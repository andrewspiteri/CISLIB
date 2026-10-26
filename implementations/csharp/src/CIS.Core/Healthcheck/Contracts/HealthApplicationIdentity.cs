namespace CIS.Healthcheck;

/// <summary>Explicit application identity input, validated and copied when a service is created.</summary>
public sealed class HealthApplicationIdentity
{
    /// <summary>The application name.</summary>
    public required string Name { get; init; }

    /// <summary>The optional actual build version.</summary>
    public string? BuildVersion { get; init; }

    /// <summary>The optional selected deployment version.</summary>
    public string? ServiceVersion { get; init; }

    /// <summary>The nonempty application instance identity.</summary>
    public required Guid InstanceId { get; init; }

    /// <summary>The optional host name.</summary>
    public string? HostName { get; init; }

    /// <summary>The optional operating-system description.</summary>
    public string? OsDescription { get; init; }
}
