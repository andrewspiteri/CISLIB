param(
    [ValidateSet('Coverage', 'Mutation')][string] $Mode = 'Coverage',
    [string] $OutputRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
$policy = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'toolchain.json') | ConvertFrom-Json
if (-not $OutputRoot) { $OutputRoot = Join-Path $repositoryRoot ('.artifacts/csharp/microsoft-logging/' + [guid]::NewGuid().ToString('N')) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Use a new output directory; retained reports are immutable.' }
New-Item -ItemType Directory -Path (Join-Path $OutputRoot 'logs') -Force | Out-Null
$commands = [Collections.Generic.List[object]]::new()
$matrix = Get-Content -Raw -LiteralPath (Join-Path $languageRoot 'verification/shipping-projects.json') | ConvertFrom-Json
$slice = @($matrix.shippingProjects.moduleSlices | Where-Object id -eq 'logging-microsoft')
if ($slice.Count -ne 1) { throw 'Expected one Microsoft logging matrix slice.' }
$expectedPaths = @($slice[0].sources | ForEach-Object { 'implementations/csharp/' + $_ } | Sort-Object)
$sourceInputs = @(Get-ChildItem -LiteralPath (Join-Path $languageRoot 'src/CIS.Core/Logging/MicrosoftLogger') -File -Filter '*.cs' | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
if (@(Compare-Object $expectedPaths @($sourceInputs.path | Sort-Object)).Count -ne 0) { throw 'Adapter source files differ from the matrix.' }
$verificationInputs = @($PSCommandPath, (Join-Path $PSScriptRoot 'toolchain.json'), (Join-Path $languageRoot 'verification/shipping-projects.json'), (Join-Path $languageRoot 'verification/mutation/microsoft-logging-config.json'), (Join-Path $languageRoot 'verification/coverage/microsoft-logging.runsettings')) | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($repositoryRoot, $_).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$verificationInputs | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputRoot 'verification-inputs.json') -Encoding utf8
$sourceInputs | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputRoot 'adapter-source-inputs.json') -Encoding utf8
function Invoke-Recorded {
    param([string] $Name, [string[]] $Arguments)
    $log = Join-Path $OutputRoot "logs/$Name.log"
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    $code = $LASTEXITCODE
    $commands.Add(@{ name = $Name; executable = 'dotnet'; argv = $Arguments; workingDirectory = (Get-Location).Path; exitCode = $code; log = "logs/$Name.log" })
    $commands | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $OutputRoot 'commands.json') -Encoding utf8
    if ($code -ne 0) { throw "$Name failed with exit $code. Native outputs retained." }
}
function Write-CoverageObservation {
    param([string] $Directory)
    [xml] $trx = Get-Content -Raw -LiteralPath (Join-Path $Directory 'report.trx')
    $attachments = @($trx.SelectNodes('//*[local-name()="UriAttachment"]/*[local-name()="A"]') | Where-Object { $_.GetAttribute('href') -like '*.cobertura.xml' })
    if ($attachments.Count -ne 1) { throw 'Expected one TRX Cobertura attachment per lane.' }
    $attachmentName = $attachments[0].GetAttribute('href').Replace('\', '/').Split('/')[-1]
    $reports = @(Get-ChildItem -LiteralPath $Directory -Recurse -File | Where-Object Name -eq $attachmentName)
    if ($reports.Count -eq 0) { throw 'Cannot resolve the TRX Cobertura attachment.' }
    $hashes = @($reports | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } | Sort-Object -Unique)
    if ($hashes.Count -ne 1) { throw 'Conflicting copies of the TRX Cobertura attachment.' }
    $report = $reports[0]
    [xml] $xml = Get-Content -Raw -LiteralPath $report.FullName
    $observations = foreach ($input in $sourceInputs) {
        $fullPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $input.path))
        $classes = @($xml.SelectNodes('//class') | Where-Object { $_.GetAttribute('filename').Replace('/', '\') -eq $fullPath.Replace('/', '\') })
        if ($classes.Count -eq 0) { throw "Adapter source absent from coverage: $($input.path)" }
        $lines = @{}
        foreach ($class in $classes) {
            foreach ($line in $class.SelectNodes('./lines/line')) {
                $number = $line.GetAttribute('number')
                $hits = [int]$line.GetAttribute('hits')
                $covered = 0; $total = 0
                if ($line.GetAttribute('branch') -ieq 'true') {
                    if ($line.GetAttribute('condition-coverage') -notmatch '\((\d+)/(\d+)\)') { throw 'Unrecognized native branch observation.' }
                    $covered = [int]$Matches[1]; $total = [int]$Matches[2]
                }
                if ($lines.ContainsKey($number)) {
                    $previous = $lines[$number]
                    if ($previous.total -gt 0 -and $total -gt 0 -and $previous.total -ne $total) { throw 'Conflicting branch denominators for a duplicate source line.' }
                    $total = [Math]::Max($total, $previous.total)
                    $hits = [Math]::Max($hits, $previous.hits); $covered = [Math]::Max($covered, $previous.covered)
                }
                $lines[$number] = @{ hits = $hits; covered = $covered; total = $total }
            }
        }
        $values = @($lines.Values)
        $coveredLines = @($values | Where-Object hits -gt 0).Count
        if ($values.Count -eq 0 -or $coveredLines -eq 0) { throw "Adapter source has no measured execution: $($input.path)" }
        @{ path = $input.path; linesCovered = $coveredLines; linesTotal = $values.Count; branchesCovered = ($values | Measure-Object covered -Sum).Sum; branchesTotal = ($values | Measure-Object total -Sum).Sum }
    }
    @{
        files = @($observations)
        trxSha256 = (Get-FileHash -LiteralPath (Join-Path $Directory 'report.trx') -Algorithm SHA256).Hash.ToLowerInvariant()
        coberturaSha256 = (Get-FileHash -LiteralPath $report.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        thresholdEvaluation = 'Deferred to WP07; these are separate lane observations, not release qualification.'
    } | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $Directory 'adapter-coverage.json') -Encoding utf8
}
function Write-MutationObservation {
    param([string] $ReportPath)
    $report = Get-Content -Raw -LiteralPath $ReportPath | ConvertFrom-Json
    $files = @($report.files.PSObject.Properties | Where-Object { $_.Name.Replace('\', '/') -match '/Logging/MicrosoftLogger/' })
    $relative = @($files | ForEach-Object { 'implementations/csharp/src/CIS.Core/Logging/MicrosoftLogger/' + [IO.Path]::GetFileName($_.Name) } | Sort-Object)
    if (@(Compare-Object $expectedPaths $relative).Count -ne 0) { throw 'Native mutant files differ from the adapter matrix.' }
    $counts = @{}
    foreach ($file in $files) {
        $mutants = @($file.Value.mutants)
        if ($mutants.Count -eq 0) { throw "Adapter source has zero mutants: $($file.Name)" }
        foreach ($mutant in $mutants) {
            if (-not $counts.ContainsKey($mutant.status)) { $counts[$mutant.status] = 0 }
            $counts[$mutant.status]++
        }
    }
    $denominator = 0
    foreach ($status in @('Killed', 'Timeout', 'Survived', 'NoCoverage')) {
        if ($counts.ContainsKey($status)) { $denominator += $counts[$status] }
    }
    if ($denominator -eq 0) { throw 'Adapter mutation denominator is zero.' }
    @{ nativeReportSha256 = (Get-FileHash -LiteralPath $ReportPath -Algorithm SHA256).Hash.ToLowerInvariant(); counts = $counts; testableDenominator = $denominator; scope = 'Matrix adapter files only; ignored/noncompiling mutants receive no kill credit.' } |
        ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $OutputRoot 'adapter-mutation.json') -Encoding utf8
}

Push-Location $languageRoot
try {
    if ((& dotnet --version).Trim() -ne $policy.sdk) { throw 'Selected SDK differs from toolchain policy.' }
    if ($Mode -eq 'Coverage') {
        foreach ($family in @('Unit', 'Integration')) {
            $project = "tests/CIS.Core.$($family)Tests/CIS.Core.$($family)Tests.csproj"
            Invoke-Recorded "build-$family" @('build', $project, '-c', 'Release', '--no-restore')
            $directory = Join-Path $OutputRoot $family.ToLowerInvariant()
            Invoke-Recorded "coverage-$family" @('test', $project, '-c', 'Release', '--no-build', '--no-restore', '--filter', "FullyQualifiedName~CIS.Tests.$($family)Tests.Provider", '--settings', 'verification/coverage/microsoft-logging.runsettings', '--collect', 'Code Coverage;Format=cobertura', '--results-directory', $directory, '--logger', 'trx;LogFileName=report.trx')
            Write-CoverageObservation $directory
        }
    }
    else {
        # Stryker must never write instrumented sources/assemblies into the shared checkout.
        $snapshot = Join-Path $OutputRoot 'source'
        Push-Location $repositoryRoot
        try {
            $files = @(& git ls-files --cached --others --exclude-standard -- implementations/csharp)
            if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate maintained mutation inputs.' }
            $files += 'README.md'
            $inventory = foreach ($relative in ($files | Sort-Object -Unique)) {
                $source = Join-Path $repositoryRoot $relative
                $copy = Join-Path $snapshot $relative
                New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $source -Destination $copy
                @{ path = $relative; sha256 = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant() }
            }
            $inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputRoot 'snapshot-inputs.json') -Encoding utf8
        }
        finally { Pop-Location }
        $isolatedLanguage = Join-Path $snapshot 'implementations/csharp'
        Set-Location $isolatedLanguage
        Invoke-Recorded 'tool-restore' @('tool', 'restore', '--configfile', 'eng/Tools.NuGet.config')
        Invoke-Recorded 'restore-unit' @('restore', 'tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj', '--locked-mode', '--configfile', 'NuGet.config')
        Invoke-Recorded 'build-unit' @('build', 'tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj', '-c', 'Release', '--no-restore')
        Invoke-Recorded 'baseline-unit' @('test', 'tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--filter', 'FullyQualifiedName~CIS.Tests.UnitTests.Provider', '--logger', 'trx;LogFileName=baseline.trx', '--results-directory', (Join-Path $OutputRoot 'baseline'))
        $packageDirectory = ((& dotnet nuget locals global-packages --list) -join '').Split(': ', 2)[1].Trim()
        $tool = Join-Path $packageDirectory 'dotnet-stryker/4.16.0/tools/net8.0/any/Stryker.CLI.dll'
        if (-not (Test-Path -LiteralPath $tool)) { throw 'Pinned Stryker entry point was not materialized.' }
        Set-Location (Join-Path $isolatedLanguage 'src/CIS.Core')
        $mutationFailure = $null
        try {
            Invoke-Recorded 'mutation' @('exec', '--fx-version', $policy.toolHostRuntime, '--roll-forward', 'Disable', $tool, '--config-file', '../../verification/mutation/microsoft-logging-config.json', '--output', (Join-Path $OutputRoot 'mutation'))
        }
        catch { $mutationFailure = $_ }
        finally {
            try { Write-MutationObservation (Join-Path $OutputRoot 'mutation/reports/mutation-report.json') }
            finally {
                foreach ($input in $inventory) {
                    if ((Get-FileHash -LiteralPath (Join-Path $snapshot $input.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $input.sha256) { throw "Mutation changed a retained source input: $($input.path)" }
                }
                Set-Location $isolatedLanguage
                Invoke-Recorded 'unmutated-rebuild' @('build', 'tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj', '-c', 'Release', '--no-restore', '--no-incremental')
            }
        }
        if ($mutationFailure) { throw $mutationFailure }
    }
    Write-Output "Microsoft logging $Mode completed. Native reports: $OutputRoot"
}
finally {
    try {
        foreach ($input in $sourceInputs) {
            if ((Get-FileHash -LiteralPath (Join-Path $repositoryRoot $input.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $input.sha256) { throw 'Canonical adapter source changed during verification.' }
        }
    }
    finally { Pop-Location }
}
