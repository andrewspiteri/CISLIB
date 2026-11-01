using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

internal static class DependencyCheckRunner
{
    internal sealed record CheckResult(int ExitCode, string StandardOutput, string StandardError);
    private static readonly ConcurrentDictionary<string, Lazy<Task<DependencyChecks.PreparedEvaluation>>> Prepared =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal static async Task<CheckResult> Run(string language, string root, string project, params string[] extra)
    {
        var clock = Stopwatch.StartNew();
        string assets = "-", assembly = "-", package = "-";
        for (int i = 0; i < extra.Length; i += 2)
        {
            switch (extra[i])
            {
                case "-AssetsPath": assets = extra[i + 1]; break;
                case "-AssemblyPath": assembly = extra[i + 1]; break;
                case "-PackagePath": package = extra[i + 1]; break;
                default: throw new ArgumentException("Unknown dependency fixture argument: " + extra[i]);
            }
        }
        bool canonicalMetadata = string.Equals(Path.GetFullPath(language), Path.GetFullPath(root),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && project == "src/CIS.Core/CIS.Core.csproj" && (assets != "-" || package != "-");
        if (canonicalMetadata)
        {
            using var stdout = new StringWriter(CultureInfo.InvariantCulture);
            using var stderr = new StringWriter(CultureInfo.InvariantCulture);
            DependencyChecks.PreparedEvaluation prepared = await Prepare(language, Remaining(clock));
            int code = await DependencyChecks.RunPreparedAsync([root, project, assets, assembly, package], prepared, stdout, stderr, Remaining(clock));
            return new(code, stdout.ToString(), stderr.ToString());
        }
#if CIS_MUTATION_TESTS
        return await RunFresh(language, root, project, assets, assembly, package);
#else
        return await RunInProcessFresh(root, project, assets, assembly, package, Remaining(clock));
#endif
    }

    internal static Task<DependencyChecks.PreparedEvaluation> Prepare(string language, TimeSpan timeout)
        => Prepared.GetOrAdd(Path.GetFullPath(language), path => new(() => DependencyChecks.PreparedEvaluation.CaptureAsync(path, timeout))).Value;

    internal static async Task<CheckResult> RunFresh(string language, string root, string project, string assets = "-", string assembly = "-", string package = "-")
    {
        string tool = Path.Combine(language, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll");
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", [tool, "dependencies", root, project, assets, assembly, package], root, TimeSpan.FromMinutes(1));
        return new(result.ExitCode, result.StandardOutput, result.StandardError);
    }

    internal static async Task<CheckResult> RunInProcessFresh(string root, string project, string assets = "-", string assembly = "-", string package = "-", TimeSpan? timeout = null)
    {
        // Keep every actual fresh SDK query and validator. Only the redundant
        // checker host is removed; requests never share results or writers.
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);
        int code = await DependencyChecks.RunFreshAsync([root, project, assets, assembly, package], stdout, stderr, timeout ?? TimeSpan.FromMinutes(1));
        return new(code, stdout.ToString(), stderr.ToString());
    }

    internal sealed record MetadataCase(Type Owner, string Method, string[] Arguments,
        Func<Func<string[], Task<CheckResult>>, Task<CheckResult>> Execute, int ExpectedValidatorCalls);
    private sealed record InputObservation(string Argument, string Path, long Bytes, string Sha256);
    private sealed record ValidatorObservation(IReadOnlyList<InputObservation> Inputs, CheckResult Result);
    private sealed record MetadataObservation(string Id, CheckResult Result, IReadOnlyList<ValidatorObservation> Validators);
#if !CIS_MUTATION_TESTS
    private static readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<string, MetadataObservation>>>> Metadata =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
#endif

    internal static void AddMetadataCases(List<MetadataCase> cases, Type owner, string method,
        Func<string[], Func<string[], Task<CheckResult>>, Task<CheckResult>> execute, int validatorCalls)
    {
        MethodInfo selected = owner.GetMethod(method) ?? throw new InvalidDataException("Missing canonical metadata case: " + method);
        DataRowAttribute[] rows = selected.GetCustomAttributes<DataRowAttribute>().ToArray();
        string[][] arguments = rows.Length == 0 ? [[]] : rows.Select(row => row.Data.Select(value =>
            value as string ?? throw new InvalidDataException("Canonical metadata rows require their original string arguments.")).ToArray()).ToArray();
        foreach (string[] values in arguments)
        { cases.Add(new(owner, method, values, check => execute(values, check), validatorCalls)); }
    }

    internal static async Task<CheckResult> MetadataResult(string language, Type owner, string method, string[] arguments,
        Func<Func<string[], Task<CheckResult>>, Task<CheckResult>> execute)
    {
#if CIS_MUTATION_TESTS
        return await execute(extra => Run(language, language, "src/CIS.Core/CIS.Core.csproj", extra));
#else
        IReadOnlyDictionary<string, MetadataObservation> rows = await Metadata.GetOrAdd(Path.GetFullPath(language),
            path => new(() => CaptureMetadataBatch(path))).Value;
        string key = MetadataId(owner, method, arguments);
        if (!rows.TryGetValue(key, out MetadataObservation? row)) { throw new InvalidDataException("Missing actual metadata batch row: " + key); }
        Console.WriteLine("CIS canonical metadata batch input: " + JsonSerializer.Serialize(row));
        return row.Result;
#endif
    }

    private static string MetadataId(Type owner, string method, string[] arguments)
        => owner.FullName + "." + method + ":" + JsonSerializer.Serialize(arguments);

#if !CIS_MUTATION_TESTS
    private static async Task<IReadOnlyDictionary<string, MetadataObservation>> CaptureMetadataBatch(string language)
    {
        var clock = Stopwatch.StartNew();
        var cases = new List<MetadataCase>();
        DependencyBoundaryTests.AddMetadataBatchCases(cases);
        MicrosoftLoggingPackageBoundaryTests.AddMetadataBatchCases(cases);
        TypeManagerBoundaryTests.AddMetadataBatchCases(cases);
        if (cases.Count != 29 || cases.Sum(item => item.ExpectedValidatorCalls) != 40)
        { throw new InvalidDataException("The explicit canonical metadata batch must retain all29 original rows and40 actual validators."); }
        var observations = new Dictionary<string, MetadataObservation>(StringComparer.Ordinal);
        DependencyChecks.PreparedEvaluation prepared = await Prepare(language, Remaining(clock));
        await DependencyChecks.RunPreparedMetadataBatchAsync(language, prepared, async validate =>
        {
            foreach (MetadataCase item in cases)
            {
                var calls = new List<ValidatorObservation>();
                CheckResult result = await item.Execute(async extra =>
                {
                    string assets = "-", assembly = "-", package = "-";
                    for (int i = 0; i < extra.Length; i += 2)
                    {
                        switch (extra[i])
                        {
                            case "-AssetsPath": assets = extra[i + 1]; break;
                            case "-AssemblyPath": assembly = extra[i + 1]; break;
                            case "-PackagePath": package = extra[i + 1]; break;
                            default: throw new ArgumentException("Unknown canonical metadata argument: " + extra[i]);
                        }
                    }
                    InputObservation[] inputs = ObserveInputs(assets, assembly, package);
                    using var stdout = new StringWriter(CultureInfo.InvariantCulture);
                    using var stderr = new StringWriter(CultureInfo.InvariantCulture);
                    int code = await validate([language, "src/CIS.Core/CIS.Core.csproj", assets, assembly, package], stdout, stderr);
                    if (!inputs.SequenceEqual(ObserveInputs(assets, assembly, package)))
                    { throw new InvalidDataException("A private canonical metadata input changed during its actual validator."); }
                    var observed = new CheckResult(code, stdout.ToString(), stderr.ToString());
                    calls.Add(new(Array.AsReadOnly(inputs), observed));
                    return observed;
                });
                if (calls.Count != item.ExpectedValidatorCalls)
                { throw new InvalidDataException("A canonical metadata row omitted or repeated its actual validators."); }
                string id = MetadataId(item.Owner, item.Method, item.Arguments);
                if (!observations.TryAdd(id, new(id, result, Array.AsReadOnly(calls.ToArray()))))
                { throw new InvalidDataException("Duplicate canonical metadata row: " + id); }
            }
        }, Remaining(clock));
        _ = Remaining(clock);
        // Every private fixture has disposed and both physical input guards have
        // completed before any immutable per-row observation becomes visible.
        return new ReadOnlyDictionary<string, MetadataObservation>(observations);
    }

    private static InputObservation[] ObserveInputs(string assets, string assembly, string package)
        => new[] { ("assets", assets), ("assembly", assembly), ("package", package) }
            .Where(input => input.Item2 != "-")
            .Select(input => new InputObservation(input.Item1, Path.GetFullPath(input.Item2), new FileInfo(input.Item2).Length,
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input.Item2))))).ToArray();
#endif

    private static TimeSpan Remaining(Stopwatch clock)
    {
        TimeSpan remaining = TimeSpan.FromMinutes(1) - clock.Elapsed;
        if (remaining <= TimeSpan.Zero) { throw new TimeoutException("The dependency fixture's shared 60-second deadline expired."); }
        return remaining;
    }
}
