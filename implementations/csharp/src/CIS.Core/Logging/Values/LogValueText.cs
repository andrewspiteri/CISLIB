using System.Buffers;
using System.Text;

namespace CIS.Logging.Values;

internal static class LogValueText
{
    public static bool IsValidUnicode(string value)
    {
        ReadOnlySpan<char> remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out int consumed) != OperationStatus.Done)
            {
                return false;
            }
            remaining = remaining[consumed..];
        }
        return true;
    }

    public static bool IsValidKey(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }
        ReadOnlySpan<char> remaining = value.AsSpan();
        int scalars = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out Rune scalar, out int consumed) != OperationStatus.Done ||
                Rune.IsControl(scalar) || ++scalars > 255)
            {
                return false;
            }
            remaining = remaining[consumed..];
        }
        return true;
    }

    public static bool IsInteger(string value)
    {
        int start = value.StartsWith('-') ? 1 : 0;
        if (start == value.Length)
        {
            return false;
        }
        if (value[start] == '0')
        {
            return start == 0 && value.Length == 1;
        }
        return value[start] is >= '1' and <= '9' && Digits(value.AsSpan(start + 1));
    }

    public static bool IsDecimal(string value)
    {
        int start = value.StartsWith('-') ? 1 : 0;
        int point = value.IndexOf('.');
        if (point <= start || point == value.Length - 1)
        {
            return false;
        }
        ReadOnlySpan<char> whole = value.AsSpan(start, point - start);
        bool validWhole = whole.Length == 1 && whole[0] == '0' ||
            whole[0] is >= '1' and <= '9' && Digits(whole[1..]);
        return validWhole && Digits(value.AsSpan(point + 1));
    }

    private static bool Digits(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }
        return true;
    }
}
