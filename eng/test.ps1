param(
    [Parameter(Mandatory)][string] $Project,
    [string] $Class,
    [string] $Category,
    [string] $Filter,
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug',
    [ValidateRange(1, 3600)][int] $TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$languageRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'implementations/csharp'
$arguments = @('focused-test', $languageRoot, '--project', $Project, '--configuration', $Configuration, '--timeout-seconds', "$TimeoutSeconds")
if ($PSBoundParameters.ContainsKey('Class')) { $arguments += @('--class', $Class) }
if ($PSBoundParameters.ContainsKey('Category')) { $arguments += @('--category', $Category) }
if ($PSBoundParameters.ContainsKey('Filter')) { $arguments += @('--filter', $Filter) }
Push-Location $languageRoot
try {
    & dotnet run --project eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj -c $Configuration -p:RestoreLockedMode=true --no-launch-profile -- @arguments
    exit $LASTEXITCODE
}
finally { Pop-Location }
