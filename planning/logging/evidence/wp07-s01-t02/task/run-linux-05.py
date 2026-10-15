from pathlib import Path
import json,tarfile,uuid,shutil,subprocess,shlex,hashlib
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp07-s01-t02';old=json.loads((t/'linux-inputs.json').read_text());native='/tmp/cislib-wp07-s01-t02-'+uuid.uuid4().hex;sdk=old['sdk'];latest=max((r/'.artifacts/csharp/candidate-consumers').glob('*/candidate.json'),key=lambda p:p.stat().st_mtime).parent
entries=[]
assets=json.loads((r/'implementations/csharp/src/CIS.Core/obj/project.assets.json').read_text(encoding='utf-8-sig'));assets['packageFolders']={native+'/dependency-cache/':{}};(t/'linux-core-assets-05.json').write_bytes((json.dumps(assets,indent=2)+'\n').encode())
for f in old['files']:
 name=f['path'];p=r/name
 if name=='implementations/csharp/src/CIS.Core/obj/project.assets.json':p=t/'linux-core-assets-05.json'
 elif name.startswith('dependency-cache/'):p=latest/'feed'/Path(name).name
 assert p.is_file(),p
 entries.append((p,name))
# Binaries have the same filenames after the test-only rebuild, and current contents are captured.
archive=t/'linux-payload-05.tar'
with tarfile.open(archive,'w') as tar:
 for p,name in entries:tar.add(p,arcname=name,recursive=False)
manifest={'root':native,'sdk':sdk,'archiveSha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'files':[{'path':name,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p,name in entries]};(t/'linux-inputs-05.json').write_bytes((json.dumps(manifest,indent=2)+'\n').encode())
command=[sdk+'/dotnet','vstest',native+'/implementations/csharp/tests/CIS.Core.IntegrationTests/bin/Release/net10.0/CIS.Core.IntegrationTests.dll','--TestCaseFilter:FullyQualifiedName~CandidateLoggingProcessTests','--ResultsDirectory:'+native+'/results','--logger:trx;LogFileName=report.trx']
base='/mnt/c/miscwork/portfolio/cislib/.artifacts/csharp/tasks/wp07-s01-t02/'
script='set -eu\n[ ! -e '+shlex.quote(native)+' ]\nmkdir -p '+shlex.quote(native)+'\ntar -xf '+shlex.quote(base+archive.name)+' -C '+shlex.quote(native)+'\nexport DOTNET_ROOT='+shlex.quote(sdk)+'\nexport PATH='+shlex.quote(sdk)+':"$PATH"\ncd '+shlex.quote(native+'/implementations/csharp')+'\ncat /etc/os-release > '+shlex.quote(native+'/os-release.txt')+'\n'+shlex.quote(sdk+'/dotnet')+' --info > '+shlex.quote(native+'/dotnet-info.txt')+'\nset +e\n'+shlex.join(command)+'\ncase_code=$?\nset -e\ncd '+shlex.quote(native)+'\ntar -cf '+shlex.quote(base+'linux-evidence-05.tar')+' results .artifacts/csharp/candidate-consumers os-release.txt dotnet-info.txt\nexit "$case_code"\n'
script=script.replace('set +e\n', 'sha256sum '+shlex.quote(native+'/implementations/csharp/tests/CIS.Core.IntegrationTests/bin/Release/net10.0/CIS.Core.IntegrationTests.dll')+' > '+shlex.quote(native+'/test-assembly.sha256')+'\nset +e\n').replace('os-release.txt dotnet-info.txt', 'os-release.txt dotnet-info.txt test-assembly.sha256')
(t/'run-linux-05.sh').write_bytes(script.encode())
with (t/'linux-05.log').open('wb') as out:
 result=subprocess.run(['wsl','-d','Ubuntu-24.04','--','bash',base+'run-linux-05.sh'],stdout=out,stderr=subprocess.STDOUT,timeout=180)
(t/'linux-05.exit').write_bytes((str(result.returncode)+'\n').encode());print('Linux corrected candidate matrix exited',result.returncode);raise SystemExit(result.returncode)
