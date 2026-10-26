using System.Globalization;
using CIS.Configuration;

namespace CIS.Healthcheck;

/// <summary>Module settings copied by each Healthcheck runtime or transport.</summary>
/// <remarks>Call Resolve explicitly to select deployment environment overrides. Construction does not read the environment.</remarks>
public sealed class HealthcheckOptions
{
    /// <summary>Gets the maximum simultaneous actual collections; default 4, range 1 through 64.</summary>
    public int MaxConcurrentCollections { get; init; } = 4;
    /// <summary>Gets the maximum active monitors; default 1024, range 1 through 65536.</summary>
    public int MaxMonitors { get; init; } = 1024;
    /// <summary>Gets whether detailed reporting is enabled; default false.</summary>
    public bool DetailsEnabled { get; init; }
    /// <summary>Gets whether an explicitly installed pipe server may open its endpoint; default false.</summary>
    public bool PipeEnabled { get; init; }
    /// <summary>Gets the portable local pipe name, required when pipe reporting is enabled.</summary>
    public string? PipeName { get; init; }
    /// <summary>Gets the maximum active pipe clients; default 8, range 1 through 64.</summary>
    public int PipeMaxClients { get; init; } = 8;
    /// <summary>Gets the total pipe connection budget in milliseconds; default 1000, range 50 through 60000.</summary>
    public int PipeRequestTimeoutMs { get; init; } = 1000;
    /// <summary>Gets the complete encoded report limit in bytes; default 4194304, range 1024 through 16777216.</summary>
    public int MaxReportBytes { get; init; } = 4194304;

    /// <summary>Reads each named environment variable once, selects overrides, validates, and returns detached settings.</summary>
    /// <param name="setup">Optional application settings below nonblank environment overrides in precedence.</param>
    /// <exception cref="CoreConfigurationException">A selected setting is malformed, out of range, or missing.</exception>
    public static HealthcheckOptions Resolve(HealthcheckOptions? setup = null)
        => Resolve(setup, System.Environment.GetEnvironmentVariable);

    internal static HealthcheckOptions Resolve(HealthcheckOptions? setup, Func<string, string?> readEnvironment)
    {
        ArgumentNullException.ThrowIfNull(readEnvironment);
        setup ??= new HealthcheckOptions();
        var issues = new List<ConfigurationIssue>();
        var selected = new HealthcheckOptions
        {
            MaxConcurrentCollections = ReadInteger("CIS_HEALTHCHECK_MAX_CONCURRENT_COLLECTIONS", setup.MaxConcurrentCollections),
            MaxMonitors = ReadInteger("CIS_HEALTHCHECK_MAX_MONITORS", setup.MaxMonitors),
            DetailsEnabled = ReadBoolean("CIS_HEALTHCHECK_DETAILS_ENABLED", setup.DetailsEnabled),
            PipeEnabled = ReadBoolean("CIS_HEALTHCHECK_PIPE_ENABLED", setup.PipeEnabled),
            PipeName = ReadText("CIS_HEALTHCHECK_PIPE_NAME") ?? setup.PipeName,
            PipeMaxClients = ReadInteger("CIS_HEALTHCHECK_PIPE_MAX_CLIENTS", setup.PipeMaxClients),
            PipeRequestTimeoutMs = ReadInteger("CIS_HEALTHCHECK_PIPE_TIMEOUT_MS", setup.PipeRequestTimeoutMs),
            MaxReportBytes = ReadInteger("CIS_HEALTHCHECK_MAX_REPORT_BYTES", setup.MaxReportBytes)
        };
        selected.Validate(issues);
        if (issues.Count != 0)
        {
            throw new CoreConfigurationException(issues);
        }
        return selected;

        string? ReadText(string key)
        {
            string? value = readEnvironment(key);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        int ReadInteger(string key, int fallback)
        {
            string? value = ReadText(key);
            if (value is null)
            {
                return fallback;
            }
            if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }
            issues.Add(new ConfigurationIssue("Healthcheck.InvalidValue", key, "Supply an invariant integer within the documented range."));
            return fallback;
        }

        bool ReadBoolean(string key, bool fallback)
        {
            string? value = ReadText(key);
            if (value is null)
            {
                return fallback;
            }
            if (bool.TryParse(value, out bool parsed))
            {
                return parsed;
            }
            issues.Add(new ConfigurationIssue("Healthcheck.InvalidValue", key, "Supply true or false."));
            return fallback;
        }
    }

    internal HealthcheckOptions ValidatedCopy()
    {
        var issues = new List<ConfigurationIssue>();
        Validate(issues);
        if (issues.Count != 0)
        {
            throw new CoreConfigurationException(issues);
        }
        return new HealthcheckOptions
        {
            MaxConcurrentCollections = MaxConcurrentCollections,
            MaxMonitors = MaxMonitors,
            DetailsEnabled = DetailsEnabled,
            PipeEnabled = PipeEnabled,
            PipeName = PipeName,
            PipeMaxClients = PipeMaxClients,
            PipeRequestTimeoutMs = PipeRequestTimeoutMs,
            MaxReportBytes = MaxReportBytes
        };
    }

    private void Validate(List<ConfigurationIssue> issues)
    {
        CheckRange(MaxConcurrentCollections, 1, 64, "CIS_HEALTHCHECK_MAX_CONCURRENT_COLLECTIONS");
        CheckRange(MaxMonitors, 1, 65536, "CIS_HEALTHCHECK_MAX_MONITORS");
        CheckRange(PipeMaxClients, 1, 64, "CIS_HEALTHCHECK_PIPE_MAX_CLIENTS");
        CheckRange(PipeRequestTimeoutMs, 50, 60000, "CIS_HEALTHCHECK_PIPE_TIMEOUT_MS");
        CheckRange(MaxReportBytes, 1024, 16777216, "CIS_HEALTHCHECK_MAX_REPORT_BYTES");
        if (PipeName is null)
        {
            if (PipeEnabled && !issues.Any(issue => issue.Key == "CIS_HEALTHCHECK_PIPE_ENABLED"))
            {
                issues.Add(new ConfigurationIssue("Healthcheck.RequiredValue", "CIS_HEALTHCHECK_PIPE_NAME", "Supply a local pipe name when pipe reporting is enabled."));
            }
        }
        else if (!IsValidPipeName(PipeName))
        {
            issues.Add(new ConfigurationIssue("Healthcheck.InvalidValue", "CIS_HEALTHCHECK_PIPE_NAME", "Use 1 through 64 ASCII letters, digits, dots, underscores or hyphens, beginning with a letter or digit; anonymous is reserved."));
        }

        void CheckRange(int value, int minimum, int maximum, string key)
        {
            if (issues.Any(issue => issue.Key == key))
            {
                return;
            }
            if (value < minimum || value > maximum)
            {
                issues.Add(new ConfigurationIssue("Healthcheck.OutOfRange", key, string.Create(CultureInfo.InvariantCulture, $"Supply an integer from {minimum} through {maximum}.")));
            }
        }
    }

    internal static bool IsValidPipeName(string value)
    {
        if (value.Length is < 1 or > 64 || !char.IsAsciiLetterOrDigit(value[0]) ||
            value.Equals("anonymous", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                return false;
            }
        }
        return true;
    }
}
