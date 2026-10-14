# Review: `verify-type-manager-package.py` lifecycle qualification

I read only the supplied text and used no tools, so I have not checked these findings against a real run. They are listed most important first.

## Defects

**1. The rename and clean/rebuild cases never check that the old-name manifest is gone.**
- After `rename-host`, `validate` only looks at `TypeManagerRenamedHost.types.json`. Nothing checks that `TypeManagerPackageHost.types.json` was removed from `directory` or from `published`.
- The publish side is probably a real leftover. `CisWritePublishTypeInventory` runs `AfterTargets="Publish"`. Its `FileWrites` output is not recorded in the SDK's publish-output tracking (`_IncrementalCleanPublishDirectory`). A `--no-build` publish also never runs `IncrementalClean`. So the old-name manifest likely stays in the publish folder.
- This also weakens `rebuild-after-clean`. `validate` only checks `is_file()` plus content, and the expected content matches the original manifest. If the original-name manifest survived the rename build and the clean, the rebuild case would pass even if no new manifest was written.
- Fix:
  - Assert the original-name manifest is absent in both `directory` and `published` after the rename build/publish.
  - Assert both names are absent after `clean`.
  - Before the rebuild, record a timestamp or delete-and-check, then require a fresh write.

**2. The design-time case only proves the target conditions are false.**
- It calls `CisWriteBuildTypeInventory;CisWritePublishTypeInventory` directly with `DesignTimeBuild=true`. Both targets have `'$(DesignTimeBuild)' != 'true'` in their condition, so the result is guaranteed by that text alone.
- It never exercises what an IDE actually runs: the design-time target graph, the `AfterTargets` hooks, or evaluation-time items such as the `Content` item and `UseCurrentRuntimeIdentifier`, all with the helper missing.
- The missing-helper control is sound: it proves the same command without `DesignTimeBuild` reaches `CISTM005`. The positive side is what's thin.
- Fix: run a representative design-time build with the helper removed, for example `-t:ResolveProjectReferencesDesignTime;ResolvePackageDependenciesDesignTime;ResolveAssemblyReferencesDesignTime;CompileDesignTime -p:DesignTimeBuild=true -p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true -p:BuildingProject=false`. Then assert success and that the manifest is unchanged.

**3. For publish, stale DLLs are only present after the manifest is generated.**
- **Build case:** stale `Unused`/`Transitive` DLLs are present when the manifest is written. This only holds because of SDK target order: `CisWriteBuildTypeInventory` runs after `CopyFilesToOutputDirectory` and before `IncrementalClean`. The verifier never asserts this, so the case could silently become empty if that order changes.
- **Publish case:** `_IncrementalCleanPublishDirectory` deletes the earlier-published `Unused`/`Transitive` DLLs before `CisWritePublishTypeInventory` runs. The verifier re-plants them only afterwards. So the publish case covers runtime with stale files, but not generation with stale files.
- Only the two DLLs are restored. The stale `fr/…Unused.resources.dll` and any `.pdb` are not.
- Fix:
  - Plant the stale DLLs and satellite into `published` before `removed-reference-publish`.
  - For the build case, assert after the build that `IncrementalClean` removed them (this proves they were tracked and present during generation), then re-plant them for the runtime check.

**4. The stale-manifest negative control can record a false pass in `results.json`.**
- `validate()` calls `passed(...)` before returning. If the control unexpectedly validates, `results.json` gets `{'case': '<host>-stale-manifest-negative-control', 'passed': True}`, and only then does the `else:` raise.
- Fix: add a `record=False` parameter to `validate` for controls, or pop the entry before raising.
- Scope note: this control tests the verifier's own set comparison, not product behavior. That's fine, but the case name should not suggest it shows the runtime rejects stale manifests.

**5. Isolation is incomplete. These gaps cause loud failures, not false passes.**
- **Upward MSBuild file search:** `Directory.Packages.props` and `Directory.Build.rsp` are searched upward from each project. The verifier writes neither at `root`.
  - If `--output-root` sits under a tree that has a `Directory.Packages.props`, every host becomes centrally managed and fails with NU1008.
  - Fix: write an empty `<Project />` `Directory.Packages.props` at `root`, or set `ImportDirectoryPackagesProps=false` in the root props. Then set `ManagePackageVersionsCentrally` only in `central/`.
- **User and machine NuGet config:** `APPDATA`, `HOME` and `PROGRAMFILES(X86)` are kept in the environment, so user- and machine-level `NuGet.Config` files are still read. `<clear/>` removes their sources, but not settings such as `packageSourceMapping` or `signatureValidationMode`.
  - The `project.assets.json` check catches sources escaping, but not config-driven differences between machines.
  - Fix: set `RestoreConfigFile` explicitly, or point `APPDATA`/`HOME` at directories under `root`.

## Minor

- The verifier records `packageSha256` but never compares it with the expected `d3249a77…52ea9`. The caller must enforce that the package is unchanged.
- `rename-host` uses `--no-restore`. Because `PackageId` defaults to `AssemblyName`, the restore's project name is now stale. The build still passes, but an IDE rename would trigger a restore, so this case isn't representative of that path.

## Coverage that looks sound

- **Reference removal:** removes exactly one reference, builds with restore, and checks the assembly set, deps leakage and runtime loading. The `--without-unused` runtime check makes any directory-scan regression visible.
- **Re-adding the reference:** the content difference proves the manifest was regenerated, and the satellite is re-checked.
- **Clean:** checks that `FileListAbsolute` registers the renamed manifest, and that clean then removes it.
- **Missing-helper control:** the exact `CISTM005` text, the helper restored in `finally`, and the unchanged manifest check.
- **Offline and locked-restore round-trip,** cross-RID publish, and the `NETSDK1047` control.
