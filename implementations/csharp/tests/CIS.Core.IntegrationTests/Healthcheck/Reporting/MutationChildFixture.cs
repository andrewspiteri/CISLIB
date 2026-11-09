using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using CIS.TestSupport;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

// A real transport child must execute the same instrumented Core and mutant as
// its parent. Native dotnet test uses its normal fixture output unchanged.
internal static class MutationChildFixture
{
    private static readonly ConditionalWeakTable<ProcessStartInfo, TemporaryDirectory> Stages = new();

    internal static string Prepare(string assemblyPath, ProcessStartInfo start)
    {
        Assembly core = typeof(HealthcheckService).Assembly;
        Type? controller = core.GetTypes().SingleOrDefault(type => type.Name == "MutantControl"
            && type.Namespace?.StartsWith("Stryker", StringComparison.Ordinal) == true);
        if (controller is null) { return assemblyPath; }
        if (controller.GetField("CaptureCoverage", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is not false)
        {
            throw new InvalidOperationException("Transport child mutation tests require Stryker coverage-analysis=off.");
        }
        MethodInfo active = controller.GetMethod("IsActive", BindingFlags.Public | BindingFlags.Static, [typeof(int)])
            ?? throw new InvalidOperationException("The Stryker mutation selector is unavailable.");
        _ = active.Invoke(null, [int.MinValue]);
        int id = (int)(controller.GetField("ActiveMutant", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("The Stryker active mutant is unavailable."));
        var stage = new TemporaryDirectory();
        try
        {
            foreach (string source in Directory.GetFiles(Path.GetDirectoryName(assemblyPath)!))
            {
                File.Copy(source, Path.Combine(stage.Path, Path.GetFileName(source)));
            }
            File.Copy(core.Location, Path.Combine(stage.Path, "CIS.Core.dll"), overwrite: true);
            string symbols = Path.ChangeExtension(core.Location, ".pdb");
            if (File.Exists(symbols)) { File.Copy(symbols, Path.Combine(stage.Path, "CIS.Core.pdb"), overwrite: true); }
            start.Environment.Remove("STRYKER_MUTANT_FILE");
            start.Environment.Remove("STRYKER_COVERAGE_FILE");
            start.Environment["STRYKER_MUTANT_ID_CONTROL_VAR"] = "CIS_TEST_CHILD_MUTANT";
            start.Environment["CIS_TEST_CHILD_MUTANT"] = id.ToString(CultureInfo.InvariantCulture);
            Stages.Add(start, stage);
            return Path.Combine(stage.Path, Path.GetFileName(assemblyPath));
        }
        catch { stage.Dispose(); throw; }
    }

    internal static void Release(ProcessStartInfo start)
    {
        if (Stages.TryGetValue(start, out TemporaryDirectory? stage))
        {
            stage.Dispose();
            Stages.Remove(start);
        }
    }
}
