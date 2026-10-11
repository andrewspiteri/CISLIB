using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConfigurationDiagnosticTests
{
    [TestMethod]
    public void ExceptionCopiesAndEnumeratesIssueInputOnceInOrder()
    {
        var first = new ConfigurationIssue(ConfigurationIssueCodes.InvalidToken, "CORE_LOG_LEVEL", "safe first");
        var second = new ConfigurationIssue(ConfigurationIssueCodes.OutOfRange, "CORE_LOG_QUEUE_CAPACITY", "safe second");
        var caller = new List<ConfigurationIssue> { first, second };
        int enumerations = 0;
        IEnumerable<ConfigurationIssue> Enumerate()
        {
            enumerations++;
            foreach (ConfigurationIssue issue in caller)
            {
                yield return issue;
            }
        }
        var exception = new CoreConfigurationException(Enumerate());
        caller.Clear();
        Assert.AreEqual(1, enumerations);
        CollectionAssert.AreEqual(new[] { first, second }, exception.Issues.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ConfigurationIssue>)exception.Issues).Clear());
        Assert.AreEqual(1, enumerations);
        Assert.AreEqual(0, new CoreConfigurationException([]).Issues.Count);
    }

    [TestMethod]
    [DataRow("code")]
    [DataRow("key")]
    [DataRow("explanation")]
    public void IssueRejectsNullArgumentsWithStableParameterNames(string parameter)
    {
        ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => new ConfigurationIssue(parameter == "code" ? null! : "code", parameter == "key" ? null! : "key", parameter == "explanation" ? null! : "explanation"));
        Assert.AreEqual(parameter, exception.ParamName);
    }

    [TestMethod]
    public void ExceptionRejectsNullSequenceAndNullElements()
    {
        Assert.AreEqual("issues", Assert.ThrowsExactly<ArgumentNullException>(() => new CoreConfigurationException(null!)).ParamName);
        Assert.AreEqual("issues", Assert.ThrowsExactly<ArgumentException>(() => new CoreConfigurationException([null!])).ParamName);
    }

    [TestMethod]
    public void DiagnosticsHaveTheActualProposedPublicShapeCodesAndNamedConstructors()
    {
        string root = Directory.GetParent(Directory.GetParent(Directory.GetParent(ConfigurationFixtureAdapter.FindFixture())!.FullName)!.FullName)!.FullName;
        JsonObject contract = ConfigurationFixtureAdapter.ReadDocument(Path.Combine(root, "implementations", "csharp", "verification", "contracts", "startup", "surface.json"))["diagnostics"]!.AsObject();
        foreach (Type type in new[] { typeof(ConfigurationIssue), typeof(CoreConfigurationException) })
        {
            Assert.IsTrue(type.IsPublic && type.IsSealed);
            Assert.AreEqual("CIS.Core", type.Assembly.GetName().Name);
            Assert.AreEqual("CIS.Configuration", type.Namespace);
        }
        CollectionAssert.AreEqual(contract["issueProperties"]!.AsArray().Select(value => value!.GetValue<string>()).OrderBy(name => name, StringComparer.Ordinal).ToArray(), typeof(ConfigurationIssue).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.IsTrue(typeof(ConfigurationIssue).GetProperties().All(property => property.PropertyType == typeof(string) && property.SetMethod is null));
        CollectionAssert.AreEqual(new[] { "code", "key", "explanation" }, typeof(ConfigurationIssue).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name).ToArray());
        ParameterInfo parameter = typeof(CoreConfigurationException).GetConstructors().Single().GetParameters().Single();
        Assert.AreEqual("issues", parameter.Name);
        Assert.AreEqual(typeof(IEnumerable<ConfigurationIssue>), parameter.ParameterType);
        PropertyInfo issues = typeof(CoreConfigurationException).GetProperty("Issues")!;
        Assert.AreEqual(typeof(IReadOnlyList<ConfigurationIssue>), issues.PropertyType);
        Assert.IsNull(issues.SetMethod);
        Assert.IsTrue(typeof(ConfigurationIssueCodes).IsPublic && typeof(ConfigurationIssueCodes).IsAbstract && typeof(ConfigurationIssueCodes).IsSealed);
        FieldInfo[] fields = typeof(ConfigurationIssueCodes).GetFields(BindingFlags.Static | BindingFlags.Public);
        CollectionAssert.AreEquivalent(contract["codes"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray(), fields.Select(field => field.Name).ToArray());
        Assert.IsTrue(fields.All(field => field.IsLiteral && field.FieldType == typeof(string) && (string)field.GetRawConstantValue()! == field.Name));
    }

    [TestMethod]
    public void ExceptionFormattingDoesNotAutomaticallyDumpEvenCallerSuppliedUnsafeIssues()
    {
        var issue = new ConfigurationIssue("caller-secret-code", "caller-secret-key", "caller-secret-value");
        var exception = new CoreConfigurationException([issue]);
        Assert.IsFalse(exception.ToString().Contains("caller-secret", StringComparison.Ordinal));
        Assert.IsFalse(issue.ToString()!.Contains("caller-secret", StringComparison.Ordinal));
        Assert.AreEqual("caller-secret-value", exception.Issues[0].Explanation);
        Assert.IsNull(exception.InnerException);
    }
}
