param([Parameter(Mandatory)][string] $FixtureRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Verification.psm1') -Force
# Caller supplies its own empty temporary directory. Never clean or modify a repository supplied by the user.
if (@(Get-ChildItem -LiteralPath $FixtureRoot -Force).Count -ne 0) { throw 'Expected an empty test directory.' }
& git -C $FixtureRoot init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Fixture init failed.' }
Set-Content -LiteralPath (Join-Path $FixtureRoot 'kept.txt') -Value 'kept' -Encoding utf8
Set-Content -LiteralPath (Join-Path $FixtureRoot 'removed.txt') -Value 'removed' -Encoding utf8
& git -C $FixtureRoot add -- kept.txt removed.txt
if ($LASTEXITCODE -ne 0) { throw 'Fixture index preparation failed.' }
Remove-Item -LiteralPath (Join-Path $FixtureRoot 'removed.txt')
Set-Content -LiteralPath (Join-Path $FixtureRoot 'new.txt') -Value 'new' -Encoding utf8
$indexBefore = @(& git -C $FixtureRoot ls-files --stage)
$inventory = @(Get-SourceInventory $FixtureRoot)
$indexAfter = @(& git -C $FixtureRoot ls-files --stage)
if (($indexBefore -join "`n") -ne ($indexAfter -join "`n")) { throw 'Inventory changed the Git index.' }
if (($inventory.path -join ',') -ne 'kept.txt,new.txt') { throw 'Inventory did not reflect actual tracked/untracked files and deletion.' }
foreach ($item in $inventory) {
    if ($item.sha256 -ne (Get-FileHash -LiteralPath (Join-Path $FixtureRoot $item.path)).Hash.ToLowerInvariant()) { throw 'Inventory digest differs.' }
}
'Passed: current files hashed; Git-confirmed deletion excluded; index unchanged.'

# Git object files are read-only on Windows. Clear that attribute only inside this owned fixture.
$taskFixturePath = (Resolve-Path -LiteralPath $FixtureRoot).Path
$taskObjectsPath = [IO.Path]::GetFullPath((Join-Path $taskFixturePath '.git/objects'))
if (-not $taskObjectsPath.StartsWith($taskFixturePath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture object path escaped its root.' }
Get-ChildItem -LiteralPath $taskObjectsPath -File -Recurse -Force | ForEach-Object { $_.IsReadOnly = $false }
