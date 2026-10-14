"""Prepare an isolated source-module mutation project; never mutate shipping Core.

Run the emitted baseline command, then Stryker with the emitted configuration.
Preparation alone is not a passing mutation result or a release qualification.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo-root', type=Path, required=True)
    parser.add_argument('--output-root', type=Path, required=True)
    parser.add_argument('--candidate-core', type=Path, required=True)
    parser.add_argument('--candidate-report', type=Path, required=True)
    parser.add_argument('--expected-candidate-report-sha256', required=True)
    parser.add_argument('--expected-core-sha256', required=True)
    args = parser.parse_args()
    root = args.output_root.resolve()
    if root.exists() and any(root.iterdir()):
        parser.error('Use an empty output directory')
    language = args.repo_root.resolve() / 'implementations/csharp'
    producer_bytes = args.candidate_report.read_bytes()
    if hashlib.sha256(producer_bytes).hexdigest() != args.expected_candidate_report_sha256:
        raise ValueError('Candidate producer differs from the independent report pin')
    producer = json.loads(producer_bytes.decode('utf-8-sig'))
    if (digest(args.candidate_core) != args.expected_core_sha256 or
            producer.get('complete') is not True or producer.get('coreSha256') != args.expected_core_sha256):
        raise ValueError('Completed producer/Core differs from the independent pin')
    module = language / 'src/CIS.Core/TypeManagement'
    sources = {p.relative_to(language / 'src/CIS.Core').as_posix(): digest(p) for p in module.rglob('*.cs')}
    expected = {key: value for key, value in producer['coreSourceInputs'].items() if key.startswith('TypeManagement/')}
    if not sources or sources != expected:
        raise ValueError('Module source set/hashes differ from the candidate producer')
    if any(len(Path(relative).parts) != 2 for relative in sources):
        raise ValueError('Expand the mutation glob before adding nested module sources')
    root.mkdir(parents=True, exist_ok=True)
    source = root / 'source'
    inputs = {}

    def copy(path, relative):
        expected_hash = digest(path)
        target = source / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        if digest(target) != expected_hash:
            raise ValueError('Input changed while copying: ' + str(path))
        inputs[relative] = expected_hash

    for relative, expected_hash in sources.items():
        copy(language / 'src/CIS.Core' / relative, 'Core/' + relative)
        if inputs['Core/' + relative] != expected_hash:
            raise ValueError('Module changed after provenance validation')
    for pattern in ('tests/CIS.Core.UnitTests/TypeManagerInputTests.cs',
                    'tests/CIS.Core.UnitTests/TypeManagerManifestTests.cs',
                    'tests/CIS.Core.UnitTests/TypeManagerCatalogTests.cs',
                    'tests/CIS.Core.UnitTests/TypeManagerOrderingTests.cs',
                    'tests/CIS.Core.ArchitectureTests/TypeManagerRuntimeArchitectureTests.cs',
                    'tests/CIS.Core.BusinessCases/TypeManagerStartupStories.cs'):
        matches = sorted(language.glob(pattern))
        if not matches:
            raise ValueError('Missing canonical test input: ' + pattern)
        for path in matches:
            copy(path, 'Tests/' + path.name)
    for name in ('global.json', 'Directory.Build.props', 'Directory.Packages.props'):
        copy(language / name, name)
    copy(language / 'eng/toolchain.json', 'toolchain.json')
    copy(language / 'verification/mutation/type-manager-config.json', 'stryker-config.json')
    policy = json.loads((source / 'toolchain.json').read_text(encoding='utf-8-sig'))
    if policy['targetFramework'] != 'net10.0':
        raise ValueError('This mutation project supports net10.0 only')
    references = []
    for name in ('Microsoft.Extensions.Logging.Abstractions', 'Microsoft.Extensions.DependencyInjection.Abstractions'):
        path = args.candidate_core.parent / (name + '.dll')
        expected_hash = producer['fixtureFiles']['Host/bin/Release/net10.0/' + path.name]
        if digest(path) != expected_hash:
            raise ValueError('Dependency differs from producer: ' + path.name)
        copy(path, 'references/' + path.name)
        if inputs['references/' + path.name] != expected_hash:
            raise ValueError('Dependency changed while copying: ' + path.name)
        references.append(f'<Reference Include="{name}" HintPath="../references/{path.name}" />')
    (source / 'Directory.Build.targets').write_text('<Project />', encoding='utf-8')
    (source / 'NuGet.config').write_text('<configuration><packageSources><clear/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><auditSources><clear/></auditSources></configuration>', encoding='utf-8')
    (source / 'Core/Core.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><AssemblyName>CIS.Core</AssemblyName><AssemblyVersion>1.0.0.0</AssemblyVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><InternalsVisibleTo Include="CIS.Core.UnitTests" /></ItemGroup></Project>', encoding='utf-8')
    (source / 'Tests/Tests.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><AssemblyName>CIS.Core.UnitTests</AssemblyName><IsTestProject>true</IsTestProject><AllowUnsafeBlocks>true</AllowUnsafeBlocks><RuntimeFrameworkVersion>' + policy['targetRuntime'] + '</RuntimeFrameworkVersion><RollForward>Disable</RollForward><EnableMSTestRunner>false</EnableMSTestRunner><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><ProjectReference Include="../Core/Core.csproj" />' + ''.join(references) + '<PackageReference Include="Microsoft.NET.Test.Sdk"/><PackageReference Include="MSTest.TestFramework"/><PackageReference Include="MSTest.TestAdapter"/></ItemGroup></Project>', encoding='utf-8')
    (source / 'Tests/AssemblyInfo.cs').write_text('[assembly: Microsoft.VisualStudio.TestTools.UnitTesting.DoNotParallelize]', encoding='utf-8')
    report = dict(prepared=True, qualified=False, scope='Isolated source-module mutation; not shipping Core',
                  candidateCoreSha256=args.expected_core_sha256, candidateReportSha256=args.expected_candidate_report_sha256,
                  preparerSha256=digest(Path(__file__)), inputs=inputs,
                  generatedFiles={p.relative_to(source).as_posix(): digest(p) for p in sorted(source.rglob('*')) if p.is_file()},
                  expectedBaselineCases=90,
                  baselineCommand='dotnet test Tests/Tests.csproj -c Release -m:1 --logger trx',
                  mutationCommand='dotnet exec --fx-version ' + policy['toolHostRuntime'] + ' --roll-forward Disable <Stryker.CLI.dll> --config-file stryker-config.json --output <report-directory>',
                  workingDirectory=str(source))
    (root / 'preparation.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
