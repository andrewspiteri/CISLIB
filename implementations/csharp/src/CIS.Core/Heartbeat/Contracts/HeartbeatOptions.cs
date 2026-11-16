using System.Globalization;
using CIS.Configuration;

namespace CIS.Heartbeat;

/// <summary>Mutable application setup copied before the application's Heartbeat runtime is activated.</summary>
/// <remarks>Construction does not read environment variables or start work. A nonblank CIS_HEARTBEAT_MAX_CONCURRENT overrides setup during runtime configuration.</remarks>
public sealed class HeartbeatOptions
{
    private const string ConcurrencyKey = "CIS_HEARTBEAT_MAX_CONCURRENT";
    private static readonly int DefaultMaxConcurrent = ReadDefaultMaxConcurrent();

    /// <summary>Gets or sets the application-wide unfinished dispatch bound, from 1 through 16.</summary>
    /// <remarks>The computed default is the completion-port maximum from ThreadPool.GetMaxThreads, clamped to 1 through 16 and read once.</remarks>
    public int MaxConcurrent { get; set; } = DefaultMaxConcurrent;

    internal static HeartbeatSettings Resolve(HeartbeatOptions? setup, Func<string, string?> readEnvironment)
    {
        ArgumentNullException.ThrowIfNull(readEnvironment);
        int selected = setup?.MaxConcurrent ?? DefaultMaxConcurrent;
        string? input = readEnvironment(ConcurrencyKey);
        if (!string.IsNullOrWhiteSpace(input))
        {
            string token = input.Trim();
            ReadOnlySpan<char> digits = token.AsSpan();
            if (digits[0] is '+' or '-') { digits = digits[1..]; }
            if (digits.IsEmpty || digits.ContainsAnyExceptInRange('0', '9'))
            {
                throw InvalidConfiguration("Heartbeat.InvalidValue");
            }
            if (!int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out selected))
            {
                throw InvalidConfiguration("Heartbeat.OutOfRange");
            }
        }
        if (selected is < 1 or > 16) { throw InvalidConfiguration("Heartbeat.OutOfRange"); }
        return new HeartbeatSettings(selected);
    }

    internal static int ComputeDefaultMaxConcurrent(int completionPortMaximum)
        => Math.Min(16, Math.Max(1, completionPortMaximum));

    private static int ReadDefaultMaxConcurrent()
    {
        ThreadPool.GetMaxThreads(out _, out int completionPortMaximum);
        return ComputeDefaultMaxConcurrent(completionPortMaximum);
    }

    private static CoreConfigurationException InvalidConfiguration(string code)
        => new([new ConfigurationIssue(code, ConcurrencyKey, "Supply an invariant decimal integer from 1 through 16.")]);
}

internal sealed record HeartbeatSettings(int MaxConcurrent);
