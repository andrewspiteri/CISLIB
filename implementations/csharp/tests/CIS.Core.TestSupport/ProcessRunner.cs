using System.Diagnostics;

namespace CIS.TestSupport;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs a bounded child process while draining its two output streams concurrently.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environment = null)
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
        Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(output, error).WaitAsync(deadline.Token).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested)
        {
            await StopAndObserveAsync(process, output, error).ConfigureAwait(false);
            throw new TimeoutException($"Child process exceeded its {timeout} deadline: {executable}", exception);
        }
    }

    private static async Task StopAndObserveAsync(Process process, Task<string> output, Task<string> error)
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
