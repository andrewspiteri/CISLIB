using CIS.Configuration;
using CIS.Logging;

namespace CIS;

/// <summary>Explicit, one-time publication of validated application configuration and metadata.</summary>
public static class EnvironmentOptions
{
    private static readonly object Gate = new();
    private static EnvironmentSnapshot? current;
    private static bool initializing;

    // Only the private gate is allocated; no configuration or metadata is read here.
    static EnvironmentOptions() { }

    /// <summary>Reports whether a complete snapshot has been successfully published.</summary>
    public static bool IsInitialized
    {
        get
        {
            lock (Gate)
            {
                return current is not null;
            }
        }
    }

    /// <summary>Gets the identical immutable snapshot published by Initialize.</summary>
    /// <exception cref="InvalidOperationException">No successful initialization has occurred.</exception>
    public static EnvironmentSnapshot Current
    {
        get
        {
            lock (Gate)
            {
                return current ?? throw new InvalidOperationException("Core has not been initialized. Call EnvironmentOptions.Initialize first.");
            }
        }
    }

    /// <summary>Copies, resolves and validates startup inputs, then publishes the complete snapshot once.</summary>
    /// <param name="setup">Caller-owned startup input; it must not be mutated concurrently with this call.</param>
    /// <returns>The same immutable instance subsequently exposed through Current.</returns>
    /// <exception cref="ArgumentNullException">setup is null.</exception>
    /// <exception cref="CoreConfigurationException">Selected inputs fail validation; nothing is published.</exception>
    /// <exception cref="InvalidOperationException">A snapshot has already been published, or a metadata callback re-enters initialization.</exception>
    public static EnvironmentSnapshot Initialize(EnvironmentSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        lock (Gate)
        {
            if (current is not null)
            {
                throw new InvalidOperationException("Core has already been initialized.");
            }
            if (initializing)
            {
                throw new InvalidOperationException("Core initialization is already in progress.");
            }
            initializing = true;
            try
            {
                EnvironmentSnapshot snapshot = EnvironmentSnapshotFactory.Create(setup);
                current = snapshot;
                return snapshot;
            }
            finally
            {
                initializing = false;
            }
        }
    }

    /// <summary>The application identity, independent of collector destinations.</summary>
    /// <exception cref="InvalidOperationException">No successful initialization has occurred.</exception>
    public static string ApplicationName => Current.ApplicationName;

    /// <summary>The actual application build identity, including prerelease and build text.</summary>
    /// <exception cref="InvalidOperationException">No successful initialization has occurred.</exception>
    public static string BuildVersion => Current.BuildVersion;

    /// <summary>The deployment or telemetry release label, independent of BuildVersion.</summary>
    /// <exception cref="InvalidOperationException">No successful initialization has occurred.</exception>
    public static string ServiceVersion => Current.ServiceVersion;

    /// <summary>The application environment label; it does not modify the host environment.</summary>
    /// <exception cref="InvalidOperationException">No successful initialization has occurred.</exception>
    public static string EnvironmentName => Current.EnvironmentName;

    /// <summary>The host name observed by this application, or null when unavailable.</summary>
    /// <exception cref="InvalidOperationException">No successful initialization has occurred.</exception>
    public static string? HostName => Current.HostName;

    /// <summary>The lowercase operating-system family.</summary>
    /// <exception cref="InvalidOperationException">No successful initialization has occurred.</exception>
    public static string OperatingSystem => Current.OperatingSystem;

    /// <summary>The logging configuration for this application.</summary>
    /// <exception cref="InvalidOperationException">No successful initialization has occurred.</exception>
    public static LoggingOptions Logging => Current.Logging;

    /// <summary>The immutable allowlisted application and process facts used by logging.</summary>
    /// <exception cref="InvalidOperationException">No successful initialization has occurred.</exception>
    public static EventMetadata EventMetadata => Current.EventMetadata;
}
