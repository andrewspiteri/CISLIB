using System.Text;

namespace CIS.Messaging;

internal static class MessageText
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    internal static string Alias(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length is < 1 or > 64 || !AlphaNumeric(value[0])
            || value.Any(character => !AlphaNumeric(character) && character is not '.' and not '_' and not '-'))
        {
            throw new ArgumentException("Use an ordinal alias matching [A-Za-z0-9][A-Za-z0-9_.-]{0,63}.", name);
        }
        return value;
    }

    internal static string Contract(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length is < 1 or > 128
            || value.Any(character => !AlphaNumeric(character) && character is not '.' and not '_' and not '-' and not '/'))
        {
            throw new ArgumentException("Use one to 128 ASCII letters, digits, dots, underscores, hyphens or slashes.", name);
        }
        return value;
    }

    internal static string Unicode(string value, int maximumBytes, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumBytes
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("Supply bounded Unicode text without control characters.", name);
        }
        try
        {
            if (Utf8.GetByteCount(value) > maximumBytes)
            {
                throw new ArgumentException("The UTF-8 byte limit was exceeded.", name);
            }
        }
        catch (EncoderFallbackException error)
        {
            throw new ArgumentException("Supply valid Unicode without unpaired surrogates.", name, error);
        }
        return value;
    }

    internal static string Code(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || value.Any(character => character is < ' ' or > '~'))
        {
            throw new ArgumentException("Use a nonempty printable ASCII code of at most 128 characters.", name);
        }
        return value;
    }

    internal static Guid Id(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A message identity must be a nonempty UUID.", name);
        }
        return value;
    }

    internal static int Range(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, $"Use a value from {minimum} through {maximum}.");
        }
        return value;
    }

    internal static TimeSpan Duration(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, "The finite duration is outside the supported range.");
        }
        return value;
    }

    internal static IReadOnlyList<string> Capabilities(IEnumerable<string> values, string name, bool required = true)
    {
        ArgumentNullException.ThrowIfNull(values, name);
        var copied = new List<string>();
        foreach (string value in values)
        {
            string selected = Security.SecurityText.Capability(value, name);
            if (copied.Count == 8 || copied.Contains(selected, StringComparer.Ordinal))
            {
                throw new ArgumentException("Supply at most eight distinct capabilities.", name);
            }
            copied.Add(selected);
        }
        if (required && copied.Count == 0)
        {
            throw new ArgumentException("Supply at least one capability.", name);
        }
        return Array.AsReadOnly(copied.ToArray());
    }

    private static bool AlphaNumeric(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
