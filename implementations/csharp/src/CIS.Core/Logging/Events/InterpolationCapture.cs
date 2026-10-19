using System.Buffers;
using System.Globalization;
using System.Text;
using CIS.Logging.Values;
using CIS.Text;

namespace CIS.Logging.Events;

// Mutable construction never crosses publication. Freeze copies the bounded parts array.
internal sealed class InterpolationCapture
{
    internal const int MaximumParts = 1024;
    internal const int MaximumFormatCharacters = 256;
    private readonly List<InterpolationPart> parts = [];
    private int remainingText = EventProjection.MaximumPayloadScalars;
    private bool truncated;
    private string? errorCode;

    internal void AddLiteral(string value)
    {
        if (!ReservePart()) { return; }
        parts.Add(new(true, OwnText(value), 0, null, false));
    }

    internal void AddSpan(ReadOnlySpan<char> value, int alignment, string? format)
    {
        // Own only a bounded prefix, but validate the complete supplied span as ordinary capture does.
        if (!ReservePart()) { return; }
        int offset = 0, retained = 0, scalars = 0;
        while (offset < value.Length)
        {
            if (Rune.DecodeFromUtf16(value[offset..], out _, out int consumed) != OperationStatus.Done)
            { errorCode = "InvalidUnicode"; return; }
            offset += consumed;
            if (scalars++ < remainingText) { retained = offset; }
        }
        truncated |= retained != value.Length;
        AddValue(value[..retained].ToString(), alignment, format);
    }

    internal void AddValue(object? value, int alignment, string? format)
    {
        if (!ReservePart()) { return; }
        bool omitted = format is { Length: > MaximumFormatCharacters } || ExcessivePrecision(value, format);
        if (omitted) { truncated = true; parts.Add(new(false, null, alignment, null, true)); return; }
        string? ownedFormat = format is null ? null : OwnText(format);
        if (ownedFormat?.Length != format?.Length) { truncated = true; omitted = true; }
        try
        {
            object? owned = value switch
            {
                null => null,
                string text => OwnText(text),
                char character => OwnText(character.ToString()),
                // Unbox and rebox: returning value would retain an alias to a caller-owned box.
                bool scalar => scalar,
                byte scalar => scalar,
                sbyte scalar => scalar,
                short scalar => scalar,
                ushort scalar => scalar,
                int scalar => scalar,
                uint scalar => scalar,
                long scalar => scalar,
                ulong scalar => scalar,
                Int128 scalar => scalar,
                UInt128 scalar => scalar,
                nint scalar => scalar,
                nuint scalar => scalar,
                Half scalar => scalar,
                float scalar => scalar,
                double scalar => scalar,
                decimal scalar => scalar,
                DateTime scalar => scalar,
                DateTimeOffset scalar => scalar,
                DateOnly scalar => scalar,
                TimeOnly scalar => scalar,
                TimeSpan scalar => scalar,
                Guid scalar => scalar,
                IFormattable formattable => OwnText(formattable.ToString(format, CultureInfo.InvariantCulture) ?? string.Empty),
                _ => OwnText(value.ToString() ?? string.Empty)
            };
            parts.Add(new(false, owned, alignment, ownedFormat, omitted));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        { errorCode = "InterpolationCaptureFailed"; }
    }

    private bool ReservePart()
    {
        if (parts.Count < MaximumParts) { return true; }
        truncated = true; return false;
    }

    private string OwnText(string value)
    {
        if (!LogValueText.IsValidUnicode(value)) { errorCode = "InvalidUnicode"; return string.Empty; }
        string result = UnicodeScalarText.Prefix(value, remainingText, out bool clipped);
        remainingText -= result.EnumerateRunes().Count();
        truncated |= clipped;
        return result;
    }

    private static bool ExcessivePrecision(object? value, string? format)
    {
        if (value is not (byte or sbyte or short or ushort or int or uint or long or ulong or
            Int128 or UInt128 or nint or nuint or Half or float or double or decimal) ||
            format is not { Length: > 1 } || !char.IsAsciiLetter(format[0])) { return false; }
        int precision = 0;
        foreach (char digit in format.AsSpan(1))
        {
            if (!char.IsAsciiDigit(digit)) { return false; }
            precision = Math.Min(EventProjection.MaximumPayloadScalars + 1, precision * 10 + digit - '0');
        }
        return precision > EventProjection.MaximumPayloadScalars;
    }

    internal CapturedInterpolation Freeze() => new(parts.ToArray(), truncated, errorCode);
}

internal readonly record struct InterpolationPart(bool Literal, object? Value, int Alignment, string? Format, bool Omitted);
