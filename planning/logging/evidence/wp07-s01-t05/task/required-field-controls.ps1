$ErrorActionPreference = 'Stop'
Import-Module ./implementations/csharp/eng/Evidence.psm1 -Force
$taskControlPath = Join-Path $PWD '.artifacts/csharp/tasks/wp07-s01-t05/required-field-input.json'
$taskNative = @{ schema = 'cis.vstest-discovery/v1'; aborted = $false; errors = 0; total = 1; tests = @(@{ Id = [guid]::NewGuid().ToString(); Method = 'Checker.Valid'; Name = 'valid'; Source = [IO.Path]::GetFullPath('Checker.dll') }) }
$taskControls = @()
foreach ($taskField in @('schema', 'aborted', 'errors', 'total', 'tests')) {
    $taskVariant = $taskNative.Clone()
    $taskVariant.Remove($taskField)
    Write-VerificationJson $taskControlPath $taskVariant
    $taskRejected = $false
    try { $null = Read-NativeDiscovery $taskControlPath }
    catch { if ($_.Exception.Message.StartsWith('InvalidDiscovery:')) { $taskRejected = $true } else { throw } }
    if (-not $taskRejected) { throw "Missing $taskField was accepted." }
    $taskControls += @{ field = $taskField; issue = 'InvalidDiscovery'; passed = $true }
}
Write-VerificationJson (Join-Path $PWD '.artifacts/csharp/tasks/wp07-s01-t05/required-field-controls.json') @{ passed = $taskControls.Count; controls = $taskControls }
'Five required-field diagnostic controls passed.'
