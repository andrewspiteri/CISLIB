param([string] $StartupRoot, [string] $OutputRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
$sampleRoot = Join-Path $languageRoot 'verification/compatibility/samples'
$surfaceRoot = Join-Path $languageRoot 'verification/contracts/startup'
if (-not $StartupRoot) { $StartupRoot = Join-Path $repositoryRoot '.artifacts/csharp/verification/Developer/contracts/startup' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $repositoryRoot '.artifacts/csharp/verification/Developer/contracts/compatibility-samples' }
$StartupRoot = [IO.Path]::GetFullPath($StartupRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$summary = [ordered]@{ schema = 'cis.compatibility-samples/v1'; scope = 'Nonshipping reference-only compiler shape controls, not a published package, runtime or historical compatibility pass.'; status = 'Running'; exitCode = 1; source = @(); mutations = @(); controls = @(); detail = $null }

function Invoke-SampleBuild {
    param([string] $Id, [string] $Project, [string] $Reference, [string] $Diagnostic)
    $lines = @(& dotnet build $Project -c Release -t:Rebuild -v minimal "-p:RestoreConfigFile=$languageRoot/NuGet.config" "-p:MicrosoftLoggingAbstractionsPath=$loggingReference" "-p:CompatibilityReferencePath=$Reference" 2>&1 | ForEach-Object { $_.ToString() })
    $actual = $LASTEXITCODE
    $log = Join-Path (Split-Path $Project -Parent) 'compiler.log'
    $lines | Set-Content -LiteralPath $log -Encoding utf8
    $matched = if ($Diagnostic) { $actual -ne 0 -and ($lines -join "`n") -match ("\berror " + [regex]::Escape($Diagnostic) + "\b") } else { $actual -eq 0 }
    $summary.controls += [ordered]@{ id = $Id; actualExitCode = $actual; expectedDiagnostic = $Diagnostic; matched = $matched; log = New-EvidenceReference $OutputRoot $log 'dotnet.compiler-log/v1' }
    if (-not $matched) { throw "Compatibility compiler sample $Id did not match its expected outcome. See $log" }
}

function Build-Consumer {
    param([string] $Id, [string] $File, [string] $Reference, [string] $Diagnostic)
    $directory = Join-Path $OutputRoot $Id
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sampleRoot 'Consumer.csproj.template') -Destination (Join-Path $directory 'Consumer.csproj')
    Copy-Item -LiteralPath (Join-Path $sampleRoot $File) -Destination (Join-Path $directory 'Consumer.cs')
    Invoke-SampleBuild $Id (Join-Path $directory 'Consumer.csproj') $Reference $Diagnostic
}

function Build-IncompatibleReference {
    param([object] $Sample)
    $directory = Join-Path $OutputRoot "$($Sample.id)-reference"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    foreach ($name in @('PublicSurface.cs', 'EventSurface.cs', 'RuntimeSurface.cs', 'ProviderSurface.cs')) {
        Copy-Item -LiteralPath (Join-Path $surfaceRoot $name) -Destination (Join-Path $directory $name)
    }
    Copy-Item -LiteralPath (Join-Path $surfaceRoot 'Reference.csproj.template') -Destination (Join-Path $directory 'Reference.csproj')
    $path = Join-Path $directory $Sample.surfaceFile
    $original = [IO.File]::ReadAllText($path)
    $position = $original.IndexOf($Sample.find, [StringComparison]::Ordinal)
    if ($position -lt 0 -or $position -ne $original.LastIndexOf($Sample.find, [StringComparison]::Ordinal)) { throw "Mutation anchor must occur exactly once: $($Sample.id)" }
    [IO.File]::WriteAllText($path, $original.Replace($Sample.find, $Sample.replace, [StringComparison]::Ordinal), [Text.UTF8Encoding]::new($false))
    $summary.mutations += [ordered]@{ id = $Sample.id; source = $Sample.surfaceFile; originalSha256 = (Get-FileHash -LiteralPath (Join-Path $surfaceRoot $Sample.surfaceFile)).Hash.ToLowerInvariant(); changedSha256 = (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant(); consumerId = $Sample.consumerId; expectedDiagnostic = $Sample.expectedDiagnostic }
    Invoke-SampleBuild "$($Sample.id)-reference" (Join-Path $directory 'Reference.csproj') '' ''
    return Join-Path $directory 'bin/Release/net10.0/CIS.Core.ContractSpecification.dll'
}

Push-Location $languageRoot
try {
    $startup = Get-Content -Raw -LiteralPath (Join-Path $StartupRoot 'summary.json') | ConvertFrom-Json -AsHashtable
    if ($startup.exitCode -ne 0) { throw 'Run successful startup contract controls first.' }
    $originalReference = [IO.Path]::GetFullPath((Join-Path $StartupRoot $startup.referenceAssembly.path))
    if (-not $originalReference.StartsWith($StartupRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $originalReference).Hash.ToLowerInvariant() -cne $startup.referenceAssembly.sha256) { throw 'Startup reference artifact is outside its root or changed.' }
    $baselineRoot = Join-Path $OutputRoot 'baseline'
    New-Item -ItemType Directory -Path $baselineRoot -Force | Out-Null
    $reference = Join-Path $baselineRoot 'CIS.Core.ContractSpecification.dll'
    Copy-Item -LiteralPath $originalReference -Destination $reference
    $summary.baselineReference = New-EvidenceReference $OutputRoot $reference 'dotnet.reference-assembly/v1'
    $assets = Get-Content -Raw -LiteralPath (Join-Path $languageRoot 'src/CIS.Core/obj/project.assets.json') | ConvertFrom-Json -AsHashtable
    $approved = $startup.approvedCompileReference
    $packageKey = "$($approved.package)/$($approved.version)"
    $relative = Join-Path $assets.libraries[$packageKey].path $approved.asset
    $candidates = @($assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $relative } | Where-Object { Test-Path -LiteralPath $_ })
    if ($candidates.Count -ne 1 -or (Get-FileHash -LiteralPath $candidates[0]).Hash.ToLowerInvariant() -cne $approved.sha256) { throw 'Approved logging reference is missing, ambiguous or changed.' }
    $loggingReference = $candidates[0]
    $summary.approvedCompileReference = $approved
    $summary.source = @(Get-ChildItem -LiteralPath $sampleRoot -Recurse -File | Sort-Object FullName | ForEach-Object { [ordered]@{ path = [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() } })
    $catalog = Get-Content -Raw -LiteralPath (Join-Path $sampleRoot 'catalog.json') | ConvertFrom-Json
    foreach ($consumer in $catalog.sources) { Build-Consumer $consumer.id $consumer.file $reference '' }
    foreach ($sample in $catalog.apiSamples) {
        $changed = Build-IncompatibleReference $sample
        $consumer = @($catalog.sources | Where-Object { $_.id -ceq $sample.consumerId })
        if ($consumer.Count -ne 1) { throw "Unknown sample consumer: $($sample.id)" }
        Build-Consumer $sample.id $consumer[0].file $changed $sample.expectedDiagnostic
    }
    $summary.status = 'Passed'
    $summary.exitCode = 0
}
catch { $summary.status = 'Failed'; $summary.detail = $_.Exception.Message }
finally { Write-VerificationJson (Join-Path $OutputRoot 'summary.json') $summary; Pop-Location }
$summary | ConvertTo-Json -Depth 8
exit $summary.exitCode
