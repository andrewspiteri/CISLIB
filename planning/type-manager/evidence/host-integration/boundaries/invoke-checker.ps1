param([string] $Checker, [string] $LanguageRoot, [string] $Project)
try { & $Checker -LanguageRoot $LanguageRoot -ProjectPath $Project; if (-not $?) { exit 1 } }
catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
