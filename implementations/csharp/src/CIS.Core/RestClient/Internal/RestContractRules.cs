using System.Text;
using CIS.Text;

namespace CIS.RestClient;

internal static class RestContractRules
{
    internal static bool Alias(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256
        && UnicodeScalarText.IsValid(value, 256, allowControls: false) && Encoding.UTF8.GetByteCount(value) <= 256;
    internal static bool Method(string? value) => value is { Length: > 0 and <= 64 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character));
    internal static void Timeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMilliseconds(uint.MaxValue - 1L))
        { throw new ArgumentOutOfRangeException(name, "Use a positive timeout representable by the native cancellation timer."); }
    }
    internal static T[] Copy<T>(IReadOnlyList<T>? source, int maximum, string name, T[]? fallback = null)
    {
        if (source is null) { return fallback is null ? [] : (T[])fallback.Clone(); }
        int count = source.Count;
        if (count < 0 || count > maximum) { throw new ArgumentException("The configured collection exceeds its finite limit.", name); }
        var copied = new T[count];
        for (int index = 0; index < count; index++) { copied[index] = source[index]; }
        return copied;
    }
}
