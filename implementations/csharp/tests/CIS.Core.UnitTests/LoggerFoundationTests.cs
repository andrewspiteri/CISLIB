using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LoggerFoundationTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void SharedFoundationCasesExerciseTheActualLogger(string id)
    {
        JsonObject row = Catalog()["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        LoggerFoundationExpectations.Validate(row, LoggerFoundationFixture.Execute(row));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\n")]
    [DataRow("bad\u0000")]
    [DataRow("unpaired")]
    public void ComponentsRejectBlankControlsAndMalformedUnicode(string component)
    {
        if (component == "unpaired") { component = new string((char)0xD800, 1); }
        Assert.ThrowsExactly<ArgumentException>(() => Runtime().CreateLogger(component));
    }

    [TestMethod]
    public void ComponentBoundsUseScalarsAndPreserveSuppliedSpelling()
    {
        var output = new LoggerFoundationFixture.Output();
        CoreLogRuntime runtime = Runtime(output);
        string component = string.Concat(Enumerable.Repeat("\U0001f600", 255));
        runtime.CreateLogger(component).Write(CoreLogLevel.Information, new LogEventData { EventName = "event", Message = "" });
        Assert.ThrowsExactly<ArgumentException>(() => runtime.CreateLogger(component + "x"));
        Assert.ThrowsExactly<ArgumentNullException>(() => runtime.CreateLogger(null!));
        Assert.AreEqual(component, JsonNode.Parse(System.Text.Encoding.UTF8.GetString(output.Events[0].JsonLine.Span))!["component"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RequiredWriteArgumentPrecedesLevelAndClosure(bool lazy)
    {
        CoreLogRuntime runtime = Runtime();
        runtime.CloseAdmission();
        CoreLogger logger = runtime.CreateLogger("Component");
        ArgumentNullException error = lazy
            ? Assert.ThrowsExactly<ArgumentNullException>(() => logger.WriteLazy((CoreLogLevel)999, null!))
            : Assert.ThrowsExactly<ArgumentNullException>(() => logger.Write((CoreLogLevel)999, null!));
        Assert.AreEqual(lazy ? "factory" : "data", error.ParamName);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(7)]
    [DataRow(int.MaxValue)]
    public void AllLoggerMethodsRejectUndefinedLevelsAfterClosure(int value)
    {
        CoreLogRuntime runtime = Runtime();
        runtime.CloseAdmission();
        CoreLogger logger = runtime.CreateLogger("Component");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => logger.IsEnabled((CoreLogLevel)value));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => logger.Write((CoreLogLevel)value, new LogEventData()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => logger.WriteLazy((CoreLogLevel)value, () => throw new AssertFailedException()));
    }

    [TestMethod]
    public void NoneSkipsMalformedContentAndFactoryButDoesNotOverrideClosure()
    {
        CoreLogRuntime runtime = Runtime();
        CoreLogger logger = runtime.CreateLogger("Component");
        var data = new LogEventData { EventName = null!, Message = null! };
        Assert.IsFalse(logger.IsEnabled(CoreLogLevel.None));
        Assert.AreEqual(LogWriteOutcome.Filtered, logger.Write(CoreLogLevel.None, data).Outcome);
        Assert.AreEqual(LogWriteOutcome.Filtered, logger.WriteLazy(CoreLogLevel.None, () => throw new AssertFailedException()).Outcome);
        runtime.CloseAdmission();
        Assert.AreEqual(LogWriteOutcome.Stopped, logger.WriteLazy(CoreLogLevel.None, () => throw new AssertFailedException()).Outcome);
        Assert.AreEqual(0L, runtime.InvalidEventCount);
    }

    [TestMethod]
    public void FactoryCanCloseAdmissionFromAnotherThreadWithoutHoldingTheGate()
    {
        var output = new LoggerFoundationFixture.Output();
        CoreLogRuntime runtime = Runtime(output);
        LogWriteResult result = runtime.CreateLogger("Component").WriteLazy(CoreLogLevel.Information, () =>
        {
            Task close = Task.Run(runtime.CloseAdmission);
            Assert.IsTrue(close.Wait(TimeSpan.FromSeconds(5)), "A caller factory ran under the admission gate.");
            return new LogEventData { EventName = "event", Message = "original" };
        });
        Assert.AreEqual(LogWriteOutcome.Stopped, result.Outcome);
        Assert.AreEqual(0, output.Attempts);
        Assert.IsTrue(result.Outputs.All(item => item.Status == LogOutputWriteStatus.Stopped && item.AcceptedSequence is null));
    }

    [TestMethod]
    public void ResultCollectionsAreOwnedAndCannotChangeWithLaterAdmissions()
    {
        var output = new LoggerFoundationFixture.Output();
        CoreLogger logger = Runtime(output).CreateLogger("Component");
        var data = new LogEventData { EventName = "event", Message = "" };
        LogWriteResult first = logger.Write(CoreLogLevel.Information, data);
        LogWriteResult second = logger.Write(CoreLogLevel.Information, data);
        Assert.AreEqual(1L, first.Outputs[0].AcceptedSequence);
        Assert.AreEqual(2L, second.Outputs[0].AcceptedSequence);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<LogOutputWriteResult>)first.Outputs)[0] = second.Outputs[0]);
        var mutable = new List<LogOutputWriteResult> { first.Outputs[0] };
        var owned = new LogWriteResult(LogWriteOutcome.Accepted, mutable);
        mutable.Clear();
        Assert.AreEqual(1, owned.Outputs.Count);
    }

    [TestMethod]
    public void OwnedEncoderFailureIsNotCountedAsInvalidCallerContent()
    {
        CoreLogRuntime runtime = Runtime();
        // Native-only fault control: inject an invalid private owned cap after constructor validation.
        typeof(CoreLogRuntime).GetField("maximumEntryBytes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(runtime, 1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => runtime.CreateLogger("Component").Write(CoreLogLevel.Information, new LogEventData { EventName = "event", Message = "" }));
        Assert.AreEqual(0L, runtime.InvalidEventCount);
    }

    [TestMethod]
    [DataRow("utf8")]
    [DataRow("lf")]
    [DataRow("bom")]
    [DataRow("size")]
    public void ComparatorRejectsCorruptedOriginalBytes(string fault)
    {
        JsonObject row = Catalog()["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "LOGGER-DIRECT-INFORMATION")!.AsObject();
        JsonObject actual = LoggerFoundationFixture.Execute(row);
        LoggerFoundationExpectations.Validate(row, actual);
        byte[] bytes = Convert.FromBase64String(actual["console"]!["wireBase64"]![0]!.GetValue<string>());
        switch (fault)
        {
            case "utf8": bytes[0] = 0xFF; break;
            case "lf": bytes[^1] = (byte)' '; break;
            case "bom": bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray(); break;
            case "size": bytes = System.Text.Encoding.UTF8.GetBytes("{\"payload\":\"" + new string('x', 65536) + "\"}\n"); break;
        }
        actual["console"]!["wireBase64"]![0] = Convert.ToBase64String(bytes);
        if (fault == "utf8") { Assert.ThrowsExactly<System.Text.DecoderFallbackException>(() => LoggerFoundationExpectations.Validate(row, actual)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => LoggerFoundationExpectations.Validate(row, actual)); }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("wrong")]
    [DataRow("rejection")]
    public void ComparatorRejectsMissingWrongAndInventedRejectionTickets(string fault)
    {
        string id = fault == "rejection" ? "LOGGER-FULL" : "LOGGER-DIRECT-INFORMATION";
        JsonObject row = Catalog()["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = LoggerFoundationFixture.Execute(row);
        LoggerFoundationExpectations.Validate(row, actual);
        JsonNode output = actual["results"]![fault == "rejection" ? 1 : 0]!["outputs"]![0]!;
        output["sequence"] = fault == "missing" ? null : JsonValue.Create(fault == "wrong" ? 2L : 1L);
        Assert.ThrowsExactly<InvalidDataException>(() => LoggerFoundationExpectations.Validate(row, actual));
    }

    [TestMethod]
    public void ComparatorUsesTheDeclaredCapOutsideTruncationMode()
    {
        JsonObject row = Catalog()["cases"]!.AsArray().Single(item => (string?)item!["id"] == "LOGGER-SETTINGS")!.AsObject();
        JsonObject actual = LoggerFoundationFixture.Execute(row);
        LoggerFoundationExpectations.Validate(row, actual);
        row["input"]!["maxEntryBytes"] = 16384;
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => LoggerFoundationExpectations.Validate(row, actual));
        Assert.AreEqual("Actual byte size/framing differs.", error.Message);
    }

    [TestMethod]
    public void TruncationCanUseAnotherDeclaredCap()
    {
        JsonObject row = Catalog()["cases"]!.AsArray().Single(item => (string?)item!["input"]!["mode"] == "truncate")!.AsObject();
        row["input"]!["maxEntryBytes"] = 32768;
        JsonObject actual = LoggerFoundationFixture.Execute(row);
        Assert.IsTrue(Convert.FromBase64String(actual["console"]!["wireBase64"]![0]!.GetValue<string>()).Length > 16384);
        LoggerFoundationExpectations.Validate(row, actual);
    }
    private static CoreLogRuntime Runtime(LoggerFoundationFixture.Output? output = null) => new(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions(), output ?? new(), null);
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "logger-foundation-cases.json")))!.AsObject();
}
