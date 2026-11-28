using CIS.RuleEngine;

namespace CIS.Tests.RuleEngineHostFixture;

public sealed class RuleEngineFixtureConfiguration(params RuleDefinition[] supportedSchemas) : RuleEngineConfiguration(supportedSchemas)
{
}
