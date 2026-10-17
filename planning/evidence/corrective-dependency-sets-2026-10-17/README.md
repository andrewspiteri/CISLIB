# Dependency inventory corrections

17 October 2026, based on 785231f2. Addresses continuation 18-F5/F6.

The established dependency entry point now matches packed asset names case-sensitively and rejects duplicate names ignoring case. Project paths and explicit package references must be distinct and equal their approved sets. Nuspec dependency IDs must be distinct and equal the direct dependency set; approved versions are still checked individually. Existing closure and compiled-reference guards remain.

Eight new C# controls operate on copied actual project definitions/packages: four case-alias duplicates, one incorrectly cased metadata path, duplicate project/package references and duplicate nuspec dependencies. Each checks its intended rule. The current package has one direct dependency, so the nuspec control duplicates that actual member and proves the duplicate guard cannot be masked by the old count check; it does not fabricate a second approved direct package.

All 25 selected dependency cases pass on Windows 760d2dbecfda478498b77c351556939d and native Ubuntu a0c1e6321d72437a983987b6ccf5a4bf, zero failures/skips. The selection includes dependent provider-host boundary cases. An initial PowerShell HashSet argument-conversion error was corrected with an explicit string-array conversion before this passing rerun.

These focused results use existing lane Release/package prerequisites and do not qualify a new candidate or release. The original roadmap remains paused.
