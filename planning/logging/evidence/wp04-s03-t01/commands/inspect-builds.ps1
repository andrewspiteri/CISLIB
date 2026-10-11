param([Parameter(Mandatory)][string] $DirectAssembly, [Parameter(Mandatory)][string] $FreshAssembly, [Parameter(Mandatory)][string] $Output)
$ErrorActionPreference='Stop'
$tokenMethod=[Reflection.Metadata.Ecma335.MetadataTokens].GetMethod('GetToken',[type[]]@([Reflection.Metadata.EntityHandle]))
$results=@()
foreach ($path in @($DirectAssembly,$FreshAssembly)) {
    $stream=[IO.File]::OpenRead($path)
    $pe=[Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $reader=[Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $methods=@()
        foreach ($handle in $reader.MethodDefinitions) {
            $method=$reader.GetMethodDefinition($handle)
            $body=$null
            if ($method.RelativeVirtualAddress -ne 0) {
                $il=[Reflection.Metadata.PEReaderExtensions]::GetMethodBody($pe,$method.RelativeVirtualAddress)
                $body=[ordered]@{
                    ilSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($il.GetILBytes())).ToLowerInvariant()
                    maxStack=$il.MaxStack
                    localsInitialized=$il.LocalVariablesInitialized
                    localSignature=$tokenMethod.Invoke($null,@([Reflection.Metadata.EntityHandle]$il.LocalSignature))
                    exceptionRegions=@($il.ExceptionRegions | ForEach-Object { [ordered]@{
                        kind=$_.Kind.ToString();tryOffset=$_.TryOffset;tryLength=$_.TryLength
                        handlerOffset=$_.HandlerOffset;handlerLength=$_.HandlerLength;filterOffset=$_.FilterOffset
                        catchType=$tokenMethod.Invoke($null,@([Reflection.Metadata.EntityHandle]$_.CatchType))
                    } })
                }
            }
            $methods += [ordered]@{
                token=$tokenMethod.Invoke($null,@([Reflection.Metadata.EntityHandle]$handle))
                name=$reader.GetString($method.Name)
                signatureSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($reader.GetBlobBytes($method.Signature))).ToLowerInvariant()
                body=$body
            }
        }
        $code=$pe.ReadCodeViewDebugDirectoryData(@($pe.ReadDebugDirectory() | Where-Object {$_.Type -eq 'CodeView'})[0])
        $assembly=[Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($path))
        $attributes=$assembly.GetCustomAttributesData()
        $fileVersion=@($attributes | Where-Object {$_.AttributeType.FullName -eq 'System.Reflection.AssemblyFileVersionAttribute'})[0].ConstructorArguments[0].Value
        $informationalVersion=@($attributes | Where-Object {$_.AttributeType.FullName -eq 'System.Reflection.AssemblyInformationalVersionAttribute'})[0].ConstructorArguments[0].Value
        $results += [ordered]@{
            path=$path;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
            assemblyName=$assembly.FullName;assemblyVersion=$assembly.GetName().Version.ToString()
            fileVersion=$fileVersion;informationalVersion=$informationalVersion
            mvid=$reader.GetGuid($reader.GetModuleDefinition().Mvid).ToString()
            pdbPath=$code.Path;pdbGuid=$code.Guid.ToString();methods=$methods
        }
    }
    finally { $pe.Dispose();$stream.Dispose() }
}
$results | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Output
[ordered]@{assemblies=$results.Count;methodDefinitions=$results[0].methods.Count;methodsWithBodies=@($results[0].methods | Where-Object {$null -ne $_.body}).Count} | ConvertTo-Json
