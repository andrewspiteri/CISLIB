using System.Collections;
using CIS.Management;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Tests.IntegrationTests.Management;

internal sealed class MalformedCohortScenario
{
    internal string Fault { get; set; } = "valid";
    internal int EnumeratedRows
    {
        get; set;
    }
}

// Only the optional result is malformed. Target resolution and authorization continue through real native reads.
internal sealed class ManagementConfigurationCohortMalformedBackend(IManagementConfigurationBackend native, MalformedCohortScenario scenario)
    : ManagementConfigurationCohortLegacyBackend(native), IManagementConfigurationCohortBackend
{
    public Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationCohortChoice>>> ReadCohortsAsync(
        ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        scenario.EnumeratedRows = 0;
        IReadOnlyList<ManagementConfigurationCohortChoice> rows = scenario.Fault switch
        {
            "too-many" => [new("a", "a"), new("b", "b"), new("c", "c")],
            "invalid-id" => [new("../forged", "forged")],
            "invalid-label" => [new("a", "")],
            "duplicate" => [new("a", "a"), new("a", "a")],
            "descending" => [new("z", "z"), new("a", "a")],
            "null-row" => [null!],
            "under-reported-count" => new CountMismatchedCohortRows(0, [new("a", "a"), new("b", "b")], scenario),
            "over-reported-count" => new CountMismatchedCohortRows(2, [new("a", "a")], scenario),
            "negative-count" => new CountMismatchedCohortRows(-1, [], scenario),
            "not-after" => [new(after ?? "a", "a")],
            _ => [new("a", "a"), new("b", "b")]
        };
        return Task.FromResult(new ManagementStoreResult<IReadOnlyList<ManagementConfigurationCohortChoice>>(rows,
            scenario.Fault == "code" ? "management.configuration.unavailable" : null, null, scenario.Fault != "not-committed"));
    }

    private sealed class CountMismatchedCohortRows(int count, ManagementConfigurationCohortChoice[] rows, MalformedCohortScenario scenario)
        : IReadOnlyList<ManagementConfigurationCohortChoice>
    {
        public int Count => count;
        public ManagementConfigurationCohortChoice this[int index] => rows[index];
        public IEnumerator<ManagementConfigurationCohortChoice> GetEnumerator()
        {
            foreach (ManagementConfigurationCohortChoice row in rows)
            {
                scenario.EnumeratedRows++;
                yield return row;
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
