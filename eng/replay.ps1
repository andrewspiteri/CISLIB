param([ValidateSet('Developer', 'Release')][string] $Profile = 'Release', [string] $InputRoot, [string] $InventoryPath, [string] $Lane = $(if ($IsWindows) { 'windows-x64' } else { 'linux-ubuntu2404-x64' }))

$repositoryRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $repositoryRoot 'implementations/csharp/eng/replay.ps1') -Profile $Profile -InputRoot $InputRoot -InventoryPath $InventoryPath -Lane $Lane
exit $LASTEXITCODE
