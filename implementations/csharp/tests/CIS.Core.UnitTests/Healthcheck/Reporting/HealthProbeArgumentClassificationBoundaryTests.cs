using CIS.Healthcheck.Pipes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthProbeArgumentClassificationBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow("--timeout-ms", "49", false)]
    [DataRow("--timeout-ms", "50", true)]
    [DataRow("--timeout-ms", "51", true)]
    [DataRow("--timeout-ms", "59999", true)]
    [DataRow("--timeout-ms", "60000", true)]
    [DataRow("--timeout-ms", "60001", false)]
    [DataRow("--max-report-bytes", "1023", false)]
    [DataRow("--max-report-bytes", "1024", true)]
    [DataRow("--max-report-bytes", "1025", true)]
    [DataRow("--max-report-bytes", "16777215", true)]
    [DataRow("--max-report-bytes", "16777216", true)]
    [DataRow("--max-report-bytes", "16777217", false)]
    public async Task PreCanceledCallerStillDistinguishesInclusiveNumericLimits(string option, string value, bool valid)
    {
        string pipe = "hc-arguments-" + Guid.NewGuid().ToString("N");
        Observation observed = await ObserveAsync(["--pipe", pipe, option, value]);

        Assert.IsNull(observed.UnexpectedIndexFault, "Caller input must be classified without escaping the documented probe error exit.");
        Assert.AreEqual(2, observed.Exit);
        Assert.AreEqual(0L, observed.OutputBytes);
        Assert.IsTrue(observed.Error.Length is > 0 and <= 1024);
        StringAssert.Contains(observed.Error, valid ? "TimeoutOrCanceled" : "InvalidArguments");
        Assert.IsFalse(observed.Error.Contains(valid ? "InvalidArguments" : "TimeoutOrCanceled", StringComparison.Ordinal));
        Assert.IsTrue(observed.CallerStreamsRemainOpen);
    }

    [TestMethod]
    [DataRow("missing-pipe")]
    [DataRow("missing-value")]
    [DataRow("trailing-option")]
    [DataRow("unknown-option")]
    [DataRow("duplicate-pipe")]
    [DataRow("duplicate-view")]
    [DataRow("duplicate-timeout")]
    [DataRow("duplicate-limit")]
    [DataRow("invalid-view")]
    [DataRow("invalid-pipe")]
    public async Task InvalidArgumentsTakePrecedenceOverAnAlreadyCanceledTransport(string scenario)
    {
        string pipe = "hc-arguments-" + Guid.NewGuid().ToString("N");
        string[] arguments = scenario switch
        {
            "missing-pipe" => [],
            "missing-value" => ["--pipe"],
            "trailing-option" => ["--pipe", pipe, "--view"],
            "unknown-option" => ["--pipe", pipe, "--unknown", "health"],
            "duplicate-pipe" => ["--pipe", pipe, "--pipe", pipe],
            "duplicate-view" => ["--pipe", pipe, "--view", "health", "--view", "health"],
            "duplicate-timeout" => ["--pipe", pipe, "--timeout-ms", "1000", "--timeout-ms", "1000"],
            "duplicate-limit" => ["--pipe", pipe, "--max-report-bytes", "1024", "--max-report-bytes", "1024"],
            "invalid-view" => ["--pipe", pipe, "--view", "Health"],
            "invalid-pipe" => ["--pipe", "invalid/name"],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        Observation observed = await ObserveAsync(arguments);

        Assert.IsNull(observed.UnexpectedIndexFault, "An incomplete argument pair must be rejected before indexing its missing value.");
        Assert.AreEqual(2, observed.Exit);
        Assert.AreEqual(0L, observed.OutputBytes);
        Assert.IsTrue(observed.Error.Length is > 0 and <= 1024);
        StringAssert.Contains(observed.Error, "InvalidArguments");
        Assert.IsFalse(observed.Error.Contains("TimeoutOrCanceled", StringComparison.Ordinal));
        Assert.IsTrue(observed.CallerStreamsRemainOpen);
    }

    private static async Task<Observation> ObserveAsync(string[] arguments)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new MemoryStream();
        using var error = new StringWriter();
        int? exit = null;
        string? unexpectedIndexFault = null;
        try
        {
            // The already-canceled caller avoids endpoint/timer scheduling as
            // the oracle. Valid arguments reach transport cancellation; invalid
            // arguments must be rejected before any transport work is admitted.
            exit = await HealthProbeClient.RunAsync(arguments, output, error, cancellation.Token).WaitAsync(Deadline);
        }
        catch (IndexOutOfRangeException failure)
        {
            // Report the actual missing-value fault as caller-visible behavior.
            // Deadline, cancellation and cleanup exceptions are not converted
            // into this observation or accepted as assertion kill evidence.
            unexpectedIndexFault = failure.GetType().FullName;
        }
        long outputBytes = output.Length;
        string diagnostic = error.ToString();
        output.WriteByte(1);
        error.Write("still-owned-by-caller");
        return new(exit, outputBytes, diagnostic, unexpectedIndexFault, output.Length == outputBytes + 1 && error.ToString() == diagnostic + "still-owned-by-caller");
    }

    private sealed record Observation(int? Exit, long OutputBytes, string Error, string? UnexpectedIndexFault, bool CallerStreamsRemainOpen);
}
