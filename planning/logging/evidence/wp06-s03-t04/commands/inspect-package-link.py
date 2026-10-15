from pathlib import Path
import hashlib,json,zipfile,shutil
r=Path.cwd();e=r/'planning/logging/evidence/wp06-s03-t04'
p=r/'.artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg'
retained=e/'candidate'/p.name
retained.parent.mkdir(parents=True,exist_ok=True)
shutil.copyfile(p,retained)
with zipfile.ZipFile(retained) as z:
 core=z.read('lib/net10.0/CIS.Core.dll')
actual=json.loads((e/'fixtures-qualified/results.json').read_text(encoding='utf-8-sig'))
coreHash=hashlib.sha256(core).hexdigest()
assert coreHash==actual['coreSha256']
assert all(row['actual']['core']['sha256']==coreHash for row in actual['results'])
result=dict(packagePath=p.relative_to(r).as_posix(),retainedPackagePath=retained.relative_to(r).as_posix(),packageSha256=hashlib.sha256(p.read_bytes()).hexdigest(),entry='lib/net10.0/CIS.Core.dll',packedCoreSha256=coreHash,consumersLoadedPackedCoreBytes=True,packageRestoreQualificationClaimed=False)
(e/'package-link.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print(json.dumps(result))
