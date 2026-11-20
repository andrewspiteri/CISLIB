namespace CIS.Transactions;

internal static class TransactionText
{
    internal static string Code(string value, string parameterName)
    {
        if (value is null || value.Length is < 1 or > 128
            || value.Any(character => character is < ' ' or > '~') || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Use a nonempty printable ASCII reason or error code of at most 128 characters.", parameterName);
        }
        return value;
    }

    internal static string Alias(string value)
    {
        if (value is null || value.Length is < 1 or > 64 || !IsAlphaNumeric(value[0])
            || value.Any(character => !IsAlphaNumeric(character) && character is not '_' and not '.' and not '-'))
        {
            throw new ArgumentException("Use an ordinal database alias matching [A-Za-z0-9][A-Za-z0-9_.-]{0,63}.", nameof(value));
        }
        return value;
    }

    internal static bool IsOrdinary(Exception error) => error is not OutOfMemoryException
        and not StackOverflowException and not AccessViolationException;

    private static bool IsAlphaNumeric(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
