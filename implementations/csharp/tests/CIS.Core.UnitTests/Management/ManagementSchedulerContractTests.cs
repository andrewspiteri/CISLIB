using System.Text.Json;
using CIS.Management;
using CIS.Messaging;
using CIS.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management;

[TestClass]
public sealed class ManagementSchedulerContractTests
{
    [TestMethod]
    public void FrameworkSnapshotPreservesSubMillisecondValuesWithoutConsultingLaterDefaults()
    {
        var original = new JobExecutionSettings(JobExecutionMode.Scheduled, TimeSpan.FromTicks(7),
            TimeSpan.FromTicks(19), TimeSpan.FromTicks(29), TimeSpan.FromTicks(31));
        ManagementSchedulerExecutionSettings captured = ManagementSchedulerExecutionSettings.FromRuntime(original);
        JobExecutionSettings detached = captured.ToRuntime();
        Assert.AreEqual(original.IdleDelay, detached.IdleDelay);
        Assert.AreEqual(original.Timeout, detached.Timeout);
        Assert.AreEqual(original.MaximumDrain, detached.MaximumDrain);
        Assert.AreEqual(original.AcquisitionRetryDelay, detached.AcquisitionRetryDelay);
        Assert.AreEqual(JobScheduledActivation.ExternalOccurrences, detached.ScheduledActivation);
        Assert.IsNull(detached.Schedule, "The trigger owns recurrence; a source capsule cannot invent another evaluator.");
        Assert.Throws<ArgumentException>(() => (captured with { IdleDelayTicks = "07" }).ToRuntime());
    }

    [TestMethod]
    public void RetainedTaskAndWholePreviewDoNotFollowLaterCallerCollectionEdits()
    {
        var mappings = new List<ManagementSchedulerInputMapping>
        {
            new("/partition", "constant", ConstantJson: "\"Europe\"")
        };
        var settings = Selection();
        var draft = new ManagementSchedulerTaskDraft("Daily check", new(App, Trigger),
            new("Demo.CountryData", "job", "countries", "customer.dormancy", "1"),
            new("constant", "Europe"), mappings, settings);
        var issues = new List<ManagementSchedulerValidationIssue> { new("/input", "unsupported", "Unsupported input") };
        var affected = new List<ManagementSchedulerAffectedTask> { new(App, TaskId, "Daily check", "1") };
        var preconditions = new List<ManagementSchedulerSettingsSelection> { settings };
        var preview = new ManagementSchedulerTaskPreview(false, issues, affected, true, [], "pending",
            Preconditions: preconditions, Definition: draft);

        mappings.Clear();
        issues.Clear();
        affected.Clear();
        preconditions.Clear();

        Assert.HasCount(1, draft.InputMappings);
        Assert.HasCount(1, preview.Issues);
        Assert.HasCount(1, preview.AffectedTasks);
        Assert.HasCount(1, preview.Preconditions!);
        Assert.Throws<NotSupportedException>(() => ((IList<ManagementSchedulerInputMapping>)draft.InputMappings).Clear());
    }

    [TestMethod]
    public void TaskCannotRetainAnOversizedMappingAndPreviewCannotRetainAnOversizedImpact()
    {
        var mapping = new ManagementSchedulerInputMapping("/partition", "constant", ConstantJson: "null");
        Assert.Throws<ArgumentException>(() => new ManagementSchedulerTaskDraft("Daily check", new(App, Trigger),
            new("Demo.CountryData", "job", "countries", "customer.dormancy", "1"),
            new("constant", "Europe"), Enumerable.Repeat(mapping, 65).ToArray(), Selection()));
        Assert.Throws<ArgumentException>(() => new ManagementSchedulerTaskPreview(true, [],
            Enumerable.Repeat(new ManagementSchedulerAffectedTask(App, TaskId, "Daily check", "1"), 4097).ToArray(),
            true, [], "pending"));
    }

    [TestMethod]
    public void RuntimeCompatibilityCopiesExactContractsAndDoesNotInferNewerVersions()
    {
        var old = new MessageContract("customer.execution", 2);
        var current = new MessageContract("customer.execution", 3);
        var starts = new List<MessageContract> { current };
        var recoveries = new List<MessageContract> { old };
        var support = new ManagementJobExecutionSupport(current, starts, recoveries, operationName: "customer.dormancy");
        starts.Add(old);
        recoveries.Clear();

        Assert.IsFalse(support.SupportedStarts.Contains(old));
        Assert.IsTrue(support.SupportedRecoveries.Contains(old));
        Assert.IsFalse(support.SupportedRecoveries.Contains(current));
        Assert.Throws<ArgumentException>(() => new ManagementJobExecutionSupport(current, [old], []));
        Assert.Throws<ArgumentException>(() => new ManagementWorkerBuildReference("1.0", "build\nforged", "handler"));
    }

    [TestMethod]
    public void JsonConstructionRetainsFrozenMappingsAndFutureStatesAreNotKnownSuccess()
    {
        var original = new ManagementSchedulerTaskDraft("Daily check", new(App, Trigger),
            new("Demo.CountryData", "job", "countries", "customer.dormancy", "1"),
            new("constant", "Europe"), [new("/partition", "constant", ConstantJson: "false")], Selection());
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ManagementSchedulerTaskDraft? roundTrip = JsonSerializer.Deserialize<ManagementSchedulerTaskDraft>(
            JsonSerializer.Serialize(original, options), options);
        Assert.IsNotNull(roundTrip);
        Assert.AreEqual("false", roundTrip.InputMappings[0].ConstantJson);
        var task = new ManagementSchedulerTask(App, TaskId, "1", null, "\"one\"", roundTrip,
            "future-success", new("human", "issuer", "operator"), "2026-11-26T00:00:00.0000000Z");
        Assert.IsFalse(task.KnownState.IsKnown);
    }

    private const string App = "11111111-1111-1111-1111-111111111111";
    private const string Trigger = "22222222-2222-2222-2222-222222222222";
    private const string TaskId = "33333333-3333-3333-3333-333333333333";

    private static ManagementSchedulerSettingsSelection Selection()
        => new(App, "customer.settings", "1", "44444444-4444-4444-4444-444444444444", "1");
}

