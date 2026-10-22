using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using C = CIS.Verification.HealthcheckMutationFiniteAspNetCatalog;
using V = CIS.Verification.HealthcheckMutationFiniteAspNetValidation;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationFiniteAspNetTests
{
    [TestMethod]
    public void FixedProfileCarriesFourSourceIdentitiesAndNoApprovalFlag()
    {
        V.Document(Document());
        CollectionAssert.AreEqual(new[]
        {
            "b2b702d79d714a262b0d4e997e3d1406d05fd032f0578773a30324670c8db21b",
            "341b5240d04b1391b7ae3c471bed96ca0ed14874003dc7fb42ff24fb22e5bea6",
            "0b7a1e7e82dc3e6778dcf84e99d1d3df1ca9289f9e6be225452e4f0db2d1b37b",
            "6ce0a0d843b81a113ec41492cd618065245df4160eeb556253c2980a2b4bbed3"
        }, C.Targets.Select(value => value.Identity).ToArray());
    }

    [TestMethod]
    [DataRow("approval")]
    [DataRow("schema")]
    [DataRow("lane")]
    [DataRow("proof-kind")]
    [DataRow("report")]
    [DataRow("unknown-original")]
    [DataRow("missing-target")]
    [DataRow("duplicate-target")]
    [DataRow("reordered-target")]
    [DataRow("null-reference")]
    [DataRow("unknown-source-field")]
    public void MalformedOrForeignProfileCannotSelectTheFixedProof(string change)
    {
        JsonObject document = Document();
        switch (change)
        {
            case "approval": document["approved"] = true; break;
            case "schema": document["schema"] = "other"; break;
            case "lane": document["osLane"] = "windows"; break;
            case "proof-kind": document["proofKind"] = "AlreadyQualified"; break;
            case "report": document["original"]!["reportSha256"] = new string('a', 64); break;
            case "unknown-original": document["original"]!["other"] = false; break;
            case "missing-target": document["original"]!["definitionIdentities"]!.AsArray().RemoveAt(3); break;
            case "duplicate-target": document["original"]!["definitionIdentities"]![3] = C.Targets[0].Identity; break;
            case "reordered-target": document["original"]!["definitionIdentities"]![0] = C.Targets[1].Identity; break;
            case "null-reference": document["toolchain"]!["objectModel"] = null; break;
            case "unknown-source-field": document["sources"]!["acceptOldBinary"] = true; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => V.Document(document));
    }

    [TestMethod]
    public void FiniteConfigurationPreservesWaitPolicyAndUsesOnlyTheOwnReceivingCohort()
    { (JsonObject parent, JsonObject finite) = Configurations(); V.Configuration(parent, finite); }

    [TestMethod]
    [DataRow("timeout")]
    [DataRow("coverage")]
    [DataRow("bail")]
    [DataRow("threshold")]
    [DataRow("concurrency")]
    [DataRow("framework")]
    [DataRow("range")]
    [DataRow("extra-range")]
    [DataRow("filter")]
    [DataRow("family")]
    public void ConfigurationCannotBorrowDifferentTestsOrRelaxItsExecutionPolicy(string change)
    {
        (JsonObject parent, JsonObject finite) = Configurations(); JsonObject settings = finite["stryker-config"]!.AsObject();
        switch (change)
        {
            case "timeout": settings["additional-timeout"] = 750001; break;
            case "coverage": settings["coverage-analysis"] = "perTest"; break;
            case "bail": settings["disable-bail"] = false; break;
            case "threshold": settings["thresholds"]!["break"] = 89; break;
            case "concurrency": settings["concurrency"] = 3; break;
            case "framework": settings["target-framework"] = "net8.0"; break;
            case "range": settings["mutate"]![0] = C.SourcePath + "{1193..1245}"; break;
            case "extra-range": settings["mutate"]!.AsArray().Add(C.SourcePath + "{1..20}"); break;
            case "filter": settings["test-case-filter"] = "FullyQualifiedName~Other"; break;
            case "family": settings["test-projects"]![0] = "../../tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj"; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => V.Configuration(parent, finite));
    }

    [TestMethod]
    public void OwnSixCaseIdentitySetAllowsThreeNewRegistrationRowsWithoutBorrowingOldFullGuidMembership()
    { V.Cases(C.Cases, C.Cases, C.Cases); Assert.AreEqual(6, C.Cases.Length); Assert.AreEqual(3, C.RegistrationGuids.Length); }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("wrong-guid")]
    [DataRow("wrong-method")]
    [DataRow("sibling-method")]
    public void BaselineAndFiniteReportMustJoinTheExactOwnCohort(string change)
    {
        string[] cohort = C.Cases;
        int registration = Array.FindIndex(cohort, value => value.EndsWith("\t" + C.Registration, StringComparison.Ordinal));
        switch (change)
        {
            case "missing": cohort = cohort[..^1]; break;
            case "duplicate": cohort[1] = cohort[0]; break;
            case "wrong-guid": cohort[0] = Guid.Empty + "\t" + C.Registration; break;
            case "wrong-method": cohort[registration] = cohort[registration].Replace(C.Registration, C.Cancellation, StringComparison.Ordinal); break;
            case "sibling-method": cohort[registration] = cohort[registration].Replace("ValidDetailsAndAuthorizationCombinationsInstallBeforeTheReservedBranch", "DisabledDetailsAcceptsOmittedAuthorizationAndStillReservesItsRoute", StringComparison.Ordinal); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => V.Cases(C.Cases, cohort, C.Cases));
    }

    [TestMethod]
    [DataRow("195", 949732)]
    [DataRow("196", 949748)]
    public void ExactSecondaryInstallationExceptionIsRetainedWithoutSupplyingAdditionalKillCredit(string id, int pid)
    {
        foreach (string guid in C.CancellationGuids) { Assert.IsTrue(V.Secondary(id, pid, guid, C.Cancellation, 2, SecondaryMessage, SecondaryStack)); }
    }

    [TestMethod]
    [DataRow("selector")]
    [DataRow("pid")]
    [DataRow("guid")]
    [DataRow("method")]
    [DataRow("outcome")]
    [DataRow("exception")]
    [DataRow("cleanup")]
    [DataRow("deadline")]
    [DataRow("origin")]
    [DataRow("stack-content")]
    public void SecondaryProofNeverAcceptsAnArbitraryCoreExceptionOrOtherHost(string change)
    {
        string id = "195", guid = C.CancellationGuids[0], method = C.Cancellation, message = SecondaryMessage, stack = SecondaryStack; int pid = 949732, outcome = 2;
        switch (change)
        {
            case "selector": id = "197"; break;
            case "pid": pid = 949748; break;
            case "guid": guid = C.RegistrationGuids[0]; break;
            case "method": method = C.Registration; break;
            case "outcome": outcome = 1; break;
            case "exception": message = message.Replace("ArgumentException", "NullReferenceException", StringComparison.Ordinal); break;
            case "cleanup": stack += "\n at TestCleanup()"; break;
            case "deadline": message += "TimeoutException"; break;
            case "origin": stack = stack.Replace("UseCisHealthcheck", "DisposeAsync", StringComparison.Ordinal); break;
            case "stack-content": stack = stack.TrimEnd(); break;
        }
        Assert.IsFalse(V.Secondary(id, pid, guid, method, outcome, message, stack));
    }

    [TestMethod]
    public void AspSymbolAdmissionIsSeparateFromTheUnitProfileAndRequiresBothExactHashes()
    { Assert.IsTrue(V.AdmitsSymbolRole(C.TestRoles + "CIS.Core.pdb", C.StalePdb, C.CleanPdb)); }

    [TestMethod]
    [DataRow("2026-10-22T11:07:14.0158438+00:00")]
    [DataRow("2026-10-22T11:07:14.9999999+00:00")]
    public void WholeSecondShellCompletionRetainsItsHonestExclusiveUpperBound(string finish)
    {
        HealthcheckMutationFiniteAspNetEvidence.BaselineWindow("2026-10-22T11:05:47Z", "2026-10-22T11:07:14Z", "2026-10-22T11:07:11.9759483+00:00", finish);
    }

    [TestMethod]
    [DataRow("upper-bound")]
    [DataRow("after-bound")]
    [DataRow("reversed-trx")]
    [DataRow("before-launch")]
    [DataRow("reversed-shell")]
    [DataRow("invented-fraction")]
    public void SecondPrecisionDoesNotExcuseMissingOrReversedChronology(string change)
    {
        string started = "2026-10-22T11:05:47Z", completed = "2026-10-22T11:07:14Z", trxStart = "2026-10-22T11:07:11.9759483+00:00", trxFinish = "2026-10-22T11:07:14.0158438+00:00";
        switch (change)
        {
            case "upper-bound": trxFinish = "2026-10-22T11:07:15.0000000+00:00"; break;
            case "after-bound": trxFinish = "2026-10-22T11:07:15.0000001+00:00"; break;
            case "reversed-trx": trxFinish = trxStart; break;
            case "before-launch": trxStart = "2026-10-22T11:05:46.9999999+00:00"; break;
            case "reversed-shell": started = "2026-10-22T11:07:15Z"; break;
            case "invented-fraction": completed = "2026-10-22T11:07:14.0000000Z"; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationFiniteAspNetEvidence.BaselineWindow(started, completed, trxStart, trxFinish));
    }

    [TestMethod]
    [DataRow("2026-10-22T11:10:30.458861+00:00", "2026-10-22T11:10:30.4588610+00:00")]
    [DataRow("2026-10-22T11:20:19.03957+00:00", "2026-10-22T11:20:19.0395700+00:00")]
    [DataRow("2026-10-22T11:20:19.1Z", "2026-10-22T11:20:19.1000000+00:00")]
    public void SerializerTrimmedFractionalDigitsKeepTheirExactUtcValue(string actual, string untrimmed)
    { Assert.AreEqual(HealthcheckMutationFiniteAspNetEvidence.ObservationUtc(untrimmed), HealthcheckMutationFiniteAspNetEvidence.ObservationUtc(actual)); }

    [TestMethod]
    [DataRow("2026-10-22T11:10:30.45886101+00:00")]
    [DataRow("2026-10-22T11:10:30.458861+01:00")]
    [DataRow("2026-02-31T11:10:30Z")]
    [DataRow("2026-10-22T11:10:30")]
    [DataRow("2026-10-22T11:10:30.+00:00")]
    [DataRow("2026-10-22T11:10:30,458861+00:00")]
    [DataRow("2026-10-22T11:10:30Z trailing")]
    [DataRow("")]
    public void AspObservationParserRejectsOtherClockFormatsAndInventedPrecision(string value)
    { Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationFiniteAspNetEvidence.ObservationUtc(value)); }

    [TestMethod]
    public void OriginalRecorderAbsenceAndLaterEmptySelectorObservationsStayDistinct()
    {
        HealthcheckMutationFiniteAspNetEvidence.SelectorObservation(new JsonObject(), true);
        HealthcheckMutationFiniteAspNetEvidence.SelectorObservation(new JsonObject { ["selectorTypes"] = new JsonArray() }, false);
    }

    [TestMethod]
    [DataRow("invented-pre")]
    [DataRow("missing-post")]
    [DataRow("null-post")]
    [DataRow("active-post")]
    public void ASelectorObservationCannotBeBorrowedAcrossRecorderPhases(string change)
    {
        JsonObject row = new() { ["selectorTypes"] = new JsonArray() }; bool preNative = false;
        switch (change)
        {
            case "invented-pre": preNative = true; break;
            case "missing-post": row.Remove("selectorTypes"); break;
            case "null-post": row["selectorTypes"] = null; break;
            case "active-post": row["selectorTypes"]!.AsArray().Add("StrykerX.MutantControl"); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationFiniteAspNetEvidence.SelectorObservation(row, preNative));
    }

    [TestMethod]
    [DataRow("unit-role")]
    [DataRow("source-role")]
    [DataRow("unknown-old")]
    [DataRow("unknown-clean")]
    [DataRow("unit-old")]
    [DataRow("already-clean")]
    public void SymbolRoleHashSelectionCannotNormalizeAnotherRunOrRuntimeBody(string change)
    {
        string role = C.TestRoles + "CIS.Core.pdb", old = C.StalePdb, clean = C.CleanPdb;
        switch (change)
        {
            case "unit-role": role = role.Replace("IntegrationTests", "UnitTests", StringComparison.Ordinal); break;
            case "source-role": role = C.CoreRoles + "CIS.Core.pdb"; break;
            case "unknown-old": old = new string('a', 64); break;
            case "unknown-clean": clean = new string('b', 64); break;
            case "unit-old": old = HealthcheckMutationFiniteAssertionCatalog.CancellationStaleTestPdbSha256; break;
            case "already-clean": old = clean; break;
        }
        Assert.IsFalse(V.AdmitsSymbolRole(role, old, clean));
    }

    private static JsonObject Document()
    {
        JsonObject Pin() => new() { ["path"] = "fixture.bin", ["sha256"] = new string('a', 64), ["bytes"] = 1L };
        return new JsonObject
        {
            ["schema"] = C.Schema,
            ["proofKind"] = HealthcheckMutationFiniteAssertionValidation.ProofKind,
            ["osLane"] = "ubuntu",
            ["original"] = new JsonObject { ["reportSha256"] = C.OriginalReport, ["definitionIdentities"] = new JsonArray(C.Targets.Select(value => (JsonNode?)JsonValue.Create(value.Identity)).ToArray()) },
            ["evidence"] = new JsonObject { ["root"] = "synthetic-inputs", ["inventory"] = Pin() },
            ["sources"] = new JsonObject { ["originalRoot"] = "synthetic-source", ["originalManifest"] = Pin() },
            ["toolchain"] = new JsonObject { ["strykerCli"] = Pin(), ["strykerCore"] = Pin(), ["objectModel"] = Pin() }
        };
    }

    private static (JsonObject Parent, JsonObject Finite) Configurations()
    {
        JsonObject settings = new()
        {
            ["test-projects"] = new JsonArray("../../tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj"),
            ["test-case-filter"] = "OldFullCohort",
            ["mutate"] = new JsonArray(C.SourcePath),
            ["mutation-level"] = "Complete",
            ["coverage-analysis"] = "off",
            ["configuration"] = "Release",
            ["target-framework"] = "net10.0",
            ["test-runner"] = "vstest",
            ["concurrency"] = 2,
            ["disable-bail"] = true,
            ["additional-timeout"] = 750000,
            ["thresholds"] = new JsonObject { ["high"] = 100, ["low"] = 90, ["break"] = 90 }
        };
        JsonObject parent = new() { ["stryker-config"] = settings.DeepClone() }; settings["mutate"] = new JsonArray(C.SourcePath + "{1194..1245}", C.SourcePath + "{5499..5505}"); settings["test-case-filter"] = C.Filter;
        return (parent, new JsonObject { ["stryker-config"] = settings });
    }

    private const string SecondaryMessage = "Test method " + C.Cancellation + " threw exception:\r\nSystem.ArgumentException: Supply an explicit details authorization predicate when details are enabled. (Parameter 'authorizeDetails')";
    private const string SecondaryStack = "   at CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.UseCisHealthcheck(IApplicationBuilder app, IHealthSnapshotSource source, HealthcheckOptions options, Func`2 authorizeDetails) in " + C.NativeRoot + "/Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs:line 29\n   at " + C.Cancellation + "(String method) in " + C.SourceRoot + "/" + C.TestPath + ":line 333\n   at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments, CancellationTokenSource timeoutTokenSource, TestInvocationState invocationState)\n";
}
