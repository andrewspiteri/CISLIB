using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class ConsoleHumanPackageTests
{
    private static readonly Lazy<Task<ConsoleHumanPackageFixture>> Prepared = new(ConsoleHumanPackageFixture.CreateAsync);
    public TestContext TestContext { get; set; } = null!;

    [ClassCleanup]
    public static void Cleanup(TestContext context)
    {
        if (!Prepared.IsValueCreated || !Prepared.Value.IsCompletedSuccessfully) { return; }
        var fixture = Prepared.Value.Result;
        try { context.WriteLine("CIS-CONSOLE-HUMAN-PACKAGE " + fixture.Summary); }
        finally { fixture.Dispose(); }
    }

    [TestMethod]
    public async Task ActualExampleBuildAndFolderPublishObserveContextAndCompletedShutdown()
    {
        var fixture = await Prepared.Value;
        await CheckNormalAsync(fixture, fixture.BuildOutput);
        await CheckNormalAsync(fixture, await fixture.PublishAsync());
    }

    private static async Task CheckNormalAsync(ConsoleHumanPackageFixture fixture, string output)
    {
        var run = await fixture.RunAsync(output);
        Assert.AreEqual(0, run.Process.ExitCode, run.Process.StandardOutput + run.Process.StandardError);
        string stdout = run.Process.StandardOutput, human = run.Process.StandardError;
        string[] admissions = stdout.Split('\n').Where(line => line.StartsWith("ADMISSION ", StringComparison.Ordinal)).ToArray();
        Assert.HasCount(2, admissions);
        foreach (string line in admissions)
        { StringAssert.Contains(line, "outcome=Accepted"); StringAssert.Contains(line, "incoming_sequence="); StringAssert.Contains(line, "truncated=False"); }
        string flush = "FLUSH completion=Completed succeeded=True loss_observed=False";
        string stop = "STOP completion=Completed succeeded=True loss_observed=False";
        StringAssert.Contains(stdout, "LOGGING max_entry_bytes=65536");
        StringAssert.Contains(stdout, flush);
        StringAssert.Contains(stdout, stop);
        StringAssert.Contains(stdout, "RUNTIME_COMPLETED");
        Assert.IsTrue(stdout.IndexOf(flush, StringComparison.Ordinal) < stdout.IndexOf(stop, StringComparison.Ordinal));
        Assert.IsTrue(stdout.IndexOf(stop, StringComparison.Ordinal) < stdout.IndexOf("RUNTIME_COMPLETED", StringComparison.Ordinal));
        foreach (string value in new[] { "order.started", "order.completed", "order-1042", "16.25", "quote-order-1042" })
        { StringAssert.Contains(human, value); }
        Assert.IsFalse(human.Contains('\u001b'));
        Assert.IsFalse(human.TrimStart().StartsWith('{'));

        JsonObject[] records = File.ReadAllLines(run.LogPath).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        CollectionAssert.AreEqual(new[] { "order.started", "order.completed" }, records.Select(item => (string?)item["event_name"]).ToArray());
        foreach (JsonObject item in records)
        {
            Assert.AreEqual("Orders", (string?)item["component"]);
            Assert.AreEqual("quote-order-1042", (string?)item["operation_id"]);
            Assert.AreEqual("ConsoleHuman.Package", (string?)item["service"]);
            Assert.AreEqual("integration", (string?)item["env"]);
            Assert.AreEqual("1.2.3", (string?)item["version"]);
            Assert.AreEqual("request-1042", (string?)item["attributes"]!["request_id"]);
            Assert.AreEqual("console", (string?)item["attributes"]!["channel"]);
        }
        Assert.AreEqual("order-1042", (string?)records[1]["attributes"]!["order_id"]);
        Assert.AreEqual(2, records[1]["attributes"]!["item_count"]!.GetValue<int>());
        Assert.AreEqual(3, records[1]["attributes"]!["unit_count"]!.GetValue<int>());
        Assert.AreEqual("16.25", (string?)records[1]["attributes"]!["total"]);
        using var lease = new FileStream(run.LogPath + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.IsTrue(lease.CanWrite, "The completed child released its file-group lease.");
    }

    [TestMethod]
    public async Task ActualExampleReportsStartupFailureWithoutClaimingAdmissionOrCompletion()
    {
        var fixture = await Prepared.Value;
        var run = await fixture.RunAsync(fixture.BuildOutput, new Dictionary<string, string?> { ["CORE_LOG_CONSOLE_FORMAT"] = "invalid-format" });
        Assert.AreEqual(1, run.Process.ExitCode);
        StringAssert.Contains(run.Process.StandardError, "STARTUP_FAILED");
        StringAssert.Contains(run.Process.StandardError, "CORE_LOG_CONSOLE_FORMAT");
        foreach (string marker in new[] { "ADMISSION ", "FLUSH ", "STOP ", "RUNTIME_COMPLETED" })
        { Assert.IsFalse(run.Process.StandardOutput.Contains(marker, StringComparison.Ordinal)); }
        Assert.IsFalse(File.Exists(run.LogPath));
    }
}
