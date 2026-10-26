using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace CIS.Verification;

internal static class HealthcheckMutationBaselineValidation
{
    private static readonly XNamespace NativeNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    internal static string[] ReadPassedCases(string path, string family)
    {
        string assembly = family switch
        {
            "unit" => "CIS.Core.UnitTests.dll",
            "integration" => "CIS.Core.IntegrationTests.dll",
            _ => throw new InvalidDataException("Unknown native baseline family.")
        };
        XDocument document;
        try
        {
            using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 128L * 1024 * 1024
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException error) { throw new InvalidDataException("Malformed or unsupported native baseline XML.", error); }
        XElement root = document.Root ?? throw new InvalidDataException("Missing native TRX root.");
        Need(root.Name == NativeNamespace + "TestRun", "Expected the native TRX TestRun namespace and root.");
        XElement resultsElement = One(root, "Results"), definitionsElement = One(root, "TestDefinitions"), entriesElement = One(root, "TestEntries");
        XElement[] results = OnlyChildren(resultsElement, "UnitTestResult"), definitions = OnlyChildren(definitionsElement, "UnitTest"), entries = OnlyChildren(entriesElement, "TestEntry");
        Need(results.Length > 0 && definitions.Length == results.Length && entries.Length == results.Length,
            "Native baseline results, definitions and entries must be nonempty and one-to-one.");
        XElement summary = One(root, "ResultSummary");
        Need(Required(summary, "outcome") is "Completed" or "Passed", "Native baseline did not complete normally.");
        XElement counters = One(summary, "Counters");
        foreach (string name in new[] { "total", "executed", "passed" })
        { Need(Count(counters, name) == results.Length, "Native baseline counters disagree with actual results: " + name); }
        foreach (XAttribute counter in counters.Attributes())
        {
            Need(counter.Name.NamespaceName.Length == 0, "Unsupported native counter namespace.");
            if (counter.Name.LocalName is "total" or "executed" or "passed") { continue; }
            Need(Count(counters, counter.Name.LocalName) == 0, "Native baseline has a contradictory non-passing counter: " + counter.Name);
        }
        Need(!root.Descendants(NativeNamespace + "ErrorInfo").Any(), "Native baseline contains error details despite passing results.");
        foreach (XElement info in root.Descendants(NativeNamespace + "RunInfo"))
        {
            Need(Required(info, "outcome") is "Passed" or "Completed" or "Warning" or "Information",
                "Native baseline has a failed or unsupported run-information outcome.");
        }

        var methods = new Dictionary<string, (string Execution, string Method)>(StringComparer.Ordinal);
        var definitionExecutions = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement definition in definitions)
        {
            string id = GuidAttribute(definition, "id"), execution = GuidAttribute(One(definition, "Execution"), "id");
            XElement method = One(definition, "TestMethod");
            string codeBase = Required(method, "codeBase").Replace('\\', '/');
            Need(codeBase.Equals(assembly, StringComparison.OrdinalIgnoreCase) || codeBase.EndsWith("/" + assembly, StringComparison.OrdinalIgnoreCase),
                "Native baseline method belongs to a different test family.");
            string name = Required(method, "className") + "." + Required(method, "name");
            Need(definitionExecutions.Add(execution) && methods.TryAdd(id, (execution, name)), "Duplicate native definition test or execution GUID.");
        }
        var entryExecutions = new Dictionary<string, string>(StringComparer.Ordinal);
        var uniqueEntryExecutions = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement entry in entries)
        {
            string id = GuidAttribute(entry, "testId"), execution = GuidAttribute(entry, "executionId");
            Need(uniqueEntryExecutions.Add(execution) && entryExecutions.TryAdd(id, execution), "Duplicate native entry test or execution GUID.");
        }
        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        var resultExecutions = new HashSet<string>(StringComparer.Ordinal);
        var cohort = new List<string>();
        foreach (XElement result in results)
        {
            Need(Required(result, "outcome") == "Passed", "Native baseline contains a failed, skipped or unexecuted case.");
            string id = GuidAttribute(result, "testId"), execution = GuidAttribute(result, "executionId");
            Need(resultIds.Add(id) && resultExecutions.Add(execution), "Duplicate native result test or execution GUID.");
            Need(methods.TryGetValue(id, out var definition) && definition.Execution == execution
                && entryExecutions.TryGetValue(id, out string? entryExecution) && entryExecution == execution,
                "Native result, definition and entry execution identities disagree.");
            cohort.Add(id + "\t" + definition.Method);
        }
        return cohort.Order(StringComparer.Ordinal).ToArray();
    }

    private static XElement One(XElement parent, string name)
    {
        XElement[] found = parent.Elements(NativeNamespace + name).ToArray();
        Need(found.Length == 1, "Missing or repeated native TRX section: " + name);
        return found[0];
    }

    private static XElement[] OnlyChildren(XElement parent, string name)
    {
        XElement[] children = parent.Elements().ToArray();
        Need(children.All(element => element.Name == NativeNamespace + name), "Unsupported native cohort entry.");
        return children;
    }

    private static string Required(XElement element, string name)
    {
        string? value = (string?)element.Attribute(name);
        Need(!string.IsNullOrWhiteSpace(value) && value == value.Trim() && !value.Any(character => character is '\t' or '\r' or '\n'),
            "Missing or malformed native attribute: " + name);
        return value;
    }

    private static string GuidAttribute(XElement element, string name)
    {
        Need(Guid.TryParseExact(Required(element, name), "D", out Guid value) && value != Guid.Empty, "Invalid native test/execution GUID.");
        return value.ToString("D");
    }

    private static long Count(XElement element, string name)
    {
        Need(long.TryParse(Required(element, name), NumberStyles.None, CultureInfo.InvariantCulture, out long value), "Malformed native counter: " + name);
        return value;
    }

    private static void Need([DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) { throw new InvalidDataException(message); } }
}
