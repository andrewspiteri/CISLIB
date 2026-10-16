using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class StopReaderMigrationTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Controls =>
    [
        new object[] { "stop-start", "normal", "pid" },
        new object[] { "stop-start", "normal", "runtime" },
        new object[] { "stop-start", "configured-below", "acquisition" },
        new object[] { "stop-start", "configured-above", "invalid-timer" },
        new object[] { "stop-start", "normal", "shape" },
        new object[] { "stop-start", "normal", "utc" },
        new object[] { "stop-start", "blocked-write", "summary" },
        new object[] { "stop-start", "late-facts", "initial" },
        new object[] { "stop-start", "late-facts", "live" },
        new object[] { "stop-start", "dual", "outputs" },
        new object[] { "stop-start", "blocked-write", "partition" },
        new object[] { "stop-start", "blocked-write", "disposition" },
        new object[] { "stop-start", "blocked-flush", "flush" },
        new object[] { "stop-start", "dual", "filter" },
        new object[] { "stop-start", "normal", "cache" },
        new object[] { "stop-start", "blocked-write", "immutable" },
        new object[] { "stop-start", "shared", "join" },
        new object[] { "stop-start", "shared", "race" },
        new object[] { "stop-start", "closed-writes", "factory" },
        new object[] { "stop-start", "pre-cancelled", "token" },
        new object[] { "stop-start", "normal", "worker" },
        new object[] { "stop-start", "configured-max", "budget" },
        new object[] { "stop-start", "shared", "timer" },
        new object[] { "stop-start", "blocked-write", "callback" },
        new object[] { "stop-start", "normal", "io" },
        new object[] { "stop-start", "dual", "order" },
        new object[] { "stop-start", "normal", "framing" },
        new object[] { "stop-start", "dual", "records" },
        new object[] { "stop-start", "normal", "live-disposition" },
        new object[] { "stop-start", "normal", "fault" },
        new object[] { "stop-drain", "normal-dual", "pid" },
        new object[] { "stop-drain", "single-write", "late-flush" },
        new object[] { "stop-drain", "single-write", "late-control" },
        new object[] { "stop-drain", "normal-dual", "runtime" },
        new object[] { "stop-drain", "normal-dual", "shape" },
        new object[] { "stop-drain", "blocked-write", "loss" },
        new object[] { "stop-drain", "blocked-write", "partition" },
        new object[] { "stop-drain", "blocked-write", "disposition" },
        new object[] { "stop-drain", "queued-only", "flush" },
        new object[] { "stop-drain", "blocked-write", "cancel" },
        new object[] { "stop-drain", "queued-only", "registration" },
        new object[] { "stop-drain", "blocked-write", "pending" },
        new object[] { "stop-drain", "blocked-write", "identity" },
        new object[] { "stop-drain", "normal-dual", "borrowed" },
        new object[] { "stop-drain", "blocked-flush", "calls" },
        new object[] { "stop-drain", "blocked-callback", "callback" },
        new object[] { "stop-drain", "normal-dual", "wire" },
        new object[] { "stop-drain", "normal-dual", "file" },
        new object[] { "stop-drain", "cancel-write", "live" },
        new object[] { "stop-drain", "cancel-flush", "fault" },
        new object[] { "stop-drain", "normal-dual", "timer" },
        new object[] { "stop-join", "pre-cancelled", "pre-admission" },
        new object[] { "stop-join", "pre-cancelled", "pre-start" },
        new object[] { "stop-join", "pre-cancelled", "pre-state" },
        new object[] { "stop-join", "pre-cancelled", "pre-write" },
        new object[] { "stop-join", "pre-cancelled", "pre-timer" },
        new object[] { "stop-join", "pre-cancelled", "pre-type" },
        new object[] { "stop-join", "stop-first", "pid" },
        new object[] { "stop-join", "stop-first", "runtime" },
        new object[] { "stop-join", "stop-first", "shape" },
        new object[] { "stop-join", "deadline-joins", "summary" },
        new object[] { "stop-join", "deadline-joins", "pending" },
        new object[] { "stop-join", "deadline-joins", "flush" },
        new object[] { "stop-join", "pre-cancelled", "token" },
        new object[] { "stop-join", "cancelled-joins", "caller" },
        new object[] { "stop-join", "dispose-first", "dispose" },
        new object[] { "stop-join", "stop-first", "identity" },
        new object[] { "stop-join", "cancel-later", "cancel" },
        new object[] { "stop-join", "deadline-joins", "late" },
        new object[] { "stop-join", "stop-first", "borrowed" },
        new object[] { "stop-join", "deadline-joins", "calls" },
        new object[] { "stop-join", "stop-first", "records" },
        new object[] { "stop-join", "deadline-joins", "live" },
        new object[] { "stop-join", "dispose-first", "timer" },
    ];

    [TestMethod]
    [DynamicData(nameof(Controls))]
    public async Task MigratedShutdownReaderRejectsTheOriginalCorruption(string command, string mode, string fault)
    {
        JsonObject actual = await LifecycleProcessChecks.Run(command, mode, TestContext);
        int pid = actual["pid"]!.GetValue<int>();
        JsonObject damaged = actual.DeepClone().AsObject();
        Damage(command, fault, damaged, pid);
        Assert.ThrowsExactly<InvalidOperationException>(() => LifecycleProcessChecks.Validate(command, mode, damaged, pid));
    }

    private static void Damage(string command, string fault, JsonObject a, int pid)
    {
        switch ((command, fault))
        {
            case ("stop-start", "pid"): a["pid"] = pid + 1; break;
            case ("stop-start", "runtime"): a["runtime"] = ".NET 9.0.0"; break;
            case ("stop-start", "acquisition"): a["acquired"] = 1; break;
            case ("stop-start", "invalid-timer"): a["timers"]!["created"] = 1; break;
            case ("stop-start", "shape"): a["result"]!["extra"] = true; break;
            case ("stop-start", "utc"): a["result"]!["captured"] = "2026-10-13T19:20:21.0000000+01:00"; break;
            case ("stop-start", "summary"): a["result"]!["succeeded"] = true; break;
            case ("stop-start", "initial"): a["result"]!["invalid"] = "2"; break;
            case ("stop-start", "live"): a["liveInvalid"] = "1"; break;
            case ("stop-start", "outputs"): a["result"]!["outputs"] = new JsonArray(a["result"]!["outputs"]!.AsArray().Reverse().Select(value => value!.DeepClone()).ToArray()); break;
            case ("stop-start", "partition"): a["result"]!["outputs"]![0]!["pending"] = "0"; break;
            case ("stop-start", "disposition"): a["result"]!["outputs"]![0]!["pending"] = "0"; a["result"]!["outputs"]![0]!["written"] = "1"; break;
            case ("stop-start", "flush"): a["result"]!["outputs"]![0]!["flush"] = "Succeeded"; break;
            case ("stop-start", "filter"): a["result"]!["outputs"]![1]!["filtered"] = "0"; break;
            case ("stop-start", "cache"): a["cachedIdentity"] = false; break;
            case ("stop-start", "immutable"): a["immutableAfterIo"] = false; break;
            case ("stop-start", "join"): a["joinedCallers"] = 63; break;
            case ("stop-start", "race"): a["concurrentStarters"] = 31; break;
            case ("stop-start", "factory"): a["lateFactories"] = 1; break;
            case ("stop-start", "token"): a["tokenPreserved"] = false; break;
            case ("stop-start", "worker"): a["sharedWorkersJoined"] = false; break;
            case ("stop-start", "budget"): a["configuredMilliseconds"] = 1; break;
            case ("stop-start", "timer"): a["timers"]!["created"] = 2; break;
            case ("stop-start", "callback"): a["timers"]!["callbacks"] = 1; break;
            case ("stop-start", "io"): a["writer"]!["maximumIo"] = 2; break;
            case ("stop-start", "order"): a["writer"]!["order"] = new JsonArray(a["writer"]!["order"]!.AsArray().Reverse().Select(value => value!.DeepClone()).ToArray()); break;
            case ("stop-start", "framing"): a["writer"]!["bytes"] = Convert.ToBase64String(Convert.FromBase64String((string)a["writer"]!["bytes"]!)[..^1]); break;
            case ("stop-start", "records"): a["writer"]!["bytes"] = a["file"]!["bytes"]!.DeepClone(); break;
            case ("stop-start", "live-disposition"): a["diagnostics"]![0]!["pending"] = "1"; break;
            case ("stop-start", "fault"): a["diagnostics"]![0]!["fault"] = "WriteFailed"; break;
            case ("stop-drain", "pid"): a["pid"] = pid + 1; break;
            case ("stop-drain", "late-flush"): a["stream"]!["flushes"] = 1; break;
            case ("stop-drain", "late-control"): a["registeredAfterIo"] = 1; break;
            case ("stop-drain", "runtime"): a["runtime"] = ".NET 10.0.13"; break;
            case ("stop-drain", "shape"): a["result"]!["extra"] = true; break;
            case ("stop-drain", "loss"): a["result"]!["loss"] = false; break;
            case ("stop-drain", "partition"): a["result"]!["outputs"]![0]!["pending"] = "0"; break;
            case ("stop-drain", "disposition"): a["result"]!["outputs"]![0]!["written"] = "1"; a["result"]!["outputs"]![0]!["pending"] = "0"; break;
            case ("stop-drain", "flush"): a["result"]!["outputs"]![0]!["flush"] = "Pending"; break;
            case ("stop-drain", "cancel"): a["cancellationRequested"] = false; break;
            case ("stop-drain", "registration"): a["registeredAfterDeadline"] = 1; break;
            case ("stop-drain", "pending"): a["pendingBeforeRelease"] = false; break;
            case ("stop-drain", "identity"): a["immutable"] = false; break;
            case ("stop-drain", "borrowed"): a["stream"]!["disposed"] = 1; break;
            case ("stop-drain", "calls"): a["stream"]!["flushes"] = 0; break;
            case ("stop-drain", "callback"): a["stream"]!["callbackGateFree"] = false; break;
            case ("stop-drain", "wire"): a["stream"]!["bytes"] = ""; break;
            case ("stop-drain", "file"): a["fileBytes"] = ""; break;
            case ("stop-drain", "live"): a["live"]![0]!["failed"] = "0"; break;
            case ("stop-drain", "fault"): a["live"]![0]!["fault"] = null; break;
            case ("stop-drain", "timer"): a["timers"]!["periodicFires"] = 1; break;
            case ("stop-join", "pre-admission"): a["preCancelledStart"]!["admissionClosed"] = false; break;
            case ("stop-join", "pre-start"): a["preCancelledStart"]!["shutdownStarted"] = false; break;
            case ("stop-join", "pre-state"): a["preCancelledStart"]!["state"] = "Running"; break;
            case ("stop-join", "pre-write"): a["preCancelledStart"]!["writeOutcome"] = "Accepted"; break;
            case ("stop-join", "pre-timer"): a["preCancelledStart"]!["deadlineCreated"] = 0; break;
            case ("stop-join", "pre-type"): a["preCancelledStart"]!["deadlineCreated"] = true; break;
            case ("stop-join", "pid"): a["pid"] = pid + 1; break;
            case ("stop-join", "runtime"): a["runtime"] = ".NET 10.0.13"; break;
            case ("stop-join", "shape"): a["result"]!["extra"] = true; break;
            case ("stop-join", "summary"): a["result"]!["succeeded"] = true; break;
            case ("stop-join", "pending"): a["result"]!["outputs"]![0]!["pending"] = "0"; break;
            case ("stop-join", "flush"): a["result"]!["outputs"]![0]!["flush"] = "Succeeded"; break;
            case ("stop-join", "token"): a["tokenIdentity"] = false; break;
            case ("stop-join", "caller"): a["cancelledWaiters"] = 63; break;
            case ("stop-join", "dispose"): a["disposeJoiners"] = 63; break;
            case ("stop-join", "identity"): a["sameTask"] = false; break;
            case ("stop-join", "cancel"): a["outputCancelledBeforeDeadline"] = true; break;
            case ("stop-join", "late"): a["pendingAtReturn"] = false; break;
            case ("stop-join", "borrowed"): a["stream"]!["disposed"] = 1; break;
            case ("stop-join", "calls"): a["stream"]!["flushes"] = 1; break;
            case ("stop-join", "records"): a["stream"]!["bytes"] = ""; break;
            case ("stop-join", "live"): a["live"]![0]!["pending"] = "1"; break;
            case ("stop-join", "timer"): a["timers"]!["created"] = 2; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }
}
