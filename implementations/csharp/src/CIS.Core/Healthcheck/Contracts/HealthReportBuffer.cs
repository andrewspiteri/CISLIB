namespace CIS.Healthcheck;

/// <summary>An owned immutable view of one complete pre-serialized UTF-8 report.</summary>
public sealed class HealthReportBuffer
{
    internal HealthReportBuffer(ReadOnlySpan<byte> utf8Bytes, bool isError, string? errorCode, bool selectedHealthy)
    {
        Utf8Bytes = utf8Bytes.ToArray();
        IsError = isError;
        ErrorCode = errorCode;
        SelectedHealthy = !isError && selectedHealthy;
    }

    /// <summary>Owned read-only UTF-8 bytes without a byte-order mark.</summary>
    public ReadOnlyMemory<byte> Utf8Bytes { get; }

    /// <summary>Whether the complete payload represents a reporting failure.</summary>
    public bool IsError { get; }

    /// <summary>A stable reporting error code when formatting failed; otherwise null.</summary>
    public string? ErrorCode { get; }

    /// <summary>The selected health/readiness result; false for every reporting error.</summary>
    public bool SelectedHealthy { get; }
}
