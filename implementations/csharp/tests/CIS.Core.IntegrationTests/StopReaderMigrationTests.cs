using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class StopReaderMigrationTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Controls =>
    [
        new object[] { "stop-start", "normal", "pid", "Actual child PID differs." },
        new object[] { "stop-start", "normal", "runtime", "Lifecycle mode/runtime differs." },
        new object[] { "stop-start", "configured-below", "acquisition", "stop-start validation" },
        new object[] { "stop-start", "configured-above", "invalid-timer", "stop-start validation" },
        new object[] { "stop-start", "normal", "shape", "Lifecycle result shape differs." },
        new object[] { "stop-start", "normal", "utc", "Lifecycle UTC label differs." },
        new object[] { "stop-start", "blocked-write", "summary", "stop-start summary" },
        new object[] { "stop-start", "late-facts", "initial", "stop-start initial facts" },
        new object[] { "stop-start", "late-facts", "live", "stop-start initial facts" },
        new object[] { "stop-start", "dual", "outputs", "stop-start output name" },
        new object[] { "stop-start", "blocked-write", "partition", "Lifecycle boundary partition differs." },
        new object[] { "stop-start", "blocked-write", "disposition", "stop-start target/written" },
        new object[] { "stop-start", "blocked-flush", "flush", "stop-start flush" },
        new object[] { "stop-start", "dual", "filter", "stop-start rejection facts" },
        new object[] { "stop-start", "normal", "cache", "stop-start completion identity" },
        new object[] { "stop-start", "blocked-write", "immutable", "stop-start completion identity" },
        new object[] { "stop-start", "shared", "join", "stop-start joiners" },
        new object[] { "stop-start", "shared", "race", "stop-start joiners" },
        new object[] { "stop-start", "closed-writes", "factory", "stop-start joiners" },
        new object[] { "stop-start", "pre-cancelled", "token", "stop-start caller token" },
        new object[] { "stop-start", "normal", "worker", "stop-start completion identity" },
        new object[] { "stop-start", "configured-max", "budget", "stop-start budget" },
        new object[] { "stop-start", "shared", "timer", "stop-start timer ownership" },
        new object[] { "stop-start", "blocked-write", "callback", "stop-start timer callbacks" },
        new object[] { "stop-start", "normal", "io", "stop-start writer calls" },
        new object[] { "stop-start", "dual", "order", "stop-start writer order" },
        new object[] { "stop-start", "normal", "framing", "Lifecycle record framing differs." },
        new object[] { "stop-start", "dual", "records", "stop-start record selection" },
        new object[] { "stop-start", "normal", "live-disposition", "stop-start live dispositions" },
        new object[] { "stop-start", "normal", "fault", "stop-start live filtering/fault" },
        new object[] { "stop-drain", "normal-dual", "pid", "Actual child PID differs." },
        new object[] { "stop-drain", "single-write", "late-flush", "stop-drain writer calls" },
        new object[] { "stop-drain", "single-write", "late-control", "stop-drain settled controls" },
        new object[] { "stop-drain", "normal-dual", "runtime", "Lifecycle mode/runtime differs." },
        new object[] { "stop-drain", "normal-dual", "shape", "Lifecycle result shape differs." },
        new object[] { "stop-drain", "blocked-write", "loss", "stop-drain summary" },
        new object[] { "stop-drain", "blocked-write", "partition", "Lifecycle boundary partition differs." },
        new object[] { "stop-drain", "blocked-write", "disposition", "stop-drain written" },
        new object[] { "stop-drain", "queued-only", "flush", "stop-drain flush" },
        new object[] { "stop-drain", "blocked-write", "cancel", "stop-drain cancellation" },
        new object[] { "stop-drain", "queued-only", "registration", "stop-drain deadline controls" },
        new object[] { "stop-drain", "blocked-write", "pending", "stop-drain held pending" },
        new object[] { "stop-drain", "blocked-write", "identity", "stop-drain completion identity" },
        new object[] { "stop-drain", "normal-dual", "borrowed", "stop-drain borrowed stream" },
        new object[] { "stop-drain", "blocked-flush", "calls", "stop-drain writer calls" },
        new object[] { "stop-drain", "blocked-callback", "callback", "stop-drain callback gate" },
        new object[] { "stop-drain", "normal-dual", "wire", "stop-drain record selection" },
        new object[] { "stop-drain", "normal-dual", "file", "stop-drain physical file bytes" },
        new object[] { "stop-drain", "cancel-write", "live", "stop-drain live dispositions" },
        new object[] { "stop-drain", "cancel-flush", "fault", "stop-drain live fault" },
        new object[] { "stop-drain", "normal-dual", "timer", "stop-drain timer ownership" },
        new object[] { "stop-join", "pre-cancelled", "pre-admission", "pre-admission start" },
        new object[] { "stop-join", "pre-cancelled", "pre-start", "pre-admission start" },
        new object[] { "stop-join", "pre-cancelled", "pre-state", "pre-admission start" },
        new object[] { "stop-join", "pre-cancelled", "pre-write", "pre-admission start" },
        new object[] { "stop-join", "pre-cancelled", "pre-timer", "pre-admission start" },
        new object[] { "stop-join", "pre-cancelled", "pre-type", "pre-admission deadline type" },
        new object[] { "stop-join", "stop-first", "pid", "Actual child PID differs." },
        new object[] { "stop-join", "stop-first", "runtime", "Lifecycle mode/runtime differs." },
        new object[] { "stop-join", "stop-first", "shape", "Lifecycle result shape differs." },
        new object[] { "stop-join", "deadline-joins", "summary", "stop-join summary" },
        new object[] { "stop-join", "deadline-joins", "pending", "stop-join pending" },
        new object[] { "stop-join", "deadline-joins", "flush", "stop-join flush" },
        new object[] { "stop-join", "pre-cancelled", "token", "stop-join shared identity" },
        new object[] { "stop-join", "cancelled-joins", "caller", "stop-join caller joins" },
        new object[] { "stop-join", "dispose-first", "dispose", "stop-join caller joins" },
        new object[] { "stop-join", "stop-first", "identity", "stop-join shared identity" },
        new object[] { "stop-join", "cancel-later", "cancel", "stop-join output cancellation" },
        new object[] { "stop-join", "deadline-joins", "late", "stop-join completion state" },
        new object[] { "stop-join", "stop-first", "borrowed", "stop-join borrowed stream" },
        new object[] { "stop-join", "deadline-joins", "calls", "stop-join writer calls" },
        new object[] { "stop-join", "stop-first", "records", "stop-join record selection" },
        new object[] { "stop-join", "deadline-joins", "live", "stop-join live delivery" },
        new object[] { "stop-join", "dispose-first", "timer", "stop-join timer ownership" },
        new object[] { "stop-cleanup", "normal", "pid", "Actual child PID differs." },
        new object[] { "stop-cleanup", "normal", "runtime", "Lifecycle mode/runtime differs." },
        new object[] { "stop-cleanup", "normal", "shape", "Lifecycle result shape differs." },
        new object[] { "stop-cleanup", "ownership-held", "summary", "cleanup success" },
        new object[] { "stop-cleanup", "normal", "console", "cleanup console delivery" },
        new object[] { "stop-cleanup", "ownership-held", "pending", "cleanup file dispositions" },
        new object[] { "stop-cleanup", "ownership-held", "flush", "cleanup file flush" },
        new object[] { "stop-cleanup", "resource-failure", "fault", "cleanup fault" },
        new object[] { "stop-cleanup", "callback-write-failure", "late", "cleanup late delivery" },
        new object[] { "stop-cleanup", "normal", "owner", "cleanup owner" },
        new object[] { "stop-cleanup", "normal", "gate", "cleanup owner" },
        new object[] { "stop-cleanup", "normal", "io", "cleanup owner" },
        new object[] { "stop-cleanup", "normal", "callback", "cleanup owner" },
        new object[] { "stop-cleanup", "normal", "borrowed", "cleanup primary order/borrowed console" },
        new object[] { "stop-cleanup", "ownership-held", "held", "cleanup held resources" },
        new object[] { "stop-cleanup", "callback-held", "callback-held", "cleanup held resources" },
        new object[] { "stop-cleanup", "normal", "identity", "cleanup identity" },
        new object[] { "stop-cleanup", "normal", "immutable", "cleanup identity" },
        new object[] { "stop-cleanup", "normal", "joiners", "cleanup identity" },
        new object[] { "stop-cleanup", "write-resource-failure", "primary", "cleanup primary error" },
        new object[] { "stop-cleanup", "write-resource-failure", "first", "cleanup primary order/borrowed console" },
        new object[] { "stop-cleanup", "write-resource-failure", "cleanup", "cleanup identity" },
        new object[] { "stop-cleanup", "write-timer-resource-failure", "errors", "cleanup worker errors" },
        new object[] { "stop-cleanup", "ownership-held", "calls", "cleanup file flush calls" },
        new object[] { "stop-cleanup", "normal", "records", "cleanup file record selection" },
        new object[] { "stop-cleanup", "normal", "timer", "cleanup timer disposal" },
        new object[] { "stop-cleanup", "deadline-cleanup-held", "inactive-flush", "cleanup late flush" },
        new object[] { "stop-cleanup", "deadline-cleanup-held", "late-flush", "cleanup late flush" },
        new object[] { "stop-cleanup", "post-stop-flush", "post-flush", "cleanup post-stop flush" },
        new object[] { "runtime-create", "file", "pid", "Actual child PID differs." },
        new object[] { "runtime-create", "file", "runtime", "Lifecycle mode/runtime differs." },
        new object[] { "runtime-create", "invalid-settings", "validation", "creation validation" },
        new object[] { "runtime-create", "invalid-metadata", "metadata", "creation validation" },
        new object[] { "runtime-create", "file", "stop", "creation stop" },
        new object[] { "runtime-create", "file", "facts", "creation process facts" },
        new object[] { "runtime-create", "file", "release", "creation ownership" },
        new object[] { "runtime-create", "file", "freeze", "creation ownership" },
        new object[] { "runtime-create", "contended", "contention", "creation contention" },
        new object[] { "runtime-create", "file", "record", "creation record framing" },
        new object[] { "runtime-create", "disabled", "disabled", "creation disabled output" },
        new object[] { "runtime-create", "file", "application", "creation application" },
        new object[] { "runtime-create", "file", "delivery", "creation delivery" },
    ];

    [TestMethod]
    [DynamicData(nameof(Controls))]
    public async Task MigratedShutdownReaderRejectsTheOriginalCorruption(string command, string mode, string fault, string reason)
    {
        JsonObject actual = await LifecycleProcessChecks.Run(command, mode, TestContext);
        int pid = actual["pid"]!.GetValue<int>();
        JsonObject damaged = actual.DeepClone().AsObject();
        Damage(command, fault, damaged, pid);
        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() => LifecycleProcessChecks.Validate(command, mode, damaged, pid));
        Assert.AreEqual(reason, error.Message, command + "/" + fault);
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
            case ("stop-cleanup", "pid"): a["pid"] = pid + 1; break;
            case ("stop-cleanup", "runtime"): a["runtime"] = ".NET 10.0.13"; break;
            case ("stop-cleanup", "shape"): a["result"]!["extra"] = true; break;
            case ("stop-cleanup", "summary"): a["result"]!["succeeded"] = true; break;
            case ("stop-cleanup", "console"): a["result"]!["outputs"]![0]!["written"] = "0"; break;
            case ("stop-cleanup", "pending"): a["result"]!["outputs"]![1]!["pending"] = "0"; break;
            case ("stop-cleanup", "flush"): a["result"]!["outputs"]![1]!["flush"] = "Succeeded"; break;
            case ("stop-cleanup", "fault"): a["live"]![1]!["fault"] = null; break;
            case ("stop-cleanup", "late"): a["live"]![1]!["failed"] = "0"; break;
            case ("stop-cleanup", "owner"): a["owner"]!["calls"] = 2; break;
            case ("stop-cleanup", "gate"): a["owner"]!["gateFree"] = false; break;
            case ("stop-cleanup", "io"): a["owner"]!["activeIo"] = true; break;
            case ("stop-cleanup", "callback"): a["owner"]!["activeCallback"] = true; break;
            case ("stop-cleanup", "borrowed"): a["borrowedConsoleOpen"] = false; break;
            case ("stop-cleanup", "held"): a["heldOwned"] = false; break;
            case ("stop-cleanup", "callback-held"): a["heldCallback"] = false; break;
            case ("stop-cleanup", "identity"): a["sameTask"] = false; break;
            case ("stop-cleanup", "immutable"): a["immutable"] = false; break;
            case ("stop-cleanup", "joiners"): a["disposeJoiners"] = 63; break;
            case ("stop-cleanup", "primary"): a["primaryPreserved"] = false; break;
            case ("stop-cleanup", "first"): a["primaryFirst"] = false; break;
            case ("stop-cleanup", "cleanup"): a["cleanupPreserved"] = false; break;
            case ("stop-cleanup", "errors"): a["workerErrors"] = 2; break;
            case ("stop-cleanup", "calls"): a["file"]!["flushes"] = 1; break;
            case ("stop-cleanup", "records"): a["file"]!["bytes"] = ""; break;
            case ("stop-cleanup", "timer"): a["timerDisposeCalls"] = 2; break;
            case ("stop-cleanup", "inactive-flush"): a["inactiveFlushResolved"] = false; break;
            case ("stop-cleanup", "late-flush"): a["lateFlushCompleted"] = false; break;
            case ("stop-cleanup", "post-flush"): a["postFlushSucceeded"] = true; break;
            case ("runtime-create", "pid"): a["pid"] = pid + 1; break;
            case ("runtime-create", "runtime"): a["runtime"] = ".NET 10.0.13"; break;
            case ("runtime-create", "validation"): a["noFileArtifacts"] = false; break;
            case ("runtime-create", "metadata"): a["error"] = null; break;
            case ("runtime-create", "stop"): a["result"]!["succeeded"] = false; break;
            case ("runtime-create", "facts"): a["factPid"] = pid + 1; break;
            case ("runtime-create", "release"): a["released"] = false; break;
            case ("runtime-create", "freeze"): a["frozen"] = false; break;
            case ("runtime-create", "contention"): a["error"] = "unsafe raw path"; break;
            case ("runtime-create", "record"): a["bytes"] = ""; break;
            case ("runtime-create", "disabled"): a["noFileArtifacts"] = false; break;
            case ("runtime-create", "application"): a["application"] = "unknown"; break;
            case ("runtime-create", "delivery"): a["result"]!["outputs"]![0]!["written"] = "0"; a["result"]!["outputs"]![0]!["pending"] = "1"; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }
}
