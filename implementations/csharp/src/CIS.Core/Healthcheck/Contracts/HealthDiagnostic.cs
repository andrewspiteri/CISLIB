using System.Buffers;
using System.Text;
using CIS.Text;

namespace CIS.Healthcheck;

/// <summary>A bounded detached diagnostic that never retains an exception or owner object.</summary>
/// <remarks>Text limits count Unicode scalar values. No stack trace or exception data is projected.</remarks>
public sealed class HealthDiagnostic
{
    private const int MaximumCodeScalars = 64;
    private const int MaximumMessageScalars = 1_024;
    private const int MaximumExceptionTypeScalars = 256;

    /// <summary>Creates a diagnostic, rejecting malformed Unicode and oversized structural identifiers.</summary>
    /// <remarks>Free message text is truncated at 1,024 Unicode scalar values.</remarks>
    public HealthDiagnostic(string? code = null, string? message = null, string? exceptionType = null)
    {
        ValidateIdentifier(code, MaximumCodeScalars, nameof(code));
        ValidateIdentifier(exceptionType, MaximumExceptionTypeScalars, nameof(exceptionType));
        if (message is not null && !UnicodeScalarText.IsValid(message, int.MaxValue, allowControls: true))
        {
            throw new ArgumentException("Supply valid Unicode scalar text.", nameof(message));
        }

        Code = code;
        ExceptionType = exceptionType;
        if (message is not null)
        {
            Message = UnicodeScalarText.Prefix(message, MaximumMessageScalars, out bool truncated);
            IsTruncated = truncated;
        }
    }

    private HealthDiagnostic(string? code, string? message, string? exceptionType, bool isTruncated)
    {
        Code = code;
        Message = message;
        ExceptionType = exceptionType;
        IsTruncated = isTruncated;
    }

    /// <summary>An optional code of at most 64 Unicode scalar values.</summary>
    public string? Code { get; }

    /// <summary>Optional free text of at most 1,024 Unicode scalar values.</summary>
    public string? Message { get; }

    /// <summary>An optional exception type identifier of at most 256 Unicode scalar values.</summary>
    public string? ExceptionType { get; }

    /// <summary>Whether text was truncated or invalid runtime exception text was replaced.</summary>
    public bool IsTruncated { get; }

    /// <summary>Projects only the exception's message and runtime type name into bounded owned text.</summary>
    /// <remarks>Malformed runtime text is replaced with U+FFFD. An explicit message is direct caller text and must be valid Unicode.</remarks>
    public static HealthDiagnostic FromException(Exception exception, string? code = null, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ValidateIdentifier(code, MaximumCodeScalars, nameof(code));
        if (message is not null && !UnicodeScalarText.IsValid(message, int.MaxValue, allowControls: true))
        {
            throw new ArgumentException("Supply valid Unicode scalar text.", nameof(message));
        }

        bool messageUnavailable = false;
        string? sourceMessage = message;
        if (sourceMessage is null)
        {
            try { sourceMessage = exception.Message; }
            catch (Exception) { messageUnavailable = true; }
        }
        string? projectedMessage = ProjectRuntimeText(sourceMessage, MaximumMessageScalars, out bool messageChanged);
        Type exceptionType = exception.GetType();
        string? projectedType = ProjectRuntimeText(exceptionType.FullName ?? exceptionType.Name, MaximumExceptionTypeScalars, out bool typeChanged);
        return new HealthDiagnostic(code, projectedMessage, projectedType, messageUnavailable || messageChanged || typeChanged);
    }

    private static void ValidateIdentifier(string? value, int maximumScalars, string parameterName)
    {
        if (value is not null && !UnicodeScalarText.IsValid(value, maximumScalars, allowControls: true))
        {
            throw new ArgumentException("Supply bounded valid Unicode scalar text.", parameterName);
        }
    }

    private static string? ProjectRuntimeText(string? value, int maximumScalars, out bool changed)
    {
        changed = false;
        if (value is null) { return null; }

        var result = new StringBuilder(Math.Min(value.Length, maximumScalars));
        int offset = 0;
        int scalarCount = 0;
        while (offset < value.Length && scalarCount < maximumScalars)
        {
            OperationStatus status = Rune.DecodeFromUtf16(value.AsSpan(offset), out Rune scalar, out int consumed);
            if (status != OperationStatus.Done)
            {
                scalar = Rune.ReplacementChar;
                consumed = Math.Max(1, consumed);
                changed = true;
            }
            result.Append(scalar.ToString());
            offset += consumed;
            scalarCount++;
        }
        changed |= offset < value.Length;
        return result.ToString();
    }
}
