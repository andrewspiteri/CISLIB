using CIS.Workflows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class StateMachineInstrumentationTests
{
    [TestMethod]
    public void FullEnumDomainAdmissionIsTransactionalAndRepeatedDefinitionsReuseItsUnion()
    {
        var instrumentation = new StateMachineInstrumentation();
        instrumentation.ValidateDefinition(Metadata("first", 507));
        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.ValidateDefinition(Metadata("second", 6)));
        instrumentation.ValidateDefinition(Metadata("second", 5));
        instrumentation.ValidateDefinition(Metadata("second", 5));
        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.ValidateDefinition(Metadata("second", 6)));
    }

    [TestMethod]
    public void DifferentDefinitionsUnderTheSameAliasAdmitOnlyDistinctLabelTuples()
    {
        var instrumentation = new StateMachineInstrumentation();
        var first = new StateMachineDefinitionMetadata("same", ["Draft"], ["Submit", "invalid"], []);
        instrumentation.ValidateDefinition(first);
        instrumentation.ValidateDefinition(first);
        instrumentation.ValidateDefinition(first with
        {
            Triggers = ["Approve", "invalid"]
        });
        instrumentation.ValidateDefinition(Metadata("rest", 509));
        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.ValidateDefinition(Metadata("new", 1)));
    }

    [TestMethod]
    public void SixteenMachineNamesRemainBoundedWithoutAnyCollector()
    {
        var instrumentation = new StateMachineInstrumentation();
        for (int index = 0; index < 16; index++)
        {
            instrumentation.ValidateDefinition(Metadata("machine" + index, 1));
        }

        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.ValidateDefinition(Metadata("seventeenth", 1)));
        instrumentation.ValidateDefinition(Metadata("machine0", 1));
    }

    [TestMethod]
    public void HandlerPairsAreBoundedAndRejectedAdmissionPublishesNoneOfItsMetadata()
    {
        var instrumentation = new StateMachineInstrumentation();
        StateMachineHandlerRegistration[] handlers = Enumerable.Range(0, 64)
            .Select(index => new StateMachineHandlerRegistration("Display" + index, "alias" + index, typeof(object))).ToArray();
        instrumentation.ValidateDefinition(Metadata("first", 1) with
        {
            Handlers = handlers
        });
        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.ValidateDefinition(Metadata("second", 1) with
        {
            Handlers = [new("Display", "handler", typeof(object))],
        }));
        instrumentation.ValidateDefinition(Metadata("second", 1));
        instrumentation.ValidateDefinition(Metadata("first", 1) with
        {
            Handlers = handlers
        });
    }

    [TestMethod]
    public void DefaultHandlerAliasesAggregateWhileClrDisplayNamesNeverConsumeNewPairs()
    {
        var instrumentation = new StateMachineInstrumentation();
        instrumentation.ValidateDefinition(Metadata("first", 1) with
        {
            Handlers = Enumerable.Range(0, 100).Select(index => new StateMachineHandlerRegistration(
                "ArbitraryDisplayType" + index, "handler", typeof(object))).ToArray(),
        });
        instrumentation.ValidateDefinition(Metadata("second", 1) with
        {
            Handlers = Enumerable.Range(0, 63).Select(index => new StateMachineHandlerRegistration(
                "Display", "alias" + index, typeof(object))).ToArray(),
        });
    }

    [TestMethod]
    [DataRow("bad\nname")]
    [DataRow("bad\u200bname")]
    [DataRow("")]
    public void InvalidLabelsCannotBePublished(string label)
    {
        var instrumentation = new StateMachineInstrumentation();
        Assert.ThrowsExactly<ArgumentException>(() => instrumentation.ValidateDefinition(Metadata("valid", 1) with { States = [label] }));
        Assert.ThrowsExactly<ArgumentException>(() => instrumentation.ValidateDefinition(Metadata("valid", 1) with { Triggers = [label, "invalid"] }));
        instrumentation.ValidateDefinition(Metadata("valid", 1));
    }

    [TestMethod]
    public void Utf8BoundsAndFixedInvalidCategoryAreValidatedWithoutTruncation()
    {
        var instrumentation = new StateMachineInstrumentation();
        string boundary = new('\u00e9', 128);
        instrumentation.ValidateDefinition(new(boundary, [boundary], ["enum:invalid", "invalid"], []));
        Assert.ThrowsExactly<ArgumentException>(() => instrumentation.ValidateDefinition(new("valid", [boundary + "x"], ["invalid"], [])));
        Assert.ThrowsExactly<ArgumentException>(() => instrumentation.ValidateDefinition(new("valid", ["Draft"], ["Submit"], [])));
    }

    private static StateMachineDefinitionMetadata Metadata(string name, int triggerCount) => new(name, ["Draft"],
        Enumerable.Range(0, triggerCount - 1).Select(index => "Trigger" + index).Append("invalid").ToArray(), []);
}
