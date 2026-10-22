using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportProtocolStateCoherenceQualificationTests
{
    private const string Time = "2026-10-20T08:03:00Z";
    private const string Registration = "abcdef01-2345-6789-abcd-ef0123456789";

    [TestMethod]
    public void CollectionInProgressRequiresAnActualJsonBoolean()
    {
        foreach (bool active in new[] { false, true })
        {
            JsonObject envelope = Details();
            Collection(envelope)["inProgress"] = active;
            Assert.IsFalse(Parse(envelope).IsError);
        }
        foreach (JsonNode? invalid in new JsonNode?[] { null, JsonValue.Create(0), JsonValue.Create(1), JsonValue.Create("true"), new JsonObject(), new JsonArray() })
        {
            JsonObject envelope = Details();
            Collection(envelope)["inProgress"] = invalid?.DeepClone();
            Reject(envelope);
        }
        JsonObject missing = Details();
        Collection(missing).Remove("inProgress");
        Reject(missing);
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("uppercase")]
    [DataRow("compact")]
    [DataRow("braced")]
    [DataRow("text")]
    [DataRow("null")]
    [DataRow("number")]
    [DataRow("boolean")]
    [DataRow("missing")]
    public void EachExecutionRequiresItsOwnCanonicalNonemptyRegistrationGuid(string invalid)
    {
        JsonObject envelope = Details();
        JsonObject execution = Execution(envelope);
        execution["registrationId"] = Registration;
        Assert.IsFalse(Parse(envelope).IsError);
        if (invalid == "missing") { execution.Remove("registrationId"); }
        else
        {
            execution["registrationId"] = invalid switch
            {
                "empty" => JsonValue.Create(Guid.Empty.ToString("D")),
                "uppercase" => JsonValue.Create(Registration.ToUpperInvariant()),
                "compact" => JsonValue.Create(Guid.Parse(Registration).ToString("N")),
                "braced" => JsonValue.Create("{" + Registration + "}"),
                "text" => JsonValue.Create("not-a-guid"),
                "null" => null,
                "number" => JsonValue.Create(1),
                "boolean" => JsonValue.Create(true),
                _ => throw new ArgumentOutOfRangeException(nameof(invalid))
            };
        }
        Reject(envelope);
    }

    [TestMethod]
    [DataRow("1")]
    [DataRow("9223372036854775807")]
    public void ExecutionIdentifiersAcceptBothPositiveInt64Endpoints(string identifier)
    {
        JsonObject envelope = Details();
        Execution(envelope)["executionId"] = identifier;
        Assert.IsFalse(Parse(envelope).IsError);
    }

    [TestMethod]
    [DataRow("-1")]
    [DataRow("0")]
    [DataRow("9223372036854775808")]
    [DataRow("18446744073709551615")]
    [DataRow("+1")]
    [DataRow("01")]
    [DataRow("-0")]
    [DataRow("1e1")]
    [DataRow("1.0")]
    [DataRow("")]
    public void ExecutionIdentifiersRejectNonpositiveNoncanonicalAndOutOfInt64Strings(string identifier)
    {
        JsonObject envelope = Details();
        Assert.IsFalse(Parse(envelope).IsError);
        Execution(envelope)["executionId"] = identifier;
        Reject(envelope);
    }

    [TestMethod]
    public void ExecutionIdentifiersAreRequiredDecimalStringsRatherThanOtherJsonScalarTypes()
    {
        foreach (JsonNode? invalid in new JsonNode?[] { null, JsonValue.Create(1), JsonValue.Create(true), new JsonObject(), new JsonArray() })
        {
            JsonObject envelope = Details();
            Execution(envelope)["executionId"] = invalid?.DeepClone();
            Reject(envelope);
        }
        JsonObject missing = Details();
        Execution(missing).Remove("executionId");
        Reject(missing);
    }

    [TestMethod]
    [DataRow("Skipped")]
    [DataRow("ExpectedCancellation")]
    [DataRow("absent")]
    public void UnhealthyFailurePolicyRequiresAPositiveStreakEvenWhenTheLastOutcomeIsNeutralOrAbsent(string lastOutcome)
    {
        JsonObject envelope = Details();
        JsonObject outcomes = Outcomes(envelope);
        outcomes["skipped"] = "1";
        outcomes["expectedCanceled"] = "1";
        if (lastOutcome == "absent") { outcomes.Remove("lastOutcome"); }
        else { outcomes["lastOutcome"] = lastOutcome; }
        Assert.IsFalse(Parse(envelope).IsError, "A neutral outcome preserves the preceding failure streak.");

        outcomes["consecutiveFailures"] = "0";

        Reject(envelope);
    }

    [TestMethod]
    public void LastSuccessfulOutcomeRequiresAnActualSuccessWithoutRelyingOnTheHealthyPolicyPredicate()
    {
        JsonObject envelope = HealthyFailures();
        JsonObject outcomes = Outcomes(envelope);
        outcomes["successful"] = "1";
        outcomes["consecutiveFailures"] = "0";
        outcomes["lastOutcome"] = "Succeeded";
        Assert.IsFalse(Parse(envelope).IsError);

        outcomes["successful"] = "0";

        Reject(envelope);
    }

    [TestMethod]
    public void LastFailedOutcomeRequiresItsStreakEvenWhenTheConfiguredThresholdHasNotMadeThePolicyUnhealthy()
    {
        JsonObject envelope = HealthyFailures();
        JsonObject outcomes = Outcomes(envelope);
        outcomes["successful"] = "1";
        outcomes["consecutiveFailures"] = "1";
        outcomes["lastOutcome"] = "Failed";
        Assert.IsFalse(Parse(envelope).IsError, "A positive failure streak may remain below the configured threshold.");

        outcomes["consecutiveFailures"] = "0";

        Reject(envelope);
    }

    [TestMethod]
    [DataRow("Skipped", "skipped")]
    [DataRow("ExpectedCancellation", "expectedCanceled")]
    public void EachLastNeutralOutcomeRequiresItsOwnCounter(string lastOutcome, string counter)
    {
        JsonObject envelope = Details();
        JsonObject outcomes = Outcomes(envelope);
        outcomes[counter] = "1";
        outcomes["lastOutcome"] = lastOutcome;
        Assert.IsFalse(Parse(envelope).IsError);

        outcomes[counter] = "0";

        Reject(envelope);
    }

    [TestMethod]
    public void SettledPhaseRequiresItsActualTimeEvenWhenNoTotalDurationWasSupplied()
    {
        JsonObject envelope = Details();
        JsonObject execution = MinimalExecution(envelope);
        execution["errorHandlingStatus"] = "NotApplicable";
        execution["cleanupStatus"] = "NotApplicable";
        Assert.IsFalse(Parse(envelope).IsError);

        execution["phase"] = "Settled";

        Reject(envelope);
        execution["settledAtUtc"] = Time;
        Assert.IsFalse(Parse(envelope).IsError);
    }

    [TestMethod]
    [DataRow("Settled", "errorHandlingStatus", "NotStarted")]
    [DataRow("Settled", "errorHandlingStatus", "Running")]
    [DataRow("Settled", "cleanupStatus", "NotStarted")]
    [DataRow("Settled", "cleanupStatus", "Running")]
    [DataRow("Retired", "errorHandlingStatus", "NotStarted")]
    [DataRow("Retired", "errorHandlingStatus", "Running")]
    [DataRow("Retired", "cleanupStatus", "NotStarted")]
    [DataRow("Retired", "cleanupStatus", "Running")]
    public void AnActualSettlementCannotClaimANonterminalStepEvenWithoutStepTimingOrErrors(string phase, string field, string status)
    {
        JsonObject envelope = Details();
        JsonObject execution = MinimalExecution(envelope);
        execution["phase"] = phase;
        execution["settledAtUtc"] = Time;
        execution["errorHandlingStatus"] = "NotApplicable";
        execution["cleanupStatus"] = "NotApplicable";
        Assert.IsFalse(Parse(envelope).IsError);

        execution[field] = status;

        Reject(envelope);
    }

    private static JsonObject Details() => new()
    {
        ["protocolVersion"] = 1,
        ["schemaVersion"] = 1,
        ["view"] = "details",
        ["kind"] = "snapshot",
        ["snapshot"] = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "healthy-after-startup-details.json"))
    };

    private static JsonObject HealthyFailures()
    {
        JsonObject envelope = Details();
        envelope["snapshot"]!["monitors"]![1]!["state"] = "Healthy";
        envelope["snapshot"]!["monitorCounts"]!["failing"] = 0;
        return envelope;
    }

    private static JsonObject Outcomes(JsonObject envelope) => envelope["snapshot"]!["monitors"]![1]!["outcomes"]!.AsObject();
    private static JsonObject Execution(JsonObject envelope) => Outcomes(envelope)["execution"]!.AsObject();
    private static JsonObject Collection(JsonObject envelope) => envelope["snapshot"]!["monitors"]![3]!["collection"]!.AsObject();
    private static JsonObject MinimalExecution(JsonObject envelope)
    {
        var execution = new JsonObject
        {
            ["registrationId"] = Registration,
            ["executionId"] = "3",
            ["phase"] = "Callback",
            ["errorHandlingStatus"] = "NotStarted",
            ["cleanupStatus"] = "NotStarted"
        };
        Outcomes(envelope)["execution"] = execution;
        return execution;
    }
    private static HealthParsedResponse Parse(JsonObject envelope) => HealthReportProtocol.ParseResponse(Encoding.UTF8.GetBytes(envelope.ToJsonString()), HealthReportView.Details, 4_194_304);
    private static void Reject(JsonObject envelope) => Assert.ThrowsExactly<FormatException>(() => Parse(envelope));
}
