# C# maintainability standard

Applied to handwritten C# across Core, tests, examples and development tooling.

- Use clear names, one statement per line, braces and guard clauses. Avoid compressed conditions and nested conditional operators.
- Keep one coherent responsibility per type. Do not spread handwritten implementations across partial files to avoid decomposition; document any source-generator exception.
- Keep side effects explicit. Getters read resolved state; initialization, I/O and lifecycle operations use named methods. Prefer named setup properties to long positional settings lists.
- Enable nullable, compiler warnings as errors, SDK analyzers, code-style checks, XML documentation for the shipping library and deterministic Release builds. Run `dotnet format --verify-no-changes` with the pinned SDK.
- Document public behavior, ownership, cancellation, exception categories and thread safety; explain invariants and unusual implementation choices.
- Review methods over roughly 60 lines, types over roughly 300 lines and cyclomatic complexity over 15. These are responsibility/decomposition review triggers, not permission to split a type mechanically.
- Do not add broad warning, complexity or coverage suppressions to make checks pass. Record any narrow justified exception beside its reason. Treat generated source separately.
- Keep test seams internal in shipping code unless a consumer actually needs an extension point. Test-only helpers stay outside Core.

These review triggers complement the mechanical checks in `Directory.Build.props`, Core's project and developer verification. They are not currently an automated complexity-scanner claim.
