param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = (Get-Location).Path
$taskRoot = Join-Path $repositoryRoot '.artifacts/csharp/tasks/wp06-s03-t03'
$scriptPath = Join-Path $repositoryRoot 'implementations/csharp/eng/verify-microsoft-logging.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Runner parse errors.' }
foreach ($function in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true)) { Invoke-Expression $function.Extent.Text }
$matrix = Get-Content implementations/csharp/verification/shipping-projects.json -Raw | ConvertFrom-Json
$expectedPaths = @($matrix.shippingProjects[0].moduleSlices[0].sources | ForEach-Object { 'implementations/csharp/' + $_ } | Sort-Object)
$sourceInputs = @($expectedPaths | ForEach-Object { @{path=$_} })
$OutputRoot = Join-Path $taskRoot 'guards'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$results = [Collections.Generic.List[object]]::new()
$native = Join-Path $taskRoot 'mutation-final/mutation/reports/mutation-report.json'
Write-MutationObservation $native
$results.Add(@{id='mutation-positive';passed=$true})
foreach ($case in @('missing', 'empty', 'zero', 'extra')) {
    $report = Get-Content $native -Raw | ConvertFrom-Json
    $adapter = @($report.files.PSObject.Properties | Where-Object { $_.Name.Replace('\', '/') -match '/Logging/MicrosoftLogger/' })
    switch ($case) {
        'missing' { $report.files.PSObject.Properties.Remove($adapter[0].Name) }
        'empty' { $adapter[0].Value.mutants = @() }
        'zero' { foreach ($file in $adapter) { foreach ($mutant in $file.Value.mutants) { $mutant.status = 'Ignored' } } }
        'extra' { $report.files | Add-Member -NotePropertyName ($adapter[0].Name.Replace('CoreLoggerProvider.cs', 'Unexpected.cs')) -NotePropertyValue $adapter[0].Value }
    }
    $path = Join-Path $OutputRoot "$case.json"
    $report | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $path -Encoding utf8
    $failure = $null
    try { Write-MutationObservation $path } catch { $failure = $_.Exception.Message }
    if (-not $failure) { throw "Mutation guard failed to reject $case" }
    $results.Add(@{id="mutation-$case";passed=$true;rejection=$failure})
}
$coverage = Join-Path $taskRoot 'coverage-final/unit'
Write-CoverageObservation $coverage
$results.Add(@{id='coverage-positive';passed=$true})
[xml] $trx = Get-Content (Join-Path $coverage 'report.trx') -Raw
$name = $trx.SelectSingleNode('//*[local-name()="UriAttachment"]/*[local-name()="A"]').GetAttribute('href').Replace('\', '/').Split('/')[-1]
$nativeXml = @(Get-ChildItem $coverage -Recurse -File | Where-Object Name -eq $name)[0]
foreach ($case in @('missing', 'unexecuted')) {
    $directory = Join-Path $OutputRoot "coverage-$case"
    New-Item -ItemType Directory $directory -Force | Out-Null
    Copy-Item (Join-Path $coverage 'report.trx') (Join-Path $directory 'report.trx')
    [xml] $xml = Get-Content $nativeXml.FullName -Raw
    $classes = @($xml.SelectNodes('//class') | Where-Object { $_.GetAttribute('filename') -match 'ProviderScopeCapture.cs$' })
    foreach ($class in $classes) {
        if ($case -eq 'missing') { $null = $class.ParentNode.RemoveChild($class) }
        else { foreach ($line in $class.SelectNodes('./lines/line')) { $line.SetAttribute('hits','0') } }
    }
    $xml.Save((Join-Path $directory $nativeXml.Name))
    $failure = $null
    try { Write-CoverageObservation $directory } catch { $failure = $_.Exception.Message }
    if (-not $failure) { throw "Coverage guard failed to reject $case" }
    $results.Add(@{id="coverage-$case";passed=$true;rejection=$failure})
}
$results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputRoot 'guard-results.json') -Encoding utf8
$results | ConvertTo-Json -Depth 5
