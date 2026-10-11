param([string] $OutputRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
$sourceRoot = Join-Path $languageRoot 'verification/consumers/log-values'
if (-not $OutputRoot) { $OutputRoot = Join-Path $repositoryRoot '.artifacts/csharp/verification/Developer/contracts/log-value-consumers' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$summary = [ordered]@{ schema = 'cis.actual-log-value-consumers/v1'; status = 'Running'; exitCode = 1; scope = 'Actual Core DLL typed API compiler controls and one executable consumer; no published compatibility claim.'; controls = @(); source = @(); references = @(); coreSha256 = $null; compilerSha256 = $null; detail = $null }
Push-Location $languageRoot
try {
    $policy = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'toolchain.json') | ConvertFrom-Json
    $version = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $version -cne $policy.sdk) { throw 'Pinned SDK mismatch.' }
    $sdkLine = @(& dotnet --list-sdks | Where-Object { $_ -match ('^' + [regex]::Escape($version) + ' \[.+\]$') })
    if ($LASTEXITCODE -ne 0 -or $sdkLine.Count -ne 1) { throw 'Expected one pinned SDK location.' }
    $sdkDirectory = $sdkLine[0].Substring($sdkLine[0].IndexOf('[') + 1).TrimEnd(']')
    $compiler = Join-Path $sdkDirectory "$version/Roslyn/bincore/csc.dll"
    $referenceRoot = Join-Path (Split-Path $sdkDirectory -Parent) "packs/Microsoft.NETCore.App.Ref/$($policy.targetRuntime)/ref/$($policy.targetFramework)"
    $core = Join-Path $languageRoot 'src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'
    $summary.coreSha256 = (Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash.ToLowerInvariant()
    $summary.compilerSha256 = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash.ToLowerInvariant()
    $references = @(Get-ChildItem -LiteralPath $referenceRoot -Filter '*.dll' -File | Sort-Object Name)
    if ($references.Count -eq 0) { throw 'Pinned framework reference pack unavailable.' }
    $summary.references = @($references | ForEach-Object { [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
    $catalog = Get-Content -Raw -LiteralPath (Join-Path $sourceRoot 'controls.json') | ConvertFrom-Json
    if ($catalog.controlCount -ne 12 -or @($catalog.controls).Count -ne 12) { throw 'Consumer control set differs.' }
    $summary.source = @(Get-ChildItem -LiteralPath $sourceRoot -File | Sort-Object Name | ForEach-Object { [ordered]@{ path = [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
    foreach ($control in $catalog.controls) {
        $directory = Join-Path $OutputRoot $control.id
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $binary = Join-Path $directory 'Consumer.dll'
        $target = if ($control.execute) { 'exe' } else { 'library' }
        $arguments = @($compiler, '/nologo', '/noconfig', '/nostdlib+', '/nullable:enable', '/warnaserror+', '/deterministic+', '/optimize+', '/langversion:latest', "/target:$target", "/out:$binary", "/reference:$core")
        $arguments += @($references | ForEach-Object { '/reference:' + $_.FullName })
        $arguments += Join-Path $sourceRoot $control.file
        $lines = @(& dotnet @arguments 2>&1 | ForEach-Object { $_.ToString() })
        $actual = $LASTEXITCODE
        $log = Join-Path $directory 'compiler.log'
        Set-Content -LiteralPath $log -Value ($lines -join [Environment]::NewLine) -Encoding utf8
        $matched = if ($control.expectedDiagnostic) { $actual -ne 0 -and ($lines -join "`n") -match [regex]::Escape($control.expectedDiagnostic) } else { $actual -eq 0 }
        $entry = [ordered]@{ id = $control.id; actualExitCode = $actual; expectedDiagnostic = $control.expectedDiagnostic; matched = $matched; log = New-EvidenceReference $OutputRoot $log 'dotnet.compiler-log/v1'; executed = $false }
        if (-not $matched) { $summary.controls += $entry; throw "Actual-Core consumer control failed: $($control.id)." }
        if ($actual -eq 0) { $entry.binary = New-EvidenceReference $OutputRoot $binary 'dotnet.consumer/v1' }
        if ($control.execute) {
            Copy-Item -LiteralPath $core -Destination (Join-Path $directory 'CIS.Core.dll')
            Write-VerificationJson (Join-Path $directory 'Consumer.runtimeconfig.json') @{ runtimeOptions = @{ tfm = $policy.targetFramework; framework = @{ name = 'Microsoft.NETCore.App'; version = $policy.targetRuntime }; rollForward = 'Disable' } }
            $output = @(& dotnet exec --fx-version $policy.targetRuntime --roll-forward Disable $binary 2>&1 | ForEach-Object { $_.ToString() })
            $entry.executionExitCode = $LASTEXITCODE
            $executionLog = Join-Path $directory 'execution.log'
            $output | Set-Content -LiteralPath $executionLog -Encoding utf8
            $entry.executionLog = New-EvidenceReference $OutputRoot $executionLog 'dotnet.consumer-execution/v1'
            $entry.executed = $true
            if ($entry.executionExitCode -ne 0 -or ($output -join "`n") -cne 'Typed log-value consumer passed.') { $summary.controls += $entry; throw 'Actual typed consumer execution failed.' }
        }
        $summary.controls += $entry
    }
    if ((Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash.ToLowerInvariant() -cne $summary.coreSha256) { throw 'Core changed during consumer qualification.' }
    $summary.status = 'Passed'
    $summary.exitCode = 0
}
catch { $summary.status = 'Failed'; $summary.detail = $_.Exception.Message }
finally {
    Pop-Location
    Write-VerificationJson (Join-Path $OutputRoot 'summary.json') $summary
}
$summary | ConvertTo-Json -Depth 10
exit $summary.exitCode
