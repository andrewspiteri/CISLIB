using CIS.Jobs;
using CIS.Management;
using CIS.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Management.Catalog;

[TestClass]
public sealed class ManagementCapabilityCatalogTests
{
    private const string Time = "2026-11-26T10:00:00.0000000Z";

    [TestMethod]
    public void CaptureOwnsNestedShapesOperationsAndFormCollections()
    {
        var values = new List<string> { "first", "second" };
        var members = new List<ManagementConfigurationMember>
        {
            new("Mode", true, new("string", false, AllowedValues: values))
        };
        var operations = new List<string> { "inspect", "configure" };
        var hints = new List<ManagementFormFieldHint> { new("/Mode", "enum", "Mode") };
        var config = new ManagementConfigurationContractReference("test.settings", "9007199254740993",
            new("object", false, Members: members));
        var form = new ManagementFormDescription(hints);
        var definition = new ManagementCapabilityDefinition(Key(), "Maintenance", operations,
            configurationContract: config, form: form, inputContract: new("test.input", 1),
            inputShape: new("object", false, Members: members));
        var source = new List<ManagementCapabilityDefinition> { definition };
        var catalog = new ManagementCapabilityCatalog(source);

        values.Clear();
        members.Clear();
        operations.Clear();
        hints.Clear();
        source.Clear();

        Assert.HasCount(1, catalog.Definitions);
        Assert.HasCount(2, definition.SupportedOperations);
        Assert.HasCount(1, definition.Form!.Fields);
        Assert.HasCount(2, definition.ConfigurationContract!.Shape.Members![0].Shape.AllowedValues!);
        Assert.HasCount(2, definition.InputShape!.Members![0].Shape.AllowedValues!);
        Assert.AreEqual("9007199254740993", definition.ConfigurationContract.SchemaVersion);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)definition.SupportedOperations).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ManagementConfigurationMember>)config.Shape.Members!).Clear());
    }

    [TestMethod]
    public void SameVersionIdenticalDescriptionCoalescesButConflictsNeverReplaceIt()
    {
        ManagementCapabilityDefinition first = Definition();
        ManagementCapabilityDefinition same = Definition();
        var catalog = new ManagementCapabilityCatalog([first, same, Definition(version: "2"), Definition(owner: "Other")]);
        Assert.HasCount(3, catalog.Definitions);
        Assert.AreSame(first, catalog.Find(Key()));
        Assert.IsNull(catalog.Find(Key(id: "JOB")));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementCapabilityCatalog([first, Definition(display: "Changed")]));
        Assert.AreEqual("Maintenance", catalog.Find(Key())!.DisplayName);
    }

    [TestMethod]
    public void UnknownDescriptionsRemainUnavailableAndCannotEnterTrustedCatalog()
    {
        var unknownKind = new ManagementCapabilityDefinition(new("Owner", "futureKind", "scope", "job", "1"),
            "Future", ["inspect"]);
        ManagementConfigurationContractReference config = Config(new ManagementConfigurationMember("Enabled", true, new("boolean", false)));
        var unknownEditor = new ManagementCapabilityDefinition(Key(), "Future", ["inspect"],
            configurationContract: config, form: new([new("/Enabled", "futureEditor", "Enabled")]));
        var unknownOperation = new ManagementCapabilityDefinition(Key(), "Future", ["futureOperation"]);
        foreach (ManagementCapabilityDefinition definition in new[] { unknownKind, unknownEditor, unknownOperation })
        {
            Assert.IsFalse(definition.IsSupported);
            Assert.IsFalse(definition.DeclaresOperation("inspect"));
            Assert.ThrowsExactly<ArgumentException>(() => new ManagementCapabilityCatalog([definition]));
        }
        var participation = new ManagementCapabilityParticipation(Key(), Guid.NewGuid().ToString("D"),
            "9007199254740993", Time, Time, "futureAvailability");
        Assert.IsFalse(participation.KnownAvailability.IsKnown);
        Assert.IsNull(participation.Build, "Missing historical provenance must not be inferred from a current deployment.");
    }

    [TestMethod]
    public void FormFieldsResolveDeclaredExactPathsKindsGroupsAndTrustedLookupNames()
    {
        ManagementConfigurationContractReference config = Config(new ManagementConfigurationMember("a/b", true, new("integer", false)));
        var valid = new ManagementCapabilityDefinition(Key(), "Maintenance", ["inspect"], configurationContract: config,
            form: new([new("/a~1b", "duration", "Delay", Unit: "seconds")]));
        Assert.IsTrue(valid.IsSupported);
        Assert.ThrowsExactly<ArgumentException>(() => WithForm(config, new("/missing", "number", "Missing")));
        Assert.ThrowsExactly<ArgumentException>(() => WithForm(config, new("/a~1b", "boolean", "Wrong kind")));
        Assert.ThrowsExactly<ArgumentException>(() => WithForm(config, new("/a~2b", "number", "Bad escape")));
        Assert.ThrowsExactly<ArgumentException>(() => WithForm(config, new("/a~1b", "duration", "Delay")));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementFormDescription([new("/a~1b", "number", "Value", GroupId: "missing")]));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementFormDescription([new("/a~1b", "number", "Value", LookupId: "missing")]));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementFormDescription([new("/a~1b", "https://outside.test/editor", "Value")]));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementFormDescription([], lookups:
            [new("choices", "Owner", "scope", "https://outside.test/lookup", new("choices", 1))]));
    }

    [TestMethod]
    public void PayloadContractsRequireTheirDetachedShapesAndJobSelectionCannotCreateHandlers()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementCapabilityDefinition(Key(), "Job", ["inspect"], inputContract: new("input", 1)));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementCapabilityDefinition(Key(), "Job", ["inspect"], inputShape: new("string", false)));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementCapabilityDefinition(Key(), "Job", ["createConfigured"]));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementCapabilityDefinition(
            new("Owner", "triggerProvider", "scope", "calendar", "1"), "Calendar", ["inspect"]));
        var trigger = new ManagementCapabilityDefinition(new("Owner", "triggerProvider", "scope", "calendar", "1"),
            "Calendar", ["inspect", "createConfigured"], configurationContract: Config(),
            eventContract: new("calendar.event", 1), eventShape: new("object", false, Members: []));
        Assert.IsTrue(trigger.DeclaresOperation("createConfigured"));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementCapabilityDefinition(Key(), "Job", ["inspect"],
            resultSummaryContract: new("result", 1)));
    }

    [TestMethod]
    public void ExplicitExecutionSupportIsCopiedAndRetainsOriginalOperationAlias()
    {
        var contract = new MessageContract("job.execution", 1);
        var starts = new List<MessageContract> { contract };
        var recoveries = new List<MessageContract> { contract };
        var support = new ManagementJobExecutionSupport(contract, starts, recoveries, operationName: "Maintain");
        var definition = new ManagementCapabilityDefinition(Key(), "Maintenance", ["inspect"], executionSupport: support);
        starts.Clear();
        recoveries.Clear();
        Assert.HasCount(1, definition.ExecutionSupport!.SupportedStarts);
        Assert.HasCount(1, definition.ExecutionSupport.SupportedRecoveries);
        Assert.AreEqual("Maintain", definition.ExecutionSupport.OperationName);
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementCapabilityDefinition(
            new("Owner", "ruleEngine", "scope", "rules", "1"), "Existing engine", ["inspect"], executionSupport: support));
    }

    [TestMethod]
    public void ExactUnicodeByteDepthCollectionAndCompleteMetadataBoundsAreEnforced()
    {
        _ = Key(id: new string('é', 64));
        Assert.ThrowsExactly<ArgumentException>(() => Key(id: new string('é', 65)));
        Assert.ThrowsExactly<ArgumentException>(() => Key(id: "bad\ud800"));
        Assert.ThrowsExactly<ArgumentException>(() => Key(version: "01"));
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementFormDescription(
            Enumerable.Range(0, 129).Select(index => new ManagementFormFieldHint("/F" + index, "text", "Field")).ToArray()));
        ManagementConfigurationShape deep = new("string", false);
        for (int index = 0; index < 16; index++)
        {
            deep = new("object", false, Members: [new("Child", true, deep)]);
        }
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementConfigurationContractReference("settings", "1", deep));
        var members = Enumerable.Range(0, 128).Select(index => new ManagementConfigurationMember("F" + index, true, new("string", false))).ToArray();
        var fields = members.Select(member => new ManagementFormFieldHint("/" + member.Name, "text", "Field", new string('x', 2048))).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementCapabilityDefinition(Key(), "Too large", ["inspect"],
            configurationContract: new("settings", "1", new("object", false, Members: members)), form: new(fields)));
        Assert.IsTrue(Definition().EncodedLength <= ManagementCapabilityLimits.MaximumDefinitionBytes);
    }

    [TestMethod]
    public void InspectingAnActualRegisteredJobAndRepeatedParticipationNeverConstructsItsHandler()
    {
        NeverConstructedHandler.Constructions = 0;
        var job = new JobDefinition("scope", "job", "Maintenance", "maintenance", [JobExecutionMode.Continuous], ["job.execute"]);
        var jobs = new JobCatalog([new("scope", "maintenance", "Maintenance")], [JobHandlerBinding.Continuous<NeverConstructedHandler>(job)]);
        JobDefinition inspected = jobs.Inspect().Single();
        var catalog = new ManagementCapabilityCatalog([new(Key(), inspected.DisplayName, ["inspect"], categoryId: inspected.CategoryId)]);
        var first = new ManagementCapabilityParticipation(Key(), Guid.NewGuid().ToString("D"), "1", Time, Time, "available");
        var second = new ManagementCapabilityParticipation(Key(), Guid.NewGuid().ToString("D"), "1", Time, Time, "available", draining: true);
        Assert.AreEqual(first.Key, second.Key);
        Assert.HasCount(1, catalog.Definitions);
        Assert.AreEqual(0, NeverConstructedHandler.Constructions);
    }

    private static ManagementCapabilityKey Key(string owner = "Owner", string id = "job", string version = "1")
        => new(owner, "job", "scope", id, version);

    private static ManagementCapabilityDefinition Definition(string display = "Maintenance", string version = "1", string owner = "Owner")
        => new(Key(owner: owner, version: version), display, ["inspect"]);

    private static ManagementConfigurationContractReference Config(params ManagementConfigurationMember[] members)
        => new("settings", "1", new("object", false, Members: members));

    private static ManagementCapabilityDefinition WithForm(ManagementConfigurationContractReference config, ManagementFormFieldHint field)
        => new(Key(), "Maintenance", ["inspect"], configurationContract: config, form: new([field]));

    private sealed class NeverConstructedHandler : IContinuousJobHandler
    {
        internal static int Constructions;

        public NeverConstructedHandler()
        {
            Constructions++;
            throw new InvalidOperationException("Passive inspection must not resolve handlers.");
        }

        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Passive inspection must not execute handlers.");
    }
}
