using System.Globalization;
using System.Reflection;
using CIS.Configuration;
using CIS.Heartbeat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Heartbeat;

[TestClass]
public sealed class HeartbeatConfigurationTests
{
    private const string Key = "CIS_HEARTBEAT_MAX_CONCURRENT";

    [TestMethod]
    [DataRow(int.MinValue, 1)]
    [DataRow(0, 1)]
    [DataRow(1, 1)]
    [DataRow(7, 7)]
    [DataRow(16, 16)]
    [DataRow(17, 16)]
    [DataRow(int.MaxValue, 16)]
    public void DefaultClampsTheConfiguredCompletionPortMaximum(int ioMaximum, int expected)
        => Assert.AreEqual(expected, HeartbeatOptions.ComputeDefaultMaxConcurrent(ioMaximum));

    [TestMethod]
    public void DefaultReadsTheThreadPoolMaximumAndConstructionKeepsSetupIndependent()
    {
        ThreadPool.GetMaxThreads(out _, out int ioMaximum);
        var first = new HeartbeatOptions();
        var second = new HeartbeatOptions();
        Assert.AreEqual(Math.Min(16, Math.Max(1, ioMaximum)), first.MaxConcurrent);
        first.MaxConcurrent = 2;
        Assert.AreEqual(HeartbeatOptions.ComputeDefaultMaxConcurrent(ioMaximum), second.MaxConcurrent);
    }

    [TestMethod]
    public void NonblankEnvironmentWinsReadsOneKnownKeyAndLeavesSetupUnchanged()
    {
        var setup = new HeartbeatOptions { MaxConcurrent = 3 };
        var reads = new List<string>();
        HeartbeatSettings selected = HeartbeatOptions.Resolve(setup, key =>
        {
            reads.Add(key);
            return " +12 ";
        });
        CollectionAssert.AreEqual(new[] { Key }, reads);
        Assert.AreEqual(12, selected.MaxConcurrent);
        Assert.AreEqual(3, setup.MaxConcurrent);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" \t\r\n")]
    public void AbsentEnvironmentPreservesExplicitSetup(string? input)
        => Assert.AreEqual(9, HeartbeatOptions.Resolve(new HeartbeatOptions { MaxConcurrent = 9 }, _ => input).MaxConcurrent);

    [TestMethod]
    public void AbsentSetupAndEnvironmentSelectTheCachedDefault()
        => Assert.AreEqual(new HeartbeatOptions().MaxConcurrent, HeartbeatOptions.Resolve(null, _ => null).MaxConcurrent);

    [TestMethod]
    [DataRow("1", 1)]
    [DataRow("16", 16)]
    [DataRow("+4", 4)]
    [DataRow("0006", 6)]
    public void InclusiveEnvironmentBoundsAndInvariantSignsAreAccepted(string input, int expected)
        => Assert.AreEqual(expected, HeartbeatOptions.Resolve(new HeartbeatOptions { MaxConcurrent = 0 }, _ => input).MaxConcurrent);

    [TestMethod]
    [DataRow("1,000")]
    [DataRow("1.0")]
    [DataRow("1e1")]
    [DataRow("1_0")]
    [DataRow("+ 1")]
    [DataRow("--1")]
    [DataRow("+")]
    [DataRow("\u0661")]
    [DataRow("1\0")]
    [DataRow("\u200b1")]
    public void MalformedSelectedEnvironmentDoesNotFallBackOrExposeInput(string input)
    {
        CoreConfigurationException failure = Assert.ThrowsExactly<CoreConfigurationException>(() =>
            HeartbeatOptions.Resolve(new HeartbeatOptions { MaxConcurrent = 0 }, _ => input));
        AssertIssue(failure, "Heartbeat.InvalidValue");
        Assert.IsFalse(failure.Message.Contains(input, StringComparison.Ordinal));
        Assert.IsFalse(failure.Issues.Single().Explanation.Contains(input, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("17")]
    [DataRow("2147483648")]
    [DataRow("9999999999999999999999999999999999")]
    public void NumericEnvironmentOutsideTheEffectiveRangeFailsWithoutClamping(string input)
    {
        CoreConfigurationException failure = Assert.ThrowsExactly<CoreConfigurationException>(() =>
            HeartbeatOptions.Resolve(new HeartbeatOptions { MaxConcurrent = 4 }, _ => input));
        AssertIssue(failure, "Heartbeat.OutOfRange");
    }

    [TestMethod]
    [DataRow(int.MinValue)]
    [DataRow(0)]
    [DataRow(17)]
    [DataRow(int.MaxValue)]
    public void InvalidSetupIsRejectedWhenNoEnvironmentOverrideWasSelected(int value)
    {
        CoreConfigurationException failure = Assert.ThrowsExactly<CoreConfigurationException>(() =>
            HeartbeatOptions.Resolve(new HeartbeatOptions { MaxConcurrent = value }, _ => " \t "));
        AssertIssue(failure, "Heartbeat.OutOfRange");
    }

    [TestMethod]
    public void PublishedSettingsAreDetachedFromLaterSetupAndEnvironmentChanges()
    {
        var setup = new HeartbeatOptions { MaxConcurrent = 5 };
        string? environment = null;
        int reads = 0;
        HeartbeatSettings selected = HeartbeatOptions.Resolve(setup, _ => { reads++; return environment; });
        setup.MaxConcurrent = 1;
        environment = "16";
        Assert.AreEqual(5, selected.MaxConcurrent);
        Assert.AreEqual(1, reads);
        Assert.IsTrue(typeof(HeartbeatSettings).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).All(field => field.IsInitOnly));
    }

    [TestMethod]
    [DoNotParallelize]
    public void ParsingUsesInvariantGrammarUnderAnApplicationCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.AreEqual(8, HeartbeatOptions.Resolve(null, _ => "+8").MaxConcurrent);
            AssertIssue(Assert.ThrowsExactly<CoreConfigurationException>(() => HeartbeatOptions.Resolve(null, _ => "\u0668")),
                "Heartbeat.InvalidValue");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    public void MissingEnvironmentReaderIsRejectedBeforeResolution()
        => Assert.ThrowsExactly<ArgumentNullException>(() => HeartbeatOptions.Resolve(null, null!));

    private static void AssertIssue(CoreConfigurationException failure, string code)
    {
        Assert.AreEqual(1, failure.Issues.Count);
        Assert.AreEqual(code, failure.Issues.Single().Code);
        Assert.AreEqual(Key, failure.Issues.Single().Key);
        Assert.IsFalse(string.IsNullOrWhiteSpace(failure.Issues.Single().Explanation));
    }
}
