using System.Globalization;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProviderStateBoundaryTests
{
    [TestMethod]
    [DataRow(false, 1022)]
    [DataRow(false, 1023)]
    [DataRow(true, 1021)]
    [DataRow(true, 1022)]
    public void ExhaustedMapBudgetOmitsInvalidKeysAndPreservesTheRetainedPrefix(bool eventState, int invalidIndex)
    {
        KeyValuePair<string, object?>[] nested = Enumerable.Range(0, 1024)
            .Select(index => new KeyValuePair<string, object?>(index == invalidIndex ? InvalidKey : Key(index), index)).ToArray();

        LogValue attributes = Capture(eventState, nested);
        LogValue retained = attributes.MapProperties["nested"];
        int expectedCount = eventState ? 1021 : 1022;
        Assert.IsTrue(attributes.IsTruncated);
        Assert.IsTrue(retained.IsTruncated);
        Assert.AreEqual(1024, attributes.NodeCount);
        Assert.AreEqual(3, attributes.Depth);
        Assert.AreEqual(expectedCount, retained.MapProperties.Count);
        for (int index = 0; index < expectedCount; index++)
        {
            LogValue value = retained.MapProperties[Key(index)];
            Assert.AreEqual(LogValueKind.Integer, value.Kind);
            Assert.AreEqual((long)index, value.Scalar.Integer);
        }
        if (eventState) { Assert.AreEqual(17L, attributes.MapProperties["event_id"].Scalar.Integer); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MaximumMapDepthOmitsInvalidKeysBeforeInspectingTheirValues(bool eventState)
    {
        KeyValuePair<string, object?>[] nested = [new(InvalidKey, new object())];
        for (int index = 0; index < 6; index++) { nested = [new("child", nested)]; }

        LogValue attributes = Capture(eventState, nested);
        Assert.IsTrue(attributes.IsTruncated);
        Assert.AreEqual(8, attributes.Depth);
        Assert.AreEqual(eventState ? 9 : 8, attributes.NodeCount);
        LogValue deepest = attributes.MapProperties["nested"];
        for (int index = 0; index < 6; index++) { deepest = deepest.MapProperties["child"]; }
        Assert.IsTrue(deepest.IsTruncated);
        Assert.AreEqual(0, deepest.MapProperties.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InvalidRetainedMapKeysStillRejectCapture(bool eventState)
    {
        KeyValuePair<string, object?>[] nested = [new(InvalidKey, 1)];
        Assert.ThrowsExactly<ArgumentException>(() => Capture(eventState, nested));
    }

    private static LogValue Capture(bool eventState, KeyValuePair<string, object?>[] nested)
    {
        KeyValuePair<string, object?>[] state = [new("nested", nested)];
        return eventState ? ProviderStateCapture.Capture(state, 17).Attributes : ProviderStateCapture.CaptureScope(state).Attributes;
    }

    private static string InvalidKey => new((char)0xd800, 1);
    private static string Key(int index) => "key" + index.ToString(CultureInfo.InvariantCulture);
}
