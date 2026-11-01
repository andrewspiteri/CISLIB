using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using G = CIS.Verification.HealthcheckGroupedMutation;

namespace CIS.Tests.Verification;

[TestClass]
public sealed class HealthcheckSdkMsBuildTests
{
    private const string LinuxPath = "/data/private-dotnet/sdk/10.0.401/MSBuild.dll";
    private const string WindowsPath = "C:\\private-dotnet\\sdk\\10.0.401\\MSBuild.dll";
    private const string LinuxHost = "/data/private-dotnet/dotnet";
    private const string WindowsHost = "C:\\private-dotnet\\dotnet.exe";

    [TestMethod]
    [DataRow(LinuxPath)]
    [DataRow(WindowsPath)]
    [DataRow("C:/private-dotnet/sdk/10.0.401/MSBuild.dll")]
    public void AnalysisSelectsTheUniqueAlreadyPinnedSdkWithoutResolvingAnotherInstallation(string path)
    {
        Assert.AreEqual(path, G.SdkMsBuildPath(Tools(path)));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("ambiguous")]
    [DataRow("wrong-sdk")]
    [DataRow("relative")]
    [DataRow("wrong-version")]
    [DataRow("traversal")]
    [DataRow("unhashed")]
    [DataRow("framework")]
    public void AbsentAmbiguousOrUnpinnedSdkCannotFallBackToGlobalFrameworkMsBuild(string change)
    {
        G.Toolchain tools = Tools(LinuxPath);
        tools = change switch
        {
            "missing" => tools with { Files = [] },
            "ambiguous" => tools with { Files = [.. tools.Files, tools.Files[0] with { Path = "/another/sdk/10.0.401/MSBuild.dll" }] },
            "wrong-sdk" => tools with { Sdk = "10.0.402" },
            "relative" => Tools("sdk/10.0.401/MSBuild.dll"),
            "wrong-version" => Tools("/data/private-dotnet/sdk/10.0.402/MSBuild.dll"),
            "traversal" => Tools("/data/../private-dotnet/sdk/10.0.401/MSBuild.dll"),
            "unhashed" => tools with { Files = [tools.Files[0] with { Sha256 = "unverified" }] },
            "framework" => Tools("C:\\VisualStudio\\MSBuild\\Current\\Bin\\MSBuild.dll"),
            _ => throw new AssertFailedException("Unknown control.")
        };
        Assert.ThrowsExactly<InvalidDataException>(() => G.SdkMsBuildPath(tools));
    }

    [TestMethod]
    [DataRow(LinuxPath, LinuxPath)]
    [DataRow(WindowsPath, WindowsPath)]
    [DataRow(WindowsPath, "c:/PRIVATE-DOTNET/sdk/10.0.401/MSBuild.dll")]
    public void ActualRunEnvironmentBindsTheSdkPinWithPlatformCorrectPathComparison(string expected, string actual)
    {
        G.ValidateSdkMsBuildEnvironment(Run(actual), Tools(expected));
    }

    [TestMethod]
    [DataRow("missing-environment")]
    [DataRow("missing-path")]
    [DataRow("null")]
    [DataRow("wrong-type")]
    [DataRow("relative")]
    [DataRow("other-sdk")]
    [DataRow("unpinned-copy")]
    [DataRow("linux-case")]
    [DataRow("framework")]
    public void ReceivingRejectsMissingOrChangedNativeAnalysisEnvironment(string change)
    {
        JsonObject run = Run(LinuxPath);
        switch (change)
        {
            case "missing-environment": run.Remove("environment"); break;
            case "missing-path": run["environment"]!.AsObject().Remove("MSBUILD_EXE_PATH"); break;
            case "null": run["environment"]!["MSBUILD_EXE_PATH"] = null; break;
            case "wrong-type": run["environment"]!["MSBUILD_EXE_PATH"] = true; break;
            case "relative": run["environment"]!["MSBUILD_EXE_PATH"] = "sdk/10.0.401/MSBuild.dll"; break;
            case "other-sdk": run["environment"]!["MSBUILD_EXE_PATH"] = "/data/private-dotnet/sdk/10.0.402/MSBuild.dll"; break;
            case "unpinned-copy": run["environment"]!["MSBUILD_EXE_PATH"] = "/another/sdk/10.0.401/MSBuild.dll"; break;
            case "linux-case": run["environment"]!["MSBUILD_EXE_PATH"] = LinuxPath.ToUpperInvariant(); break;
            case "framework": run["environment"]!["MSBUILD_EXE_PATH"] = "C:\\VisualStudio\\MSBuild\\Current\\Bin\\MSBuild.exe"; break;
            default: throw new AssertFailedException("Unknown control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => G.ValidateSdkMsBuildEnvironment(run, Tools(LinuxPath)));
    }

    [TestMethod]
    [DataRow(LinuxPath, "/data/private-dotnet:/usr/bin:/bin")]
    [DataRow(WindowsPath, "C:\\private-dotnet;C:\\Windows")]
    [DataRow(WindowsPath, "c:/PRIVATE-DOTNET;C:\\Windows")]
    public void AnalysisHostPrefixUsesTheForeignPlatformsDelimiterAndPathRules(string sdk, string path)
    {
        JsonObject run = Run(sdk);
        run["environment"]!["PATH"] = path;
        G.ValidateSdkMsBuildEnvironment(run, Tools(sdk));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("null")]
    [DataRow("wrong-type")]
    [DataRow("empty")]
    [DataRow("global-first")]
    [DataRow("relative-first")]
    [DataRow("unpinned-host")]
    [DataRow("windows-delimiter")]
    [DataRow("renamed-host")]
    [DataRow("windows-drive")]
    public void GlobalOrUnpinnedAnalysisHostCannotReplaceTheSelectedSubjectDotnet(string change)
    {
        bool windows = change is "windows-delimiter" or "windows-drive";
        G.Toolchain tools = Tools(windows ? WindowsPath : LinuxPath);
        JsonObject run = Run(windows ? WindowsPath : LinuxPath);
        switch (change)
        {
            case "missing": run["environment"]!.AsObject().Remove("PATH"); break;
            case "null": run["environment"]!["PATH"] = null; break;
            case "wrong-type": run["environment"]!["PATH"] = true; break;
            case "empty": run["environment"]!["PATH"] = ""; break;
            case "global-first": run["environment"]!["PATH"] = "/usr/bin:/data/private-dotnet"; break;
            case "relative-first": run["environment"]!["PATH"] = "private-dotnet:/usr/bin"; break;
            case "unpinned-host": tools = tools with { Files = tools.Files[..1] }; break;
            case "windows-delimiter": run["environment"]!["PATH"] = "C:\\private-dotnet:C:\\Windows"; break;
            case "renamed-host":
                tools = tools with { SubjectDotnet = "/data/private-dotnet/renamed-dotnet", Files = [tools.Files[0], tools.Files[1] with { Path = "/data/private-dotnet/renamed-dotnet" }] };
                break;
            case "windows-drive":
                tools = tools with { SubjectDotnet = "1:/private-dotnet/dotnet.exe", Files = [tools.Files[0], tools.Files[1] with { Path = "1:/private-dotnet/dotnet.exe" }] };
                run["environment"]!["PATH"] = "1:/private-dotnet;C:\\Windows";
                break;
            default: throw new AssertFailedException("Unknown control.");
        }
        Assert.ThrowsExactly<InvalidDataException>(() => G.ValidateSdkMsBuildEnvironment(run, tools));
    }

    private static JsonObject Run(string path) => new()
    {
        ["environment"] = new JsonObject
        {
            ["MSBUILD_EXE_PATH"] = path,
            ["PATH"] = path.Length > 2 && path[1] == ':' ? "C:\\private-dotnet;C:\\Windows" : "/data/private-dotnet:/usr/bin"
        }
    };

    private static G.Toolchain Tools(string path)
    {
        string subject = path.Length > 2 && path[1] == ':' ? WindowsHost : LinuxHost;
        return new("cis.healthcheck.grouped-toolchain/v1", "platform", "host", subject, "tool", "10.0.401", "10.0.12", "8.0.31",
            [new(path, new string('a', 64)), new(subject, new string('b', 64))], []);
    }
}
