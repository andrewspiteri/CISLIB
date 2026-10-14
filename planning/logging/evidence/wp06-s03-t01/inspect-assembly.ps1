param([Parameter(Mandatory)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
public static class LoggingPackageMetadata {
 public static string[] Inspect(string path) {
  using var stream = File.OpenRead(path);
  using var pe = new PEReader(stream);
  var reader = pe.GetMetadataReader();
  if (reader.GetString(reader.GetAssemblyDefinition().Name) != "CIS.Core") throw new InvalidDataException("Unexpected shipping assembly.");
  return reader.TypeDefinitions.Select(h => reader.GetTypeDefinition(h))
   .Where(t => reader.GetString(t.Namespace).StartsWith("CIS.Logging", StringComparison.Ordinal))
   .Select(t => reader.GetString(t.Namespace) + "." + reader.GetString(t.Name) + "|" + ((t.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public ? "public" : "nonpublic"))
   .OrderBy(x => x, StringComparer.Ordinal).ToArray();
 }
}
"@
$names = [LoggingPackageMetadata]::Inspect($AssemblyPath)
if ('CIS.Logging.MicrosoftLogger.CoreLoggerProvider|public' -notin $names) { throw 'Embedded public Microsoft provider is absent.' }
@{ assembly = 'CIS.Core'; embeddedProvider = 'CIS.Logging.MicrosoftLogger.CoreLoggerProvider'; types = @($names) } | ConvertTo-Json -Depth 5
