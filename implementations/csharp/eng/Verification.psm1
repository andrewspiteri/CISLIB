Set-StrictMode -Version Latest

function Get-SourceInventory {
    param([string] $Root)
    $gitRoot = (& git -C $Root rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -eq 0 -and [IO.Path]::GetFullPath($gitRoot) -eq [IO.Path]::GetFullPath($Root)) {
        $files = @(& git -C $Root ls-files --cached --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate Git source inputs.' }
    }
    else {
        $files = @(Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/') } | Where-Object { $_ -notmatch '(^|/)(\.git|\.artifacts|bin|obj|\.vs)(/|$)' })
    }
    return @($files | Sort-Object -Unique | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $Root $_) -Algorithm SHA256).Hash.ToLowerInvariant() } })
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
