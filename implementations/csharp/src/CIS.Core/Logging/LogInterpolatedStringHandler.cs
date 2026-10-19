using System.Runtime.CompilerServices;
using CIS.Logging.Events;

namespace CIS.Logging;

/// <summary>Captures C# interpolation arguments for owned, bounded logging.</summary>
/// <remarks>Expressions execute on the caller, including when the level is disabled. Built-in
/// scalars are formatted by the consumer using invariant culture. User-defined formatting runs
/// at most once on the caller; only its copied text is retained. This handler does not derive
/// structured attribute names from expressions.</remarks>
[InterpolatedStringHandler]
public struct LogInterpolatedStringHandler
{
    private InterpolationCapture? capture;

    /// <summary>Starts a bounded capture; compiler size hints never determine allocation size.</summary>
    public LogInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        capture = new InterpolationCapture();
    }

    /// <summary>Captures literal text.</summary>
    public void AppendLiteral(string value) => Capture.AddLiteral(value);

    /// <summary>Captures an interpolation expression.</summary>
    public void AppendFormatted<T>(T value) => Capture.AddValue(value, 0, null);

    /// <summary>Captures an expression and its .NET format string.</summary>
    public void AppendFormatted<T>(T value, string? format) => Capture.AddValue(value, 0, format);

    /// <summary>Captures an expression and alignment.</summary>
    public void AppendFormatted<T>(T value, int alignment) => Capture.AddValue(value, alignment, null);

    /// <summary>Captures an expression, alignment and .NET format string.</summary>
    public void AppendFormatted<T>(T value, int alignment, string? format) => Capture.AddValue(value, alignment, format);

    /// <summary>Captures nullable string text, alignment and optional format metadata.</summary>
    public void AppendFormatted(string? value, int alignment = 0, string? format = null) => Capture.AddValue(value, alignment, format);

    /// <summary>Copies a span immediately; no borrowed memory is retained.</summary>
    public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => Capture.AddSpan(value, alignment, format);

    private InterpolationCapture Capture => capture ??= new InterpolationCapture();
    internal CapturedInterpolation Freeze() => Capture.Freeze();
}
