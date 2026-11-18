using CIS.Metrics;

namespace CIS.RestClient.Http;

// One small native stream decorator, with no history. Borrowing wrappers never
// dispose the underlying stream; its actual response/content owner does that.
internal sealed class RestTransferStream(Stream inner, bool reading, long limit, Action<long>? observed = null,
    bool signing = false, int? status = null) : Stream
{
    private long transferred;
    private bool exceeded;
    internal bool LimitExceeded => exceeded;
    public override bool CanRead => reading && inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => !reading && inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken token) => inner.FlushAsync(token);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        Ensure(true); int count;
        try { count = inner.Read(buffer[..ReadLength(buffer.Length)]); }
        catch (IOException error) { throw new RestResponseReadException(error, status); }
        ReadCompleted(count); return count;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        Ensure(true); int count;
        try { count = await inner.ReadAsync(buffer[..ReadLength(buffer.Length)], token).ConfigureAwait(false); }
        catch (IOException error) { throw new RestResponseReadException(error, status); }
        ReadCompleted(count); return count;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Ensure(false); CheckWrite(buffer.Length);
        try { inner.Write(buffer); }
        catch (IOException error) { throw new RestRequestWriteException(error); }
        transferred += buffer.Length; Observe(buffer.Length);
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    {
        Ensure(false); CheckWrite(buffer.Length);
        try { await inner.WriteAsync(buffer, token).ConfigureAwait(false); }
        catch (IOException error) { throw new RestRequestWriteException(error); }
        transferred += buffer.Length; Observe(buffer.Length);
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
    private void Ensure(bool read) { if (reading != read) { throw new NotSupportedException(); } }
    private int ReadLength(int requested) => (int)Math.Min(requested, Math.Max(1, limit - transferred + (limit == long.MaxValue ? 0 : 1)));
    private void ReadCompleted(int count)
    {
        transferred = checked(transferred + count); Observe(count);
        if (transferred > limit) { exceeded = true; throw new RestResponseLimitException(); }
    }
    private void CheckWrite(int count)
    { if (count > limit - transferred) { exceeded = true; if (signing) { throw new RestContentPreparationException(); } throw new RestResponseLimitException(); } }
    private void Observe(int count)
    {
        if (count == 0) { return; }
        try { observed?.Invoke(count); }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { }
    }
}
