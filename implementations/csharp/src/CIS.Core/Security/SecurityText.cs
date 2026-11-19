using CIS.Text;

namespace CIS.Security;

internal static class SecurityText
{
    internal static string Identifier(string value, string name) => Bounded(value, 1024, name);
    internal static string Alias(string value, string name) => Bounded(value, 64, name);
    internal static string Application(string value, string name) => Bounded(value, 255, name);
    internal static string? OptionalIdentifier(string? value, string name) => value is null ? null : Identifier(value, name);

    internal static string Bounded(string value, int maximum, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length > maximum * 2)
        {
            throw new ArgumentException("Supply bounded valid Unicode without control characters.", name);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (!UnicodeScalarText.IsValid(value, maximum, allowControls: false))
        {
            throw new ArgumentException("Supply bounded valid Unicode without control characters.", name);
        }
        return value;
    }

    internal static string Capability(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length > 128)
        {
            throw new ArgumentException("A capability has at most 128 ASCII characters.", name);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        bool segmentStart = true;
        foreach (char character in value)
        {
            if (character == '.')
            {
                if (segmentStart)
                {
                    throw new ArgumentException("Capabilities use nonempty lowercase ASCII segments.", name);
                }
                segmentStart = true;
            }
            else
            {
                if (segmentStart ? character is < 'a' or > 'z'
                    : character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')
                {
                    throw new ArgumentException("Capabilities use lowercase ASCII letter-led segments.", name);
                }
                segmentStart = false;
            }
        }
        if (segmentStart)
        {
            throw new ArgumentException("Capabilities do not end in a separator.", name);
        }
        return value;
    }
}
