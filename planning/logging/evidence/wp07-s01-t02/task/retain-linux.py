from pathlib import Path,PurePosixPath
import tarfile,sys
root=Path.cwd();task=root/'.artifacts/csharp/tasks/wp07-s01-t02';run=sys.argv[1]
assert run.isdecimal() and len(run)==2
out=task/('linux-'+run+'-retained');out.mkdir(exist_ok=True)
with tarfile.open(task/('linux-evidence-'+run+'.tar')) as archive:
 for member in archive.getmembers():
  path=PurePosixPath(member.name);assert not path.is_absolute() and '..' not in path.parts
  keep=member.name.startswith('results/') or member.name in ['os-release.txt','dotnet-info.txt','test-assembly.sha256'] or (member.name.startswith('.artifacts/csharp/candidate-consumers/') and Path(member.name).suffix in ['.json','.txt','.log','.bin'] and not '/packages/' in member.name and not '/http-cache/' in member.name)
  if member.isfile() and keep:
   target=out/member.name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(archive.extractfile(member).read())
print('Retained native run',run)
