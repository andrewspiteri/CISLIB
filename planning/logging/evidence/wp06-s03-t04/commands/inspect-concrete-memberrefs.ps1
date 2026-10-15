param([Parameter(Mandatory)][string] $Consumer)
$ErrorActionPreference = 'Stop'
$stream = [IO.File]::OpenRead([IO.Path]::GetFullPath($Consumer))
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $found = foreach ($handle in $reader.MemberReferences) {
        $member = $reader.GetMemberReference($handle)
        if ($member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
        $type = $reader.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
        $fullName = $reader.GetString($type.Namespace) + '.' + $reader.GetString($type.Name)
        if ($fullName -eq 'CIS.Logging.MicrosoftLogger.CoreLoggerProvider') { $reader.GetString($member.Name) }
    }
    foreach ($name in @('.ctor','CreateLogger','SetScopeProvider','Dispose')) { if ($name -notin $found) { throw "Concrete memberref absent: $name" } }
    @{ consumerSha256 = (Get-FileHash -LiteralPath $Consumer -Algorithm SHA256).Hash.ToLowerInvariant(); concreteProviderMemberRefs = @($found | Sort-Object -Unique); method = 'PE metadata only, without loading or executing the consumer'; passed = $true } | ConvertTo-Json -Depth 5
}
finally { $pe.Dispose(); $stream.Dispose() }
