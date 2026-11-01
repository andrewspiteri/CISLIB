[CmdletBinding(DefaultParameterSetName = 'Focused')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Focused')][string] $Project,
    [Parameter(ParameterSetName = 'Focused')][string] $Class,
    [Parameter(ParameterSetName = 'Focused')][string] $Category,
    [Parameter(ParameterSetName = 'Focused')][string] $Filter,
    [Parameter(Mandatory, ParameterSetName = 'Fast')][switch] $Fast,
    [Parameter(ParameterSetName = 'Fast')][switch] $NoBuild,
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug',
    [ValidateRange(1, 3600)][int] $TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$languageRoot = Join-Path $repository 'implementations/csharp'
if ($Fast) {
    $arguments = @('fast-tests', '--repo-root', $repository, '--configuration', $Configuration,
        '--no-build', ($NoBuild.IsPresent.ToString().ToLowerInvariant()), '--timeout-seconds', "$TimeoutSeconds")
    Push-Location $languageRoot
    try {
        if ($NoBuild) {
            $runner = Join-Path $languageRoot "eng/CIS.DependencyChecker/bin/$Configuration/net10.0/CIS.DependencyChecker.dll"
            if (!(Test-Path -LiteralPath $runner)) { throw 'Build the fast test runner first with eng/test.ps1 -Fast in this configuration.' }
            & dotnet exec --fx-version 10.0.12 --roll-forward Disable $runner @arguments
        }
        else {
            & dotnet run --project eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj -c $Configuration -p:RestoreLockedMode=true --no-launch-profile -- @arguments
        }
        exit $LASTEXITCODE
    }
    finally { Pop-Location }
}
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
