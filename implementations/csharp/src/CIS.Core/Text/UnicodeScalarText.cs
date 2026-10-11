using System.Buffers;
using System.Text;

namespace CIS.Text;

// Shared validation only: callers choose whether controls/empty/whitespace are permitted.
internal static class UnicodeScalarText
{
    internal static bool IsValid(string value, int maximumScalars, bool allowControls)
    {
        ReadOnlySpan<char> remaining = value.AsSpan();
        int count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out Rune scalar, out int consumed) != OperationStatus.Done ||
                !allowControls && Rune.IsControl(scalar) || ++count > maximumScalars)
            {
                return false;
            }
            remaining = remaining[consumed..];
        }
        return true;
    }

    // Validate the retained prefix only; never split a surrogate pair or copy an unbounded suffix.
    internal static string Prefix(string value, int maximumScalars, out bool truncated)
    {
        int offset = 0;
        int scalars = 0;
        while (offset < value.Length && scalars < maximumScalars)
        {
            if (Rune.DecodeFromUtf16(value.AsSpan(offset), out _, out int consumed) != OperationStatus.Done)
            {
                throw new ArgumentException("Use valid Unicode scalar text.", nameof(value));
            }
            offset += consumed;
            scalars++;
        }
        truncated = offset < value.Length;
        return truncated ? value[..offset] : value;
    }
}
