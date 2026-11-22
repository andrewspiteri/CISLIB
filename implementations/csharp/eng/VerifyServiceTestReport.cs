#:property PublishAot=false
#:property SelfContained=false
#:property UseAppHost=false
#:property RuntimeFrameworkVersion=10.0.12
#:property RollForward=Disable
#:property NuGetAudit=false
#:property RestorePackagesWithLockFile=false

using System.Globalization;
using System.Xml;
using System.Xml.Linq;

try
{
    if (args.Length != 2 || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nativeExitCode))
    {
        throw new InvalidDataException("Supply one service TRX path and the native test command's exit code.");
    }

    if (nativeExitCode != 0)
    {
        throw new InvalidDataException($"The native service test command failed with exit code {nativeExitCode}.");
    }

    const long maximumBytes = 16 * 1024 * 1024;
    var file = new FileInfo(args[0]);
    if (!file.Exists || file.Length is < 1 or > maximumBytes)
    {
        throw new InvalidDataException("The service TRX is missing, empty or exceeds 16 MiB.");
    }

    var settings = new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = maximumBytes,
        IgnoreComments = true
    };
    using XmlReader reader = XmlReader.Create(file.FullName, settings);
    XDocument document = XDocument.Load(reader);
    XNamespace schema = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    XElement root = document.Root ?? throw new InvalidDataException("The service TRX has no root.");
    if (root.Name != schema + "TestRun")
    {
        throw new InvalidDataException("The service report is not a native VSTest TRX.");
    }

    XElement summary = root.Element(schema + "ResultSummary")
        ?? throw new InvalidDataException("The service TRX has no run summary.");
    string? outcome = (string?)summary.Attribute("outcome");
    if (outcome is not ("Completed" or "Passed"))
    {
        throw new InvalidDataException("The service test run did not complete successfully.");
    }

    XElement counters = summary.Element(schema + "Counters")
        ?? throw new InvalidDataException("The service TRX has no native counters.");
    int total = Count(counters, "total", required: true);
    int executed = Count(counters, "executed", required: true);
    int passed = Count(counters, "passed", required: true);
    string[] unsuccessfulCounters =
    [
        "error", "failed", "timeout", "aborted", "inconclusive", "passedButRunAborted",
        "notRunnable", "notExecuted", "disconnected", "warning", "inProgress", "pending"
    ];
    if (total == 0 || executed != total || passed != total
        || unsuccessfulCounters.Any(name => Count(counters, name) != 0))
    {
        throw new InvalidDataException("The selected service cases are empty, incomplete, failed or skipped.");
    }

    XElement results = root.Element(schema + "Results")
        ?? throw new InvalidDataException("The service TRX has no selected test results.");
    XElement[] cases = results.Elements().ToArray();
    if (cases.Length != total || cases.Any(result => result.Name != schema + "UnitTestResult"
        || (string?)result.Attribute("outcome") != "Passed"))
    {
        throw new InvalidDataException("Every selected service result must be present and Passed.");
    }
    if (summary.Descendants(schema + "RunInfo").Any(info =>
        (string?)info.Attribute("outcome") is "Error" or "Failed" or "Aborted" or "Timeout"))
    {
        throw new InvalidDataException("The native service run reported an infrastructure or cleanup failure.");
    }

    string elapsed = "unknown";
    XElement? times = root.Element(schema + "Times");
    if (DateTimeOffset.TryParse((string?)times?.Attribute("start"), CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTimeOffset start)
        && DateTimeOffset.TryParse((string?)times?.Attribute("finish"), CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTimeOffset finish)
        && finish >= start)
    {
        elapsed = (finish - start).TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + "s";
    }
    Console.WriteLine($"Service tests: total={total}, passed={passed}, failed=0, skipped=0, elapsed={elapsed}.");
    return 0;
}
catch (Exception failure) when (failure is InvalidDataException or IOException or UnauthorizedAccessException or XmlException or InvalidOperationException)
{
    Console.Error.WriteLine("Service test verification failed: " + failure.Message);
    return 1;
}

static int Count(XElement counters, string name, bool required = false)
{
    string? value = (string?)counters.Attribute(name);
    if (value is null && !required)
    {
        return 0;
    }
    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int count))
    {
        throw new InvalidDataException("The service TRX has an invalid native counter.");
    }
    return count;
}
