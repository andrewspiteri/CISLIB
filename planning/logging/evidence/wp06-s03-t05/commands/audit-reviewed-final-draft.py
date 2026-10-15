from pathlib import Path
import json,hashlib,zipfile
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp06-s03-t05';root=t/'reviewed-final-draft'
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'));sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
j=read(root/'manifest.json');spec=read(r/'implementations/csharp/verification/bundle/manifest.json')
assert j['state']=='IncompleteReleaseDraft' and j['releaseQualified'] is False and len(j['releaseAreas'])==9 and all(a['fullReleaseState']=='Pending' for a in j['releaseAreas'])
files={f['path']:f for f in j['files']};assert len(files)==len(j['files'])
for path,f in files.items():assert (r/path).is_file() and sha(r/path)==f['sha256'] and (r/path).stat().st_size==f['bytes'],path
for path in spec['module']['documentation']+spec['module']['requiredFiles']:assert path in files
for group in spec['module']['priorEvidenceRoots']:
 actual={p.relative_to(r).as_posix() for p in (r/group).rglob('*') if p.is_file()};assert actual and all(p in files and files[p]['role']=='PriorDevelopmentEvidenceOnly' for p in actual)
for name in ['json','html']:assert files[spec['module']['nativeMutation'][name]]['role']=='LocalNativeMutationReportNotReleaseRetained'
for source in spec['module']['sourceRoots']:
 members=[p for p in __import__('subprocess').check_output(['git','ls-files','--cached','--others','--exclude-standard','--',source['path']]).decode().splitlines() if (r/p).is_file()]
 assert members and all(p in files for p in members)
 assert all(files[p]['role']==source['role'] for p in members if p not in spec['module']['requiredFiles'])
audit=read(r/spec['module']['nativeMutation']['audit']);assert sha(r/spec['module']['nativeMutation']['json'])==audit[spec['module']['nativeMutation']['run']]['nativeReportSha256']
product=j['product'];assert sha(root/product['retainedPackage'])==product['packageSha256'] and sha(root/product['assembly'])==product['assemblySha256'] and sha(root/product['apiDocumentation'])==product['apiDocumentationSha256']
assert sha(root/'manifest.json')==(root/'manifest.sha256').read_text().strip()
with zipfile.ZipFile(root/product['retainedPackage']) as z:assert z.read('lib/net10.0/CIS.Core.dll')==(root/product['assembly']).read_bytes() and z.read('lib/net10.0/CIS.Core.xml')==(root/product['apiDocumentation']).read_bytes()
summary=dict(indexedFiles=len(files),indexedBytes=sum(f['bytes'] for f in files.values()),documentation=len(spec['module']['documentation']),priorEvidenceRoots=len(spec['module']['priorEvidenceRoots']),nativeMutationFormats=2,apiMembers=len(product['providerApiMembers']),packageSha256=product['packageSha256'],coreSha256=product['assemblySha256'],packageSourceCommit=product['packageSourceCommit'],developerSourceCommit=j['developer']['sourceCommit'],currentHead=j['source']['headCommit'],releaseQualified=False,releaseAreasPending=9,allIndexedHashesChecked=True,productsByteExact=True,manifestSha256=sha(root/'manifest.json'))
(t/'reviewed-final-draft-audit.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8');print(json.dumps(summary))
