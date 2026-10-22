using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

// One retained Ubuntu run. These fingerprints select evidence; they do not
// replace parsing its source, process, assertions, symbols or restoration.
internal static class HealthcheckMutationFiniteAspNetCatalog
{
    internal const string Schema = "cis.healthcheck.finite-aspnet-assertion-rerun/v1";
    internal const string Task = "/data/cis-healthcheck-20261021-01a15a72/";
    internal const string Run = "aspnet-finite-28";
    internal const string SourceRoot = Task + Run + "/source";
    internal const string LanguageRoot = SourceRoot + "/implementations/csharp";
    internal const string NativeRoot = LanguageRoot + "/src/CIS.Core";
    internal const string SourcePath = "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs";
    internal const string SourceSha256 = "8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e";
    internal const string OriginalReport = "5f15348728dd4443acbfc6d5fd5392fb3c7fbbed45ba9d4405075436d21549c4";
    internal const string FiniteReport = "689f7779d19dc791c89370542e0131ea6da3acf4949386bd027655ea443d3070";
    internal const string Inventory = "c36c22dd24c0ab4c5d298143119f6b2fab4506f7e0ad5d90966f5f330759d2c6";
    internal const string OriginalManifest = "3aec60743af897759d4f417c484e6adbc2dc35579b5c32bc694ee2bb733a2929";
    internal const string DerivedManifest = "0b62088df3c14cf8215d3bc9c6f00b70d67a374ee6c6e58e79136b6c844d42ec";
    internal const string DerivedDigest = "73bed4f6de7b0989c6eb4da72d3c49ebd4fc18ee2b2d79dbced541f1c215fbde";
    internal const string TestPath = "implementations/csharp/tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/HealthcheckHttpTransportTests.cs";
    internal const string OldTest = "f63892cb9261f90bc0c00976327d5467ac5b801777d80e97c68594b072cd10ab";
    internal const string NewTest = "d9866486a96a829d487ae347fd31353c5eb8968240b887bb181f38d6da84badf";
    internal const string ParentConfig = "4c6c018b3f55285ae090ba859401ae59e22f378c24c3ad2ae382619e147d237c";
    internal const string FiniteConfig = "4a4a073de9af920521de886eac8fe0433f450f882894ba57a6744cffc956674e";
    internal const string StrykerCli = "1d5fbd3df14f3ab0425421c597a36a5cbdd474ad9804a0bd4e212e5b0320eba9";
    internal const string StrykerCore = "bf02f78d3725e1243edb7a32d88dc1a02c88f1f9f74b60b18730636073f234d7";
    internal const string ObjectModel = "ff730c68bebe827f74e2bdcfb571400ab328c00be11c7fe3a1fe01143b483bc6";
    internal const string Core = "802a359904b54ad9b21aaaffe2028c9a21fe68bc03282a974297a067d34b9c4d";
    internal const string CoreMvid = "3caf6024-1dd8-417d-84cf-5562ec8f16fd";
    internal const string IntegrationDll = "b88a5713a60c4595175282dcb29eb53e505e8768f3f96cc8d0894a87011b0083";
    internal const string IntegrationPdb = "6f59bcbaf66148f7c3fae117488453bf293386ef285975f3717cc8333bbbee3c";
    internal const string StalePdb = "6bebd8a98568faa6b76d496bec18059c5769654b42fc6aab7ce8a87d6b9f5259";
    internal const string CleanPdb = "9329a8fe2bc2ba2869bea52ed80bf1f4974e0a0214720ca0df5cc30919da07d9";
    internal const string Registration = "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.ValidDetailsAndAuthorizationCombinationsInstallBeforeTheReservedBranch";
    internal const string Cancellation = "CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.CanceledSnapshotCaptureTerminatesOnlyItsRequestWithoutPublishingAnError";
    internal const string Scheduling = "CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode";
    internal const string RegistrationMessage = "Assertion failed.\nA valid details/predicate combination must install successfully; actual exception type ArgumentException, parameter authorizeDetails.";
    internal const string CancellationMessage = "Assertion failed.\nMatching request cancellation during capture must complete without escaping NullReferenceException.";
    internal const string Filter = "FullyQualifiedName=" + Registration + "|FullyQualifiedName=" + Cancellation + "|FullyQualifiedName=" + Scheduling;
    internal const string Initial = Run + "/source/.artifacts/csharp/focused/c06b37cc5ace4b13a8911160802312e3/";
    internal const string Fresh = Run + "/source/.artifacts/csharp/focused/d0026a721e6b4cc8a4ee23d76bb70109/";
    internal const string CoreRoles = "src/CIS.Core/bin/Release/net10.0/";
    internal const string TestRoles = "tests/CIS.Core.IntegrationTests/bin/Release/net10.0/";
    internal static readonly string[] RegistrationGuids = ["1b676e05-d6cd-8f2b-a8e4-7df1508e4d59", "1f2a6f1c-5cd9-8900-a13f-6e7aa7e3191c", "1aba96be-3733-8079-9246-8da862c1529b"];
    internal static readonly string[] CancellationGuids = ["1eb6557c-e8b6-82e3-9c14-c2a0021d80c3", "1b594d84-66a2-85be-acb9-888a89657743"];
    internal static string[] Cases => RegistrationGuids.Select(guid => guid + "\t" + Registration)
        .Concat(CancellationGuids.Select(guid => guid + "\t" + Cancellation)).Append("1375042e-fcac-8d31-a796-2814e23b6b9f\t" + Scheduling).Order(StringComparer.Ordinal).ToArray();
    internal static readonly Definition[] Targets =
    [
        new("195", SourcePath, SourceSha256, 1194, 1245, "Logical mutation", "settings.DetailsEnabled && authorizeDetails is null", "settings.DetailsEnabled || authorizeDetails is null", "Killed", []),
        new("196", SourcePath, SourceSha256, 1194, 1245, "Negate expression", "settings.DetailsEnabled && authorizeDetails is null", "!(settings.DetailsEnabled && authorizeDetails is null)", "Killed", []),
        new("197", SourcePath, SourceSha256, 1221, 1245, "Equality mutation", "authorizeDetails is null", "authorizeDetails is not null", "Killed", []),
        new("278", SourcePath, SourceSha256, 5499, 5505, "Statement mutation", "throw;", ";", "Killed", [])
    ];
    internal static string[] DirectGuids(string id) => id switch
    {
        "195" => [RegistrationGuids[0], RegistrationGuids[2]],
        "196" => RegistrationGuids.ToArray(),
        "197" => [RegistrationGuids[2]],
        "278" => CancellationGuids.ToArray(),
        _ => throw new InvalidDataException("Unknown ASP28 finite definition.")
    };
}
