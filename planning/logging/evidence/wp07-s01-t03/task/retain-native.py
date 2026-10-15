from pathlib import Path,PurePosixPath
import tarfile
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp07-s01-t03';out=t/'native-retained';assert not out.exists(),'Refuse stale extraction output';out.mkdir()
with tarfile.open(t/'native-evidence.tar') as archive:
 for member in archive.getmembers():
  p=PurePosixPath(member.name);assert not p.is_absolute() and '..' not in p.parts
  if member.isfile():
   target=out/member.name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(archive.extractfile(member).read())
print('Native reports retained in a fresh root')
