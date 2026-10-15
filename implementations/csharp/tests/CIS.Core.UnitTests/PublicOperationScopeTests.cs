using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class PublicOperationScopeTests
{
    [TestMethod]
    public async Task PublicScopesCopyInputsRejectBadLifoAndRestoreAcrossAwait()
    {
        using var bytes = new MemoryStream();
        await using CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.Information, FileLevel = CoreLogLevel.None }, item => bytes.WriteAsync(item.Event.JsonLine), fileWrite: null, consoleFlush: () => ValueTask.CompletedTask);
        CoreLogger logger = runtime.CreateLogger("PublicScopes");
        var attributes = new Dictionary<string, LogValue> { ["owner"] = LogValue.String("before") };
        var context = new OperationContextData { OperationId = "before", Attributes = attributes };
        IDisposable outer = runtime.BeginScope(context: context);
        context.OperationId = "after"; attributes["owner"] = LogValue.String("after");
        _ = logger.Write(CoreLogLevel.Information, new() { EventName = "outer", Message = "scope" });
        IDisposable inner = runtime.BeginScope(new() { OperationId = "inner" });
        Assert.ThrowsExactly<InvalidOperationException>(outer.Dispose);
        _ = logger.Write(CoreLogLevel.Information, new() { EventName = "inner", Message = "scope" });
        inner.Dispose(); inner.Dispose(); await Task.Yield();
        _ = logger.Write(CoreLogLevel.Information, new() { EventName = "restored", Message = "scope" });
        outer.Dispose(); outer.Dispose();
        _ = logger.Write(CoreLogLevel.Information, new() { EventName = "empty", Message = "scope" });
        LogStopResult stop = await runtime.StopAsync(CancellationToken.None); Assert.IsTrue(stop.Succeeded);
        JsonObject[] rows = Encoding.UTF8.GetString(bytes.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => JsonNode.Parse(s)!.AsObject()).ToArray();
        CollectionAssert.AreEqual(new string?[] { "before", "inner", "before", null }, rows.Select(r => (string?)r["operation_id"]).ToArray());
        foreach (JsonObject row in rows.Take(3)) { Assert.AreEqual("before", (string?)row["attributes"]!["owner"]); }
    }
    [TestMethod]
    public async Task PublicScopeNullGuardPrecedesClosedStateAndValidScopeCannotStartAfterStop()
    {
        await using CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None }, null, null);
        Assert.ThrowsExactly<ArgumentNullException>(() => runtime.BeginScope(null!));
        await runtime.StopAsync(CancellationToken.None);
        Assert.ThrowsExactly<ArgumentNullException>(() => runtime.BeginScope(null!));
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.BeginScope(new()));
    }
    [TestMethod]
    public async Task CreatorCanDisposeExistingScopeAfterRuntimeStopAndRepeatSafely()
    {
        await using CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None }, null, null);
        IDisposable handle = runtime.BeginScope(new() { OperationId = "kept" });
        await runtime.StopAsync(CancellationToken.None);
        handle.Dispose(); handle.Dispose();
        Assert.IsNull(runtime.Scopes.Capture().OperationId);
    }
}
