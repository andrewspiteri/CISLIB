using System.Diagnostics;
using System.Text;

namespace CIS.TestSupport;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public int ProcessId { get; init; }
    public ReadOnlyMemory<byte> StandardOutputBytes { get; init; }
    public ReadOnlyMemory<byte> StandardErrorBytes { get; init; }
}

/// <summary>Runs a bounded child process while draining its two output streams concurrently.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environment = null,
        Encoding? outputEncoding = null)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A positive finite timeout is required.");
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
        using var deadline = new CancellationTokenSource(timeout);
        process.Start();
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
        Encoding stdoutEncoding = process.StandardOutput.CurrentEncoding, stderrEncoding = process.StandardError.CurrentEncoding;
        Task output = process.StandardOutput.BaseStream.CopyToAsync(stdout, deadline.Token);
        Task error = process.StandardError.BaseStream.CopyToAsync(stderr, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(output, error).WaitAsync(deadline.Token).ConfigureAwait(false);
            byte[] stdoutBytes = stdout.ToArray(), stderrBytes = stderr.ToArray();
            return new ProcessResult(process.ExitCode, Decode(stdoutBytes, stdoutEncoding), Decode(stderrBytes, stderrEncoding))
            {
                ProcessId = process.Id,
                StandardOutputBytes = stdoutBytes,
                StandardErrorBytes = stderrBytes
            };
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested)
        {
            await StopAndObserveAsync(process, output, error).ConfigureAwait(false);
            throw new TimeoutException($"Child process exceeded its {timeout} deadline: {executable}", exception);
        }
    }

    private static string Decode(byte[] bytes, Encoding encoding)
    {
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false), encoding, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static async Task StopAndObserveAsync(Process process, Task output, Task error)
    {
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

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        try
        {
            // Both readers received the original deadline token; observe their finite completion.
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected after the original deadline expires.
        }
    }
}
