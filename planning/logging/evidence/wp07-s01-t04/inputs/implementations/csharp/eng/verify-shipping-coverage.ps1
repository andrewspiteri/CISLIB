param([Parameter(Mandatory)][string] $OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$languageRoot = Split-Path $PSScriptRoot -Parent
$policy = Get-Content -Raw (Join-Path $languageRoot 'verification/coverage/shipping.json') | ConvertFrom-Json
$toolchain = Get-Content -Raw (Join-Path $PSScriptRoot 'toolchain.json') | ConvertFrom-Json
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Use a new output directory; native reports are immutable.' }
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
$commands = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[string]]::new()
$inputPaths = @((Join-Path $languageRoot 'Directory.Build.props'), (Join-Path $languageRoot 'Directory.Packages.props'), (Join-Path $languageRoot 'global.json'), (Join-Path $languageRoot 'NuGet.config'), $PSCommandPath, (Join-Path $PSScriptRoot 'summarize-shipping-coverage.py'), (Join-Path $PSScriptRoot 'toolchain.json'),
    (Join-Path $languageRoot 'verification/coverage/shipping.json'), (Join-Path $languageRoot $policy.settings))
foreach ($assembly in $policy.assemblies) {
    $inputPaths += Join-Path $languageRoot $assembly.project
    $inputPaths += Get-ChildItem (Join-Path $languageRoot $assembly.sourceRoot) -Recurse -File -Filter '*.cs' |
        Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]' | ForEach-Object FullName
    $binary = Join-Path (Split-Path (Join-Path $languageRoot $assembly.project) -Parent) ("bin/Release/net10.0/" + $assembly.name + '.dll')
    $inputPaths += $binary
    $inputPaths += [IO.Path]::ChangeExtension($binary, '.pdb')
}
$inputPaths += Get-ChildItem (Join-Path $languageRoot 'tests') -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs', '.csproj', '.props', '.targets', '.json', '.py', '.ps1') } | ForEach-Object FullName
foreach ($family in $policy.families.PSObject.Properties) {
    $inputPaths += Join-Path $languageRoot ("tests/CIS.Core.$($family.Value)/bin/Release/net10.0/CIS.Core.$($family.Value).dll")
}
$inputs = @($inputPaths | Sort-Object -Unique | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($languageRoot, $_).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$inputs | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputRoot 'inputs.json') -Encoding utf8
$previousReuse = $env:MSBUILDDISABLENODEREUSE
Push-Location $languageRoot
try {
    $env:MSBUILDDISABLENODEREUSE = '1'
    if ((& dotnet --version).Trim() -ne $toolchain.sdk) { throw 'Selected SDK differs from the pin.' }
    [ordered]@{ os = [Runtime.InteropServices.RuntimeInformation]::OSDescription; processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString();
        sdk = (& dotnet --version); sourceCommit = (& git -C $languageRoot rev-parse HEAD) } | ConvertTo-Json | Set-Content (Join-Path $OutputRoot 'environment.json') -Encoding utf8
    $repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
    $packages = @(Get-ChildItem (Join-Path $repositoryRoot '.artifacts/csharp/packages') -File -Filter 'CIS.Core.*.nupkg')
    if ($packages.Count -ne 1) { throw 'Expected exactly one prepared candidate Core package.' }
    Add-Type -AssemblyName System.IO.Compression
    $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        $expected = @($policy.assemblies | ForEach-Object packageEntry | Sort-Object)
        $actual = @($archive.Entries | Where-Object { $_.FullName -match '^(lib|tools)/.*[.]dll$' } | ForEach-Object FullName | Sort-Object)
        if (@(Compare-Object $expected $actual).Count -ne 0) { throw 'Shipped DLL inventory differs from the coverage policy.' }
        foreach ($assembly in $policy.assemblies) {
            $entry = $archive.GetEntry($assembly.packageEntry)
            $stream = $entry.Open()
            try { $packedHash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            $binary = Join-Path (Split-Path (Join-Path $languageRoot $assembly.project) -Parent) ("bin/Release/net10.0/" + $assembly.name + '.dll')
            if ((Get-FileHash $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $packedHash) { throw "Repack the current candidate before coverage: $($assembly.name)" }
        }
    }
    finally { $archive.Dispose() }
    $inputPaths += $packages[0].FullName
    $packageHash = (Get-FileHash $packages[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    @{ path = [IO.Path]::GetRelativePath($repositoryRoot, $packages[0].FullName).Replace('\', '/'); sha256 = $packageHash } |
        ConvertTo-Json | Set-Content (Join-Path $OutputRoot 'candidate.json') -Encoding utf8
    foreach ($family in $policy.families.PSObject.Properties) {
        $directory = Join-Path $OutputRoot $family.Name
        $project = "tests/CIS.Core.$($family.Value)/CIS.Core.$($family.Value).csproj"
        $arguments = @('test', $project, '-c', 'Release', '--no-build', '--no-restore', '--settings', $policy.settings,
            '--collect', 'Code Coverage;Format=cobertura', '--results-directory', $directory, '--logger', 'trx;LogFileName=report.trx')
        $started = [DateTime]::UtcNow.ToString('O')
        & dotnet @arguments *> (Join-Path $OutputRoot ($family.Name + '.log'))
        $code = $LASTEXITCODE
        $commands.Add(@{ family = $family.Name; executable = 'dotnet'; argv = $arguments; workingDirectory = $languageRoot; startedUtc = $started; finishedUtc = [DateTime]::UtcNow.ToString('O'); exitCode = $code })
        $commands | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputRoot 'commands.json') -Encoding utf8
        if ($code -ne 0) { throw "Coverage test family $($family.Name) failed ($code); see its retained log." }
        $results.Add($directory)
    }
    if ((Get-FileHash $packages[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $packageHash) { throw 'Candidate package changed during coverage.' }
    foreach ($input in $inputs) {
        if ((Get-FileHash -LiteralPath (Join-Path $languageRoot $input.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $input.sha256) { throw "Coverage input changed: $($input.path)" }
    }
    & python (Join-Path $PSScriptRoot 'summarize-shipping-coverage.py') --language-root $languageRoot --results @results --output (Join-Path $OutputRoot 'coverage.json')
    exit $LASTEXITCODE
}
finally {
    $env:MSBUILDDISABLENODEREUSE = $previousReuse
    Pop-Location
}
