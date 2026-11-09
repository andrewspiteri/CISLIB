# Approved C# dependencies

`CIS.Core` is the single shipping package. Its approved runtime closure contains `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`, both 10.0.12, with their approved closure recorded in [runtime-dependencies.json](../../implementations/csharp/eng/runtime-dependencies.json). A Microsoft-looking prefix alone does not approve a new dependency. Changing the selected runtime closure requires an explicit repository decision.

The user approved the `Microsoft.AspNetCore.App` framework reference in Core for Healthcheck. Keep that framework requirement and Core-only package-pruning exception; do not silently migrate the two established NuGets. Third-party runtime integrations belong in separate optional packages.

`CIS.TypeManager.Build` is the non-runtime MSBuild helper delivered inside the Core package with its SDK/build imports. Its build-only reference must not introduce a runtime assembly dependency. Test frameworks, adapters, fixtures and coverage/mutation/API tooling remain development dependencies and never ship in the runtime closure.

The existing native test framework is MSTest. SDK and package versions are pinned locally in the C# implementation. Native restore/build/test/pack are the operating route in [README](../../README.md). The retired project census, checker protocols and generated inspection receipts are not package/API contracts; useful runtime/package behavior remains covered by native tests.
