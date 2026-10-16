using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConsoleStartupTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Where(row => !row!["physical"]!.GetValue<bool>()).Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task FailedStartupJoinsOnlyItsWorkersAndPreservesBorrowedStreams(string id)
    {
        JsonObject row = Row(Catalog(), id); ConsoleStartupExpectations.Validate(row, await ConsoleStartupFixture.ExecuteAsync(row));
    }
    [TestMethod]
    [DataRow("joined")]
    [DataRow("worker")]
    [DataRow("queue")]
    [DataRow("disposed")]
    [DataRow("flow")]
    [DataRow("inner")]
    [DataRow("message")]
    [DataRow("output")]
    [DataRow("bytes")]
    [DataRow("file")]
    public async Task StartupMatcherRejectsLeakedWorkersUnsafeErrorsAndChangedStreams(string fault)
    {
        JsonObject row = Row(Catalog(), "STARTUP-CANWRITE-THROW-BOTH-0"), actual = await ConsoleStartupFixture.ExecuteAsync(row);
        ConsoleStartupExpectations.Validate(row, actual);
        ConsoleStartupExpectations.Damage(actual, fault);
        string message = fault switch
        {
            "joined" => "Startup joinedAtReturn differs.",
            "worker" => "Startup workers differs.",
            "queue" => "Startup queues differs.",
            "disposed" => "Startup disposes differs.",
            "flow" => "Startup flowSuppressed differs.",
            "inner" or "message" or "output" => "Safe initialization error differs.",
            "bytes" => "Startup emitted data or modified retained stream bytes.",
            _ => "Failed startup attempted ordinary file I/O."
        };
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => ConsoleStartupExpectations.Validate(row, actual)); Assert.AreEqual(message, error.Message);
    }
    [TestMethod]
    public void InitializationExceptionHasOnlyOwnedReadonlyFactsAndNoPublicConstructor()
    {
        Type type = typeof(CoreLogInitializationException); Assert.IsTrue(type.IsSealed); Assert.AreEqual(typeof(Exception), type.BaseType); Assert.AreEqual(0, type.GetConstructors().Length);
        foreach (string name in new[] { "OutputName", "ErrorCode" }) { var property = type.GetProperty(name)!; Assert.AreEqual(typeof(string), property.PropertyType); Assert.IsFalse(property.CanWrite); }
    }
    [TestMethod]
    [DataRow("metadata")]
    [DataRow("format")]
    [DataRow("capacity")]
    public void BadCallerArgumentsStayArgumentErrorsBeforeAnyAcquisition(string kind)
    {
        var options = new LoggingOptions(); int selected = 0, launched = 0;
        if (kind == "format") { options.ConsoleFormat = (LogOutputFormat)999; }
        if (kind == "capacity") { options.QueueCapacity = 0; }
        Exception error = Assert.Throws<ArgumentException>(() => CoreLogRuntime.CreateConsoleQueued(kind == "metadata" ? null! : ConsoleCompositionFixture.Metadata(), options,
            acquireConsole: _ => { selected++; return new MemoryStream(); }, launchWorker: _ => { launched++; return Task.CompletedTask; }));
        Assert.IsInstanceOfType<ArgumentException>(error); Assert.IsNotInstanceOfType<CoreLogInitializationException>(error); Assert.AreEqual(0, selected); Assert.AreEqual(0, launched);
    }
    private static JsonObject Row(JsonObject catalog, string id) => catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
    private static JsonObject Catalog()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        { string path = Path.Combine(current.FullName, "conformance", "logging", "console-startup-cases.json"); if (File.Exists(path)) { return JsonNode.Parse(File.ReadAllText(path))!.AsObject(); } }
        throw new FileNotFoundException("Console startup catalog missing.");
    }
}
