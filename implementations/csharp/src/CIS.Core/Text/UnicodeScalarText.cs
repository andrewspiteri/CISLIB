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
}
