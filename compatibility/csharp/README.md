# C# compatibility baselines

Retain append-only package/API, unchanged source-consumer, compiled-consumer and observable-behavior baselines here after the first published contract. Identify actual package and loaded assembly bytes for each replay.

There is no historical CisLib release baseline yet. Repository initialization and the current development package do not establish a published compatibility claim.

The [assembly policy](../../docs/architecture/csharp-assembly-policy.md) selects the initial assembly identity and signing mode. The [platform policy](../../docs/architecture/csharp-platform-policy.md) specifies target retention and original-runtime/newer-runtime replay treatment. Future historical entries must retain the exact package, DLL, consumer, runtime and roll-forward evidence required by those policies.
