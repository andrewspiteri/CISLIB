using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class DependencyPreparedEvaluationTests
{
    private const string CoreProject = "src/CIS.Core/CIS.Core.csproj";

    [TestMethod]
    public async Task FreshCliAndPreparedCanonicalMetadataChecksAgree()
    {
        string language = Language();
        string assets = Path.Combine(language, "src/CIS.Core/obj/project.assets.json");
        DependencyCheckRunner.CheckResult fresh = await DependencyCheckRunner.RunFresh(language, language, CoreProject, assets);
        DependencyCheckRunner.CheckResult prepared = await DependencyCheckRunner.Run(language, language, CoreProject, "-AssetsPath", assets);
        Assert.AreEqual(0, fresh.ExitCode, fresh.StandardOutput + fresh.StandardError);
        Assert.AreEqual(fresh.ExitCode, prepared.ExitCode, prepared.StandardOutput + prepared.StandardError);
        Assert.AreEqual(fresh.StandardOutput, prepared.StandardOutput);
        Assert.AreEqual(fresh.StandardError, prepared.StandardError);
    }

    [TestMethod]
    public async Task EveryPreparedProjectionHasPrivateMutableJson()
    {
        string language = Language(), project = Path.Combine(language, CoreProject);
        DependencyChecks.PreparedEvaluation prepared = await DependencyCheckRunner.Prepare(language, TimeSpan.FromMinutes(1));
        prepared.VerifyUnchanged();
        JsonObject evaluation = prepared.Evaluation(project, false), references = prepared.Evaluation(project, true);
        string originalEvaluation = evaluation.ToJsonString(), originalReferences = references.ToJsonString();
        evaluation["Properties"]!["TargetFramework"] = "changed-private-value";
        references["Items"]!["ReferencePath"]!.AsArray().Clear();
        Assert.AreEqual(originalEvaluation, prepared.Evaluation(project, false).ToJsonString());
        Assert.AreEqual(originalReferences, prepared.Evaluation(project, true).ToJsonString());
        prepared.VerifyUnchanged();
    }

    [TestMethod]
    [DataRow("changed-import")]
    [DataRow("removed-assets")]
    [DataRow("created-optional-import")]
    public void StalePhysicalInputPinsAreRejected(string mutation)
    {
        using var root = new TemporaryDirectory();
        string import = Path.Combine(root.Path, "Directory.Build.props"), assets = Path.Combine(root.Path, "project.assets.json"), optional = Path.Combine(root.Path, "Directory.Build.targets");
        File.WriteAllText(import, "<Project />");
        File.WriteAllText(assets, "{}");
        DependencyChecks.InputPin[] inputs = DependencyChecks.CaptureInputPins([import, assets, optional]);
        DependencyChecks.VerifyInputPins(inputs);
        if (mutation == "changed-import") { File.WriteAllText(import, "<Project><PropertyGroup><TargetFramework>changed</TargetFramework></PropertyGroup></Project>"); }
        else if (mutation == "removed-assets") { File.Delete(assets); }
        else { File.WriteAllText(optional, "<Project />"); }
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => DependencyChecks.VerifyInputPins(inputs));
        StringAssert.Contains(error.Message, "Prepared dependency evaluation input changed:");

        int batchBodies = 0, batchScans = 0;
        Task VerifyBatchInputs()
        {
            batchScans++;
            DependencyChecks.VerifyInputPins(inputs);
            return Task.CompletedTask;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => DependencyChecks.VerifyMetadataBatchBoundaryAsync(VerifyBatchInputs,
            () => { batchBodies++; return Task.CompletedTask; }, () => { }).GetAwaiter().GetResult());
        Assert.AreEqual(0, batchBodies, "A stale batch input must reject the batch before any real variant executes.");
        Assert.AreEqual(1, batchScans);
        File.WriteAllText(import, "<Project />");
        File.WriteAllText(assets, "{}");
        File.Delete(optional);
        Assert.ThrowsExactly<InvalidDataException>(() => DependencyChecks.VerifyMetadataBatchBoundaryAsync(VerifyBatchInputs, () =>
        {
            batchBodies++;
            if (mutation == "changed-import") { File.WriteAllText(import, "Changed during the actual metadata batch."); }
            else if (mutation == "removed-assets") { File.Delete(assets); }
            else { File.WriteAllText(optional, "An optional import appeared during the actual metadata batch."); }
            return Task.CompletedTask;
        }, () => { }).GetAwaiter().GetResult());
        Assert.AreEqual(1, batchBodies);
        Assert.AreEqual(3, batchScans, "A successful entry scan cannot replace the mandatory physical scan after every batch.");
        File.WriteAllText(import, "<Project />");
        File.WriteAllText(assets, "{}");
        File.Delete(optional);
        Assert.ThrowsExactly<InvalidOperationException>(() => DependencyChecks.VerifyMetadataBatchBoundaryAsync(VerifyBatchInputs,
            () => throw new InvalidOperationException("The real variant body failed."), () => { }).GetAwaiter().GetResult());
        Assert.AreEqual(5, batchScans, "A failed variant must still join the physical exit verification.");
    }

    [TestMethod]
    public void NestedSdkResolverAndTaskExecutionInputsArePinned()
    {
        using var root = new TemporaryDirectory();
        string[] paths =
        [
            Path.Combine(root.Path, "MSBuild.dll"),
            Path.Combine(root.Path, "SdkResolvers/Resolver/Resolver.dll"),
            Path.Combine(root.Path, "SdkResolvers/Resolver/Resolver.deps.json"),
            Path.Combine(root.Path, "Sdks/Example/tools/net10.0/Task.dll"),
            Path.Combine(root.Path, "Sdks/Example/tools/net10.0/Task.runtimeconfig.json")
        ];
        foreach (string path in paths)
        { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "Original physical SDK input: " + Path.GetFileName(path)); }
        File.WriteAllText(Path.Combine(root.Path, "README.md"), "Not an execution input.");
        string[] selected = DependencyChecks.SdkInputFiles(root.Path, () => { });
        CollectionAssert.AreEquivalent(paths, selected);
        DependencyChecks.InputPin[] inputs = DependencyChecks.CaptureInputPins(selected);
        File.WriteAllText(paths[1], "Changed nested resolver input.");
        Assert.ThrowsExactly<InvalidDataException>(() => DependencyChecks.VerifyInputPins(inputs));
        string added = Path.Combine(root.Path, "Sdks/Example/tools/net10.0/Task.deps.json");
        File.WriteAllText(added, "A new physical task dependency input.");
        Assert.IsFalse(selected.SequenceEqual(DependencyChecks.SdkInputFiles(root.Path, () => { })));
    }

    [TestMethod]
    public void HashingChecksTheSharedDeadlineBetweenPhysicalReadBlocks()
    {
        using var root = new TemporaryDirectory();
        string first = Path.Combine(root.Path, "first.dll"), second = Path.Combine(root.Path, "second.dll");
        File.WriteAllBytes(first, new byte[131072]);
        File.WriteAllBytes(second, new byte[131072]);
        int checks = 0;
        Assert.ThrowsExactly<TimeoutException>(() => DependencyChecks.CaptureInputPins([first, second], () =>
        { if (++checks == 7) { throw new TimeoutException("The shared deadline elapsed during a physical input hash."); } }));
        Assert.AreEqual(7, checks);
        using var reopened = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.AreEqual(131072L, reopened.Length);
    }

    [TestMethod]
    public async Task OverlappingInputRequestsNeedAScanStartedAfterEachRequest()
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "input.dll");
        File.WriteAllText(path, "Original physical input.");
        DependencyChecks.InputPin[] pins = DependencyChecks.CaptureInputPins([path]);
        var gate = new DependencyChecks.InputVerificationGate();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int scans = 0;
        void Check() => budget.Token.ThrowIfCancellationRequested();
        Task first = Task.Run(() => gate.VerifyAsync(() =>
        {
            Interlocked.Increment(ref scans);
            entered.SetResult();
            release.Wait(budget.Token);
            DependencyChecks.VerifyInputPins(pins);
        }, Check, budget.Token));
        Task[] waiting = [];
        try
        {
            await entered.Task.WaitAsync(budget.Token);
            waiting = Enumerable.Range(0, 8).Select(_ => gate.VerifyAsync(() =>
            {
                Interlocked.Increment(ref scans);
                DependencyChecks.VerifyInputPins(pins);
            }, Check, budget.Token)).ToArray();
            release.Set();
            await Task.WhenAll([first, .. waiting]);
            Assert.AreEqual(2, scans, "The scan already in progress cannot cover the later requests; their next full scan can be shared.");
            File.WriteAllText(path, "Changed after the completed verification.");
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => gate.VerifyAsync(() =>
            {
                Interlocked.Increment(ref scans);
                DependencyChecks.VerifyInputPins(pins);
            }, Check, budget.Token));
            Assert.AreEqual(3, scans, "A later request must inspect the current physical input again.");
        }
        finally { release.Set(); await Task.WhenAll([first, .. waiting]); }
    }

    [TestMethod]
    public async Task FailedInputScansCannotAdmitWaitingRequests()
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "input.dll");
        File.WriteAllText(path, "Original physical input.");
        DependencyChecks.InputPin[] pins = DependencyChecks.CaptureInputPins([path]);
        var gate = new DependencyChecks.InputVerificationGate();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int scans = 0;
        void Check() => budget.Token.ThrowIfCancellationRequested();
        Task first = Task.Run(() => gate.VerifyAsync(() =>
        {
            Interlocked.Increment(ref scans);
            entered.SetResult();
            release.Wait(budget.Token);
            DependencyChecks.VerifyInputPins(pins);
        }, Check, budget.Token));
        Task[] waiting = [];
        try
        {
            await entered.Task.WaitAsync(budget.Token);
            waiting = Enumerable.Range(0, 7).Select(_ => gate.VerifyAsync(() =>
            {
                Interlocked.Increment(ref scans);
                DependencyChecks.VerifyInputPins(pins);
            }, Check, budget.Token)).ToArray();
            File.WriteAllText(path, "Every pending request must reject these changed bytes.");
            release.Set();
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Task.WhenAll([first, .. waiting]));
            Assert.IsTrue(first.IsFaulted && waiting.All(task => task.IsFaulted));
            Assert.AreEqual(8, scans, "A failed physical verification cannot publish a successful receipt for another caller.");
        }
        finally
        {
            release.Set();
            try { await Task.WhenAll([first, .. waiting]); }
            catch (InvalidDataException) { }
        }
    }

    [TestMethod]
    public async Task CanceledInputWaitersLeaveTheOwnerAndNextRequestIntact()
    {
        var gate = new DependencyChecks.InputVerificationGate();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var canceled = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int scans = 0, canceledScans = 0;
        void Check() => budget.Token.ThrowIfCancellationRequested();
        Task first = Task.Run(() => gate.VerifyAsync(() =>
        {
            entered.SetResult();
            release.Wait(budget.Token);
            Interlocked.Increment(ref scans);
        }, Check, budget.Token));
        Task? waiting = null;
        try
        {
            await entered.Task.WaitAsync(budget.Token);
            waiting = gate.VerifyAsync(() => Interlocked.Increment(ref canceledScans),
                () => canceled.Token.ThrowIfCancellationRequested(), canceled.Token);
            canceled.Cancel();
            try { await waiting; Assert.Fail("The queued request must honor its own cancellation."); }
            catch (OperationCanceledException) { }
            Assert.AreEqual(0, canceledScans);
            Assert.IsFalse(first.IsCompleted, "Canceling a waiter must not release or cancel the active owner.");
            release.Set();
            await first;
            await gate.VerifyAsync(() => Interlocked.Increment(ref scans), Check, budget.Token);
            Assert.AreEqual(2, scans);
        }
        finally
        {
            release.Set();
            await first;
            if (waiting is not null)
            {
                try { await waiting; }
                catch (OperationCanceledException) { }
            }
        }
    }

    [TestMethod]
    public void ExpandedInputPinsRetainEarlierPhysicalIdentities()
    {
        using var root = new TemporaryDirectory();
        string existing = Path.Combine(root.Path, "prior.dll"), optional = Path.Combine(root.Path, "optional.props"), added = Path.Combine(root.Path, "new.dll");
        const string original = "Original previously pinned physical bytes.";
        File.WriteAllText(existing, original);
        File.WriteAllText(added, "Newly discovered physical input.");
        DependencyChecks.InputPin[] before = DependencyChecks.CaptureInputPins([existing, optional]);
        DependencyChecks.InputPin originalPin = before.Single(pin => pin.Path == existing);
        File.WriteAllText(existing, "Changed between initial verification and closure expansion.");
        File.WriteAllText(optional, "An originally absent input appeared.");
        DependencyChecks.InputPin[] expanded = DependencyChecks.ExpandInputPins(
            [existing, optional, added, Path.Combine(root.Path, ".", "new.dll")], before, () => { });
        Assert.AreEqual(3, expanded.Length, "Aliases of an added physical path must not create another input identity.");
        Assert.AreEqual(originalPin.Sha256, expanded.Single(pin => pin.Path == existing).Sha256);
        Assert.IsNull(expanded.Single(pin => pin.Path == optional).Sha256);
        Assert.ThrowsExactly<InvalidDataException>(() => DependencyChecks.VerifyInputPins([expanded.Single(pin => pin.Path == existing)]));
        Assert.ThrowsExactly<InvalidDataException>(() => DependencyChecks.VerifyInputPins([expanded.Single(pin => pin.Path == optional)]));
        Assert.ThrowsExactly<InvalidDataException>(() => DependencyChecks.ExpandInputPins([existing],
            [originalPin, new(Path.Combine(root.Path, ".", "prior.dll"), originalPin.Sha256)], () => { }));
        File.WriteAllText(existing, original);
        File.Delete(optional);
        DependencyChecks.VerifyInputPins(expanded);
        File.WriteAllText(added, "Changed newly discovered physical bytes.");
        Assert.ThrowsExactly<InvalidDataException>(() => DependencyChecks.VerifyInputPins(expanded));
    }

    private static string Language()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
        { if (File.Exists(Path.Combine(at.FullName, "eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj"))) { return at.FullName; } }
        throw new DirectoryNotFoundException("The canonical C# source root is required.");
    }
}
