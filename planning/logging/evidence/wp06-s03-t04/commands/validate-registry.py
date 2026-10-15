from pathlib import Path
import json,hashlib,xml.etree.ElementTree as E
r=Path.cwd();e=r/'planning/logging/evidence/wp06-s03-t04'
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
p=r/'compatibility/csharp/microsoft-logging-fixtures.json';j=read(p)
assert j['identity']==dict(packageId='CIS.Core',assembly='CIS.Core',namespace='CIS.Logging.MicrosoftLogger',separateProviderPackage=False)
assert [s['id'] for s in j['binaryScenarios']]==['CoreReplacement','CompleteUpgrade']
assert j['currentControls']['scenarios']==['Source','CoreReplacement','CompleteUpgrade']
root=r/j['fixtureRoot'];assert root.resolve().is_relative_to(r.resolve())
assert len(j['fixtures'])==1;f=j['fixtures'][0]
for name in ['source','projectTemplate','api']:
 assert (root/f[name]).is_file() and (root/f[name]).resolve().is_relative_to(root.resolve())
tree=E.parse(root/f['projectTemplate']);props={n.tag:n.text for group in tree.findall('PropertyGroup') for n in group}
settings=f['sourceSettings'];assert settings['targetFramework']==props['TargetFramework']=='net10.0'
assert settings['langVersion']==props['LangVersion']=='14.0' and settings['nullable']==props['Nullable']=='enable'
assert settings['warningsAsErrors'] and props['TreatWarningsAsErrors']=='true' and props['IsPackable']=='false'
assert not tree.findall('.//PackageReference')
assert f['binaryCapture']==['CIS.Logging.ProviderContractConsumer.dll','CIS.Logging.ProviderContractConsumer.pdb','CIS.Logging.ProviderContractConsumer.deps.json','CIS.Logging.ProviderContractConsumer.runtimeconfig.json']
for name in f['binaryCapture']:assert (e/'fixtures-qualified/frozen'/name).is_file()
assert (r/j['harness']).is_file() and (r/j['protocol']).is_file() and read(r/j['publicationLedger'])['publications']==[]
families=read(r/'implementations/csharp/verification/families.json');family=next(x for x in families['families'] if x['id']=='compatibility');assert not family['developerEnabled']
entry=family['moduleHarnesses'][0];assert entry['registry']==p.relative_to(r).as_posix() and entry['harness']=='eng/check-microsoft-logging-compatibility.ps1'
catalog=read(r/'implementations/csharp/verification/compatibility/samples/catalog.json');entry=catalog['implementedModuleFixtures'][0];assert entry['registry']==p.relative_to(r).as_posix() and entry['harness']==j['harness']
paths=[p,r/'implementations/csharp/verification/families.json',r/'implementations/csharp/verification/compatibility/samples/catalog.json',root/f['projectTemplate'],root/f['source'],root/f['api']]
result=dict(status='Passed',scope='Targeted current provider registry, supported source settings, artifact membership and catalog/family/harness links; not full release or a general JSON-schema validator.',files=[dict(path=p.relative_to(r).as_posix(),sha256=sha(p)) for p in paths])
(e/'registry-validation.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print(json.dumps(result))
