using System.Text;
using CIS.Security;

namespace CIS.Jobs;

internal static class JobText
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static string Text(string value, int maximumBytes, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumBytes || value.Any(char.IsControl))
        {
            throw new ArgumentException("Supply bounded Unicode text without control characters.", parameterName);
        }

        try
        {
            if (Utf8.GetByteCount(value) > maximumBytes)
            {
                throw new ArgumentException("The UTF-8 text exceeds its published byte limit.", parameterName);
            }
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("Supply well-formed Unicode text.", parameterName);
        }

        return value;
    }

    internal static IReadOnlyList<string> Capabilities(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copied = new List<string>();
        foreach (string value in values)
        {
            if (copied.Count == 8)
            {
                throw new ArgumentException("Declare one through eight distinct execution capabilities.", nameof(values));
            }

            string capability = SecurityText.Capability(value, nameof(values));
            if (copied.Contains(capability, StringComparer.Ordinal))
            {
                throw new ArgumentException("Execution capabilities must be distinct.", nameof(values));
            }

            copied.Add(capability);
        }

        if (copied.Count == 0)
        {
            throw new ArgumentException("Declare at least one execution capability.", nameof(values));
        }

        return copied.AsReadOnly();
    }

    internal static TimeSpan Duration(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromDays(49))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Supply a positive duration no greater than 49 days.");
        }

        return value;
    }
}
