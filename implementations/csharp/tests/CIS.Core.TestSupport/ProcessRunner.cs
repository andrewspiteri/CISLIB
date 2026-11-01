using System.Diagnostics;
using System.Text;

namespace CIS.TestSupport;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public int ProcessId { get; init; }
    public ReadOnlyMemory<byte> StandardOutputBytes { get; init; }
    public ReadOnlyMemory<byte> StandardErrorBytes { get; init; }
    public long StandardOutputTotalBytes { get; init; }
    public long StandardErrorTotalBytes { get; init; }
    public bool StandardOutputTruncated { get; init; }
    public bool StandardErrorTruncated { get; init; }
    public bool CaptureTruncated => StandardOutputTruncated || StandardErrorTruncated;
    public bool OutputDrained { get; init; } = true;
}

/// <summary>A process deadline with the bounded output observed before cleanup finished.</summary>
public sealed class ProcessTimeoutException : TimeoutException
{
    internal ProcessTimeoutException(string message, Exception innerException, ProcessResult result, bool cleanupCompleted)
        : base(message, innerException)
    {
        Result = result;
        CleanupCompleted = cleanupCompleted;
    }

    public ProcessResult Result { get; }
    public bool CleanupCompleted { get; }
}

/// <summary>A completed process whose bounded capture cannot supply complete authoritative output.</summary>
public sealed class ProcessOutputLimitException : IOException
{
    internal ProcessOutputLimitException(ProcessResult result)
        : base("Child process output exceeded its capture limit; bounded head/tail diagnostics are available in Result.")
        => Result = result;

    public ProcessResult Result { get; }
}

/// <summary>Runs a bounded child process, retaining bounded head/tail output while draining both streams.</summary>
public static class ProcessRunner
{
    private static readonly TimeSpan CleanupDeadline = TimeSpan.FromSeconds(5);

    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environment = null,
        Encoding? outputEncoding = null,
        int maximumCapturedBytesPerStream = 1_048_576,
        bool allowTruncatedOutput = false,
        int? maximumCapturedStandardOutputBytes = null,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive finite timeout is required.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCapturedBytesPerStream);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCapturedBytesPerStream, 64 * 1024 * 1024);
        if (maximumCapturedStandardOutputBytes is int outputLimit)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(outputLimit, nameof(maximumCapturedStandardOutputBytes));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(outputLimit, 64 * 1024 * 1024, nameof(maximumCapturedStandardOutputBytes));
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach ((string name, string? value) in environment)
            {
                if (value is null) { startInfo.Environment.Remove(name); }
                else { startInfo.Environment[name] = value; }
            }
        }

        using var process = new Process { StartInfo = startInfo };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var readers = new CancellationTokenSource();
        process.Start();
        var stdout = new BoundedCapture(maximumCapturedStandardOutputBytes ?? maximumCapturedBytesPerStream);
        var stderr = new BoundedCapture(maximumCapturedBytesPerStream);
        Encoding stdoutEncoding = process.StandardOutput.CurrentEncoding, stderrEncoding = process.StandardError.CurrentEncoding;
        Task output = stdout.DrainAsync(process.StandardOutput.BaseStream, readers.Token);
        Task error = stderr.DrainAsync(process.StandardError.BaseStream, readers.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(output, error).WaitAsync(deadline.Token).ConfigureAwait(false);
            ProcessResult result = Result();
            if (result.CaptureTruncated && !allowTruncatedOutput) { throw new ProcessOutputLimitException(result); }
            return result;
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested)
        {
            bool cleanupCompleted = await StopAndObserveAsync(process, output, error, readers).ConfigureAwait(false);
            string reason = cancellationToken.IsCancellationRequested ? "Child process was canceled by its owner" : $"Child process exceeded its {timeout} deadline";
            throw new ProcessTimeoutException($"{reason}: {executable}", exception,
                Result(cleanupCompleted), cleanupCompleted);
        }

        ProcessResult Result(bool cleanupCompleted = true)
        {
            (byte[] Bytes, long Total, bool Complete) capturedOutput = stdout.Snapshot(), capturedError = stderr.Snapshot();
            bool outputTruncated = capturedOutput.Total > capturedOutput.Bytes.Length;
            bool errorTruncated = capturedError.Total > capturedError.Bytes.Length;
            return new ProcessResult(process.HasExited ? process.ExitCode : -1,
                Decode(capturedOutput.Bytes, stdoutEncoding, outputTruncated || !capturedOutput.Complete || !cleanupCompleted),
                Decode(capturedError.Bytes, stderrEncoding, errorTruncated || !capturedError.Complete || !cleanupCompleted))
            {
                ProcessId = process.Id,
                StandardOutputBytes = capturedOutput.Bytes,
                StandardErrorBytes = capturedError.Bytes,
                StandardOutputTotalBytes = capturedOutput.Total,
                StandardErrorTotalBytes = capturedError.Total,
                StandardOutputTruncated = outputTruncated,
                StandardErrorTruncated = errorTruncated,
                OutputDrained = cleanupCompleted && capturedOutput.Complete && capturedError.Complete
            };
        }
    }

    private static string Decode(byte[] bytes, Encoding encoding, bool truncated)
    {
        if (truncated)
        {
            // A head/tail boundary may split an otherwise valid encoded character.
            encoding = (Encoding)encoding.Clone();
            encoding.DecoderFallback = DecoderFallback.ReplacementFallback;
        }
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false), encoding, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static async Task<bool> StopAndObserveAsync(Process process, Task output, Task error, CancellationTokenSource readers)
    {
        using var cleanup = new CancellationTokenSource(CleanupDeadline);
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // The process can exit between the observation and the kill request.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // A failed stop remains an incomplete cleanup, never a successful run.
        }
        try
        {
            await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            await Task.WhenAll(output, error).WaitAsync(cleanup.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // A descendant can inherit pipes and outlive an already exited parent.
            // Do not wait indefinitely or claim that such descendants were stopped.
            readers.Cancel();
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception readException) when (readException is OperationCanceledException or IOException or ObjectDisposedException or TimeoutException)
            {
                // Observe reader cancellation; capture is explicitly incomplete.
            }
            return false;
        }
    }

    private sealed class BoundedCapture(int maximumBytes)
    {
        private readonly byte[] retained = new byte[maximumBytes];
        private readonly int headCapacity = maximumBytes / 2 + maximumBytes % 2;
        private readonly object gate = new();
        private int headLength, tailLength, nextTail;
        private long total;
        private bool complete;

        internal async Task DrainAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) { lock (gate) { complete = true; } return; }
                Append(buffer.AsSpan(0, read));
            }
        }

        private void Append(ReadOnlySpan<byte> value)
        {
            lock (gate)
            {
                total = checked(total + value.Length);
                int head = Math.Min(value.Length, headCapacity - headLength);
                value[..head].CopyTo(retained.AsSpan(headLength));
                headLength += head;
                value = value[head..];
                int tailCapacity = retained.Length - headCapacity;
                if (tailCapacity == 0 || value.IsEmpty) { return; }
                if (value.Length >= tailCapacity)
                {
                    value[^tailCapacity..].CopyTo(retained.AsSpan(headCapacity));
                    tailLength = tailCapacity;
                    nextTail = 0;
                    return;
                }
                int first = Math.Min(value.Length, tailCapacity - nextTail);
                value[..first].CopyTo(retained.AsSpan(headCapacity + nextTail));
                value[first..].CopyTo(retained.AsSpan(headCapacity));
                nextTail = (nextTail + value.Length) % tailCapacity;
                tailLength = Math.Min(tailCapacity, tailLength + value.Length);
            }
        }

        internal (byte[] Bytes, long Total, bool Complete) Snapshot()
        {
            lock (gate)
            {
                byte[] bytes = new byte[headLength + tailLength];
                retained.AsSpan(0, headLength).CopyTo(bytes);
                int tailCapacity = retained.Length - headCapacity;
                int start = tailLength == tailCapacity ? nextTail : 0;
                int first = Math.Min(tailLength, tailCapacity - start);
                retained.AsSpan(headCapacity + start, first).CopyTo(bytes.AsSpan(headLength));
                retained.AsSpan(headCapacity, tailLength - first).CopyTo(bytes.AsSpan(headLength + first));
                return (bytes, total, complete);
            }
        }
    }
}
