Set-StrictMode -Version Latest

function Get-SourceInventory {
    param([string] $Root)
    $project = Join-Path $PSScriptRoot 'CIS.DependencyChecker/CIS.DependencyChecker.csproj'
    # Bootstrap before the profile build, including on a fresh checkout. Keep build output outside the JSON pipeline.
    $build = @(& dotnet build $project -c Release -p:RestoreLockedMode=true --nologo --verbosity quiet 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Unable to build source inventory tool: $($build -join [Environment]::NewLine)" }
    $checker = Join-Path $PSScriptRoot 'CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll'
    $output = @(& dotnet $checker source-inventory $Root)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to capture source inventory.' }
    return ($output -join [Environment]::NewLine | ConvertFrom-Json -AsHashtable)
}

function Invoke-VerificationStage {
    param([hashtable] $Context, [string] $Name, [scriptblock] $Action)
    $stage = [ordered]@{ id = $Name; status = 'Running'; exitCode = $null; startedUtc = [DateTime]::UtcNow.ToString('o'); finishedUtc = $null; detail = $null }
    $Context.stages.Add($stage)
    try { & $Action; $stage.status = 'Passed'; $stage.exitCode = 0 }
    catch { $stage.status = 'Failed'; $stage.exitCode = 1; $stage.detail = $_.Exception.Message; throw }
    finally { $stage.finishedUtc = [DateTime]::UtcNow.ToString('o') }
}

function Invoke-VerificationCommand {
    param([string] $LogPath, [string[]] $Arguments, [string] $Executable = 'dotnet')
    $lines = @(& $Executable @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $lines | Set-Content -LiteralPath $LogPath -Encoding utf8
    $lines | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) { throw "$Executable $($Arguments -join ' ') failed with exit $exitCode. See $LogPath" }
    return $lines
}

Export-ModuleMember -Function Get-SourceInventory, Invoke-VerificationStage, Invoke-VerificationCommand
