# C# startup input ownership

WP03-S04-T02, 11 October 2026, qualifies the owned copying and complete-result publication introduced with [EnvironmentOptions](environment-options.md). The actual shipping types are in CIS.Core with CIS/CIS.Logging namespaces. [Windows/Ubuntu evidence](../../../planning/logging/evidence/wp03-s04-t02-windows-linux.json) records native checks and retained process observations.

## Caller contract

EnvironmentSetup and LoggingSetup are mutable caller inputs. Do not modify them while Initialize runs. After it returns, callers can change every property, replace the nested LoggingSetup, or release those objects. Current, all convenience getters, LoggingOptions and EventMetadata keep the original resolved values and object identities. Source environment and current-directory changes after publication cannot redirect the stored file path or regenerate metadata/InstanceId.

The selected public shape contains nine setup properties and 15 logging-input properties. LoggingSetup is its only nested mutable object; there are no collection properties. The source backlog's generic nested-collection instruction is applied to this actual contract. Any future collection input will need an owned copy and corresponding qualification before exposure. Strings are immutable values; ApplicationAssembly is used during local identity discovery and is absent from the published graph.

## Copy and validation boundary

StartupInputCapture reads all caller fields into private identity/configuration inputs before the first required directory or environment read. Later resolution, validation and metadata composition consume those captured inputs. Tests change every declared property, one at a time, at both read boundaries and verify the complete originally selected projection. Additional controls replace the supplied application assembly and the identity strings while actual local discovery still observes the original assembly.

Repairing invalid caller inputs after that capture cannot change the attempt being validated. Original invalid file/queue values still produce the ordered safe errors; runtime discovery and initialization identity generation are not reached. After full validation, the builder creates owned LoggingOptions and EventMetadata and then their immutable EnvironmentSnapshot. Publication follows that complete construction.

EnvironmentSnapshot contains only owned LoggingOptions and EventMetadata. Their instance fields hold immutable strings, enums, numeric/boolean values, DateTimeOffset/TimeSpan and nullable value forms. Compiled field inspection verifies sealed outputs, private readonly fields and the allowed graph, with negative controls showing why a readonly reference to a mutable setup or array is insufficient. No output object retains the caller's setup or nested logging object.

## Executed ownership checks

Fifty-three new unit cases cover all 24 fields at two source boundaries, captured assembly discovery, invalid-input repair and independent local snapshots after setup reuse. Three architecture cases qualify the actual stored graph and readonly mutable-reference controls. Four fresh child-process integration cases mutate the original parent/nested inputs, replace the nested object, alter all 21 recognized sources and the collector decoy, and release both caller objects. Every case also changes the actual child directory and compares all eight convenience getters, all resolved logging properties and the complete 17-field metadata projection. The release-input case uses weak references from a non-inlined helper and forced collection to verify the actual static snapshot has no retained caller objects.

The same four actual public scenarios run on Windows and Ubuntu 24.04.4 x64 under WSL2 with identical managed Core/host bytes, the existing .NET 10.0.12 runtime and roll-forward disabled. The independent catalog supplies exactly the 21 names cleared before each child; the harness preserves parent environment/cwd and verifies empty owned output directories. Child JSON is a test result protocol, not an event wire-format implementation. No Linux SDK or full native suite is claimed.

The copying/publication implementation from T01 needed no algorithm change. This task documents the public post-initialization mutation guarantee and qualifies it with real adversarial mutations/lifetimes. Original plans, contracts, reference-only compiler fixtures and historical evidence retain their bytes. T03 still owns concurrent-publisher qualification, and T04 the complete shared metadata/lifecycle fixtures and separate-process identity criterion. No logger/workers, reset, global operation context, publication or release qualification is introduced.
