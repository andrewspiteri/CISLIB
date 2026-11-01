#Requires -Version 7.5
param(
    [string] $OutputRoot = '',
    [ValidateRange(1, 3600)][int] $NativeBudgetSeconds = 300,
    [ValidateRange(1, 4)][int] $NativeWorkers = 1,
    [ValidateRange(1, 3600)][int] $PreparationTimeoutSeconds = 1800,
    [string] $PreparationRoot = '',
    [string] $ApiCompatTool = '',
    [string] $ApiDotnet = ''
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$language = Join-Path $repository 'implementations/csharp'
$output = if ($OutputRoot) { [IO.Path]::GetFullPath($OutputRoot) } else { Join-Path $repository ('.artifacts/csharp/regression/' + [guid]::NewGuid().ToString('N')) }
$selectedApiTool = if ($ApiCompatTool) { [IO.Path]::GetFullPath($ApiCompatTool) } else { '' }
$selectedApiDotnet = if ($ApiDotnet) { [IO.Path]::GetFullPath($ApiDotnet) } else { '' }
$selectedPreparation = if ($PreparationRoot) { [IO.Path]::GetFullPath($PreparationRoot) } else { '' }
if ($selectedPreparation) {
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $prefix = [IO.Path]::TrimEndingDirectorySeparator($selectedPreparation) + [IO.Path]::DirectorySeparatorChar
    if ($output.Equals($selectedPreparation, $comparison) -or $output.StartsWith($prefix, $comparison)) {
        throw 'Regression output must not modify the selected prepared-input directory.'
    }
}
if (Test-Path -LiteralPath $output) { throw 'Use a new regression output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
Push-Location $language
try {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & dotnet build eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj -c Release -p:RestoreLockedMode=true --nologo *> (Join-Path $output 'bootstrap.log')
    if ($LASTEXITCODE -ne 0) { throw "Regression checker bootstrap failed. See $(Join-Path $output 'bootstrap.log')" }
    $bootstrap = $clock.Elapsed.TotalSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    # Freeze the active checker so preparation can rebuild canonical outputs on Windows.
    $runner = Join-Path $output 'runner'
    Copy-Item -LiteralPath (Join-Path $language 'eng/CIS.DependencyChecker/bin/Release/net10.0') -Destination $runner -Recurse
    $arguments = @('regression-tests', '--repo-root', $repository, '--output-root', $output,
        '--native-budget-seconds', "$NativeBudgetSeconds", '--native-workers', "$NativeWorkers", '--preparation-timeout-seconds', "$PreparationTimeoutSeconds",
        '--bootstrap-seconds', $bootstrap, '--powershell', (Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })))
    if ($selectedApiTool) { $arguments += @('--api-compat-tool', $selectedApiTool) }
    if ($selectedApiDotnet) { $arguments += @('--api-dotnet', $selectedApiDotnet) }
    if ($selectedPreparation) { $arguments += @('--preparation-root', $selectedPreparation) }
    & dotnet (Join-Path $runner 'CIS.DependencyChecker.dll') @arguments
    exit $LASTEXITCODE
}
finally { Pop-Location }
