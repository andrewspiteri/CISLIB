"""Independent startup rollback/task/borrowed-stream and original physical-byte checks."""
import argparse
import base64
import copy
import hashlib
import itertools
import json
from pathlib import Path
import subprocess
import tempfile

def require(condition, message):
    if not condition: raise AssertionError(message)

def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def pairs(items):
    result = {}
    for key, value in items:
        require(key not in result, 'Duplicate JSON member'); result[key] = value
    return result

def read(token):
    return json.loads(token, object_pairs_hook=pairs, parse_constant=lambda value: (_ for _ in ()).throw(AssertionError('Invalid JSON number')))

def same(a, b):
    if type(a) is bool or type(b) is bool: return type(a) is type(b) and a == b
    if isinstance(a, (int, float)) and isinstance(b, (int, float)): return a == b
    if type(a) is not type(b): return False
    if isinstance(a, dict): return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    if isinstance(a, list): return len(a) == len(b) and all(same(x, y) for x, y in zip(a, b))
    return a == b


def check_record(raw,event_name,message,component):
    require(len(raw)<=65536 and raw.endswith(b'\n') and raw.count(b'\n')==1 and not raw.startswith(b'\xef\xbb\xbf'),'Survivor record framing differs')
    value=read(raw.decode('utf-8'))
    wanted=dict(event_name=event_name,message=message,component=component,service='Console.App',env='test',version='1.2.3',status='info',timestamp='2026-10-13T10:11:12.0000000Z')
    require(all(same(value[k],v) for k,v in wanted.items()),'Survivor identity differs')
    require(same(value['schema_version'],1) and value['resource']['instance_id']=='11111111-1111-1111-1111-111111111111','Survivor protected identity differs')

def verify(row,a):
    e=row['expected'];mode=row['mode'];physical=row['physical'];require(a['caseId']==row['id'],'Startup case identity differs')
    for key in ['selectionCalls','launchCalls','workers','canWriteCalls','disposes','canRead','joinedAtReturn','flowSuppressed','beforeChecks','queues','successOutcome']:
        require(same(a[key],e[key]),'Startup '+key+' differs')
    require(same(a['asyncDisposes'],0),'Borrowed async disposal occurred')
    require(same(a['existingRunning'],e['existing']),'Existing runtime was stopped')
    if e['code'] is None: require(a['error'] is None,'Disabled/unselected console startup failed')
    else:
        output=e['output'];code=e['code'];wanted=dict(type='CIS.Logging.CoreLogInitializationException',output=output,code=code,message=f"Logging output '{output}' could not initialize ({code}).",inner=False,dataCount=0)
        require(same(a['error'],wanted),'Safe initialization error differs')
    before=base64.b64decode(a['beforeApplication'],validate=True);selected=base64.b64decode(a['selectedBytes'],validate=True)
    if mode=='existing': check_record(before,'startup.survived','old-runtime-still-usable','StartupExisting')
    else: require(before==(b'existing-console-data' if mode=='read-only' else b''),'Startup emitted data or changed retained bytes')
    wanted=before if mode in ['read-only','closed'] or physical else before+b'{"application":"selected-startup-preserved"}\n'
    require(selected==wanted,'Borrowed stream changed or application cannot reuse it')
    file=base64.b64decode(a['fileBytes'],validate=True)
    if e['fileRecords']==1: check_record(file,'startup.file','file-only-remains-independent','Startup')
    else: require(file==b'','Failed startup attempted ordinary file I/O')
    if physical:
        for stream in ['stdout','stderr']:
            require(base64.b64decode(a[stream],validate=True)==('{"application":"'+stream+'-startup-preserved"}\n').encode('utf-8'),'Physical application streams changed')
    else: require(a['stdout']==a['stderr']=='','Controlled startup emitted extra streams')

def damage(a,fault):
    v=copy.deepcopy(a)
    if fault=='joined': v['joinedAtReturn'][0]=False
    elif fault=='worker': v['workers']=99
    elif fault=='queue': v['queues'][0]['pending']=1
    elif fault=='disposed': v['disposes']=1
    elif fault=='flow': v['flowSuppressed'][0]=False
    elif fault=='inner': v['error']['inner']=True
    elif fault=='message': v['error']['message']='private-startup-path-and-payload'
    elif fault=='output': v['error']['output']='file'
    elif fault=='bytes': v['beforeApplication']=base64.b64encode(b'\n').decode('ascii')
    elif fault=='file': v['fileBytes']=base64.b64encode(b'\n').decode('ascii')
    else: raise ValueError('Unknown startup damage')
    return v

def main():
    parser=argparse.ArgumentParser()
    for name in ['dotnet','host','catalog','output']: parser.add_argument('--'+name,required=True)
    parser.add_argument('--family',choices=['windows','linux'],required=True);args=parser.parse_args()
    catalog=read(Path(args.catalog).read_text(encoding='utf-8'));rows=catalog['cases'];out=Path(args.output);out.mkdir(parents=True,exist_ok=True)
    ids={f'STARTUP-{m}-{p}-{s}' for m,p,s in itertools.product(['SELECT-THROW','SELECT-NULL','READ-ONLY','CLOSED','CANWRITE-THROW','LAUNCH-CONSOLE'],['CONSOLE','BOTH'],[0,1])}
    ids|={f'STARTUP-{m}-{p}-{s}' for m,p in [('LAUNCH-FILE','BOTH'),('EXISTING','CONSOLE'),('DISABLED','DISABLED'),('FILE-ONLY','FILE')] for s in [0,1]}
    ids|={f'STARTUP-PHYSICAL-READONLY-{p}-{s}' for p,s in itertools.product(['CONSOLE','BOTH'],[0,1])}
    require(len(rows)==36 and {r['id'] for r in rows}==ids,'Exact startup vector set differs')
    report=dict(scope='WP05-S04-T03 actual safe startup failure, acquired idle-worker join and borrowed/old-runtime preservation; file workers are controlled callbacks, physical file/public lifecycle/full S04 consumers remain later',family=args.family,cases=[],negativeControlsRejected=[]);actuals={}
    for row in rows:
        command=[args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',args.host,'console-startup',str(Path(args.catalog).resolve()),row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-startup-') as cwd:
            process=subprocess.run(command,cwd=cwd,capture_output=True,timeout=30);require(not list(Path(cwd).iterdir()),'Unexpected files')
        (out/(row['id']+'.stdout.bin')).write_bytes(process.stdout);(out/(row['id']+'.stderr.bin')).write_bytes(process.stderr)
        require(process.returncode==0,'Child failed '+row['id']+' '+process.stderr.decode('utf-8',errors='replace'))
        raw=process.stdout.rstrip(b'\r\n');start=raw.rfind(b'\n')+1;a=read(raw[start:].decode('utf-8'))
        if row['physical']: a['stdout']=base64.b64encode(raw[:start]).decode('ascii');a['stderr']=base64.b64encode(process.stderr).decode('ascii')
        else: require(start==0 and process.stderr==b'','Controlled child emitted extra streams')
        verify(row,a);actuals[row['id']]=a;o=a['observed']
        require(o['runtimeDescription']=='.NET 10.0.12' and o['operatingSystem']==args.family,'Exact runtime/platform differs')
        require(o['fixtureSha256']==sha(args.host) and o['coreSha256']==sha(Path(args.host).with_name('CIS.Core.dll')) and o['inputsSha256']==sha(args.catalog),'Actual input/binary identity differs')
        report['cases'].append(dict(id=row['id'],passed=True,command=command,observed=o))
    row=next(r for r in rows if r['id']=='STARTUP-CANWRITE-THROW-BOTH-0')
    messages={'joined':'Startup joinedAtReturn differs','worker':'Startup workers differs','queue':'Startup queues differs','disposed':'Startup disposes differs','flow':'Startup flowSuppressed differs','inner':'Safe initialization error differs','message':'Safe initialization error differs','output':'Safe initialization error differs','bytes':'Startup emitted data or changed retained bytes','file':'Failed startup attempted ordinary file I/O'}
    for fault,message in messages.items():
        try: verify(row,damage(actuals[row['id']],fault))
        except AssertionError as error:
            require(str(error)==message,'Wrong rejection reason '+fault);report['negativeControlsRejected'].append(fault)
        else: raise AssertionError('Accepted startup damage '+fault)
    require(len(report['negativeControlsRejected'])==10,'Startup controls missing')
    report.update(passed=True,count=36,coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')),hostSha256=sha(args.host),inputsSha256=sha(args.catalog))
    (out/'summary.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(family=args.family,count=36,passed=True)))

if __name__=='__main__': main()
