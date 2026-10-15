from pathlib import Path
import json,hashlib,tarfile,uuid,shlex,subprocess
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp07-s01-t03'
assert (t/'developer.exit').read_text(encoding='utf-8-sig').strip()=='0','Complete full Windows verification before staging native binaries'
root='/tmp/cislib-wp07-s01-t03-'+uuid.uuid4().hex
sdk='/mnt/c/miscwork/portfolio/cislib/.artifacts/type-manager/toolchains/linux-sdk-10.0.401'
filters=json.loads((t/'focus-filters.json').read_text(encoding='utf-8-sig'))
entries=[]
for directory in ['implementations/csharp/tests/CIS.Core.UnitTests/bin/Release/net10.0','implementations/csharp/tests/CIS.Core.IntegrationTests/bin/Release/net10.0','conformance','contracts']:
 for f in (r/directory).rglob('*'):
  if f.is_file():entries.append((f,f.relative_to(r).as_posix()))
for name in ['implementations/csharp/CisLib.slnx','implementations/csharp/global.json','conformance/logging/concurrency-qualification.json']:
 if name not in {name for _,name in entries}:entries.append((r/name,name))
archive=t/'native-inputs.tar'
with tarfile.open(archive,'w') as tar:
 for f,name in entries:tar.add(f,arcname=name,recursive=False)
manifest={'root':root,'sdk':sdk,'archiveSha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'filters':filters,'files':[{'path':name,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()} for f,name in entries]}
(t/'native-inputs.json').write_bytes((json.dumps(manifest,indent=2)+'\n').encode())
base='/mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t03/'
lines=['set -eu','[ ! -e '+shlex.quote(root)+' ]','mkdir -p '+shlex.quote(root),'sha256sum '+shlex.quote(base+'native-inputs.tar')+' > '+shlex.quote(root+'/payload.sha256'),'tar -xf '+shlex.quote(base+'native-inputs.tar')+' -C '+shlex.quote(root),'export DOTNET_ROOT='+shlex.quote(sdk),'export PATH='+shlex.quote(sdk)+':"$PATH"','cd '+shlex.quote(root+'/implementations/csharp'),'cat /etc/os-release > '+shlex.quote(root+'/os-release.txt'),shlex.quote(sdk+'/dotnet')+' --info > '+shlex.quote(root+'/dotnet-info.txt')]
assembly_paths=[]
for family,project in [('unit','CIS.Core.UnitTests'),('integration','CIS.Core.IntegrationTests')]:assembly_paths.append(root+'/implementations/csharp/tests/'+project+'/bin/Release/net10.0/'+project+'.dll')
lines.append('sha256sum '+shlex.join(assembly_paths)+' > '+shlex.quote(root+'/test-assemblies.sha256'))
lines+=['set +e']
for family,path in zip(['unit','integration'],assembly_paths):
 command=[sdk+'/dotnet','vstest',path,'--TestCaseFilter:'+filters[family],'--ResultsDirectory:'+root+'/results/'+family,'--logger:trx;LogFileName=report.trx']
 lines += [shlex.join(command),family+'_code=$?']
lines+=['set -e','cd '+shlex.quote(root),'tar -cf '+shlex.quote(base+'native-evidence.tar')+' results payload.sha256 test-assemblies.sha256 os-release.txt dotnet-info.txt','[ "$unit_code" -eq 0 ] && [ "$integration_code" -eq 0 ]']
script='\n'.join(lines)+'\n';(t/'run-native.sh').write_bytes(script.encode())
with (t/'native.log').open('wb') as output:
 result=subprocess.run(['wsl','-d','Ubuntu-24.04','--','bash',base+'run-native.sh'],stdout=output,stderr=subprocess.STDOUT,timeout=300)
(t/'native.exit').write_bytes((str(result.returncode)+'\n').encode());print('Native focused families exited',result.returncode)
raise SystemExit(result.returncode)
