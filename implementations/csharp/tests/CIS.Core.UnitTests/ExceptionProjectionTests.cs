using System.Collections;
using System.Globalization;
using System.Text;
using CIS.Logging.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ExceptionProjectionTests
{
    [TestMethod]
    [DataRow("en-US", false)]
    [DataRow("fr-FR", false)]
    [DataRow("ar-SA", false)]
    [DataRow("en-US", true)]
    [DataRow("fr-FR", true)]
    [DataRow("ar-SA", true)]
    public void RequiredFieldsAndOptionalRealStackAreCapturedAcrossCultures(string culture, bool thrown)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Exception source = new InvalidOperationException("messageðŸ˜€\n");
            if (thrown)
            {
                source = ThrowMeasured();
            }
            CapturedException result = ExceptionProjection.Capture(source)!;
            Assert.AreEqual(typeof(InvalidOperationException).FullName, result.Type);
            Assert.AreEqual(source.Message, result.Message);
            Assert.AreEqual(source.StackTrace, result.StackTrace);
            Assert.AreEqual(thrown, result.StackTrace is not null);
            Assert.AreEqual(0, result.InnerExceptions.Count);
            Assert.AreEqual(1, result.NodeCount);
            Assert.AreEqual(1, result.Depth);
            Assert.IsFalse(result.IsTruncated);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [TestMethod]
    public void NullInputProducesNoExceptionProjection()
    {
        Assert.IsNull(ExceptionProjection.Capture(null));
    }

    [TestMethod]
    public void SingleInnerExceptionsUseAnOwnedArrayAtEveryLevel()
    {
        var source = new InvalidOperationException("outer",
            new ArgumentException("middle", new Exception("inner")));
        CapturedException result = ExceptionProjection.Capture(source)!;
        Assert.AreEqual("outer", result.Message);
        Assert.AreEqual("middle", result.InnerExceptions.Single().Message);
        Assert.AreEqual("inner", result.InnerExceptions.Single().InnerExceptions.Single().Message);
        Assert.AreEqual(3, result.NodeCount);
        Assert.AreEqual(3, result.Depth);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<CapturedException>)result.InnerExceptions).Clear());
    }

    [TestMethod]
    public void AggregateChildrenPreserveOrderAndEveryContainmentOccurrence()
    {
        var shared = new Exception("shared");
        var source = new AggregateException("outer", shared,
            new AggregateException("branch", new Exception("first"), shared), shared);
        CapturedException result = ExceptionProjection.Capture(source)!;
        Assert.AreEqual(source.Message, result.Message);
        CollectionAssert.AreEqual(new[] { "shared", source.InnerExceptions[1].Message, "shared" }, result.InnerExceptions.Select(child => child.Message).ToArray());
        CollectionAssert.AreEqual(new[] { "first", "shared" }, result.InnerExceptions[1].InnerExceptions.Select(child => child.Message).ToArray());
        Assert.AreEqual(6, result.NodeCount);
        Assert.AreEqual(3, result.Depth);
        Assert.AreNotSame(result.InnerExceptions[0], result.InnerExceptions[2]);
    }

    [TestMethod]
    public void MutableGettersAreReadOnceAndTheirStringsAreOwned()
    {
        var source = new ObservedException { Text = "before", Trace = "captured stack" };
        CapturedException result = ExceptionProjection.Capture(source)!;
        source.Text = "after";
        source.Trace = "different stack";
        Assert.AreEqual("before", result.Message);
        Assert.AreEqual("captured stack", result.StackTrace);
        Assert.AreEqual(1, source.MessageReads);
        Assert.AreEqual(1, source.StackReads);
        Assert.AreEqual(0, source.DataReads);
        Assert.AreEqual(0, source.FormattingCalls);
        Assert.IsFalse(result.HasTruncatedContent);
    }

    [TestMethod]
    [DataRow("nodes")]
    [DataRow("depth")]
    [DataRow("message")]
    [DataRow("stack")]
    public void InitialTraversalAndTextGuardsBoundTheOwnedProjection(string limit)
    {
        Exception source;
        if (limit == "nodes")
        {
            source = new AggregateException("root", Enumerable.Range(0, 17).Select(index => new Exception(index.ToString(CultureInfo.InvariantCulture))));
        }
        else if (limit == "depth")
        {
            source = new Exception("fifth");
            for (int index = 4; index > 0; index--)
            {
                source = new Exception(index.ToString(CultureInfo.InvariantCulture), source);
            }
        }
        else
        {
            source = new ObservedException
            {
                Text = limit == "message" ? string.Concat(Enumerable.Repeat("ðŸ˜€", 4097)) : "message",
                Trace = limit == "stack" ? new string('s', 16385) : null
            };
        }
        CapturedException result = ExceptionProjection.Capture(source)!;
        Assert.IsTrue(result.HasTruncatedContent);
        Assert.IsTrue(result.NodeCount <= 16 && result.Depth <= 4);
        if (limit == "nodes")
        {
            Assert.AreEqual(16, result.NodeCount);
            Assert.AreEqual("14", result.InnerExceptions.Last().Message);
        }
        else if (limit == "depth")
        {
            Assert.AreEqual(4, result.Depth);
            Assert.IsTrue(result.InnerExceptions[0].InnerExceptions[0].InnerExceptions[0].IsTruncated);
        }
        else if (limit == "message")
        {
            Assert.AreEqual(4096, result.Message.EnumerateRunes().Count());
        }
        else
        {
            Assert.AreEqual(16384, result.StackTrace!.Length);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MalformedRetainedTextRejectsWithoutEchoingCallerContent(bool stack)
    {
        const string invalid = "CIS_NO_EXCEPTION_ECHO\uD800";
        var source = new ObservedException { Text = stack ? "message" : invalid, Trace = stack ? invalid : null };
        ArgumentException failure = Assert.ThrowsExactly<ArgumentException>(() => ExceptionProjection.Capture(source));
        Assert.AreEqual("value", failure.ParamName);
        Assert.IsFalse(failure.Message.Contains("CIS_NO_EXCEPTION_ECHO", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CallerAccessorFailuresPropagateWithoutFabricatingCapturedFields(bool stack)
    {
        var failure = new NotSupportedException("accessor failure");
        var source = new ObservedException { MessageFailure = stack ? null : failure, StackFailure = stack ? failure : null };
        Assert.AreSame(failure, Assert.ThrowsExactly<NotSupportedException>(() => ExceptionProjection.Capture(source)));
    }

    private static Exception ThrowMeasured()
    {
        try
        {
            throw new InvalidOperationException("thrownðŸ˜€\n");
        }
        catch (InvalidOperationException error)
        {
            return error;
        }
    }

    private sealed class ObservedException : Exception
    {
        internal string Text { get; set; } = "message";
        internal string? Trace { get; set; }
        internal Exception? MessageFailure { get; set; }
        internal Exception? StackFailure { get; set; }
        internal int MessageReads { get; private set; }
        internal int StackReads { get; private set; }
        internal int DataReads { get; private set; }
        internal int FormattingCalls { get; private set; }
        public override string Message
        {
            get
            {
                MessageReads++;
                if (MessageFailure is { } failure)
                {
                    throw failure;
                }
                return Text;
            }
        }
        public override string? StackTrace
        {
            get
            {
                StackReads++;
                if (StackFailure is { } failure)
                {
                    throw failure;
                }
                return Trace;
            }
        }
        public override IDictionary Data
        {
            get
            {
                DataReads++;
                throw new InvalidOperationException("Data must not be read.");
            }
        }
        public override string ToString()
        {
            FormattingCalls++;
            throw new InvalidOperationException("Arbitrary formatting must not run.");
        }
        public string ArbitraryProperty => throw new InvalidOperationException("No arbitrary property access.");
    }
}
