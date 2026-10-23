using System.Security.Cryptography;
using System.Text.Json;

const string repo = "C:/miscwork/portfolio/cislib";
const string lane = "planning/healthcheck/evidence/HC12/execution-repair-2026-10-23/capture72";
const string union = "implementations/csharp/eng/CIS.DependencyChecker/HealthcheckQualification/Mutation/Union/";
var targets = new[]
{
    ("HealthcheckMutationNativeProcessProjection.cs", union + "HealthcheckMutationNativeProcessProjection.cs", "0dc51a3e0ce2f8472022295b7c415394b122d8f19757dcc5b969585d0a775c1c", (string?)null),
    ("HealthcheckMutationNativeKernelEvidence.cs", union + "HealthcheckMutationNativeKernelEvidence.cs", "b21028df2e5ade687c847c07431a64c898cd000651be6f49577d2057e5183d25", "796a5aa64dfaa593e84de9764f2a6040a93bcfe25ca8a8a8db9a60293ef3d9c5"),
    ("HealthcheckMutationUnionKernelInputs.cs", union + "HealthcheckMutationUnionKernelInputs.cs", "cb27df2cf355d6477cdc0727dfa3704d1e376bba509429adf9ff7ab747f90799", "a0f5cb586c5c5e8b818f52904cedbd770bcbb2b077507b5a0fdb79fa05f498cf"),
    ("HealthcheckMutationNativeProcessProjectionTests.cs", "implementations/csharp/tests/CIS.Core.ArchitectureTests/HealthcheckMutationNativeProcessProjectionTests.cs", "54da9329c2c141bb43c6a5f0fa92bdc62d37687c70a39f46e34bb68c1d52ef9d", (string?)null)
};
foreach (var item in targets)
{
    string source = Path.Combine(repo, lane, "proposed", item.Item1), target = Path.Combine(repo, item.Item2);
    Need(Hash(source) == item.Item3, "Reviewed proposal changed.");
    Need(item.Item4 is null ? !File.Exists(target) : File.Exists(target) && Hash(target) == item.Item4, "Canonical predecessor changed.");
}
var observations = new List<object>();
foreach (var item in targets)
{
    string source = Path.Combine(repo, lane, "proposed", item.Item1), target = Path.Combine(repo, item.Item2);
    File.Copy(source, target, item.Item4 is not null);
    Need(Hash(target) == item.Item3 && File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(target)), "Adoption bytes differ.");
    observations.Add(new { source, target, beforeSha256 = item.Item4, afterSha256 = item.Item3, bytes = new FileInfo(target).Length });
}
string receipt = Path.Combine(repo, lane, "adoption.json");
using (var output = new FileStream(receipt, FileMode.CreateNew))
{
    JsonSerializer.Serialize(output, new { schema = "cis.healthcheck.capture72-exact-adoption/v1", observedUtc = DateTimeOffset.UtcNow,
        files = observations, sharedProjectChanged = false, subjectRuns = 0, qualificationCredit = 0 }, new JsonSerializerOptions { WriteIndented = true });
    output.Flush(true);
}
Console.WriteLine(JsonSerializer.Serialize(new { receipt, sha256 = Hash(receipt), adopted = observations.Count }));
static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(input)); }
static void Need(bool value, string message) { if (!value) { throw new InvalidDataException(message); } }
