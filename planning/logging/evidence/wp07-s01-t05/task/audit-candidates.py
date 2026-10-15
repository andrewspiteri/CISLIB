from pathlib import Path
import argparse,json,hashlib
p=argparse.ArgumentParser();p.add_argument('--directory',required=True,type=Path);p.add_argument('--os',required=True);p.add_argument('--output',required=True,type=Path);a=p.parse_args();load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'));d=a.directory;index=load(d/'index.json')
for v in index['files']:assert hashlib.sha256((d/v['path']).read_bytes()).hexdigest()==v['sha256']
meta=list(d.rglob('candidate.json'));assert len(meta)==1;c=load(meta[0]);assert c['packages']['CIS.Core.'+c['packageVersion']+'.nupkg']==c['packageSha256'];reports=list(d.rglob('report.json'));assert len(reports)==30;consumers=set()
for p in reports:
 v=load(p);assert v['os']==a.os and v['architecture']=='X64' and v['runtime']=='.NET 10.0.12' and v['pid']>0
 for role in ['core','logging','di']:assert v[role]['sha256']==c['assemblyHashes'][role] and '/app/bin/Release/net10.0/' in v[role]['path'].replace('\\','/')
 consumers.add(v['consumer']['sha256'])
assert len(consumers)==1
out={'os':a.os,'childReports':len(reports),'runtime':'.NET 10.0.12','architecture':'X64','packageVersion':c['packageVersion'],'packageSha256':c['packageSha256'],'loadedAssemblyHashes':c['assemblyHashes'],'consumerSha256':next(iter(consumers))};a.output.write_bytes((json.dumps(out,indent=2)+'\n').encode());print(json.dumps(out))
