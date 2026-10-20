using System.Globalization;
using CIS.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

public sealed partial class InterpolationTests
{
    [TestMethod]
    public async Task InterpolationNullGuardsPreserveParameterNames()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("Guards");
        ArgumentNullException values = Assert.ThrowsExactly<ArgumentNullException>(() =>
            logger.Write(CoreLogLevel.Information, $"value {42}", values: null!));
        ArgumentNullException context = Assert.ThrowsExactly<ArgumentNullException>(() =>
            logger.Write(CoreLogLevel.Information, $"value {42}", context: null!));
        Assert.AreEqual("values", values.ParamName); Assert.AreEqual("context", context.ParamName);
        await Drain(runtime); Assert.IsEmpty(writer.Records);
    }

    [TestMethod]
    public async Task CompleteTemplateRetainsEscapedBracesHoleIndexesAlignmentAndFormats()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("Template");
        string? empty = null;
        logger.Info($"{{order}} {42:X4}|{42,5}|{42,-5}|{empty}");
        await Drain(runtime);
        Assert.AreEqual("{{order}} {0:X4}|{1,5}|{2,-5}|{3}", writer.Records.Single().GetProperty("message_template").GetString());
        Assert.AreEqual("{order} 002A|   42|42   |", Message(writer, 0));
    }

    [TestMethod]
    public async Task CaptureAndTemplateClippingRemainStickyTogether()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("Sticky");
        var message = new LogInterpolatedStringHandler(0, 0); message.AppendLiteral(new string('{', 9000));
        Assert.IsTrue(logger.Info(ref message).IsTruncated);
        await Drain(runtime);
        Assert.AreEqual(new string('{', 8192), Message(writer, 0));
        Assert.IsTrue(writer.Records.Single().GetProperty("truncated").GetBoolean());
    }

    [TestMethod]
    public async Task CombinedOwnedTextBudgetAndRepeatedClippingAreVisibleAtPublication()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("Combined");
        string first = new('a', 5000), second = new('b', 5000), third = new('c', 5000);
        Assert.IsTrue(logger.Info($"{first}{second}{third}").IsTruncated);
        await Drain(runtime);
        Assert.AreEqual(first + new string('b', 3192), Message(writer, 0));
        Assert.IsTrue(writer.Records.Single().GetProperty("truncated").GetBoolean());
    }

    [TestMethod]
    [DataRow("empty", false)]
    [DataRow("null", false)]
    [DataRow("value", true)]
    [DataRow("padding", true)]
    [DataRow("omitted", true)]
    public async Task ExactRenderBudgetDistinguishesHarmlessAndLostTails(string tail, bool truncated)
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("Tails");
        var message = new LogInterpolatedStringHandler(0, 2); message.AppendFormatted(new string('x', 8192));
        switch (tail)
        {
            case "empty": message.AppendFormatted(string.Empty); break;
            case "null": message.AppendFormatted((string?)null); break;
            case "value": message.AppendFormatted(7); break;
            case "padding": message.AppendFormatted(string.Empty, 1); break;
            case "omitted": message.AppendFormatted(42, "D8193"); break;
        }
        Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info(ref message).Outcome);
        await Drain(runtime);
        Assert.AreEqual(new string('x', 8192), Message(writer, 0));
        Assert.AreEqual(truncated, writer.Records.Single().TryGetProperty("truncated", out var flag) && flag.GetBoolean());
    }

    [TestMethod]
    public async Task OmittedAlignedValueContributesOneHoleAndNoPadding()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("Omitted");
        var message = new LogInterpolatedStringHandler(0, 1); message.AppendFormatted(42, 5, "D8193"); message.AppendLiteral("tail");
        Assert.IsTrue(logger.Info(ref message).IsTruncated);
        await Drain(runtime);
        Assert.AreEqual("tail", Message(writer, 0));
        Assert.AreEqual("{0,5}tail", writer.Records.Single().GetProperty("message_template").GetString());
    }

    [TestMethod]
    public async Task LeftPaddingCannotEraseAnEarlierOmissionFlag()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("Padding");
        var message = new LogInterpolatedStringHandler(0, 2); message.AppendFormatted(42, "D8193"); message.AppendFormatted(7, -10000);
        Assert.IsTrue(logger.Info(ref message).IsTruncated);
        await Drain(runtime);
        Assert.AreEqual("7" + new string(' ', 8191), Message(writer, 0));
        Assert.IsTrue(writer.Records.Single().GetProperty("truncated").GetBoolean());
    }

    [TestMethod]
    [DataRow(1023)]
    [DataRow(1024)]
    [DataRow(1025)]
    public async Task PartLimitIsInclusiveAndTheFirstExcessPartIsOmitted(int count)
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("Parts");
        var message = new LogInterpolatedStringHandler(0, count);
        for (int index = 0; index < count; index++) { message.AppendFormatted(1); }
        Assert.AreEqual(count > 1024, logger.Info(ref message).IsTruncated);
        await Drain(runtime);
        Assert.AreEqual(new string('1', Math.Min(count, 1024)), Message(writer, 0));
    }

    [TestMethod]
    public async Task LiteralAfterThePartLimitCannotExtendThePublishedMessage()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("LiteralLimit");
        var message = new LogInterpolatedStringHandler(0, 1024);
        for (int index = 0; index < 1024; index++) { message.AppendFormatted(1); }
        message.AppendLiteral("unretained");
        Assert.IsTrue(logger.Info(ref message).IsTruncated);
        await Drain(runtime); Assert.AreEqual(new string('1', 1024), Message(writer, 0));
    }

    [TestMethod]
    [DataRow(255)]
    [DataRow(256)]
    [DataRow(257)]
    public async Task FormatCharacterLimitPreservesTheInclusiveBoundary(int length)
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("FormatLimit");
        string format = new('0', length); var message = new LogInterpolatedStringHandler(0, 1); message.AppendFormatted(42, format);
        Assert.AreEqual(length > 256, logger.Info(ref message).IsTruncated);
        await Drain(runtime);
        Assert.AreEqual(length <= 256 ? new string('0', length - 2) + "42" : string.Empty, Message(writer, 0));
        Assert.AreEqual(length <= 256 ? "{0:" + format + "}" : "{0}", writer.Records.Single().GetProperty("message_template").GetString());
    }

    [TestMethod]
    [DataRow("short", 4096)]
    [DataRow("short", 8192)]
    [DataRow("short", 8193)]
    [DataRow("ushort", 8192)]
    [DataRow("ushort", 8193)]
    [DataRow("int", 8192)]
    [DataRow("int", 8193)]
    public async Task NumericPrecisionUsesTheFullSupportedBufferAndInclusiveLimit(string type, int precision)
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("Precision");
        object value = type switch { "short" => (object)(short)42, "ushort" => (ushort)42, _ => 42 };
        var message = new LogInterpolatedStringHandler(0, 1); message.AppendFormatted(value, "D" + precision.ToString(CultureInfo.InvariantCulture));
        Assert.AreEqual(precision > 8192, logger.Info(ref message).IsTruncated);
        await Drain(runtime);
        Assert.AreEqual(precision <= 8192 ? new string('0', precision - 2) + "42" : string.Empty, Message(writer, 0));
        Assert.AreEqual(precision > 8192, writer.Records.Single().TryGetProperty("truncated", out var flag) && flag.GetBoolean());
    }

    [TestMethod]
    [DataRow("a#", "a42")]
    [DataRow("a9999#", "a999942")]
    [DataRow("D", "42")]
    [DataRow("X", "2A")]
    public async Task CustomAndSingleLetterNumericFormatsKeepNativeSemantics(string format, string expected)
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("NativeFormats");
        var message = new LogInterpolatedStringHandler(0, 1); message.AppendFormatted(42, format);
        Assert.IsFalse(logger.Info(ref message).IsTruncated);
        await Drain(runtime); Assert.AreEqual(expected, Message(writer, 0));
    }

    [TestMethod]
    public async Task NullReturningUserFormattersContributeEmptyOwnedText()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("NullText");
        logger.Info($"a{new NullTextValue()}b{new NullFormattedValue()}c");
        await Drain(runtime); Assert.AreEqual("abc", Message(writer, 0));
    }

    [TestMethod]
    [DataRow(8191)]
    [DataRow(8192)]
    [DataRow(8193)]
    public async Task SpanBudgetCountsUnicodeScalarsAndExposesCaptureClipping(int scalars)
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("SpanLimit");
        string prefix = new string('x', 8190) + "😀";
        string text = prefix + new string('y', scalars - 8191);
        Assert.AreEqual(scalars > 8192, PublishSpan(logger, text).IsTruncated);
        await Drain(runtime);
        Assert.AreEqual(prefix + new string('y', Math.Min(scalars - 8191, 1)), Message(writer, 0));
        Assert.AreEqual(scalars > 8192, writer.Records.Single().TryGetProperty("truncated", out var flag) && flag.GetBoolean());
    }

    [TestMethod]
    public async Task MultipleOversizedSpansDoNotToggleAwayTheTruncationFlag()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("SpanSticky");
        string text = new('x', 8193); var message = new LogInterpolatedStringHandler(0, 2);
        message.AppendFormatted(text.AsSpan()); message.AppendFormatted(text.AsSpan());
        Assert.IsTrue(logger.Info(ref message).IsTruncated);
        await Drain(runtime); Assert.AreEqual(new string('x', 8192), Message(writer, 0));
        Assert.IsTrue(writer.Records.Single().GetProperty("truncated").GetBoolean());
    }

    [TestMethod]
    public async Task AClippedOwnedFormatOmitsItsValueRatherThanChangingItsFormatting()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("OwnedFormat");
        var message = new LogInterpolatedStringHandler(0, 1); message.AppendFormatted(new string('x', 8191)); message.AppendFormatted(42, "D4");
        Assert.IsTrue(logger.Info(ref message).IsTruncated);
        await Drain(runtime); Assert.AreEqual(new string('x', 8191), Message(writer, 0));
    }

    [TestMethod]
    public void SuppliedSpanLengthDoesNotDetermineCaptureAllocation()
    {
        string text = new('x', 65536);
        var warmup = new LogInterpolatedStringHandler(0, 1); warmup.AppendFormatted("x".AsSpan());
        long before = GC.GetAllocatedBytesForCurrentThread();
        var message = new LogInterpolatedStringHandler(0, 1); message.AppendFormatted(text.AsSpan());
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsLessThan(131072L, allocated, "Capturing a large borrowed span must allocate only a bounded owned prefix.");
        Assert.IsTrue(message.Freeze().IsTruncated);
    }

    [TestMethod]
    public void ExhaustedPartBudgetDoesNotCopyAnIgnoredSpan()
    {
        string text = new('x', 65536); var message = new LogInterpolatedStringHandler(0, 1024);
        for (int index = 0; index < 1024; index++) { message.AppendFormatted(1); }
        message.AppendFormatted("x".AsSpan());
        long before = GC.GetAllocatedBytesForCurrentThread();
        message.AppendFormatted(text.AsSpan());
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsLessThan(8192L, allocated, "An exhausted part budget must not copy another supplied span.");
        Assert.IsTrue(message.Freeze().IsTruncated);
    }

    [TestMethod]
    public async Task FullRenderBudgetSkipsLaterNativeFormatting()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("RenderStop");
        var message = new LogInterpolatedStringHandler(0, 2); message.AppendFormatted(42, "D8192"); message.AppendFormatted(42, "Q");
        Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info(ref message).Outcome);
        await Drain(runtime);
        Assert.AreEqual(new string('0', 8190) + "42", Message(writer, 0));
        Assert.IsTrue(writer.Records.Single().GetProperty("truncated").GetBoolean());
        Assert.AreEqual(0L, runtime.GetDiagnostics().Incoming!.Failed);
    }

    private static LogWriteResult PublishSpan(CoreLogger logger, string text) => logger.Info($"{text.AsSpan()}");
    private sealed class NullTextValue { public override string ToString() => null!; }
    private sealed class NullFormattedValue : IFormattable { public string ToString(string? format, IFormatProvider? provider) => null!; }
}
