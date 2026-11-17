using System.Text.Json;
using System.Text.Json.Serialization;
using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
[DoNotParallelize]
public sealed class ConfigurationConverterBoundaryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EnumConverterAttributesAreRejectedBeforeSerializationEvenThroughANullableWrapper(bool nullable)
    {
        NumericConverter.Reset();
        string usableOutput = nullable ? JsonSerializer.Serialize<ConvertedMode?>(ConvertedMode.Ready)
            : JsonSerializer.Serialize(ConvertedMode.Ready);
        Assert.AreEqual("1", usableOutput, "The regression fixture must have a usable converter, not fail by throwing during defaults capture.");
        Assert.AreEqual(1, NumericConverter.Writes);
        NumericConverter.Reset();
        Type type = nullable ? typeof(NullableSettings) : typeof(NonnullableSettings);
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Shape", () =>
            ConfigurationCatalog.Create(new[] { type }, ConfigurationObjectTestSupport.Options()));
        Assert.AreEqual(0, NumericConverter.Writes, "Reject unsupported metadata before invoking a user converter.");
    }

    [JsonConverter(typeof(NumericConverter))]
    public enum ConvertedMode { Ready = 1 }

    public sealed class NumericConverter : JsonConverter<ConvertedMode>
    {
        private static int writes;
        public NumericConverter() { }
        internal static int Writes => Volatile.Read(ref writes);
        internal static void Reset() => Volatile.Write(ref writes, 0);
        public override ConvertedMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => (ConvertedMode)reader.GetInt32();
        public override void Write(Utf8JsonWriter writer, ConvertedMode value, JsonSerializerOptions options)
        { Interlocked.Increment(ref writes); writer.WriteNumberValue((int)value); }
    }

    private sealed class NonnullableSettings : ConfigurationObject
    {
        public NonnullableSettings() { }
        public ConvertedMode Mode { get; set; } = ConvertedMode.Ready;
    }

    private sealed class NullableSettings : ConfigurationObject
    {
        public NullableSettings() { }
        public ConvertedMode? Mode { get; set; } = ConvertedMode.Ready;
    }
}
