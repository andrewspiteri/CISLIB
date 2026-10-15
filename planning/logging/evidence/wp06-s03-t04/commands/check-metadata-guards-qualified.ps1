$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = (Get-Location).Path
$taskRoot = Join-Path $repositoryRoot '.artifacts/csharp/tasks/wp06-s03-t04'
$runner = Join-Path $repositoryRoot 'implementations/csharp/eng/check-microsoft-logging-compatibility.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($runner, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Runner parse failure.' }
foreach ($function in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true)) { Invoke-Expression $function.Extent.Text }
$policy = Get-Content implementations/csharp/eng/toolchain.json -Raw | ConvertFrom-Json
$native = Join-Path $taskRoot 'qualified-fixtures/source/bin/Release/net10.0'
$originalFixtures = Join-Path $repositoryRoot 'implementations/csharp/verification/compatibility/microsoft-logging'
$OutputRoot = Join-Path $taskRoot 'metadata-guards-qualified'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$results = [Collections.Generic.List[object]]::new()
foreach ($case in @('positive','api-parameter','candidate-hash')) {
    $fixtures = Join-Path $OutputRoot "$case-fixtures"
    New-Item -ItemType Directory -Path $fixtures -Force | Out-Null
    Copy-Item (Join-Path $originalFixtures 'api-contract.json') (Join-Path $fixtures 'api-contract.json')
    $report = @{ coreSha256 = Hash (Join-Path $native 'CIS.Core.dll'); results = @() }
    if ($case -eq 'api-parameter') {
        $api = Get-Content (Join-Path $fixtures 'api-contract.json') -Raw | ConvertFrom-Json
        $api.constructor.parameters[0].name = 'wrongName'
        $api | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixtures 'api-contract.json') -Encoding utf8
    }
    if ($case -eq 'candidate-hash') { $report.coreSha256 = 'f' * 64 }
    $failure = $null
    try { Invoke-Consumer $case $native } catch { $failure = $_.Exception.Message }
    if (($case -eq 'positive') -ne ($null -eq $failure)) { throw "Unexpected metadata guard outcome: $case/$failure" }
    if ($case -eq 'api-parameter' -and $failure -ne 'Provider parameter name changed.') { throw 'Wrong API rejection reason.' }
    if ($case -eq 'candidate-hash' -and $failure -ne 'Consumer did not load selected Core bytes.') { throw 'Wrong Core rejection reason.' }
    $results.Add(@{ id = $case; matched = $true; actualRejection = $failure })
}
@{ runnerSha256 = Hash $runner; nativeConsumerSha256 = Hash (Join-Path $native 'CIS.Logging.ProviderContractConsumer.dll'); results = $results.ToArray(); scope = 'Metadata and selected-load guard controls using the actual runner function and native consumer. Not full compiled breaking API qualification.' } |
    ConvertTo-Json -Depth 7 | Set-Content (Join-Path $OutputRoot 'results.json') -Encoding utf8
$results | ConvertTo-Json -Depth 5
