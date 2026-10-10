param(
    [ValidateSet('Developer', 'Release')][string] $Profile = 'Release',
    [string] $InputRoot,
    [string] $InventoryPath,
    [string] $Lane = $(if ($IsWindows) { 'windows-x64' } else { 'linux-ubuntu2404-x64' })
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Verification.psm1') -Force
$repositoryRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
if (-not $InputRoot) { $InputRoot = $repositoryRoot }
$InputRoot = [IO.Path]::GetFullPath($InputRoot)
$runRoot = Join-Path $repositoryRoot ('.artifacts/csharp/replay/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N'))
$immutableRoot = Join-Path $runRoot 'input'
$workingRoot = Join-Path $runRoot 'work'
$previous = @{}
$result = [ordered]@{ schema = 'cis.replay/v1'; profile = $Profile; lane = $Lane; runRoot = $runRoot; exitCode = 1; inputVerifiedBefore = $false; inputVerifiedAfter = $false; detail = $null }
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

function Assert-Inputs {
    param([string] $Root, [object[]] $Items)
    foreach ($item in $Items) {
        $path = [IO.Path]::GetFullPath((Join-Path $Root $item.path))
        if (-not $path.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Input path outside root: $($item.path)" }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $item.sha256) { throw "Input checksum mismatch: $($item.path)" }
    }
}

try {
    if ($InventoryPath) { $inputs = @(Get-Content -Raw -LiteralPath $InventoryPath | ConvertFrom-Json -AsHashtable) }
    else { $inputs = @(Get-SourceInventory $InputRoot) }
    if ($inputs.Count -eq 0) { throw 'Input inventory is empty.' }
    Assert-Inputs $InputRoot $inputs
    foreach ($item in $inputs) {
        foreach ($destination in @($immutableRoot, $workingRoot)) {
            $path = Join-Path $destination $item.path
            New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $InputRoot $item.path) -Destination $path
        }
    }
    Assert-Inputs $immutableRoot $inputs
    Assert-Inputs $workingRoot $inputs
    $result.inputVerifiedBefore = $true
    Write-VerificationJson (Join-Path $runRoot 'input-inventory.json') $inputs
    foreach ($name in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'GIT_CEILING_DIRECTORIES')) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
    $env:DOTNET_CLI_HOME = Join-Path $runRoot 'dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $runRoot 'packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $runRoot 'http-cache'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:GIT_CEILING_DIRECTORIES = $runRoot
    & pwsh -NoProfile -File (Join-Path $workingRoot 'eng/verify.ps1') -Profile $Profile -Lane $Lane 2>&1 | Tee-Object -FilePath (Join-Path $runRoot 'verification.log') | Out-Host
    $result.exitCode = $LASTEXITCODE
    Assert-Inputs $InputRoot $inputs
    Assert-Inputs $immutableRoot $inputs
    Assert-Inputs $workingRoot $inputs
    $result.inputVerifiedAfter = $true
    $result.detail = 'Copied source/input replay with isolated caches. This is not historical binary/package replay or a release bundle certification.'
}
catch { $result.exitCode = 1; $result.detail = $_.Exception.Message }
finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    Write-VerificationJson (Join-Path $runRoot 'summary.json') $result
    Write-VerificationJson (Join-Path $repositoryRoot '.artifacts/csharp/replay/latest.json') $result
}
$result | ConvertTo-Json -Depth 5
exit $result.exitCode
