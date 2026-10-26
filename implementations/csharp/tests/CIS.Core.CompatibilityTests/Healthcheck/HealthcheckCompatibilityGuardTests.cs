using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.CompatibilityTests.Healthcheck.HealthcheckCompatibilityInputs;

namespace CIS.Tests.CompatibilityTests.Healthcheck;

[TestClass]
public sealed class HealthcheckCompatibilityGuardTests
{
    [TestMethod]
    [DataRow("M void Clear()", "M void Reset()")]
    [DataRow("M HealthSnapshot CaptureSnapshot()", "M object CaptureSnapshot()")]
    [DataRow("M void Flag(HealthDiagnostic? diagnostic=null)", "M void Flag(HealthDiagnostic? reason=null)")]
    [DataRow("M void Flag(HealthDiagnostic? diagnostic=null)", "M void Flag(HealthDiagnostic? diagnostic)")]
    [DataRow("P string? Name get", "P string Name get")]
    [DataRow("P int MaxMonitors get init", "P int MaxMonitors get set")]
    [DataRow("P bool IsCritical get init required", "P bool IsCritical get init")]
    [DataRow("Healthy=0", "Healthy=1")]
    public void ApiContractMatcherRejectsSpecifiedCompatibilityBreaks(string required, string changed)
        => Assert.ThrowsExactly<InvalidDataException>(() => RequireMembers([required], [changed]));

    [TestMethod]
    public void ApiContractMatcherRejectsRemovalAndAllowsCompatibleAdditions()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => RequireMembers(["M void Clear()"], []));
        RequireMembers(["M void Clear()"], ["M void Clear()", "M void NewOptionalOperation()"]);
    }

    [TestMethod]
    [DataRow("true", "1")]
    [DataRow("\"9223372036854775807\"", "9223372036854775807")]
    [DataRow("2", "2.0")]
    [DataRow("2", "2e0")]
    [DataRow("[1,2]", "[2,1]")]
    [DataRow("{\"unit\":null}", "{}")]
    [DataRow("{\"a\":1}", "{\"a\":1,\"b\":2}")]
    [DataRow("\"9223372036854775807\"", "\"9223372036854775806\"")]
    public void ObservationMatcherRejectsTypePrecisionOrderingAndOmissionChanges(string expected, string changed)
        => Assert.IsFalse(Same(JsonNode.Parse(expected), JsonNode.Parse(changed)));

    [TestMethod]
    public void ObservationMatcherIgnoresOnlyObjectPropertyOrder()
        => Assert.IsTrue(Same(JsonNode.Parse("{\"a\":1,\"b\":true}"), JsonNode.Parse("{\"b\":true,\"a\":1}")));
}
