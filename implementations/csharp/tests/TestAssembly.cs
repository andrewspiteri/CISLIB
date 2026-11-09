using Microsoft.VisualStudio.TestTools.UnitTesting;

#if CIS_MUTATION_TESTS
[assembly: DoNotParallelize]
#else
[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]
#endif

namespace CIS.Tests;

[TestClass]
public sealed class TestAssemblySetup
{
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        // Fixture builds must not retain compiler servers or reusable MSBuild nodes.
        Environment.SetEnvironmentVariable("UseSharedCompilation", "false");
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
    }
}
