using System.Text;

namespace CIS.Operations;

internal static class OperationText
{
    internal static readonly UTF8Encoding Utf8Encoding = new(false, true);

    internal static string Alias(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length is < 1 or > 64 || !AlphaNumeric(value[0])
            || value.Any(character => !AlphaNumeric(character) && character is not '_' and not '.' and not '-'))
        {
            throw new ArgumentException("Use an ordinal alias matching [A-Za-z0-9][A-Za-z0-9_.-]{0,63}.", name);
        }
        return value;
    }

    internal static string Contract(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length is < 1 or > 128 || !AlphaNumeric(value[0])
            || value.Any(character => !AlphaNumeric(character) && character is not '_' and not '.' and not '-' and not ':' and not '/'))
        {
            throw new ArgumentException("Use a stable ASCII contract identifier of at most 128 characters.", name);
        }
        return value;
    }

    internal static string Key(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length is < 1 or > 128 || value.Any(character => character is < '!' or > '~'))
        {
            throw new ArgumentException("Use an opaque key containing one to 128 visible ASCII bytes.", name);
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

    internal static string Utf8(string value, int maximumBytes, string name, bool allowControls = false)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumBytes || value.Contains('\0')
            || (!allowControls && value.Any(char.IsControl)))
        {
            throw new ArgumentException("Supply bounded valid Unicode without prohibited control characters.", name);
        }
        try
        {
            if (Utf8Encoding.GetByteCount(value) > maximumBytes)
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

    internal static int ByteLimit(int value, string name)
    {
        if (value is < 1 or > 1_048_576)
        {
            throw new ArgumentOutOfRangeException(name, "Use a byte limit between one and one MiB.");
        }
        return value;
    }

    internal static bool IsOrdinary(Exception error) => error is not OutOfMemoryException
        and not StackOverflowException and not AccessViolationException;

    private static bool AlphaNumeric(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
