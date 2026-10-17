#Requires -Version 7.5
Set-StrictMode -Version Latest
$script:evidenceTool = $null

function Write-VerificationJson {
    param([string] $Path, [object] $Value)
    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    ConvertTo-Json -InputObject $Value -Depth 30 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Get-EvidenceTool {
    if ($script:evidenceTool) { return $script:evidenceTool }
    $copied = Join-Path $PSScriptRoot '.evidence-tool'
    if (Test-Path -LiteralPath $copied) {
        $tool = Join-Path $copied 'CIS.DependencyChecker.dll'
        if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'Copied evidence tool is incomplete.' }
    }
    else {
        $project = Join-Path $PSScriptRoot 'CIS.DependencyChecker/CIS.DependencyChecker.csproj'
        if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw 'Native evidence tool was not copied with Evidence.psm1.' }
        Push-Location (Split-Path $PSScriptRoot -Parent)
        try { $build = @(& dotnet build $project -c Release -p:RestoreLockedMode=true --nologo --verbosity quiet 2>&1) }
        finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { throw "Unable to build evidence tool: $($build -join [Environment]::NewLine)" }
        $tool = Join-Path $PSScriptRoot 'CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll'
    }
    $script:evidenceTool = $tool
    return $tool
}

function Invoke-EvidenceTool {
    param([string[]] $Arguments, [switch] $AllowRefusal)
    $tool = Get-EvidenceTool
    $output = @(& dotnet $tool release-evidence @Arguments 2>&1)
    $code = $LASTEXITCODE
    if ($code -ne 0 -and -not ($AllowRefusal -and $code -eq 2)) { throw ($output -join [Environment]::NewLine) }
    return ($output -join [Environment]::NewLine | ConvertFrom-Json -AsHashtable -DateKind String)
}

function Read-NativeTrx {
    param([string] $Path)
    Invoke-EvidenceTool -Arguments @('trx', [IO.Path]::GetFullPath($Path))
}

function New-EvidenceReference {
    param([string] $Root, [string] $Path, [string] $Schema)
    return [ordered]@{ path = [IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant(); schema = $Schema }
}

function Copy-EvidenceTool {
    param([string] $Root)
    $tool = Get-EvidenceTool
    $destination = Join-Path $Root '.evidence-tool'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    # Retain the managed tool and its complete current runtime dependency/configuration set.
    foreach ($file in Get-ChildItem -LiteralPath (Split-Path $tool -Parent) -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name.EndsWith('.deps.json') -or $_.Name.EndsWith('.runtimeconfig.json') }) {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $target = Join-Path $destination $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $target
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) { throw "Evidence tool changed while copying: $($file.Name)" }
        [ordered]@{ path = '.evidence-tool/' + $file.Name; sha256 = $hash }
    }
}

Export-ModuleMember -Function Get-EvidenceTool, Write-VerificationJson, Read-NativeTrx, New-EvidenceReference, Invoke-EvidenceTool, Copy-EvidenceTool
