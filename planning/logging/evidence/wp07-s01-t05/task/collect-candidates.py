from pathlib import Path
import argparse,json,re,hashlib,shutil,xml.etree.ElementTree as E
p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();root=a.root.resolve();a.output.mkdir(parents=True,exist_ok=True)
trx=root/'.artifacts/csharp/verification/Developer/families/integration/report.trx';tree=E.parse(trx);observed=set(re.findall(r'Exact package consumer evidence: ([^\r\n]+)','\n'.join(tree.getroot().itertext())));assert len(observed)==1,observed
base=(root/'.artifacts/csharp/candidate-consumers').resolve();files=[]
for value in observed:
 source=Path(value.strip()).resolve();assert source.is_relative_to(base)
 for path in sorted(source.rglob('*')):
  if not path.is_file() or path.name not in {'candidate.json','report.json','replies.json','restore.log','build.log'}:continue
  if {'packages','feed','app','http-cache'}.intersection(path.relative_to(source).parts):continue
  name=path.relative_to(base).as_posix();dest=a.output/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(path,dest);data=dest.read_bytes();files.append({'path':name,'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data)})
assert sum(f['path'].endswith('/candidate.json') for f in files)==1
assert sum(f['path'].endswith('/report.json') for f in files)>0
(a.output/'index.json').write_bytes((json.dumps({'sourceTrxSha256':hashlib.sha256(trx.read_bytes()).hexdigest(),'files':files},indent=2)+'\n').encode());print('Candidate native artifacts retained:',len(files))
