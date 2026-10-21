using System.Buffers;

namespace CIS.Healthcheck.Reporting;

// The JSON writer may request more scratch space than it ultimately commits (escaping
// uses a worst-case hint). Only committed bytes count against the complete payload cap.
internal sealed class BoundedHealthBuffer(int maximumBytes) : IBufferWriter<byte>
{
    private readonly ArrayBufferWriter<byte> output = new(Math.Min(maximumBytes, 4096));
    private byte[] scratch = [];

    internal ReadOnlySpan<byte> WrittenSpan => output.WrittenSpan;

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > scratch.Length) { throw new ArgumentOutOfRangeException(nameof(count)); }
        if (count > maximumBytes - output.WrittenCount) { throw new HealthReportTooLargeException(); }
        output.Write(scratch.AsSpan(0, count));
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        // No individual input can legitimately exceed the payload budget. JSON's
        // worst-case UTF-16 escaping hint is six bytes per input character.
        if (sizeHint > (long)maximumBytes * 6 + 4096) { throw new HealthReportTooLargeException(); }
        int size = Math.Max(sizeHint, 4096);
        if (scratch.Length < size) { scratch = new byte[size]; }
        return scratch;
    }

    public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
}

internal sealed class HealthReportTooLargeException : Exception;
