---
title: 'Approved C# dependencies'
type: architecture
status: unknown
cis:
  stable_id: cislib.architecture.architecture.csharp.dependency.policy
---

# Approved C# dependencies

`CIS.Core` is the base shipping package. Its approved runtime closure contains `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`, both 10.0.12, with their approved closure recorded in [runtime-dependencies.json](../../implementations/csharp/eng/runtime-dependencies.json). A Microsoft-looking prefix alone does not approve a new dependency. Changing the selected Core runtime closure requires an explicit repository decision.

The user approved the `Microsoft.AspNetCore.App` framework reference in Core for Healthcheck. Keep that framework requirement and Core-only package-pruning exception; do not silently migrate the two established NuGets. Third-party runtime integrations belong in separate optional packages.

CIS.Metrics.OpenTelemetry is the separately approved optional integration under
[CIS-0009](../changes/CIS-0009/proposal.md). Core does not reference it or acquire
its SDK/exporter dependencies. The locked optional net10.0 package closure is:

| Package | Version |
| --- | --- |
| OpenTelemetry | 1.21.0 |
| OpenTelemetry.Api | 1.21.0 |
| OpenTelemetry.Api.ProviderBuilderExtensions | 1.21.0 |
| OpenTelemetry.Extensions.Hosting | 1.21.0 |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | 1.21.0-beta.1 |
| System.Diagnostics.DiagnosticSource | 11.0.0 |

The exporter remains prerelease. The cached OpenTelemetry.Api 1.21.0 manifest
requires DiagnosticSource 11.0.0 on net10.0. DiagnosticSource 11.0.0 itself
[targets net10.0 with no further package dependencies](https://www.nuget.org/packages/System.Diagnostics.DiagnosticSource/11.0.0#dependencies-body-tab).
This is a consumer deployment dependency, not a .NET 11 runtime requirement.
Keep the selected .NET/ASP.NET Core runtime 10.0.12 and SDK 10.0.401 pins.
Optional consumers must deploy the resolved DiagnosticSource 11 package runtime
asset through their normal build/publish output rather than relying on the
shared framework's DiagnosticSource 10 copy.

Current net10.0 restore assets prune the SDK's declared Microsoft.Extensions
10.0.0 dependencies against the approved ASP.NET shared framework. That does not
change Core's two-package closure or its pruning exception. Core-only consumers
do not acquire the six optional integration packages.

The first governed optional restore failed NU1100 because DiagnosticSource was
outside the original five-ID source mapping. A later NU1102 reported the same
requested and nearest 11.0.0 version. Both failures preceded collection tests.
After the sixth exact mapping was approved, a cache-bypassed restore resolved
the unchanged six-package set and its net10.0 DiagnosticSource runtime asset.
The optional assembly compiled, and the subsequent locked collection proof
passed 31 cases with zero failures/skips under native execution
fd6199e633ea4200ae005be912f159e2.

That proof covers all-format identity, host isolation and actual Core callback
drain. Full hosting owner order, the product endpoint/HTTP policy, example and
packaged-consumer deployment, and the default footprint observation remain
WORK-005 obligations. See the [verification record](../changes/CIS-0009/verification.md);
the focused collection result does not establish release qualification.

`CIS.RestClient.Resilience` is separately optional under the user-approved
[CIS-0010 plan](../changes/CIS-0010/implementation-plan.md) and P07. Its selected
third-party dependency is **Polly.Core 8.9.1**, with an empty net10.0 dependency
group. The exact source mapping, central pin and generated lock are included in
that approval; Core acquires no Polly or other new NuGet. Locked native validation
passed 25 policy cases plus 11 collector cases in execution
`native/incremental/46d18a0a9e05422487ef910281f60937`. This establishes the adapter
and package closure, while actual RestClient operation/host and package-consumer
composition remains in [the verification record](../changes/CIS-0010/verification.md).
SDK/runtime pins remain 10.0.401/10.0.12. Core/None consumers do not acquire Polly.

`CIS.TypeManager.Build` is the non-runtime MSBuild helper delivered inside the Core package with its SDK/build imports. Its build-only reference must not introduce a runtime assembly dependency. Test frameworks, adapters, fixtures and coverage/mutation/API tooling remain development dependencies and never ship in the runtime closure.

The existing native test framework is MSTest. SDK and package versions are pinned locally in the C# implementation. Native restore/build/test/pack are the operating route in [README](../../README.md). The retired project census, checker protocols and generated inspection receipts are not package/API contracts; useful runtime/package behavior remains covered by native tests.
